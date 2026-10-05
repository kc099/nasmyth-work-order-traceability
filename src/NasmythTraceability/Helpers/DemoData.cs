using NasmythTraceability.Models;

namespace NasmythTraceability.Helpers;

/// <summary>
/// Generates a realistic sample dataset for testing: units that pass through stations in varied order over the last N days, with a mix of
/// completed / in-progress units, occasional NG reads and duplicate / error log rows.
/// </summary>
public static class DemoData
{
    public sealed record Summary(
        int Units, int Events, int Logs, int Completed, int InProgress, int Ng,
        DateTime From, DateTime To);

    public static Summary Generate(AppServices svc, int units = 40, int days = 7, bool reset = true)
    {
        units = Math.Clamp(units, 1, 5000);
        days = Math.Clamp(days, 1, 120);

        var rng = new Random(20260909); // fixed seed -> repeatable dataset

        if (reset)
            svc.Trace.PurgeAll();

        var stations = svc.Stations.GetStations()
                          .Where(s => s.IsEnabled)
                          .OrderBy(s => s.Sequence).ToList();
        if (stations.Count == 0)
            return new Summary(0, 0, 0, 0, 0, 0, DateTime.Now, DateTime.Now);

        var final = stations.FirstOrDefault(s => s.IsFinal) ?? stations[^1];
        var nonFinal = stations.Where(s => s.Id != final.Id).ToList();
        if (nonFinal.Count == 0)
            nonFinal = stations;

        // A made-up reader id per station for the sample scans. The readers are not assigned to
        // the stations, so real readers can still be.
        var deviceKeys = new Dictionary<int, string>();
        foreach (var s in stations)
            deviceKeys[s.Id] = $@"\\?\HID#VID_05E0&PID_1200#DEMO-{s.Code}#{{884b96c3-56ef-11d1-bc8c-00a0c91405dd}}";

        var now = DateTime.Now;
        var from = now.AddDays(-days);
        var events = 0;
        var logs = 0;
        var completed = 0;
        var inProgress = 0;
        var ngTotal = 0;

        for (var u = 1; u <= units; u++)
        {
            var barcode = $"NA-{now:yyMM}-{u:00000}";

            // start somewhere in the window, but not in the last 15 min
            var t = from.AddSeconds(rng.NextDouble() * Math.Max(1, (now.AddMinutes(-15) - from).TotalSeconds));

            var visitCount = rng.Next(1, nonFinal.Count + 1);
            var path = nonFinal.OrderBy(_ => rng.Next()).Take(visitCount).ToList();

            var reachesFinal = rng.NextDouble() < 0.72;
            var steps = new List<Station>(path);
            if (reachesFinal)
                steps.Add(final);

            var unitHadNg = false;
            foreach (var st in steps)
            {
                var isNg = !st.IsFinal && rng.NextDouble() < 0.05;
                var result = isNg ? ScanResult.NG : ScanResult.OK;
                var message = isNg ? "Re-scan required" : st.IsFinal ? "Reached final station" : "";

                svc.Trace.Record(barcode, st, result, message, null, deviceKeys[st.Id], st.IsFinal, t);
                events++;
                if (isNg) { ngTotal++; unitHadNg = true; }

                svc.Trace.LogScan(barcode, st.Id, st.Code, deviceKeys[st.Id], $"Demo scanner {st.Code}",
                    isNg ? ScanLogType.Rejected : ScanLogType.Raw,
                    isNg ? "Re-scan required" : "Accepted", t,
                    isNg ? ScanOutcome.Rejected : ScanOutcome.Accepted);
                logs++;

                if (rng.NextDouble() < 0.07)
                {
                    svc.Trace.LogScan(barcode, st.Id, st.Code, deviceKeys[st.Id], $"Demo scanner {st.Code}",
                        ScanLogType.Duplicate, $"Already at {st.Code}", t.AddSeconds(2), ScanOutcome.Duplicate);
                    logs++;
                }

                t = t.AddMinutes(rng.Next(2, 26)).AddSeconds(rng.Next(0, 60));
            }

            if (reachesFinal && !unitHadNg) completed++;
            else inProgress++;
        }

        // a handful of taps of tags that were never assigned, spread across the window
        var errorCount = Math.Max(3, units / 15);
        for (var i = 0; i < errorCount; i++)
        {
            var at = from.AddSeconds(rng.NextDouble() * (now - from).TotalSeconds);
            var st = stations[rng.Next(stations.Count)];
            svc.Trace.LogScan("", st.Id, st.Code, deviceKeys[st.Id], $"Demo scanner {st.Code}",
                ScanLogType.Error, "Tag is not assigned to a work order", at, ScanOutcome.Error, RandomTag(rng));
            logs++;
        }

        svc.Trace.RebuildAllCurrentTraces();

        return new Summary(units, events, logs, completed, inProgress, ngTotal, from, now);
    }

    private static string RandomTag(Random rng)
    {
        const string chars = "0123456789ABCDEF";
        const int len = 8;
        return string.Concat(Enumerable.Range(0, len).Select(_ => chars[rng.Next(chars.Length)]));
    }
}
