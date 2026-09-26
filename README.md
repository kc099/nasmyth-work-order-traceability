# Nasmyth Asia — Work Order Traceability System

C# / WPF (.NET 8) desktop application that implements the Work Order Traceability
System described in *Nasmyth_Barcode_Traceability_Project_Design_v3*.

It reads barcodes from **multiple USB scanners**, identifies the **station** each
scan came from, and answers the core question: **which barcode is at which station
right now, and what is its history**. Everything is stored in a local **SQLite3**
database. A dedicated **Reports & Analytics** screen provides KPIs, charts and
**Excel / PDF** export.

There is **no fixed routing** - a unit may pass through stations in any order; only
the **final station** is fixed (its scan marks the unit *Completed*). The `routes` /
`route_stations` tables and `RouteService` are kept for spec completeness but are not
wired into the scan flow.

---

## Requirements

| | |
|---|---|
| OS | Windows 10 / 11 (x64) |
| SDK to build | .NET SDK 8.0 or later (repo built and tested with SDK 10) |
| To run only | .NET Desktop Runtime 8.0 |

## Build & run

```powershell
# from the repo root
dotnet build NasmythTraceability.sln -c Release

# run
dotnet run --project src/NasmythTraceability -c Release
```

or launch `src/NasmythTraceability/bin/Release/net8.0-windows/NasmythTraceability.exe`.

### Command-line switches

| Switch | Purpose |
|---|---|
| *(none)* | normal application |
| `--selftest` | headless check: builds a throwaway DB, drives scans through the pipeline, exercises the Excel/PDF exporters and DB backup, prints `PASS/FAIL`, exits with a code |
| `--seed-demo [units] [days] [--append]` | fills the database with a realistic sample dataset (default **40 units / 7 days**): demo scanner mappings, units passing through stations in varied order, a mix of completed / in-progress units, a few NG reads and duplicate / error log rows. Replaces existing scan data unless `--append` is given |
| `--wipe-data` | resets to a fresh-install state: clears all scan history, current positions, logs and scanner mappings. Stations and settings are kept |
| `--smoke` | opens the main window, verifies XAML/VM wiring, closes after ~1 s |
| `--shot <file.png>` | opens the main window and saves a screenshot of it (add `--page <name>` and `--tab <n>` to choose what is shown) |
| `--logopng <file.png>` | renders just the header band with the logo |
| `--calpreview <file.png>` | renders a themed `Calendar` (DatePicker popup styling check) |

```powershell
dotnet run --project src/NasmythTraceability -- --selftest
```

## First run

* The database is created at
  `%ProgramData%\Nasmyth\Traceability\traceability.db`
  (override with `databasePath` in `config.json` next to the exe).
* Seeded data: stations **ST01–ST04**, with **ST04** fixed as the final station.
* No scanners are mapped yet — open **Settings ▸ USB Scanners**, press
  **Detection**, trigger a scanner, pick a station, **Map to station**.
* With no hardware you can still use **Dashboard ▸ Simulate scan** (pick a
  station, type a barcode, Enter).
* To explore with data, run `NasmythTraceability.exe --seed-demo` once, then
  start the app — the Dashboard, Current Information and Reports will be populated.

---

## How scanning works

USB "keyboard-wedge" scanners are read through the Win32 **Raw Input** API
(`RawInputScannerService`). Every keystroke is attributed to the exact HID
device, so several scanners on one PC are told apart and mapped to different
stations. A read ends on **Enter** or **Tab**.

**Only devices mapped in Settings ▸ USB Scanners are read.** The PC keyboard is
never mapped, so ordinary typing (in this app or any other) is ignored. The
"Start detection" button in that tab temporarily reads every keyboard device so a
new scanner can be identified; it stops on mapping, on leaving Settings, or on exit.

`ScanCoordinator` is the pipeline for every read:

1. resolve the **station** from the scanner-to-station mapping;
2. reject empty / too-short barcodes → `NG` + `scan_logs(Rejected)`;
3. drop repeats inside the duplicate window → `scan_logs(Duplicate)`;
4. write `trace_history` + upsert `current_trace` (`TraceService`) — every valid,
   non-duplicate read is `OK`; a scan at the fixed final station marks the unit
   *Completed*. No route/sequence rules are applied.
5. raise `ScanProcessed` → the dashboard and Current Positions update live.

---

## Database tables (design §5)

| Table | Purpose |
|---|---|
| `stations` | production stations + the fixed final station |
| `station_devices` | USB scanner → station mapping (Raw Input device key) |
| `routes` | available barcode routes |
| `route_stations` | stations and sequence per route |
| `trace_history` | complete barcode station history (OK / NG) |
| `current_trace` | latest / current station per barcode (+ status) |
| `scan_logs` | raw, duplicate, rejected and error scans |
| `settings` | key/value store behind the Settings screen |

Schema and first-run seed: [`Data/SchemaSql.cs`](src/NasmythTraceability/Data/SchemaSql.cs),
[`Data/Seed.cs`](src/NasmythTraceability/Data/Seed.cs).

## C# modules (design §6)

| Module | File |
|---|---|
| BarcodeScannerService | `Services/Scanning/RawInputScannerService.cs` (+ `SimulatedScannerService`) |
| StationService | `Services/StationService.cs` |
| TraceService | `Services/TraceService.cs` |
| RouteService | `Services/RouteService.cs` |
| ReportService | `Services/ReportService.cs` |
| DatabaseService | `Data/DatabaseService.cs` |
| *(glue)* ScanCoordinator | `Services/ScanCoordinator.cs` |

## Screens

| Nav item | View | Notes |
|---|---|---|
| Dashboard | `Views/DashboardView.xaml` | station tiles, live scan log, counters, barcode search, current info, history, simulate-scan |
| Current Information | `Views/CurrentInformationView.xaml` | **Current Positions** table (every barcode + its current station / status / last scan) with station filter + search; click a row for that unit's full history |
| Reports & Analytics | `Views/ReportsView.xaml` | date filter, KPIs, *Scans by Station*, *OK vs NG* donut, *Station-wise Trend*, Recent Scans, **Export to Excel / PDF**; re-runs its query each time the page opens |
| Settings | `Views/SettingsView.xaml` | General, Stations (add/edit/**set final**/delete-with-cascade), USB Scanners, Database (backup/restore), Appearance, Logs (filter / delete / clear / CSV) |

Station edits (add / rename / enable / set-final / delete) raise `StationService.Changed`;
the dashboard tiles and the manual-scan station list rebuild automatically. Final status is
the `is_final` flag (shown in the Final column and the dashboard badge) - never part of a
station's name.

All traceability clean-up lives on the **Logs** tab: "Delete selected" removes a log row and,
for `Raw`/`Rejected` rows, its matching `trace_history` row; "Clear all" with no filter wipes
history + current positions too. `TraceService.Changed` fires on any such delete/clear, so the
**Dashboard**, **Current Information** and **Reports & Analytics** views refresh immediately.

Charts are lightweight, dependency-free custom controls
(`Controls/BarChartControl`, `DonutChartControl`, `LineChartControl`).

### Branding / logo

The header shows `Controls/BrandLogo`. It renders a **vector Nasmyth mark** by
default; to use the exact company artwork instead, drop the file at
`src/NasmythTraceability/Assets/nasmyth-logo.png` (or `.jpg`) and rebuild — it is
picked up automatically, no code change. Preview the header band with:

```powershell
dotnet run --project src/NasmythTraceability -- --logopng header.png
```

## Reports & export (design §3)

`ReportService` produces the KPIs (Total / OK / NG / Unique), *Scans by Station*,
*OK vs NG*, the time trend (hourly for ≤ 2 days, else daily) and the recent-scan
list. Export (`Export/ExcelExporter.cs`, `Export/PdfExporter.cs`):

* **Excel** — `ClosedXML`, multi-sheet workbook (Summary, Scans by Station,
  Trend, Scans).
* **PDF** — `PDFsharp`, A4, company header + KPI band + station table +
  paginated scan detail.

## Third-party packages

| Package | Use | Licence |
|---|---|---|
| `CommunityToolkit.Mvvm` | MVVM (`ObservableObject`, `RelayCommand`) | MIT |
| `Microsoft.Data.Sqlite` | SQLite3 access | MIT |
| `ClosedXML` | `.xlsx` export | MIT |
| `PdfSharp` | `.pdf` export | MIT |

## Project layout

```
src/NasmythTraceability/
  App.xaml(.cs)            composition root, --selftest / --smoke
  config.json              bootstrap config (db path, names)
  Themes/                  dark-blue theme + control styles
  Models/                  POCOs + enums
  Data/                    DatabaseService, schema, seed, settings, config
  Services/                Station/Route/Trace/Report services + ScanCoordinator
  Services/Scanning/       Raw Input + simulated scanner
  Export/                  ExcelExporter, PdfExporter
  ViewModels/              Main / Dashboard / Reports / Settings
  Views/                   MainWindow + the five screens
  Controls/                bar / donut / line charts
  Converters/              value converters
  Helpers/                 AppServices, SelfTest
```
