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

            // stations before the final one may be visited in any order, all accepted OK
            var o1 = svc.Coordinator.SubmitManualScan(st3, "UNIT-000123");
            var o2 = svc.Coordinator.SubmitManualScan(st2, "UNIT-000123");
            Check("earlier stations may be visited in any order",
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

            // ---- readers, tags and the station sequence ----------------------------
            const string readerA = @"\\?\HID#VID_FFFF&PID_0035&MI_00#SELFTEST-A";
            const string readerB = @"\\?\HID#VID_FFFF&PID_0035&MI_00#SELFTEST-B";
            const string readerC = @"\\?\HID#VID_FFFF&PID_0035&MI_00#SELFTEST-C";
            const string tag1 = "0012345678";
            const string tag2 = "0087654321";
            ScanProcessedEventArgs? lastFeed = null;
            void OnFeed(object? s, ScanProcessedEventArgs a) => lastFeed = a;
            svc.Coordinator.ScanProcessed += OnFeed;

            svc.Simulated!.Emit(tag1, readerA, "Test reader A", isNewDevice: true);
            Check("first card tap links a new reader to ST01",
                svc.Stations.GetDeviceByKey(readerA)?.StationCode == "ST01");
            Check("a tag with no work order is reported, not recorded",
                lastFeed is { Outcome: ScanOutcome.Error, TagId: tag1 } && svc.Trace.GetCurrent(tag1) is null);

            svc.Stations.MapDeviceToStation(readerB, "Test reader B", st4);
            Check("a reader can be assigned to the final station",
                svc.Stations.GetStation(st4)?.ReaderName == "Test reader B");

            svc.Tags.Assign(tag1, "wo-1001a");
            Check("tag assignment stores the work order in upper case",
                svc.Tags.GetByTag(tag1)?.WorkOrder == "WO-1001A");
            Check("work order numbers are validated",
                TagService.ValidateWorkOrder("WO-1001A", 4) is null
                && TagService.ValidateWorkOrder("WO 1001", 4) is not null
                && TagService.ValidateWorkOrder("AB", 4) is not null);

            Services.Scanning.BarcodeScannedEventArgs? captured = null;
            svc.Coordinator.ReadInterceptor = a => captured = a;
            lastFeed = null;
            svc.Simulated.Emit("0099990000", readerA, "Test reader A");
            svc.Coordinator.ReadInterceptor = null;
            Check("in assignment mode a tap is captured, not tracked",
                captured?.Barcode == "0099990000" && lastFeed is null);

            // the right order: reader 1, then the final reader
            svc.Simulated.Emit(tag1, readerA, "Test reader A");
            Check("a tag tap at reader 1 records the entry of its work order",
                svc.Trace.GetCurrent("WO-1001A") is { StationCode: "ST01", Status: TraceStatus.InProgress }
                && lastFeed is { Barcode: "WO-1001A", TagId: tag1, Outcome: ScanOutcome.Accepted });

            svc.Simulated.Emit(tag1, readerA, "Test reader A");
            Check("a second tap at the same station is ignored",
                lastFeed?.Outcome == ScanOutcome.Duplicate && svc.Trace.GetHistory("WO-1001A").Count == 1);

            svc.Simulated.Emit(tag1, readerB, "Test reader B");
            var done = svc.Trace.GetHistory("WO-1001A");
            Check("a tap at the final reader completes the work order",
                svc.Trace.GetCurrent("WO-1001A")?.Status == TraceStatus.Completed);
            Check("the exit from reader 1 is logged when the tag reaches the final reader",
                done.Count == 2 && done[0].StationCode == "ST01" && done[0].ExitedAt is not null
                && done[1].StationCode == "ST04" && done[1].ExitedAt is null);
            Check("the exit is also written to the scan log",
                svc.Trace.GetScanLogs(50, ScanLogType.Info)
                   .Any(l => l.RawData == "WO-1001A" && l.Message == "Exit from ST01"));

            svc.Simulated.Emit(tag1, readerA, "Test reader A");
            Check("a completed work order is not scanned again",
                lastFeed?.Outcome == ScanOutcome.Error && svc.Trace.GetHistory("WO-1001A").Count == 2);

            // the wrong order: final reader first
            svc.Tags.Assign(tag2, "WO-2002");
            svc.Simulated.Emit(tag2, readerB, "Test reader B");
            Check("a tap at the final reader first is recorded as NG",
                lastFeed?.Outcome == ScanOutcome.Rejected
                && svc.Trace.GetCurrent("WO-2002")?.Status == TraceStatus.Rejected);

            svc.Simulated.Emit(tag2, readerA, "Test reader A");
            Check("the work order stays blocked until the wrong scan is deleted",
                lastFeed?.Outcome == ScanOutcome.Error && svc.Trace.GetHistory("WO-2002").Count == 1);

            svc.Trace.DeleteWorkOrder("WO-2002");
            Check("deleting its scans clears the work order but keeps the tag",
                svc.Trace.GetCurrent("WO-2002") is null && svc.Tags.GetByTag(tag2)?.WorkOrder == "WO-2002");

            svc.Simulated.Emit(tag2, readerA, "Test reader A");
            svc.Simulated.Emit(tag2, readerB, "Test reader B");
            Check("after the delete, scanning in the right order completes it",
                svc.Trace.GetCurrent("WO-2002")?.Status == TraceStatus.Completed);

            svc.Trace.DeleteLastScan("WO-2002");
            var back = svc.Trace.GetHistory("WO-2002");
            Check("deleting the last scan puts the work order back at reader 1",
                svc.Trace.GetCurrent("WO-2002") is { StationCode: "ST01", Status: TraceStatus.InProgress }
                && back.Count == 1 && back[0].ExitedAt is null);

            // tags are reused; one tag per work order
            svc.Tags.Assign(tag1, "WO-3003");
            Check("a tag can be moved to a new work order",
                svc.Tags.GetByTag(tag1)?.WorkOrder == "WO-3003" && svc.Tags.GetByWorkOrder("WO-1001A") is null);
            svc.Tags.Assign("0055556666", "WO-3003");
            Check("a work order keeps only one tag",
                svc.Tags.GetByTag(tag1) is null && svc.Tags.GetByWorkOrder("WO-3003")?.TagId == "0055556666");
            Check("the assignment list shows the tracking status",
                svc.Tags.GetAll().Any(a => a.WorkOrder == "WO-2002" && a.Status == TraceStatus.InProgress)
                && svc.Tags.GetAll("3003").Count == 1);

            // search as you type: by part of a work order number or of a tag id
            var byNumber = svc.Trace.SearchWorkOrders("wo-", 10);
            Check("suggestions match part of a work order number",
                byNumber.Any(s => s.WorkOrder == "WO-1001A" && s.Status == TraceStatus.Completed)
                && byNumber.Any(s => s.WorkOrder == "WO-2002" && s.StationCode == "ST01"));
            Check("suggestions include a work order that has a tag but no scan yet",
                byNumber.Any(s => s is { WorkOrder: "WO-3003", Status: null, TagId: "0055556666" }));
            Check("suggestions match part of a tag id",
                svc.Trace.SearchWorkOrders("05555", 10) is [{ WorkOrder: "WO-3003" }]);
            Check("suggestions are empty when nothing matches",
                svc.Trace.SearchWorkOrders("ZZZZ", 10).Count == 0 && svc.Trace.SearchWorkOrders("  ", 10).Count == 0);

            // one reader per station
            svc.Stations.MapDeviceToStation(readerC, "Test reader C", st1);
            Check("assigning a reader replaces the previous reader of that station",
                svc.Stations.GetDeviceByKey(readerA) is null
                && svc.Stations.GetStation(st1)?.ReaderName == "Test reader C");

            svc.Settings.Set(SettingsService.AutoDetectReaders, false);
            svc.Simulated.Emit(tag2, readerA, "Test reader A", isNewDevice: true);
            Check("with automatic linking off an unknown reader is ignored",
                svc.Stations.GetDeviceByKey(readerA) is null);
            svc.Settings.Set(SettingsService.AutoDetectReaders, true);

            svc.Coordinator.ScanProcessed -= OnFeed;
            svc.Stations.DeleteAllDevices();

            // ---- a database from before exit times were added is upgraded ---------
            var oldDb = Path.Combine(work, "old.db");
            using (var cn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={oldDb};Pooling=False"))
            {
                cn.Open();
                using var cmd = cn.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE schema_info (id INTEGER PRIMARY KEY, version INTEGER NOT NULL, applied_at TEXT NOT NULL);" +
                    "INSERT INTO schema_info VALUES (1, 1, '2026-01-01 00:00:00.000');" +
                    "CREATE TABLE trace_history (id INTEGER PRIMARY KEY AUTOINCREMENT, barcode TEXT NOT NULL, " +
                    "station_id INTEGER NOT NULL, station_code TEXT NOT NULL, result TEXT NOT NULL, " +
                    "message TEXT NOT NULL DEFAULT '', route_id INTEGER, device_key TEXT NOT NULL DEFAULT '', " +
                    "scanned_at TEXT NOT NULL);";
                cmd.ExecuteNonQuery();
            }

            using (var old = new AppServices(new AppConfig { DatabasePath = oldDb }, useSimulatedScanner: true))
            {
                var oldStations = old.Stations.GetStations();
                old.Coordinator.SubmitManualScan(oldStations[0].Id, "OLD-0001");
                old.Coordinator.SubmitManualScan(oldStations.First(s => s.IsFinal).Id, "OLD-0001");
                Check("an older database is upgraded and records exit times",
                    old.Trace.GetHistory("OLD-0001") is { Count: 2 } h && h[0].ExitedAt is not null);
            }

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

            // ---- a deleted database file is created again, empty -----------------
            var lostDb = Path.Combine(work, "lost.db");
            var lostConfig = new AppConfig { DatabasePath = lostDb };
            using (var first = new AppServices(lostConfig, useSimulatedScanner: true))
            {
                first.Coordinator.SubmitManualScan(first.Stations.GetStations()[0].Id, "LOST-0001");

                var blocked = false;
                try { File.Delete(lostDb); }
                catch (IOException) { blocked = true; }
                catch (UnauthorizedAccessException) { blocked = true; }
                Check("database file cannot be deleted while the app is running",
                    blocked && File.Exists(lostDb));
            }

            File.Delete(lostDb);
            File.WriteAllText(lostDb + "-wal", "left behind by the deleted database");
            using (var second = new AppServices(lostConfig, useSimulatedScanner: true))
            {
                Check("a deleted database is created again on the next start",
                    File.Exists(lostDb) && second.Stations.GetStations().Count == 4);
                Check("the new database starts empty",
                    second.Trace.GetCurrentPositions().Count == 0 && second.Trace.GetScanLogs(10).Count == 0);
                Check("the new database accepts scans",
                    second.Coordinator.SubmitManualScan(second.Stations.GetStations()[0].Id, "LOST-0002")
                        .Outcome == ScanOutcome.Accepted);
            }

            // ---- restoring a backup still works with the file held open ----------
            using (var restored = new AppServices(
                       new AppConfig { DatabasePath = Path.Combine(work, "restore.db") }, useSimulatedScanner: true))
            {
                restored.Database.Restore(backup);
                Check("restore brings back the backed-up scans",
                    restored.Trace.GetCurrent("UNIT-000123")?.StationCode == "ST04");
                Check("scans are accepted after a restore",
                    restored.Coordinator.SubmitManualScan(restored.Stations.GetStations()[0].Id, "AFTER-RESTORE")
                        .Outcome == ScanOutcome.Accepted);
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
            }

            // ---- idle timeout: a page left alone goes back to the Dashboard --------
            var shell = new ViewModels.MainViewModel(svc);
            var t0 = DateTime.Now;
            shell.CurrentPage = ViewModels.AppPage.Reports;
            shell.NotifyActivity(t0);
            shell.CheckIdle(t0.AddSeconds(89));
            Check("no idle warning before the last 30 seconds", !shell.IsIdleWarningVisible);
            shell.CheckIdle(t0.AddSeconds(95));
            Check("the idle warning counts down in the last 30 seconds",
                shell.IsIdleWarningVisible && shell.IdleWarningText.Contains("25 seconds"));
            shell.NotifyActivity(t0.AddSeconds(100));
            shell.CheckIdle(t0.AddSeconds(150));
            Check("a key press or click restarts the 120 seconds",
                !shell.IsIdleWarningVisible && shell.CurrentPage == ViewModels.AppPage.Reports);
            shell.CheckIdle(t0.AddSeconds(221));
            Check("an idle page closes back to the Dashboard",
                shell.CurrentPage == ViewModels.AppPage.Dashboard && !shell.IsIdleWarningVisible);
            shell.CheckIdle(t0.AddSeconds(5000));
            Check("the Dashboard itself never times out",
                shell.CurrentPage == ViewModels.AppPage.Dashboard && !shell.IsIdleWarningVisible);

            // ---- tag assignment needs the administrator password ------------------
            var assign = shell.TagAssignment;
            shell.CurrentPage = ViewModels.AppPage.TagAssignment;
            Check("tag assignment opens locked, with the readers still tracking",
                !assign.IsUnlocked && svc.Coordinator.ReadInterceptor is null && !assign.AssignCommand.CanExecute(null));
            assign.UnlockCommand.Execute(new System.Windows.Controls.PasswordBox { Password = "not-the-password" });
            Check("a wrong password does not unlock tag assignment", !assign.IsUnlocked);
            assign.UnlockCommand.Execute(new System.Windows.Controls.PasswordBox
            {
                Password = SettingsService.DefaultSettingsPassword,
            });
            Check("the administrator password unlocks it and turns assignment mode on",
                assign.IsUnlocked && svc.Coordinator.ReadInterceptor is not null && assign.AssignCommand.CanExecute(null));
            shell.NotifyActivity(t0);
            shell.CheckIdle(t0.AddSeconds(121));
            Check("timing out locks tag assignment and returns the readers to tracking",
                shell.CurrentPage == ViewModels.AppPage.Dashboard
                && !assign.IsUnlocked && svc.Coordinator.ReadInterceptor is null);
            shell.Dispose();

            // ---- clearing trace data updates the live dashboard view model -------
            var dash = new ViewModels.DashboardViewModel(svc);
            svc.Coordinator.SubmitManualScan(st1, "DASHSYNC-1");
            Check("dashboard reflects a new scan",
                dash.CurrentPositions.Any(p => p.Barcode == "DASHSYNC-1") && dash.TotalScans >= 1);

            dash.SearchText = "d";
            Check("dashboard search waits for two characters before suggesting",
                !dash.IsSuggestionsOpen && dash.Suggestions.Count == 0);
            dash.SearchText = "dashs";
            Check("dashboard search suggests matches as you type",
                dash.IsSuggestionsOpen && dash.Suggestions.Any(s => s.WorkOrder == "DASHSYNC-1"));
            dash.MoveSuggestionCommand.Execute(1);
            dash.SearchCommand.Execute(null);
            Check("picking a suggestion shows that work order and closes the list",
                !dash.IsSuggestionsOpen && dash.SearchText == "DASHSYNC-1"
                && dash.CurrentBarcode == "DASHSYNC-1" && dash.BarcodeFound);

            svc.Trace.PurgeAll();
            Check("dashboard / scan information clear when the scan data is wiped",
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
