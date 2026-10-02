using System.IO;
using NasmythTraceability.Data;
using NasmythTraceability.Export;
using NasmythTraceability.Models;
using NasmythTraceability.Services;

namespace NasmythTraceability.Helpers;

/// <summary>
/// Headless smoke test (<c>NasmythTraceability.exe --selftest</c>): builds a throwaway
/// database, drives scans through the coordinator, then exercises the report exporters.
/// Prints a PASS/FAIL summary and returns a process exit code.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        var work = Path.Combine(Path.GetTempPath(), "nasmyth_selftest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var dbPath = Path.Combine(work, "selftest.db");
        var failures = 0;

        var log = new System.Text.StringBuilder();
        var resultFile = Path.Combine(Path.GetTempPath(), "nasmyth_selftest_result.txt");

        void Line(string s)
        {
            Console.WriteLine(s);
            log.AppendLine(s);
        }

        void Check(string name, bool ok)
        {
            Line($"  [{(ok ? "PASS" : "FAIL")}] {name}");
            if (!ok) failures++;
        }

        try
        {
            var config = new AppConfig { DatabasePath = dbPath };
            using var svc = new AppServices(config, useSimulatedScanner: true);

            var stations = svc.Stations.GetStations();
            Check("seed creates 4 stations", stations.Count == 4);
            Check("final station is ST04", svc.Stations.GetFinalStation()?.Code == "ST04");
            Check("station names carry no '(Final)' text", stations.All(s => !s.Name.Contains("(Final)")));

            var st1 = stations.First(s => s.Code == "ST01").Id;
            var st2 = stations.First(s => s.Code == "ST02").Id;
            var st3 = stations.First(s => s.Code == "ST03").Id;
            var st4 = stations.First(s => s.Code == "ST04").Id;

            var r1 = svc.Coordinator.SubmitManualScan(st1, "UNIT-000123");
            Check("first scan accepted OK", r1 is { Outcome: ScanOutcome.Accepted, Result: ScanResult.OK });

            var dup = svc.Coordinator.SubmitManualScan(st1, "UNIT-000123");
            Check("immediate re-scan flagged duplicate", dup.Outcome == ScanOutcome.Duplicate);

            var shortScan = svc.Coordinator.SubmitManualScan(st1, "AB");
            Check("short code rejected as NG", shortScan is { Outcome: ScanOutcome.Rejected, Result: ScanResult.NG });

            // no fixed routing: stations may be visited in any order, all accepted OK
            var o1 = svc.Coordinator.SubmitManualScan(st3, "UNIT-000123");
            var o2 = svc.Coordinator.SubmitManualScan(st2, "UNIT-000123");
            Check("out-of-order scans are accepted (no route enforcement)",
                o1.Outcome == ScanOutcome.Accepted && o2.Outcome == ScanOutcome.Accepted);
            var r4 = svc.Coordinator.SubmitManualScan(st4, "UNIT-000123");
            Check("final station scan accepted", r4.Outcome == ScanOutcome.Accepted);

            var current = svc.Trace.GetCurrent("UNIT-000123");
            Check("current_trace at ST04", current?.StationCode == "ST04");
            Check("current_trace marked Completed", current?.Status == TraceStatus.Completed);
            Check("full station history recorded (>= 4 rows)", svc.Trace.GetHistory("UNIT-000123").Count >= 4);
            Check("accepted scans carry no route message",
                svc.Trace.GetHistory("UNIT-000123")
                   .Where(h => h.Result == ScanResult.OK && h.StationCode != "ST04")
                   .All(h => string.IsNullOrEmpty(h.Message)));
            Check("scan_logs captured the duplicate",
                svc.Trace.GetScanLogs(50, ScanLogType.Duplicate).Any(l => l.RawData == "UNIT-000123"));

            // "which barcode is at which station now"
            var positions = svc.Trace.GetCurrentPositions();
            Check("current positions list is populated", positions.Count >= 1);
            Check("current positions filter by station works",
                svc.Trace.GetCurrentPositions("ST04").All(p => p.StationCode == "ST04"));
            Check("current positions search by barcode works",
                svc.Trace.GetCurrentPositions(null, "UNIT-000123").Any(p => p.Barcode == "UNIT-000123"));

            // a few more units for the report
            for (var i = 0; i < 8; i++)
            {
                svc.Coordinator.SubmitManualScan(st1, $"BULK-{i:0000}");
                if (i % 2 == 0)
                    svc.Coordinator.SubmitManualScan(st4, $"BULK-{i:0000}");
            }

            var from = DateTime.Today.AddDays(-1);
            var to = DateTime.Today;
            var summary = svc.Reports.GetSummary(from, to);
            Check("report summary total > 0", summary.TotalScans > 0);
            Check("report summary ng >= 1", summary.NgScans >= 1);
            Check("scans-by-station returns 4 rows", svc.Reports.GetByStation(from, to).Count == 4);

            var bundle = svc.Reports.BuildBundle(from, to);

            var xlsx = Path.Combine(work, "report.xlsx");
            ExcelExporter.Export(bundle, xlsx);
            Check("excel export produced a file", new FileInfo(xlsx).Length > 0);

            var pdf = Path.Combine(work, "report.pdf");
            PdfExporter.Export(bundle, pdf);
            Check("pdf export produced a file", new FileInfo(pdf).Length > 0);

            var backup = Path.Combine(work, "backup.db");
            svc.Database.Backup(backup);
            Check("database backup produced a file", new FileInfo(backup).Length > 0);

            // ---- a new card reader links itself on its first tap -------------------
            const string readerA = @"\\?\HID#VID_FFFF&PID_0035&MI_00#SELFTEST-A";
            const string readerB = @"\\?\HID#VID_FFFF&PID_0035&MI_00#SELFTEST-B";
            const string readerC = @"\\?\HID#VID_FFFF&PID_0035&MI_00#SELFTEST-C";
            ScanProcessedEventArgs? lastFeed = null;
            void OnFeed(object? s, ScanProcessedEventArgs a) => lastFeed = a;
            svc.Coordinator.ScanProcessed += OnFeed;

            svc.Simulated!.Emit("0012345678", readerA, "Test reader A", isNewDevice: true);
            var linkedA = svc.Stations.GetDeviceByKey(readerA);
            Check("first card tap links a new reader to ST01", linkedA?.StationCode == "ST01");
            Check("first card tap is saved to the database",
                svc.Trace.GetCurrent("0012345678")?.StationCode == "ST01");
            Check("first card tap reaches the live scan feed",
                lastFeed is { Barcode: "0012345678", Outcome: ScanOutcome.Accepted });

            svc.Simulated.Emit("0012345678", readerB, "Test reader B", isNewDevice: true);
            Check("a second new reader takes the next free station",
                svc.Stations.GetDeviceByKey(readerB)?.StationCode == "ST02");
            Check("the card is now at ST02", svc.Trace.GetCurrent("0012345678")?.StationCode == "ST02");

            svc.Simulated.Emit("0087654321", readerA, "Test reader A");
            Check("later taps on a linked reader are saved",
                svc.Trace.GetCurrent("0087654321")?.StationCode == "ST01");

            svc.Stations.MoveDeviceToStation(linkedA!.Id, st3);
            svc.Simulated.Emit("0055556666", readerA, "Test reader A");
            Check("a moved reader records at its new station",
                svc.Trace.GetCurrent("0055556666")?.StationCode == "ST03");

            svc.Stations.SetDeviceEnabled(linkedA.Id, false);
            svc.Simulated.Emit("0011112222", readerA, "Test reader A", isNewDevice: true);
            Check("a disabled reader is ignored, not linked again",
                svc.Trace.GetCurrent("0011112222") is null
                && svc.Stations.GetDeviceByKey(readerA) is { IsEnabled: false });

            svc.Settings.Set(SettingsService.AutoDetectReaders, false);
            svc.Simulated.Emit("0033334444", readerC, "Test reader C", isNewDevice: true);
            Check("with auto-detect off an unknown reader is ignored",
                svc.Stations.GetDeviceByKey(readerC) is null && svc.Trace.GetCurrent("0033334444") is null);
            svc.Settings.Set(SettingsService.AutoDetectReaders, true);

            svc.Coordinator.ScanProcessed -= OnFeed;
            svc.Stations.DeleteAllDevices();

            // ---- set final station -------------------------------------------------
            var st2Id = stations.First(s => s.Code == "ST02").Id;
            svc.Stations.SetFinalStation(st2Id);
            Check("set final station moves the final flag", svc.Stations.GetFinalStation()?.Code == "ST02");
            svc.Stations.SetFinalStation(st4); // restore

            // ---- legacy "(Final)" text in a name is cleaned up on startup --------
            svc.Database.Execute("UPDATE stations SET name = 'Station 3 (Final)' WHERE code = 'ST03';");
            Seed.Run(svc.Database);
            Check("Seed.Run strips '(Final)' from station names",
                svc.Stations.GetStationByCode("ST03")?.Name == "Station 3");

            // ---- deleting a Raw log also removes its trace row --------------------
            svc.Coordinator.SubmitManualScan(st1, "LOGLINK-1");
            var beforeTotal = svc.Reports.GetSummary(from, to).TotalScans;
            var rawLog = svc.Trace.GetScanLogs(50, ScanLogType.Raw).First(l => l.RawData == "LOGLINK-1");
            svc.Trace.DeleteScanLogAndTrace(rawLog);
            var afterTotal = svc.Reports.GetSummary(from, to).TotalScans;
            Check("deleting a Raw log removes the linked trace row", afterTotal == beforeTotal - 1);
            Check("deleting a Raw log clears current_trace", svc.Trace.GetCurrent("LOGLINK-1") is null);

            // ---- delete one trace row + repair current_trace ---------------------
            svc.Coordinator.SubmitManualScan(st1, "DELROW-1");
            svc.Coordinator.SubmitManualScan(stations.First(s => s.Code == "ST02").Id, "DELROW-1");
            var hist = svc.Trace.GetHistory("DELROW-1");
            svc.Trace.DeleteTrace(hist[^1].Id); // remove the ST02 row
            Check("delete trace row keeps earlier history", svc.Trace.GetHistory("DELROW-1").Count == 1);
            Check("delete trace row repairs current_trace back to ST01",
                svc.Trace.GetCurrent("DELROW-1")?.StationCode == "ST01");

            // ---- delete station with trace data (cascade) ------------------------
            svc.Coordinator.SubmitManualScan(stations.First(s => s.Code == "ST03").Id, "STDEL-1");
            var st3Id = stations.First(s => s.Code == "ST03").Id;
            Check("station has trace refs before delete", svc.Stations.CountStationTraceRefs(st3Id) > 0);
            svc.Stations.DeleteStation(st3Id, cascadeTraceData: true);
            Check("cascade delete removes the station", svc.Stations.GetStationByCode("ST03") is null);
            Check("cascade delete removes its trace rows",
                svc.Reports.GetScans(from, to, 5000).All(h => h.StationCode != "ST03"));

            // ---- purge -----------------------------------------------------------
            svc.Trace.PurgeAll();
            Check("purge clears trace_history", svc.Reports.GetSummary(from, to).TotalScans == 0);
            Check("purge clears scan_logs", svc.Trace.GetScanLogs(10).Count == 0);
            Check("purge keeps stations", svc.Stations.GetStations().Count >= 3);

            // ---- real Raw Input reader: allow-list wiring builds without error ---
            using (var real = new AppServices(
                       new AppConfig { DatabasePath = Path.Combine(work, "real.db") }, useSimulatedScanner: false))
            {
                real.Stations.MapDeviceToStation(@"\\?\HID#VID_TEST&PID_0001", "Test scanner",
                    real.Stations.GetStations()[0].Id);
                Check("real reader + scanner allow-list wire up cleanly", real.RawInput is not null);
            }

            // ---- sample dataset generator --------------------------------------
            using (var demoSvc = new AppServices(
                       new AppConfig { DatabasePath = Path.Combine(work, "demo.db") }, useSimulatedScanner: true))
            {
                var demo = DemoData.Generate(demoSvc, units: 25, days: 5, reset: true);
                var f = DateTime.Today.AddDays(-6);
                var tt = DateTime.Today.AddDays(1);
                var sum = demoSvc.Reports.GetSummary(f, tt);
                Check("demo data: 25 units generated", demo.Units == 25 && demo.Events >= 25);
                Check("demo data: current positions match unit count",
                    demoSvc.Trace.GetCurrentPositions().Count == 25);
                Check("demo data: report summary is populated",
                    sum.TotalScans == demo.Events && sum.UniqueBarcodes == 25);
                Check("demo data: completed + in-progress = units",
                    demo.Completed + demo.InProgress == 25);
                Check("demo data: scanners mapped for the USB tab", demoSvc.Stations.GetDevices().Count >= 3);
            }

            // ---- clearing trace data updates the live dashboard view model -------
            var dash = new ViewModels.DashboardViewModel(svc);
            svc.Coordinator.SubmitManualScan(st1, "DASHSYNC-1");
            Check("dashboard reflects a new scan",
                dash.CurrentPositions.Any(p => p.Barcode == "DASHSYNC-1") && dash.TotalScans >= 1);
            svc.Trace.ClearScanLogsAndTraces();
            Check("dashboard / current information clear when data is wiped in settings",
                dash.CurrentPositions.Count == 0 && dash.TotalScans == 0
                && string.IsNullOrEmpty(dash.CurrentBarcode));
            dash.Dispose();

            Line("");
            Line($"Artifacts: {work}");
        }
        catch (Exception ex)
        {
            failures++;
            Line("  [FAIL] unhandled exception");
            Line(ex.ToString());
        }

        Line("");
        Line(failures == 0 ? "SELF-TEST PASSED" : $"SELF-TEST FAILED ({failures} failing checks)");

        try { File.WriteAllText(resultFile, log.ToString()); } catch { /* ignore */ }
        return failures == 0 ? 0 : 1;
    }
}
