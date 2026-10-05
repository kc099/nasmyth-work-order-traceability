using System.IO;
using System.Windows.Threading;
using NasmythTraceability.Data;
using NasmythTraceability.Export;
using NasmythTraceability.Models;
using NasmythTraceability.Services;
using NasmythTraceability.Services.Rfid;

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
            Check("exported reports hold the valid scans only",
                bundle.Scans.Count > 0 && bundle.Scans.All(s => s.Result == ScanResult.OK)
                && bundle.ByStation.All(s => s.Ng == 0)
                && bundle.Summary.TotalScans == summary.OkScans && bundle.Summary.NgScans == 0);

            var breakdown = svc.Reports.GetScanBreakdown(from, to);
            var st1Counts = breakdown.First(b => b.StationCode == "ST01");
            Check("scans by station are split into valid, invalid and repeat",
                breakdown.Count == 4
                && st1Counts is { Repeat: >= 1, Invalid: >= 1 }
                && breakdown.Sum(b => b.Valid) == summary.OkScans);

            var xlsx = Path.Combine(work, "report.xlsx");
            ExcelExporter.Export(bundle, xlsx);
            Check("excel export produced a file", new FileInfo(xlsx).Length > 0);

            var pdf = Path.Combine(work, "report.pdf");
            PdfExporter.Export(bundle, pdf);
            Check("pdf export produced a file", new FileInfo(pdf).Length > 0);

            var backup = Path.Combine(work, "backup.db");
            svc.Database.Backup(backup);
            Check("database backup produced a file", new FileInfo(backup).Length > 0);

            // ---- tags and the station sequence ------------------------------------
            const string tag1 = "04A1B2C3";
            const string tag2 = "04D4E5F6";
            ScanProcessedEventArgs? lastFeed = null;
            void OnFeed(object? s, ScanProcessedEventArgs a) => lastFeed = a;
            svc.Coordinator.ScanProcessed += OnFeed;

            svc.Simulated!.Emit(tag1, st1, cardData: "SOMETHING");
            Check("a tag with no work order is reported, not recorded",
                lastFeed is { Outcome: ScanOutcome.Error, TagId: tag1 } && svc.Trace.GetCurrent(tag1) is null
                && lastFeed.Message.Contains("SOMETHING"));

            svc.Tags.Assign(tag1, "wo-1001a");
            Check("tag assignment stores the work order in upper case",
                svc.Tags.GetByTag(tag1)?.WorkOrder == "WO-1001A");
            Check("work order numbers are validated (16 characters fit on a tag)",
                TagService.ValidateWorkOrder("WO-1001A", 4) is null
                && TagService.ValidateWorkOrder("WO 1001", 4) is not null
                && TagService.ValidateWorkOrder("AB", 4) is not null
                && TagService.ValidateWorkOrder("ABCDEFGHIJ123456", 4) is null
                && TagService.ValidateWorkOrder("ABCDEFGHIJ1234567", 4) is not null);

            // the right order: station 1, then the final station
            svc.Simulated.Emit(tag1, st1);
            Check("a tag tap at station 1 records the entry of its work order",
                svc.Trace.GetCurrent("WO-1001A") is { StationCode: "ST01", Status: TraceStatus.InProgress }
                && lastFeed is { Barcode: "WO-1001A", TagId: tag1, Outcome: ScanOutcome.Accepted });

            svc.Simulated.Emit(tag1, st1);
            Check("a second tap at the same station is ignored",
                lastFeed?.Outcome == ScanOutcome.Duplicate && svc.Trace.GetHistory("WO-1001A").Count == 1);

            svc.Simulated.Emit(tag1, st4);
            var done = svc.Trace.GetHistory("WO-1001A");
            Check("a tap at the final station completes the work order",
                svc.Trace.GetCurrent("WO-1001A")?.Status == TraceStatus.Completed);
            Check("the exit from station 1 is logged when the tag reaches the final station",
                done.Count == 2 && done[0].StationCode == "ST01" && done[0].ExitedAt is not null
                && done[1].StationCode == "ST04" && done[1].ExitedAt is null);
            Check("the exit is also written to the scan log",
                svc.Trace.GetScanLogs(50, ScanLogType.Info)
                   .Any(l => l.RawData == "WO-1001A" && l.Message == "Exit from ST01"));

            svc.Simulated.Emit(tag1, st1);
            Check("a completed work order is not scanned again",
                lastFeed?.Outcome == ScanOutcome.Error && svc.Trace.GetHistory("WO-1001A").Count == 2);

            // the wrong order: final station first
            svc.Tags.Assign(tag2, "WO-2002");
            svc.Simulated.Emit(tag2, st4);
            Check("a tap at the final station first is recorded as NG",
                lastFeed?.Outcome == ScanOutcome.Rejected
                && svc.Trace.GetCurrent("WO-2002")?.Status == TraceStatus.Rejected);

            svc.Simulated.Emit(tag2, st1);
            Check("the work order stays blocked until the wrong scan is deleted",
                lastFeed?.Outcome == ScanOutcome.Error && svc.Trace.GetHistory("WO-2002").Count == 1);

            svc.Trace.DeleteWorkOrder("WO-2002");
            Check("deleting its scans clears the work order but keeps the tag",
                svc.Trace.GetCurrent("WO-2002") is null && svc.Tags.GetByTag(tag2)?.WorkOrder == "WO-2002");

            svc.Simulated.Emit(tag2, st1);
            svc.Simulated.Emit(tag2, st4);
            Check("after the delete, scanning in the right order completes it",
                svc.Trace.GetCurrent("WO-2002")?.Status == TraceStatus.Completed);

            svc.Trace.DeleteLastScan("WO-2002");
            var back = svc.Trace.GetHistory("WO-2002");
            Check("deleting the last scan puts the work order back at station 1",
                svc.Trace.GetCurrent("WO-2002") is { StationCode: "ST01", Status: TraceStatus.InProgress }
                && back.Count == 1 && back[0].ExitedAt is null);

            // a station that is switched off records nothing
            var st2Row = svc.Stations.GetStation(st2)!;
            st2Row.IsEnabled = false;
            svc.Stations.UpdateStation(st2Row);
            svc.Simulated.Emit(tag2, st2);
            Check("a tap at a disabled station is not recorded",
                lastFeed?.Outcome == ScanOutcome.Error && svc.Trace.GetCurrent("WO-2002")?.StationCode == "ST01");
            st2Row.IsEnabled = true;
            svc.Stations.UpdateStation(st2Row);

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

            svc.Coordinator.ScanProcessed -= OnFeed;

            // ---- reader addresses typed in Settings --------------------------------
            Check("reader addresses are normalised",
                ReaderAddress.Normalize(" 192.168.1.57 ", out _) == "192.168.1.57"
                && ReaderAddress.Normalize("http://192.168.1.57/", out _) == "192.168.1.57"
                && ReaderAddress.Normalize("rfid-rw-e5f6.local", out _) == "rfid-rw-e5f6.local"
                && ReaderAddress.Normalize("127.0.0.1:8080", out _) == "127.0.0.1:8080"
                && ReaderAddress.Normalize("", out _) == "");
            Check("an all-zero or missing MAC is not taken as a reader's identity",
                !ReaderAddress.IsUsableMac("00:00:00:00:00:00") && !ReaderAddress.IsUsableMac("")
                && !ReaderAddress.IsUsableMac("FF:FF:FF:FF:FF:FF") && ReaderAddress.IsUsableMac("A1:B2:C3:D4:E5:F6"));
            Check("bad reader addresses are refused",
                ReaderAddress.Normalize("192.168.1", out _) is null
                && ReaderAddress.Normalize("192.168.1.300", out _) is null
                && ReaderAddress.Normalize("https://192.168.1.57", out _) is null
                && ReaderAddress.Normalize("192.168.1.57/api", out _) is null
                && ReaderAddress.Normalize("two words", out _) is null);

            NetworkReaderTests(work, Check);

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
                    "scanned_at TEXT NOT NULL);" +
                    // scan_logs as it was before version 4 (no outcome / tag_id columns)
                    "CREATE TABLE scan_logs (id INTEGER PRIMARY KEY AUTOINCREMENT, raw_data TEXT NOT NULL DEFAULT '', " +
                    "station_id INTEGER, station_code TEXT NOT NULL DEFAULT '', device_key TEXT NOT NULL DEFAULT '', " +
                    "device_name TEXT NOT NULL DEFAULT '', log_type TEXT NOT NULL, message TEXT NOT NULL DEFAULT '', " +
                    "created_at TEXT NOT NULL);" +
                    "INSERT INTO scan_logs (raw_data, station_id, station_code, log_type, message, created_at) VALUES " +
                    "('WO-OLD-1', 1, 'ST01', 'Raw', 'Entered ST01', '2026-01-01 08:00:00.000'), " +
                    "('WO-OLD-1', 1, 'ST01', 'Duplicate', 'Already at ST01', '2026-01-01 08:00:05.000'), " +
                    "('', 1, 'ST01', 'Error', 'Reader offline (10.0.0.1): no reply', '2026-01-01 08:01:00.000'), " +
                    "('04OLDTAG', 1, 'ST01', 'Error', 'Tag is not assigned to a work order', '2026-01-01 08:02:00.000'), " +
                    "('WO-OLD-1', 1, 'ST01', 'Info', 'Exit from ST01', '2026-01-01 08:03:00.000');";
                cmd.ExecuteNonQuery();
            }

            using (var old = new AppServices(new AppConfig { DatabasePath = oldDb }, useSimulatedScanner: true))
            {
                var oldStations = old.Stations.GetStations();
                old.Coordinator.SubmitManualScan(oldStations[0].Id, "OLD-0001");
                old.Coordinator.SubmitManualScan(oldStations.First(s => s.IsFinal).Id, "OLD-0001");
                Check("an older database is upgraded and records exit times",
                    old.Trace.GetHistory("OLD-0001") is { Count: 2 } h && h[0].ExitedAt is not null);

                var oldExceptions = old.Trace.GetScanExceptions();
                Check("older scan logs are sorted into invalid and repeat scans on upgrade",
                    oldExceptions.Count == 2
                    && oldExceptions.Any(l => l is { Outcome: ScanOutcome.Duplicate, RawData: "WO-OLD-1" })
                    && oldExceptions.Any(l => l is { Outcome: ScanOutcome.Error, TagId: "04OLDTAG", RawData: "" }));
                var oldBreakdown = old.Reports.GetScanBreakdown(new DateTime(2026, 1, 1), new DateTime(2026, 1, 1))
                    .First(b => b.StationCode == "ST01");
                Check("reader status and exit lines are not counted as scans",
                    oldBreakdown is { Invalid: 1, Repeat: 1 });
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

            // ---- the app's own wiring: readers start polling by themselves --------
            var emptyNet = new FakeRfidNetwork();
            using (var real = new AppServices(
                       new AppConfig { DatabasePath = Path.Combine(work, "real.db") }, useSimulatedScanner: false, emptyNet))
            {
                var first = real.Stations.GetStations()[0];
                first.ReaderIp = "10.255.255.1";
                real.Stations.UpdateStation(first);
                Check("readers poll from start-up and an unreachable one is shown offline",
                    real.Readers.IsRunning
                    && WaitUntil(() => real.Readers.StationStatus(first.Id).State == Services.Rfid.ReaderState.Offline));
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
            Check("tag assignment opens locked", !assign.IsUnlocked);
            assign.WorkOrder = "wo-typed-1";
            Check("a work order can be typed while the page is locked",
                assign.WorkOrder == "wo-typed-1" && assign.WorkOrderHintKey == "OK");
            assign.WriteTagCommand.Execute(null);
            Check("Write to tag on a locked page says it must be unlocked, and writes nothing",
                !assign.IsWriting && assign.WriteStateKey == "NG" && assign.WriteMessage.Contains("locked"));
            assign.WorkOrder = "AB";
            var tooShort = assign.WorkOrderHintKey == "NG";
            assign.WorkOrder = "ABCDEFGHIJ1234567";
            var tooLong = assign.WorkOrderHintKey == "NG" && assign.WorkOrderHint.Contains("16");
            assign.WorkOrder = "WO 12";
            Check("the work order box explains straight away why a work order is not valid",
                tooShort && tooLong && assign.WorkOrderHintKey == "NG");
            assign.WorkOrder = "";
            assign.UnlockCommand.Execute(new System.Windows.Controls.PasswordBox { Password = "not-the-password" });
            Check("a wrong password does not unlock tag assignment", !assign.IsUnlocked);
            assign.UnlockCommand.Execute(new System.Windows.Controls.PasswordBox
            {
                Password = SettingsService.DefaultSettingsPassword,
            });
            Check("the administrator password unlocks writing tags",
                assign.IsUnlocked && assign.WriteTagCommand.CanExecute(null));
            svc.Tags.Assign("04OPEN0001", "WO-OPEN-1");
            svc.Simulated.Emit("04OPEN0001", st1);
            Check("station scans are recorded while Tag Assignment is open and unlocked",
                svc.Trace.GetCurrent("WO-OPEN-1")?.StationCode == "ST01");
            assign.WorkOrder = "WO-OPEN-2";
            assign.WriteTagCommand.Execute(null);
            Check("writing needs a work order assigning station to be set up",
                !assign.IsWriting && assign.WriteStateKey == "NG" && assign.WriteMessage.Contains("assigning station"));
            shell.NotifyActivity(t0);
            shell.CheckIdle(t0.AddSeconds(121));
            Check("timing out locks tag assignment",
                shell.CurrentPage == ViewModels.AppPage.Dashboard && !assign.IsUnlocked && !svc.TagWriter.IsWaiting);
            shell.Dispose();

            // ---- clearing trace data updates the live dashboard view model -------
            var dash = new ViewModels.DashboardViewModel(svc);
            svc.Coordinator.SubmitManualScan(st1, "DASHSYNC-1");
            Check("dashboard reflects a new scan",
                dash.CurrentPositions.Any(p => p.Barcode == "DASHSYNC-1") && dash.TotalScans >= 1);

            svc.Simulated!.Emit("04NOTASSIGNED", st1);
            svc.Coordinator.SubmitManualScan(st1, "DASHSYNC-1");
            Check("Scan Information lists invalid and repeat scans as they happen",
                dash.ScanExceptions.Any(l => l is { TagId: "04NOTASSIGNED", KindText: "Invalid", StationCode: "ST01" })
                && dash.ScanExceptions.Any(l => l is { RawData: "DASHSYNC-1", KindText: "Repeat" }));
            dash.ExceptionKind = "Repeat";
            Check("invalid and repeat scans can be shown separately",
                dash.ScanExceptions.Count > 0 && dash.ScanExceptions.All(l => l.KindText == "Repeat"));
            dash.ExceptionKind = dash.ExceptionKindOptions[0];

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

    /// <summary>
    /// Network RFID readers against <see cref="FakeRfidNetwork"/>: polling, the work order
    /// assigning station, reader restarts, outages, catch-up after an app restart.
    /// </summary>
    private static void NetworkReaderTests(string work, Action<string, bool> check)
    {
        // The wire format, checked against the README's own examples.
        var events = RfidReaderClient.Parse<RfidEventsDto>("""
            {"device":"RFID-RW-E5F6","bootId":2873461123,"uptimeMs":934211,"lastEventId":9,"events":[
             {"id":8,"ms":923456,"type":"read","ok":true,"uid":"A1B2C3D4","cardType":"MIFARE 1KB","data":"TOOL-0042",
              "hex":"544F4F4C2D3030343200000000000000","error":""},
             {"id":9,"ms":925010,"type":"write","ok":false,"uid":"04A1B2C3D4E5F6",
              "cardType":"MIFARE Ultralight or Ultralight C","data":"","hex":"",
              "error":"authentication failed: Timeout in communication."}]}
            """);
        var status = RfidReaderClient.Parse<RfidStatusDto>("""
            {"mode":"read","data":"TOOL-0042","once":false,"device":"RFID-RW-E5F6","firmware":"1.2.0",
             "mac":"A1:B2:C3:D4:E5:F6","hostname":"rfid-rw-e5f6.local","bootId":2873461123,"block":2,
             "readerOk":true,"uptimeMs":934211,"ap":{"ssid":"RFID-RW-E5F6","ip":"192.168.4.1","clients":0},
             "sta":{"ssid":"Factory","connected":true,"ip":"192.168.1.57","rssi":-58},
             "webhook":{"url":"","lastCode":0},"lastEventId":9}
            """);
        check("the README's event and status replies are read correctly",
            events is { BootId: 2873461123, LastEventId: 9, Events.Count: 2 }
            && events.Events[0] is { Id: 8, Type: "read", Ok: true, Uid: "A1B2C3D4", Data: "TOOL-0042" }
            && events.Events[1] is { Type: "write", Ok: false, Error: "authentication failed: Timeout in communication." }
            && status is { Mode: "read", Mac: "A1:B2:C3:D4:E5:F6", BootId: 2873461123, LastEventId: 9, ReaderOk: true });
        check("mode requests are sent exactly as the README shows",
            System.Text.Encoding.UTF8.GetString(RfidReaderClient.SerializeBody(RfidModeRequest.WriteOnce("TOOL-0042")))
                == """{"mode":"write","data":"TOOL-0042","once":true}"""
            && System.Text.Encoding.UTF8.GetString(RfidReaderClient.SerializeBody(RfidModeRequest.Read()))
                == """{"mode":"read"}""");

        var net = new FakeRfidNetwork();
        var r1 = net.Add("10.0.0.11", "AA:00:00:00:00:01", "RFID-RW-0001");
        var r4 = net.Add("10.0.0.14", "AA:00:00:00:00:04", "RFID-RW-0004");
        var ra = net.Add("10.0.0.20", "AA:00:00:00:00:20", "RFID-RW-0020");

        // Taps made before the app ever saw these readers are not imported.
        r1.Tap("04OLD0001");
        // A production reader left in write mode would overwrite every card presented.
        r4.Data = "JUNK";
        r4.Mode = "write";

        var config = new AppConfig { DatabasePath = Path.Combine(work, "network.db") };
        var svc = new AppServices(config, useSimulatedScanner: true, readerHandler: net);
        try
        {
            svc.Settings.Set(SettingsService.ReaderPollIntervalMs, 50);
            var stations = svc.Stations.GetStations();
            var s1 = stations.First(s => s.Code == "ST01");
            var s4 = stations.First(s => s.Code == "ST04");
            s1.ReaderIp = "10.0.0.11";
            s4.ReaderIp = "10.0.0.14";
            svc.Stations.UpdateStation(s1);
            svc.Stations.UpdateStation(s4);
            svc.Settings.Set(SettingsService.AssignmentReaderIp, "10.0.0.20");

            var feed = new List<ScanProcessedEventArgs>();
            svc.Coordinator.ScanProcessed += (_, a) => { lock (feed) feed.Add(a); };
            var progress = new List<TagWriteProgressEventArgs>();
            svc.TagWriter.Progress += (_, a) => { lock (progress) progress.Add(a); };
            ReaderCardEvent? seen = null;
            svc.TagWriter.CardSeen += (_, a) => seen = a;

            svc.Readers.Start();

            check("every configured reader comes online",
                WaitUntil(() => svc.Readers.StationStatus(s1.Id).State == ReaderState.Online
                                && svc.Readers.StationStatus(s4.Id).State == ReaderState.Online
                                && svc.Readers.AssignmentStatus.State == ReaderState.Online));
            check("each reader's MAC is recorded next to its address",
                WaitUntil(() => svc.Stations.GetStation(s1.Id)?.ReaderMac == r1.Mac
                                && svc.Settings.Get(SettingsService.AssignmentReaderMac) == ra.Mac));
            check("a production reader found in write mode is switched back to read mode",
                WaitUntil(() => r4.Mode == "read"));
            Pump(300);
            check("taps made before the app first saw a reader are not imported",
                feed.Count == 0 && svc.Trace.GetScanLogs(50).All(l => l.TagId != "04OLD0001"));

            // ---- write a work order to a tag at the assigning station ----------
            check("a write is armed as a one-shot write of the work order",
                svc.TagWriter.StartAsync("WO-NET-1", TimeSpan.FromSeconds(30)).Result
                && ra is { Mode: "write", Data: "WO-NET-1", Once: true } && svc.TagWriter.IsWaiting
                && net.LastContentType == "application/json");

            // Production keeps scanning while a write is pending.
            svc.Tags.Assign("04BUSY0001", "WO-BUSY-1");
            r1.Tap("04BUSY0001");
            check("production readers keep recording while a tag write is pending",
                WaitUntil(() => svc.Trace.GetCurrent("WO-BUSY-1")?.StationCode == "ST01") && svc.TagWriter.IsWaiting);

            ra.FailNextWrite("authentication failed: Timeout in communication.");
            ra.Tap("04NEW0001");
            check("a failed write attempt is reported and the reader stays armed",
                WaitUntil(() => { lock (progress) return progress.Any(p => p.IsRetryable); })
                && svc.TagWriter.IsWaiting && ra.Mode == "write"
                && svc.Tags.GetByTag("04NEW0001") is null);

            ra.Tap("04NEW0001");
            check("presenting the tag again writes it and assigns the tag to the work order",
                WaitUntil(() => svc.Tags.GetByTag("04NEW0001")?.WorkOrder == "WO-NET-1")
                && svc.TagWriter.State == TagWriteState.Succeeded
                && ra.CardData("04NEW0001") == "WO-NET-1" && ra.Mode == "read");
            check("the tag write is logged",
                svc.Trace.GetScanLogs(50, ScanLogType.Info)
                   .Any(l => l.RawData == "WO-NET-1" && l.StationCode == AppServices.AssignmentStationCode));

            ra.Tap("04NEW0001");
            check("a tap at the assigning station in read mode is shown, not tracked",
                WaitUntil(() => seen is { Uid: "04NEW0001", Data: "WO-NET-1" })
                && svc.Trace.GetCurrent("WO-NET-1") is null);

            r1.Tap("04NEW0001");
            check("the written tag is tracked at a production station",
                WaitUntil(() => svc.Trace.GetCurrent("WO-NET-1")?.StationCode == "ST01"));

            r4.Tap("04NEW0001");
            check("the final station's reader completes the work order",
                WaitUntil(() => svc.Trace.GetCurrent("WO-NET-1")?.Status == TraceStatus.Completed));

            // ---- timeout and cancel put the reader back in read mode -----------
            svc.TagWriter.StartAsync("WO-NET-2", TimeSpan.FromSeconds(30)).Wait();
            svc.TagWriter.CheckTimeout(DateTime.Now.AddMinutes(1));
            check("a write nobody presents a tag for times out and disarms the reader",
                WaitUntil(() => svc.TagWriter.State == TagWriteState.TimedOut && ra.Mode == "read"));

            svc.TagWriter.StartAsync("WO-NET-2", TimeSpan.FromSeconds(30)).Wait();
            svc.TagWriter.CancelAsync().Wait();
            check("cancelling a write disarms the reader",
                svc.TagWriter.State == TagWriteState.Cancelled && ra.Mode == "read");

            // A restart of the assigning reader drops the armed write: it is armed again.
            svc.TagWriter.StartAsync("WO-NET-3", TimeSpan.FromSeconds(30)).Wait();
            ra.Reboot();
            check("an assigning reader that restarts is armed again",
                WaitUntil(() => ra is { Mode: "write", Data: "WO-NET-3" }) && svc.TagWriter.IsWaiting);
            svc.TagWriter.CancelAsync().Wait();

            // ---- unassigned tag ------------------------------------------------
            r1.Tap("04UNKNOWN1");
            check("an unassigned tag at a production reader is reported as an error",
                WaitUntil(() => { lock (feed) return feed.Any(f => f is { TagId: "04UNKNOWN1", Outcome: ScanOutcome.Error }); }));

            // ---- outage: taps made while the PC cannot reach the reader --------
            svc.Tags.Assign("04OUT0001", "WO-OUT-1");
            r1.Offline = true;
            check("an unreachable reader is shown offline",
                WaitUntil(() => svc.Readers.StationStatus(s1.Id).State == ReaderState.Offline));
            r1.Tap("04OUT0001");
            r1.Offline = false;
            check("taps made while a reader was unreachable are recorded when it is back",
                WaitUntil(() => svc.Trace.GetCurrent("WO-OUT-1")?.StationCode == "ST01")
                && svc.Readers.StationStatus(s1.Id).State == ReaderState.Online);
            check("the outage is logged",
                svc.Trace.GetScanLogs(200).Any(l => l.StationCode == "ST01" && l.Message.StartsWith("Reader offline")));

            // ---- the reader restarts: its event ids start again from 1 ----------
            svc.Tags.Assign("04RST0001", "WO-RST-1");
            r1.Reboot();
            r1.Tap("04RST0001");
            check("a tap right after a reader restart is recorded",
                WaitUntil(() => svc.Trace.GetCurrent("WO-RST-1")?.StationCode == "ST01"));

            // ---- readers whose firmware reports no MAC (00:00:00:00:00:00) -------
            var z2 = net.Add("10.0.0.12", "00:00:00:00:00:00", "RFID-RW-0000");
            var z3 = net.Add("10.0.0.13", "00:00:00:00:00:00", "RFID-RW-0000");
            var s2 = svc.Stations.GetStationByCode("ST02")!;
            var s3 = svc.Stations.GetStationByCode("ST03")!;
            s2.ReaderIp = "10.0.0.12";
            s3.ReaderIp = "10.0.0.13";
            svc.Stations.UpdateStation(s2);
            svc.Stations.UpdateStation(s3);
            svc.Tags.Assign("04ZM0002", "WO-ZM-2");
            svc.Tags.Assign("04ZM0003", "WO-ZM-3");
            check("readers that report no MAC come online",
                WaitUntil(() => svc.Readers.StationStatus(s2.Id).State == ReaderState.Online
                                && svc.Readers.StationStatus(s3.Id).State == ReaderState.Online));
            z2.Tap("04ZM0002");
            z3.Tap("04ZM0003");
            check("taps on two readers without a MAC are each recorded at their own station",
                WaitUntil(() => svc.Trace.GetCurrent("WO-ZM-2")?.StationCode == "ST02"
                                && svc.Trace.GetCurrent("WO-ZM-3")?.StationCode == "ST03"));
            check("a missing MAC is logged once per reader and never taken for a swapped reader",
                svc.Trace.GetScanLogs(500).Count(l => l.Message.StartsWith("Reader reports no usable MAC")) == 2
                && !svc.Trace.GetScanLogs(500, ScanLogType.Error)
                    .Any(l => l.Message.Contains("10.0.0.12") || l.Message.Contains("10.0.0.13")));

            // ---- the app restarts: taps made while it was closed are caught up --
            svc.Dispose();
            svc = null!;

            svc = new AppServices(config, useSimulatedScanner: true, readerHandler: net);
            svc.Tags.Assign("04CLOSED01", "WO-CLOSED-1");
            r1.Tap("04CLOSED01");
            svc.Tags.Assign("04ZM2C", "WO-ZM-2C");
            svc.Tags.Assign("04ZM3C", "WO-ZM-3C");
            z2.Tap("04ZM2C");
            z3.Tap("04ZM3C");
            var before = svc.Trace.GetHistory("WO-RST-1").Count;
            svc.Readers.Start();
            check("taps made while the app was closed are recorded on the next start",
                WaitUntil(() => svc.Trace.GetCurrent("WO-CLOSED-1")?.StationCode == "ST01"));
            Pump(300);
            check("taps recorded before the restart are not recorded twice",
                svc.Trace.GetHistory("WO-RST-1").Count == before
                && svc.Trace.GetScanLogs(500).Count(l => l.TagId == "04UNKNOWN1") == 1);
            check("readers without a MAC catch up by their address, each exactly once",
                WaitUntil(() => svc.Trace.GetCurrent("WO-ZM-2C")?.StationCode == "ST02"
                                && svc.Trace.GetCurrent("WO-ZM-3C")?.StationCode == "ST03")
                && svc.Trace.GetScanLogs(500).Count(l => l.TagId == "04ZM0002") == 1
                && svc.Trace.GetScanLogs(500).Count(l => l.TagId == "04ZM0003") == 1);

            // ---- another unit answers at a station's address --------------------
            var replacement = new FakeRfidReader("AA:00:00:00:00:99", "RFID-RW-0099");
            net.Put("10.0.0.14", replacement);
            check("a different reader at a known address is detected and logged",
                WaitUntil(() => svc.Stations.GetStation(s4.Id)?.ReaderMac == replacement.Mac)
                && WaitUntil(() => svc.Trace.GetScanLogs(200, ScanLogType.Error)
                    .Any(l => l.Message.Contains("A different reader answers at 10.0.0.14"))));

            // ---- configuration changes take effect without a restart ------------
            s4 = svc.Stations.GetStation(s4.Id)!;
            s4.ReaderIp = "";
            svc.Stations.UpdateStation(s4);
            check("removing a station's reader address stops polling it",
                WaitUntil(() => svc.Readers.StationStatus(s4.Id).State == ReaderState.NotConfigured));
        }
        finally
        {
            svc?.Dispose();
        }
    }

    /// <summary>Waits for a condition while letting queued reader events run on this thread.</summary>
    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < until)
        {
            Pump(0);
            if (condition())
                return true;
            Thread.Sleep(10);
        }

        Pump(0);
        return condition();
    }

    /// <summary>Runs the work queued on this thread's dispatcher, for at least <paramref name="ms"/>.</summary>
    private static void Pump(int ms)
    {
        var until = Environment.TickCount64 + ms;
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(_ =>
                {
                    frame.Continue = false;
                    return null;
                }), null);
            Dispatcher.PushFrame(frame);
            if (ms > 0)
                Thread.Sleep(10);
        } while (Environment.TickCount64 < until);
    }
}
