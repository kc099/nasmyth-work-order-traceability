# Work Order Traceability System — Full Project Documentation

**Company:** Nasmyth Asia (IN) Pvt Ltd.
**Application version:** 1.0.0
**Document date:** 26 September 2026
**Based on:** *Nasmyth Barcode Traceability Project Design v3*, updated for work orders

---

## Contents

1. [At a glance](#1-at-a-glance)
2. [The problem it solves](#2-the-problem-it-solves)
3. [Key features](#3-key-features)
4. [How it works](#4-how-it-works)
5. [A tour of the screens](#5-a-tour-of-the-screens)
6. [Built-in rules and safeguards](#6-built-in-rules-and-safeguards)
7. [Day-to-day use](#7-day-to-day-use)
8. [Installation and first run](#8-installation-and-first-run)
9. [Reports and exports](#9-reports-and-exports)
10. [Data, backups and security](#10-data-backups-and-security)
11. [Recent changes](#11-recent-changes)
12. [Under the hood (technical reference)](#12-under-the-hood-technical-reference)
13. [Troubleshooting and FAQ](#13-troubleshooting-and-faq)
14. [Limitations and suggested next steps](#14-limitations-and-suggested-next-steps)
15. [Glossary](#15-glossary)

---

## 1. At a glance

The Work Order Traceability System is a Windows desktop application. At any moment it tells anyone **which work order is at which station right now, and every station it has passed through**.

Each workstation on the shop floor has a handheld scanner. When an operator scans a work order's label, the system records the station, the time and the result, and the screens update straight away. Supervisors can look up any work order, see where every job currently sits, and export reports to Excel or PDF.

| | |
|---|---|
| **Who uses it** | Operators (scan work orders), supervisors and managers (monitor progress, look up jobs, run reports), an administrator (sets up stations and keeps the data safe) |
| **Where it runs** | One Windows 10/11 PC on the shop floor with USB scanners plugged in. No internet or server is needed |
| **What it keeps** | A complete, time-stamped history of every scan, stored on that PC |
| **What it produces** | Live monitoring screens, a current-position list, charts, and Excel/PDF reports |

---

## 2. The problem it solves

Without a tracking system, finding out where a job is means walking the floor, asking people, or checking paper sheets. That is slow, and records get lost or written wrong.

This system replaces that with a single scan at each station:

- **"Where is work order X right now?"** Type it into the search box and see its current station and status straight away.
- **"Which stations has it been through, and when?"** Its full history is listed with exact times.
- **"What is sitting at each station today?"** The Current Information screen lists every work order and its location.
- **"How busy was each station this week?"** Reports show scan counts per station and the trend over time.
- **"Is it finished?"** When a work order is scanned at the final station, it is automatically marked **Completed**.

The record is written by the scanner, not by hand, so it is accurate, time-stamped and can be checked later, for example during a customer audit or a quality investigation.

---

## 3. Key features

| Feature | What it means for you |
|---|---|
| **Multiple readers on one PC** | Each station has its own RFID card reader (or barcode scanner). The system knows which reader (and so which station) every scan came from |
| **Automatic reader detection** | A new reader is recognised and linked to a station the first time a card is tapped on it. No setup step is needed |
| **Live station monitoring** | Coloured tiles show every station's status, its number of scans and the time of its last scan, updating live |
| **Work order search** | Find any work order by full or partial number in seconds |
| **Current positions list** | One list showing where every work order is right now, filterable by station |
| **Full history** | Every station visit for every work order, with date, time and result |
| **Automatic completion** | A scan at the final station marks the job Completed. No extra step is needed |
| **Mistake protection** | Too-short codes are rejected, and accidental double scans are ignored |
| **Reports and charts** | Scans per station and activity trends for any date range |
| **Excel and PDF export** | One click creates a report file ready to email or print |
| **Password-protected settings** | Only the administrator can change stations or data |
| **Backup and restore** | Save a copy of all data, and put it back if ever needed |
| **Works offline** | Everything runs on one PC. No internet connection is required |

---

## 4. How it works

Every scan follows the same short path, and the whole thing takes less than a second. The operator only has to point the scanner and pull the trigger. The system does the rest.

```
 ┌────────────────┐   ┌──────────────────┐   ┌─────────────────┐  yes  ┌─────────────────────┐
 │ Operator scans │──▶│ System identifies │──▶│ Valid and not a │──────▶│ Saved to history    │
 │ work order     │   │ the station       │   │ repeat?         │       │ with date and time  │
 └────────────────┘   └──────────────────┘   └────────┬────────┘       └──────────┬──────────┘
                                                      │ no                        │
                                                      ▼                           ▼
                                           ┌─────────────────────┐   ┌──────────────────────────┐
                                           │ Too short: NG        │   │ Status: In Progress,     │
                                           │ Repeat: ignored      │   │ or Completed (final)     │
                                           │ (both kept in Logs)  │   └────────────┬─────────────┘
                                           └─────────────────────┘                ▼
                                                                    ┌──────────────────────────┐
                                                                    │ Screens and reports       │
                                                                    │ update instantly          │
                                                                    └──────────────────────────┘
```

Step by step:

1. **The operator taps the work order's RFID card on the reader** (or scans its label). Each workstation has its own USB reader.
2. **The system knows which station it came from.** Every reader is linked to one station, so even with several readers on one PC, each scan lands at the right place. A reader that has never been used before is linked automatically on its first card tap (see [Readers](#54-settings-password-protected)). Typing on an ordinary keyboard is ignored.
3. **Two quick checks run.**
   - If the code is too short to be real (fewer than 4 characters by default), it is recorded as **NG** (not good), the work order is marked **Rejected**, and the event is noted in the logs.
   - If the same work order was scanned at the same station a moment ago (within 5 seconds by default), for example from a double trigger pull, the repeat is ignored so it isn't counted twice. It is still noted in the logs.
4. **The visit is saved.** The system stores the work order, station, date and time, and updates that work order's current position.
5. **The status is set.** While a work order is moving between stations it shows **In Progress**. When it is scanned at the station marked as *final*, it becomes **Completed**.
6. **Every screen refreshes.** The station tiles, live log, current-position list and reports all show the new scan straight away.

**There is no fixed order of stations.** A work order may visit stations in any sequence. Only the final station is fixed, because that is the one that marks a job as done.

**Example.** Work order *NA-2609-00042* is scanned at ST01 at 09:05, then at ST03 at 10:20, then at the final station at 11:45:

| Time | Station | Result | Status after this scan |
|---|---|---|---|
| 09:05 | ST01 | OK | In Progress |
| 10:20 | ST03 | OK | In Progress |
| 11:45 | Final station | OK | **Completed** |

Anyone searching for it at 10:30 sees "ST03, In Progress". After 11:45 they see "Completed", with all three visits listed in its history.

---

## 5. A tour of the screens

The application has four screens, chosen from the **MENU** on the left. The header across the top always shows the Nasmyth logo, the company name, "Work Order Traceability System", and the current date and time. The version number is shown at the bottom of the menu.

### 5.1 Dashboard (Live Station Monitoring)

This is the everyday control-room view, the screen to leave running on the shop-floor monitor.

| Part of the screen | What it shows / does |
|---|---|
| **Station tiles** (top row) | One tile per station: station code and name, **ONLINE** (green) or **OFFLINE** (switched off in Settings), number of scans since the app was opened, and the time of its last scan. The final station has a **FINAL** badge |
| **Live Scan Log** | Scans as they happen: time, station, work order, result (OK/NG coloured badge) and a short message. **Clear** empties the on-screen list only. Saved records are not touched |
| **Manual / Test Scan** | Pick a station, type a work order, press **Enter** or **Simulate**. It works exactly like a real scan. Use it for testing, or when a label won't read |
| **Work Order Search** | Type a full or partial work order number and press **Search** (or Enter) |
| **Current Information** | For the work order found or just scanned: work order, current station, status and last scan time. The **View Reports & Analytics** button jumps to the reports |
| **History** | Every station that work order has visited, with time, station and result |

### 5.2 Current Information

The answer to "what is where right now?" for the whole factory.

- **Current Positions** lists every work order the system knows about, with these columns:
  - **Work Order**, the code
  - **Station**, where it is now
  - **Status**: In Progress, Completed or Rejected (coloured badge)
  - **Result**, the result of its last scan (OK/NG)
  - **Scans**, how many times it has been scanned in total
  - **Last Scan**, date and time
- Narrow the list with the **Station** drop-down, or type part of a work order number in the search box.
- Click any row to see that work order's details and full history on the right (**Selected work order**).

### 5.3 Reports & Analytics

For supervisors and managers who want the bigger picture over a period of time.

- **Date range.** Choose *From* and *To* dates and press **Apply**, or use the quick buttons **Today**, **7 days** or **30 days**. The page opens on the last 7 days by default.
- **Scans by Station.** A bar chart of how many scans each station handled in that period. It shows where the workload is.
- **Station-wise Scan Trend.** A line chart of scan activity over time. It shows busy and quiet days.
- **Export to Excel / Export to PDF.** Saves a report for the chosen dates. See [section 9](#9-reports-and-exports).

The page refreshes automatically every time it is opened, and whenever new scans arrive.

### 5.4 Settings (password protected)

Anyone can *look* at Settings, but making any change needs the administrator password.

- **Locked (normal state):** the top right shows "Locked – enter password to edit". Type the password and press **Unlock** (or Enter). Edit buttons are greyed out while locked.
- **Unlocked:** the top right shows "Unlocked", a **New password** box with a **Change password** button, and a **Lock** button.
- Settings **lock again automatically** as soon as you leave the page. Unsaved edits in the Stations table are thrown away.

It has four tabs:

**Stations**

| Column / button | Purpose |
|---|---|
| Code, Name, Sequence | Station identifier (e.g. ST01), display name, and display order |
| Enabled | Switches the station on or off (shows ONLINE/OFFLINE on the Dashboard) |
| Final | Shows which station is the final one (only one at a time) |
| **Add station** | Adds a new station row |
| **Save changes** | Saves edits made in the table |
| **Set as final** | Makes the selected station the final station |
| **Delete** | Deletes the selected station. If it already has scans, you are asked to confirm, because its history is deleted too |

**Readers**

Lists the RFID card readers (and barcode scanners) linked to stations.

A new reader needs no setup. The first time a card is tapped on it, the system links it to the first station that has no reader yet (or to the first station, if every station already has one), records that tap, and notes "New reader detected" in the logs. If it landed on the wrong station, move it here.

| Item | Purpose |
|---|---|
| **Link a new reader automatically…** | Tick box. When off, readers that are not in the list are ignored (needs unlock) |
| Station, Reader, Enabled, Linked on, Device ID | Which station each reader belongs to, its name, whether it is switched on, when it was linked, and its Windows device ID |
| **Move** | Puts the selected reader on the station chosen in *Move to station* (needs unlock) |
| **Enable / disable** | Switches the selected reader on or off. A disabled reader's card taps are ignored (needs unlock) |
| **Remove** | Removes the selected reader. With automatic linking on, it is linked again on its next card tap (needs unlock) |

The system recognises a reader in two ways: by model (the JT308 125 kHz USB card reader is built in), or by speed, because a reader sends a whole card number far faster than anyone can type. Keep each reader plugged into the same USB port; if it is moved to another port, Windows may treat it as a new reader.

**Database**

| Item | Purpose |
|---|---|
| Database file | Shows where the data file is stored |
| **Backup…** | Saves a copy of all data to a file you choose (works while locked) |
| **Restore…** | Replaces **all** current data with a chosen backup (needs unlock, asks to confirm) |
| **Open folder** | Opens the folder containing the database file |

**Logs**

A technical record of every scan event, including those set aside.

| Control | Purpose |
|---|---|
| **Filter** | Show All, or only Raw, Duplicate, Rejected, Error or Info entries |
| **Refresh** | Reloads the list |
| **Delete selected** | Deletes one entry and, if it was a recorded scan, that scan's record too (needs unlock) |
| **Clear all** | With a filter: deletes those log entries only. With "All": deletes all logs **and** all scan history and current positions. Stations and settings are kept (needs unlock, asks to confirm) |
| **Export CSV** | Saves the visible log list as a spreadsheet file |

---

## 6. Built-in rules and safeguards

The system checks every scan and protects its own data, so the records can be trusted without anyone watching over it.

| Situation | What the system does | Why it matters |
|---|---|---|
| Code too short (under 4 characters) | Saves it as **NG**, marks the work order **Rejected**, notes it in the logs | Catches misreads and partial scans |
| Same work order scanned twice at one station within 5 seconds | Ignores the repeat and logs it as a *Duplicate* | A double trigger pull doesn't inflate counts |
| Empty scan | Ignored and logged as an *Error* | Nothing meaningless enters the history |
| Typing on the PC keyboard | Ignored completely | Typing in other programs never creates false records |
| Card tapped on a reader that is not linked yet | Links the reader to a station, records the tap, notes it in the logs | A new or replacement reader works without any setup |
| Card tapped on a disabled reader | Ignored | A reader can be taken out of use without unplugging it |
| Reader types its card number into a box on screen | Those keystrokes are dropped | A tap is never recorded a second time through the manual scan box |
| Work order scanned at the **final** station | Marked **Completed** | "Done" is recorded automatically |
| Someone tries to change Settings | Asks for the administrator password, and locks again on leaving | Stops accidental or unauthorised changes |
| Deleting a station that has history | Warns and asks for confirmation | Prevents losing history by accident |
| Restoring a backup / clearing all logs | Warns that data will be replaced or deleted | Avoids wiping records by mistake |

The 4-character minimum and the 5-second duplicate window are stored settings and can be adjusted by a technician if needed.

---

## 7. Day-to-day use

For most people, using the system means scanning and looking. Nobody needs to type anything during normal work.

### Operators

1. When a work order arrives at your station, scan its label with your station's scanner.
2. Glance at the Dashboard. The scan appears at the top of the Live Scan Log with **OK**, and your station tile's count goes up.
3. If a label won't scan, use **Manual / Test Scan** on the Dashboard: choose your station, type the work order number, and press Enter.
4. At the final station, scanning the work order completes it. No other action is needed.

### Supervisors and managers

1. **Find one job:** type its number into *Work Order Search* on the Dashboard to see where it is and where it has been.
2. **See everything in progress:** open *Current Information* and filter by station if needed.
3. **Review a period:** open *Reports & Analytics*, pick the dates, and read the two charts.
4. **Share a report:** press *Export to Excel* or *Export to PDF*. The file opens in its folder, ready to email or print.

### Administrator

1. **Add or change stations** in *Settings › Stations* (unlock with the password first) and press *Save changes*.
2. **Choose the final station:** select it and press *Set as final*.
3. **Back up regularly:** *Settings › Database › Backup* saves a copy of all data. Keep copies somewhere other than this PC, such as a USB drive or network folder. A weekly backup is a sensible minimum.
4. **Change the password** after first use. The starting password is **admin**.
5. **Check the logs** if something looks wrong. Rejected, repeated and unreadable scans are all listed there.

---

## 8. Installation and first run

### What you need

| | |
|---|---|
| Computer | Windows 10 or 11 (64-bit) |
| Software to run | Microsoft .NET 8 Desktop Runtime (free from Microsoft) |
| Software to build from source | .NET SDK 8.0 or later |
| Readers | USB RFID card readers (e.g. JT308, 125 kHz) or barcode scanners that work as a "keyboard" (the most common type), set to send **Enter** after each read |
| Cards | Cards that match the reader. The JT308 reads 125 kHz EM4100 / TK4100 cards and key fobs only; 13.56 MHz cards (MIFARE, NFC, metro and bank cards) do not work |

### Starting the program

- Run **NasmythTraceability.exe** from the program folder, or
- Build from source (for IT staff), from the project folder:

```powershell
dotnet build NasmythTraceability.sln -c Release
dotnet run --project src/NasmythTraceability -c Release
```

### What happens the first time

- The database file is created automatically at
  `C:\ProgramData\Nasmyth\Traceability\traceability.db`.
- Four starting stations are created (**ST01–ST04**, with **ST04** as the final station). They can be renamed, added to or changed in Settings.
- The settings password is set to **admin**.
- To try the system without scanners, use **Manual / Test Scan** on the Dashboard.
- To see the screens filled with sample data for training, run the program once with `--seed-demo` (see [section 12](#12-under-the-hood-technical-reference)).

---

## 9. Reports and exports

Reports are created from **Reports & Analytics** for the selected date range.

| | Excel (.xlsx) | PDF |
|---|---|---|
| Best for | Further analysis, filtering, sharing numbers | Printing, emailing, archiving |
| Contents | Sheets: **Summary**, **Scans by Station**, **Trend**, **Scans** (every scan in the period) | Company header, summary figures, scans by station, and scan detail pages |

- Files are saved to **Documents › Nasmyth Traceability › Exports**.
- File names include the date range and time created, for example
  `Traceability_20260920_20260926_101530.xlsx`.
- After saving, the folder opens automatically with the new file selected.

---

## 10. Data, backups and security

- **Where data lives:** one database file on the shop-floor PC (`C:\ProgramData\Nasmyth\Traceability\traceability.db`). Nothing is sent over the internet.
- **What is stored:** stations, scanner-to-station links, every scan (history), the current position of each work order, logs, and settings.
- **Backups:** use *Settings › Database › Backup*. The file is named like `traceability_backup_20260926_101530.db`. Store copies away from the PC.
- **Restore:** replaces all current data with the backup's data, including the settings password saved in that backup.
- **Password:** one administrator password protects all changes in Settings. It is stored as a one-way scrambled code (SHA-256 hash), not as readable text. If it is forgotten, a technician can reset it in the database.
- **Deleting data:** only possible from Settings when unlocked, and always with a confirmation prompt.

---

## 11. Recent changes

The project began as the *Barcode Traceability System*. It has since been reshaped around **work orders** and simplified for everyday users. Each change was built and checked with the application's automatic self-test.

| Area | Change |
|---|---|
| Name | Renamed to **Work Order Traceability System** in the header, window title and reports. Existing installations pick up the new name automatically |
| Dashboard | "Barcode Search" is now **Work Order Search**. The Current Information label reads **Work Order**. The manual scan box now says "Type a work order" |
| Dashboard | Removed the Total Scans / OK / NG / OK rate counter line |
| Current Information | "Barcode" column and "Selected barcode" heading now read **Work Order** / **Selected work order** |
| Reports & Analytics | Removed the Total / OK / NG / Unique number tiles, the OK-vs-NG chart and the Recent Scans table. Two full-width charts remain |
| Settings | Removed the *General* tab (app name, company name, date format, font size, start-up options) and the *USB Scanners* tab |
| Settings | Added a **password lock**. Changes need the password, and it locks again on leaving |
| Side menu | Removed the "USB scanners (Raw Input)" text |
| RFID card readers | A new reader is detected and linked to a station automatically on its first card tap |
| Settings | Added the **Readers** tab: see linked readers, move one to another station, enable/disable or remove it |
| Dashboard / Logs / exports | The remaining "Barcode" column headings now read **Work Order** |
| Dashboard | A reader's keystrokes no longer land in the search or manual scan boxes |

---

## 12. Under the hood (technical reference)

### Technology

| Topic | Detail |
|---|---|
| Language / framework | C# on .NET 8, WPF desktop user interface, MVVM pattern (CommunityToolkit.Mvvm 8.4) |
| Database | SQLite (Microsoft.Data.Sqlite 8.0) |
| Excel export | ClosedXML 0.104 |
| PDF export | PdfSharp 6.1 |
| Reader input | Windows Raw Input API. Every keystroke is tied to the exact USB device, so several readers on one PC are told apart. A read ends on Enter or Tab |
| Reader auto-detection | An unlinked device is treated as a reader if its hardware id is in the `scanning.knownReaderIds` setting (default `VID_FFFF&PID_0035`, the JT308), or if it sends at least 4 characters plus Enter with no more than 50 ms between keys. Switched by the `scanning.autoDetectReaders` setting |
| Configuration | `config.json` next to the program: company name, and an optional `databasePath` to move the database |

### Database tables

| Table | Holds |
|---|---|
| `stations` | Stations, their order, on/off state, and which is final |
| `station_devices` | Which USB scanner belongs to which station |
| `trace_history` | Every station visit: work order, station, result, time |
| `current_trace` | Latest position and status for each work order |
| `scan_logs` | Raw, duplicate, rejected, error and info events |
| `settings` | Key/value settings, including the password hash |
| `routes`, `route_stations` | Kept from the original design, not used in scanning |

Note: the work order code is stored in columns named `barcode`. Only the screens were renamed.

### Main program parts

| Part | File | Job |
|---|---|---|
| Scanner reader | `Services/Scanning/RawInputScannerService.cs` | Reads the USB scanners |
| Scan coordinator | `Services/ScanCoordinator.cs` | Runs the checks and decides what happens to each scan |
| Trace service | `Services/TraceService.cs` | Saves history, current positions, logs and search |
| Station service | `Services/StationService.cs` | Stations and scanner links |
| Report service | `Services/ReportService.cs` | Report figures and chart data |
| Database service | `Data/DatabaseService.cs` | Database access, backup, restore |
| Settings service | `Data/SettingsService.cs` | Settings and password |
| Screens | `Views/*.xaml` + `ViewModels/*.cs` | Dashboard, Current Information, Reports, Settings |

### Command-line options

| Option | Purpose |
|---|---|
| *(none)* | Normal start |
| `--selftest` | Automatic check: builds a temporary database, runs test scans, reports, exports and backup, prints PASS or FAIL |
| `--seed-demo [units] [days] [--append]` | Fills the database with sample data (default 40 work orders over 7 days) for training or demonstration |
| `--wipe-data` | Resets to a clean state (clears history, positions, logs and scanner links; keeps stations and settings) |
| `--smoke` | Opens and closes the main window to check it starts |
| `--shot <file.png> [--page <name>] [--tab <n>]` | Saves a screenshot of the window |

---

## 13. Troubleshooting and FAQ

| Question / problem | Answer |
|---|---|
| **A scan doesn't appear on the Dashboard** | Check the reader is plugged in, listed and *Enabled* in *Settings › Readers*, and that its station is *Enabled*. Look in *Settings › Logs* for Error or Info entries |
| **The reader's light stays red and nothing happens** | The reader is not reading the card. The card is the wrong type for the reader (see [What you need](#what-you-need)). Test in Notepad: a working card makes the reader beep and type a number |
| **A new reader's scans show at the wrong station** | It was linked automatically to the first free station. Open *Settings › Readers*, select it, choose the right station and press *Move* |
| **A scan shows NG** | The code was shorter than 4 characters, which usually means a misread. Rescan the label, or use Manual / Test Scan |
| **I scanned twice but only one scan shows** | The repeat was within 5 seconds and was ignored on purpose. It appears in the logs as a Duplicate |
| **A work order never shows Completed** | It must be scanned at the station marked *Final* in Settings › Stations |
| **I can't edit anything in Settings** | Settings are locked. Enter the password at the top right and press Unlock |
| **I forgot the password** | Restore a backup whose password you know, or ask IT to reset the password value in the database |
| **Where are my exported reports?** | Documents › Nasmyth Traceability › Exports |
| **Can I use the normal keyboard to scan?** | No. The keyboard is deliberately ignored. Use Manual / Test Scan to enter a code by hand |
| **Does it need the internet?** | No, it runs entirely on the PC |
| **The Dashboard counts reset** | Tile counts are per session and restart when the app restarts. The saved history is not affected |

---

## 14. Limitations and suggested next steps

- **Card reader not yet tried with real cards.** Automatic detection and the Readers tab pass the automatic self-test, but have not been tried with a physical reader and a matching card. Check them on site: the first tap should link the reader, and the card number should not appear in the search or manual scan boxes.
- **Card number is the work order.** The number read from the card is recorded as the work order. There is no table that translates a card to a separate work order number.
- **Fixed display settings.** Company name, date format, font size and full-screen start-up can no longer be changed in the app. They keep their current values.
- **One PC only.** Data lives on one computer. Other offices can't view it live, and regular off-PC backups are essential. *Suggestion:* scheduled automatic backups, or a shared network database if several PCs are needed later.
- **Export totals.** The Excel/PDF exports still include the overall totals that were removed from the Reports screen.

---

## 15. Glossary

| Term | Meaning |
|---|---|
| **Work order** | A production job, identified by the code on its label |
| **Station** | A workstation on the shop floor where a work order is processed and scanned |
| **Final station** | The last station. A scan here marks the work order as Completed |
| **Scan** | Reading a work order's label with a scanner (or entering it by hand) |
| **OK / NG** | Result of a scan: OK is accepted, NG ("not good") is rejected |
| **In Progress / Completed / Rejected** | Work order status: still moving, finished at the final station, or last scan rejected |
| **Traceability** | Being able to show where an item has been, and when |
| **Duplicate** | The same work order scanned again at the same station within a few seconds, ignored so it isn't counted twice |
| **Log** | The technical record of every scan event, including the ones set aside |
| **Backup / Restore** | Saving a copy of all data, and later putting that copy back |
| **Database** | The single file on the PC where all records are kept |
| **Export** | Saving a report as an Excel or PDF file |
