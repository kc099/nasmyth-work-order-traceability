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

Each work order travels with an RFID tag, and each station on the shop floor has an RFID reader. The tag is linked to the work order once. After that, tapping the tag on a station's reader records the station and the time, and the screens update straight away. Supervisors can look up any work order, see where every job currently sits, and export reports to Excel or PDF.

| | |
|---|---|
| **Who uses it** | Operators (assign tags and tap them at the stations), supervisors and managers (monitor progress, look up jobs, run reports), an administrator (sets up stations and readers, corrects wrong scans, keeps the data safe) |
| **Where it runs** | One Windows 10/11 PC on the shop floor with the USB RFID readers plugged in. No internet or server is needed |
| **What it keeps** | A complete, time-stamped history of every scan, stored on that PC |
| **What it produces** | Live monitoring screens, a current-position list, charts, and Excel/PDF reports |

---

## 2. The problem it solves

Without a tracking system, finding out where a job is means walking the floor, asking people, or checking paper sheets. That is slow, and records get lost or written wrong.

This system replaces that with a single scan at each station:

- **"Where is work order X right now?"** Type it into the search box and see its current station and status straight away.
- **"Which stations has it been through, and when?"** Its full history is listed with exact times.
- **"What is sitting at each station today?"** The Scan Information screen lists every work order and its location.
- **"How busy was each station this week?"** Reports show scan counts per station for any date range.
- **"Is it finished?"** When a work order's tag is tapped at the final station, it is automatically marked **Completed**.

The record is written by the reader, not by hand, so it is accurate, time-stamped and can be checked later, for example during a customer audit or a quality investigation.

---

## 3. Key features

| Feature | What it means for you |
|---|---|
| **One reader per station** | Each station has its own RFID reader. The system knows which reader (and so which station) every tap came from |
| **Tag to work order assignment** | A tag is linked to a work order number once. After that the work order is tracked and searched by its own number |
| **Entry and exit times** | Each station visit is logged with its entry time, and its exit time is logged when the tag is tapped at the next station |
| **Sequence check** | The final station must come last. A tag tapped there first is recorded as NG so the mistake is visible |
| **Correcting mistakes** | An administrator can delete the last scan, or all scans, of a work order and have it scanned again |
| **Live station monitoring** | The Dashboard lists every scan as it happens, with its station, time and result |
| **Work order search** | Find any work order by full or partial number in seconds |
| **Current positions list** | One list showing where every work order is right now, filterable by station |
| **Full history** | Every station visit for every work order, with date, time and result |
| **Automatic completion** | A tap at the final station marks the job Completed. No extra step is needed |
| **Mistake protection** | Unassigned tags, repeat taps and taps on a finished work order are not recorded |
| **Reports and charts** | Scans per station for any date range |
| **Excel and PDF export** | One click creates a report file ready to email or print |
| **Password-protected settings** | Only the administrator can change stations or data |
| **Backup and restore** | Save a copy of all data, and put it back if ever needed |
| **Works offline** | Everything runs on one PC. No internet connection is required |

---

## 4. How it works

A work order travels with an RFID tag. There are three steps: the tag is linked to the work order once, then it is tapped at each station.

```
 ┌───────────────────┐   ┌────────────────────┐   ┌─────────────────────┐
 │ 1. ASSIGN         │──▶│ 2. FIRST STATION   │──▶│ 3. FINAL STATION    │
 │ Tap the tag,      │   │ Tap the tag on     │   │ Tap the tag on      │
 │ type the work     │   │ reader 1.          │   │ the final reader.   │
 │ order number.     │   │ Entry is logged.   │   │ Exit from station 1 │
 │ (Tag Assignment)  │   │ Status: In Progress│   │ and entry here are  │
 └───────────────────┘   └────────────────────┘   │ logged. Completed.  │
                                                  └─────────────────────┘
```

Step by step:

1. **Assign the tag.** On the *Tag Assignment* page, tap the tag on any reader. Its tag ID appears. Type the work order number and press **Assign**. From then on, wherever that tag is tapped, the system knows which work order it is.
2. **Tap at the first station.** Each station has its own USB reader, so the system knows which station the tap came from. The entry is saved with date and time, and the work order shows **In Progress** at that station.
3. **Tap at the final station.** The system saves the entry at the final station and, at the same moment, logs the **exit** from the station the work order came from. The work order becomes **Completed**.
4. **Every screen refreshes.** The live log, the work order list and the reports show each scan straight away.

**The right order is: first station, then the final station.** The final station is always last, because it is the one that marks a job as done. If there are more than two stations, the ones before the final station may be visited in any order.

**Example.** Tag *0012345678* is assigned to work order *WO-1001A*, tapped at ST01 at 09:05 and at the final station ST02 at 11:45:

| Station | Time in | Time out | Result | Status after this scan |
|---|---|---|---|---|
| ST01 | 09:05 | 11:45 | OK | In Progress |
| ST02 (final) | 11:45 | – | OK | **Completed** |

Anyone searching for *WO-1001A* at 10:30 sees "ST01, In Progress". After 11:45 they see "Completed", with both visits and the exit time from ST01 in its history.

**What happens when something is done wrong** is listed in [section 6](#6-built-in-rules-and-safeguards).

---

## 5. A tour of the screens

The application has five screens, chosen from the **MENU** on the left. The header across the top always shows the Nasmyth logo, the company name, "Work Order Traceability System", and the current date and time. The version number is shown at the bottom of the menu.

### 5.1 Dashboard (Live Station Monitoring)

This is the everyday control-room view, the screen to leave running on the shop-floor monitor.

| Part of the screen | What it shows / does |
|---|---|
| **Live Scan Log** | Scans as they happen: time, station, tag, work order, result (OK / NG / DUP / ERR coloured badge) and a short message. **Clear** empties the on-screen list only. Saved records are not touched |
| **Work Order Search** | Start typing part of a work order number or of a tag ID. After two characters a list drops down with up to ten matches, newest first, each showing its station and status. Pick one with the arrow keys and **Enter**, or click it. **Search** (or Enter with nothing highlighted) shows the most recent match. A work order that has a tag but has not been scanned yet is listed as "Not scanned yet" |
| **Scan Information** | For the work order found or just scanned: work order, current station, status and last scan time. The **View Reports & Analytics** button jumps to the reports |
| **History** | Every station that work order has visited, with time in, time out and result |

### 5.2 Tag Assignment

This is where an RFID tag is linked to a work order. **The page is locked until the administrator password is entered** at the top right (the same password as Settings). While it is locked, the readers keep recording station scans as usual. **Once unlocked, the readers are in assignment mode:** a tag tapped on any reader is read here and is *not* recorded as a station scan. Press **Lock**, or leave the page, to go back to tracking. The page also locks itself when it is left or times out.

| Part of the screen | What it shows / does |
|---|---|
| **Tag ID** | Filled in when a tag is tapped on any reader. It can also be typed. The line below says whether the tag is free or which work order it carries now |
| **Work order number** | Type the work order. Letters and digits, with `-` `_` `/` `.` allowed inside, 4 to 40 characters. It is stored in capital letters |
| **Assign** | Links the tag to the work order (Enter does the same). **Clear** empties both boxes |
| **Assigned Tags** | Every tag and its work order, with the work order's status, its current station and when the tag was assigned. Use **Search** to find a tag or work order |
| **Remove assignment** | Unlinks the selected tag. Scans already recorded for the work order are kept |

Rules for assigning:

- **One tag, one work order.** A tag carries one work order at a time, and a work order has one tag.
- **Tags are reused.** When a work order is completed, its tag can be assigned to the next work order.
- **Moving a tag that is still in use** (its work order is not completed) asks for confirmation first.
- **Giving a work order a different tag** asks for confirmation and replaces the old tag.
- **A completed work order cannot be given a tag again.** Delete its scans in *Scan Information* first if it really has to be run again.

### 5.3 Scan Information

The answer to "what is where right now?" for the whole factory.

- **Scanned Work Orders** lists every work order the system knows about, with these columns:
  - **Work Order**, the code
  - **Station**, where it is now
  - **Status**: In Progress, Completed or Rejected (coloured badge)
  - **Result**, the result of its last scan (OK/NG)
  - **Scans**, how many times it has been scanned in total
  - **Last Scan**, date and time
- Narrow the list with the **Station** drop-down, or type part of a work order number in the search box.
- Click any row to see that work order's details and full history on the right (**Scan Details** and **Scan History**). The history shows the time in and the time out of each station.
- **Delete last scan** removes the most recent scan of the selected work order, for example a tap at the wrong reader. The work order goes back to the station before.
- **Delete all scans of this work order** removes everything recorded for it, so it can be scanned again from the first station. Its tag assignment is kept.
- Both delete buttons ask for the administrator password and say exactly what will be deleted.

### 5.4 Reports & Analytics

For supervisors and managers who want the bigger picture over a period of time.

- **Date range.** Choose *From* and *To* dates and press **Apply**. The page opens on the last 7 days by default.
- **Export to Excel / Export to PDF.** These buttons sit on the same row as the dates. Each saves a report for the chosen dates.
- **Scans by Station.** A bar chart of how many scans each station handled in that period. It shows where the workload is. See [section 9](#9-reports-and-exports).

The page refreshes automatically every time it is opened, and whenever new scans arrive.

### 5.5 Settings (password protected)

Anyone can *look* at Settings, but making any change needs the administrator password.

- **Locked (normal state):** the top right shows "Locked – enter password to edit". Type the password and press **Unlock** (or Enter). Edit buttons are greyed out while locked.
- **Unlocked:** the top right shows "Unlocked", a **New password** box with a **Change password** button, and a **Lock** button.
- Settings **lock again automatically** as soon as you leave the page. Unsaved edits in the Stations table are thrown away.

It has two tabs:

**Stations**

Each station has one RFID reader. The table shows the stations and the reader assigned to each.

| Column / button | Purpose |
|---|---|
| Code, Name, Sequence | Station identifier (e.g. ST01), display name, and display order |
| Reader | The reader assigned to this station, or "Not assigned" |
| Enabled | Switches the station on or off. Tag taps on the reader of a switched-off station are not recorded |
| Final | Shows which station is the final one (only one at a time) |
| **Add station** | Adds a new station row |
| **Save changes** | Saves edits made in the table |
| **Set as final** | Makes the selected station the final station. A scan there completes the work order |
| **Delete** | Deletes the selected station. If it already has scans, you are asked to confirm, because its history is deleted too |
| **Assign reader** | Select a station, press this, then tap any card on that station's reader. That reader now belongs to the station. If the reader belonged to another station, it is moved. The button reads **Cancel** while it is waiting |
| **Remove reader** | Takes the reader away from the selected station |
| **Link a new reader to the first station without one…** | Tick box. When on, a reader nobody has set up is linked by itself, on its first card tap, to the first station that has no reader. When off, readers are only linked with **Assign reader** |

For two stations: add or keep two stations, mark the second one **Final**, then use **Assign reader** on each station in turn.

The system recognises a reader in two ways: by model (the JT308 125 kHz USB card reader is built in), or by speed, because a reader sends a whole card number far faster than anyone can type. Keep each reader plugged into the same USB port; if it is moved to another port, Windows may treat it as a new reader and it must be assigned again.

**About COM ports.** The JT308 is a USB "keyboard" type reader. Windows does not give it a COM port, so readers are assigned to stations by tapping a card, not by choosing a port.

**Database**

| Item | Purpose |
|---|---|
| Database file | Shows where the data file is stored |
| **Backup…** | Saves a copy of all data to a file you choose (works while locked) |
| **Restore…** | Replaces **all** current data with a chosen backup (needs unlock, asks to confirm) |
| **Open folder** | Opens the folder containing the database file |

**About the logs.** The system still keeps a technical log of every scan event, including repeats, rejected reads and newly linked readers. It is stored in the database (the `scan_logs` table) for a technician to inspect. The application no longer has a screen for it; recorded scans are shown on the Scan Information page.

---

## 6. Built-in rules and safeguards

The system checks every scan and protects its own data, so the records can be trusted without anyone watching over it.

| Situation | What the system does | What to do |
|---|---|---|
| Tag tapped that is not assigned to a work order | Nothing is recorded. The Live Scan Log shows "Tag is not assigned to a work order" | Assign the tag on the *Tag Assignment* page |
| Tag tapped at the **final** station before any other station | Recorded as **NG**, "Out of sequence". The work order shows **Rejected** | An administrator deletes that scan in *Scan Information*, then the tag is tapped in the right order |
| Any tap of a work order that is **Rejected** | Nothing is recorded. The log shows "Blocked - delete the rejected scan…" | Delete the rejected scan first |
| Tag tapped again at the station it is already at | Ignored, shown as **DUP** "Already at ST01" | Nothing. It is not counted twice |
| Tag tapped at the final station by mistake (too early) | Recorded, and the work order shows Completed | An administrator uses **Delete last scan**. The work order goes back to the first station and its exit time is cleared |
| Any tap of a work order that is already **Completed** | Nothing is recorded. The log shows "Work order is already completed" | Assign the tag to its next work order |
| Tag tapped while the *Tag Assignment* page is open and unlocked | Read for assignment only, not recorded as a scan | Lock or leave the page to go back to tracking |
| Typing on the PC keyboard | Ignored completely | Typing in other programs never creates false records |
| Tap on a reader that is not assigned to a station | With automatic linking on, the reader is linked to the first station without a reader. Otherwise ignored | Use **Assign reader** in *Settings › Stations* |
| Reader types its card number into a box on screen | Those keystrokes are dropped | A tap never types into, or submits, the search box |
| Someone tries to change Settings | Asks for the administrator password, and locks again on leaving | Stops accidental or unauthorised changes |
| Someone tries to delete scans | Asks for the administrator password, in a window that says exactly what will be deleted | Stops records being removed casually |
| Someone tries to assign or unassign a tag | The *Tag Assignment* page asks for the administrator password, and locks again on leaving | Stops tags being linked to the wrong work order by anyone passing |
| A page is left open with nobody using it | After 120 seconds without a key press or click it closes back to the Dashboard. A warning with a countdown shows for the last 30 seconds | An unlocked Settings or Tag Assignment page is not left open for the next person |
| Deleting a station that has history | Warns and asks for confirmation | Prevents losing history by accident |
| Restoring a backup | Warns that all data will be replaced | Avoids wiping records by mistake |

The 4-character minimum is a stored setting and can be adjusted by a technician if needed.

---

## 7. Day-to-day use

For most people, using the system means scanning and looking. Nobody needs to type anything during normal work.

### Operators

1. For a new work order, open *Tag Assignment*, tap a free tag on a reader, type the work order number and press **Assign**. Then leave that page.
2. Tap the tag on the first station's reader. The scan appears at the top of the Live Scan Log with **OK**.
3. When the work is done, tap the tag on the final station's reader. The work order is completed and the tag is free for the next work order.
4. If a tag won't read, assign a different tag to the work order on the *Tag Assignment* page and tap that one.

### Supervisors and managers

1. **Find one job:** type its number into *Work Order Search* on the Dashboard to see where it is and where it has been.
2. **See everything in progress:** open *Scan Information* and filter by station if needed.
3. **Review a period:** open *Reports & Analytics*, pick the dates, and read the Scans by Station chart.
4. **Share a report:** press *Export to Excel* or *Export to PDF*. The file opens in its folder, ready to email or print.

### Administrator

1. **Add or change stations** in *Settings › Stations* (unlock with the password first) and press *Save changes*.
2. **Choose the final station:** select it and press *Set as final*.
3. **Back up regularly:** *Settings › Database › Backup* saves a copy of all data. Keep copies somewhere other than this PC, such as a USB drive or network folder. A weekly backup is a sensible minimum.
4. **Change the password** after first use. The starting password is **admin**. To change it: open *Settings*, unlock with the current password, type the new one in **New password** (4 characters or more) and press **Change password**. The same password unlocks *Tag Assignment* and confirms deleting scans.
5. **Correct a wrong scan** in *Scan Information*: select the work order and use *Delete last scan* or *Delete all scans of this work order* (password needed).
6. **Assign the readers** in *Settings › Stations*: select a station, press *Assign reader*, and tap a card on that station's reader.

---

## 8. Installation and first run

### What you need

| | |
|---|---|
| Computer | Windows 10 or 11 (64-bit) |
| Software to run | Microsoft .NET 8 Desktop Runtime (free from Microsoft) |
| Software to build from source | .NET SDK 8.0 or later |
| Readers | One USB RFID card reader per station (e.g. JT308, 125 kHz), of the "keyboard" type, set to send **Enter** after each read |
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
- To see the screens filled with sample data without readers, use `--seed-demo` (below). A tag ID can also be typed by hand on the *Tag Assignment* page.
- To see the screens filled with sample data for training, run the program once with `--seed-demo` (see [section 12](#12-under-the-hood-technical-reference)).

---

## 9. Reports and exports

Reports are created from **Reports & Analytics** for the selected date range.

| | Excel (.xlsx) | PDF |
|---|---|---|
| Best for | Further analysis, filtering, sharing numbers | Printing, emailing, archiving |
| Contents | Sheets: **Summary**, **Scans by Station**, **Scans** (every scan in the period) | Company header, summary figures, scans by station, and scan detail pages |

- Files are saved to **Documents › Nasmyth Traceability › Exports**.
- File names include the date range and time created, for example
  `Traceability_20260920_20260926_101530.xlsx`.
- After saving, the folder opens automatically with the new file selected.

---

## 10. Data, backups and security

- **Where data lives:** one database file on the shop-floor PC (`C:\ProgramData\Nasmyth\Traceability\traceability.db`). Nothing is sent over the internet.
- **What is stored:** stations, the reader assigned to each station, tag-to-work-order assignments, every scan with its entry and exit time (history), the current position of each work order, logs, and settings.
- **Backups:** use *Settings › Database › Backup*. The file is named like `traceability_backup_20260926_101530.db`. Store copies away from the PC.
- **Restore:** replaces all current data with the backup's data, including the settings password saved in that backup.
- **Password:** one administrator password protects all changes in Settings. It is stored as a one-way scrambled code (SHA-256 hash), not as readable text. If it is forgotten, a technician can reset it in the database.
- **Deleting data:** in *Scan Information* an administrator can delete the last scan or all scans of one work order (password needed). Deleting a station removes that station's scans. To clear all scan data, a technician runs the program with `--wipe-data`.
- **If the database file is deleted:** while the application is running, Windows refuses to delete the file because the application keeps it open. If it is deleted while the application is closed, the next start creates a new, empty database with the four starting stations and the starting password **admin**. The old scans are gone unless you restore a backup.

---

## 11. Recent changes

The project began as the *Barcode Traceability System*. It has since been reshaped around **work orders** and simplified for everyday users. Each change was built and checked with the application's automatic self-test.

| Area | Change |
|---|---|
| Name | Renamed to **Work Order Traceability System** in the header, window title and reports. Existing installations pick up the new name automatically |
| Dashboard | "Barcode Search" is now **Work Order Search**. The Current Information label reads **Work Order**. The manual scan box now says "Type a work order" |
| Dashboard | Removed the Total Scans / OK / NG / OK rate counter line |
| Current Information | "Barcode" column and "Selected barcode" heading now read **Work Order** / **Selected work order** |
| Reports & Analytics | Removed the Total / OK / NG / Unique number tiles, the OK-vs-NG chart and the Recent Scans table |
| Settings | Removed the *General* tab (app name, company name, date format, font size, start-up options) and the *USB Scanners* tab |
| Settings | Added a **password lock**. Changes need the password, and it locks again on leaving |
| Side menu | Removed the "USB scanners (Raw Input)" text |
| RFID card readers | A new reader is detected and linked to a station automatically on its first card tap |
| Settings | Readers are now shown and assigned on the **Stations** tab (*Assign reader*, *Remove reader*). The separate Readers tab is gone, and each station has one reader |
| Dashboard / Logs / exports | The remaining "Barcode" column headings now read **Work Order** |
| Dashboard | A reader's keystrokes no longer land in the search or manual scan boxes |
| Dashboard | Removed the station tiles from the top of the screen |
| Dashboard / menu | "Current Information" is now **Scan Information**, both the menu page and the Dashboard section |
| Reports & Analytics | Removed the Station-wise Scan Trend chart and the Today / 7 days / 30 days buttons. The export buttons moved onto the date row |
| Excel export | Removed the **Trend** sheet |
| Scan Information | The cards are now titled **Scanned Work Orders**, **Scan Details** and **Scan History** |
| Settings | Removed the **Logs** tab. The log is still kept in the database |
| Tag Assignment | New page: tap a tag, type its work order number, assign. Readers are in assignment mode while it is open |
| Scanning | A reader now reads a tag; the work order comes from the tag assignment. A tag with no work order is not recorded |
| Scanning | The final station must come last. A tap there first is recorded as NG and blocks the work order until it is deleted |
| Scanning | The exit from a station is logged when the tag is tapped at the next one. History shows time in and time out |
| Scanning | A repeat tap at the same station is ignored whenever it happens (the 5-second window is no longer used) |
| Scan Information | Added **Delete last scan** and **Delete all scans of this work order**, protected by the administrator password |
| Excel export | The Scans sheet has a **Time Out** column |
| Dashboard | Work Order Search now suggests matching work orders as you type, by work order number or tag ID |
| Dashboard | Removed **Manual / Test Scan** (the Simulate button). Scans now come only from tags tapped on readers |
| Tag Assignment | The page is locked until the administrator password is entered, and locks again on leaving |
| All pages | Idle timeout: a page other than the Dashboard closes after 120 seconds without a key press or click, with a 30-second warning |
| Database | Upgraded automatically on first start: exit time column and tag assignment table |
| Database | The database file can no longer be deleted while the application is running. If it is deleted while the application is closed, a new empty one is created on the next start |

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
| Idle timeout | Pages other than the Dashboard close after the number of seconds in the `security.pageTimeoutSeconds` setting (default 120; 0 switches it off). The warning shows for the last 30 seconds |
| Configuration | `config.json` next to the program: company name, and an optional `databasePath` to move the database |

### Database tables

| Table | Holds |
|---|---|
| `stations` | Stations, their order, on/off state, and which is final |
| `station_devices` | Which USB reader belongs to which station (one per station) |
| `tag_assignments` | Which RFID tag carries which work order |
| `trace_history` | Every station visit: work order, station, result, entry time (`scanned_at`) and exit time (`exited_at`) |
| `current_trace` | Latest position and status for each work order |
| `scan_logs` | Raw, duplicate, rejected, error and info events |
| `settings` | Key/value settings, including the password hash |
| `routes`, `route_stations` | Kept from the original design, not used in scanning |

Note: the work order code is stored in columns named `barcode`. Only the screens were renamed.

### Main program parts

| Part | File | Job |
|---|---|---|
| Reader input | `Services/Scanning/RawInputScannerService.cs` | Reads the USB readers |
| Scan coordinator | `Services/ScanCoordinator.cs` | Turns a tag into its work order, applies the sequence rules, and decides what happens to each scan |
| Trace service | `Services/TraceService.cs` | Saves history, current positions, logs and search |
| Station service | `Services/StationService.cs` | Stations and the reader assigned to each |
| Tag service | `Services/TagService.cs` | Tag to work order assignments |
| Report service | `Services/ReportService.cs` | Report figures and chart data |
| Database service | `Data/DatabaseService.cs` | Database access, backup, restore |
| Settings service | `Data/SettingsService.cs` | Settings and password |
| Screens | `Views/*.xaml` + `ViewModels/*.cs` | Dashboard, Tag Assignment, Scan Information, Reports, Settings |

### Command-line options

| Option | Purpose |
|---|---|
| *(none)* | Normal start |
| `--selftest` | Automatic check: builds a temporary database, runs test scans, reports, exports and backup, prints PASS or FAIL |
| `--seed-demo [units] [days] [--append]` | Fills the database with sample data (default 40 work orders over 7 days) for training or demonstration |
| `--wipe-data` | Resets to a clean state (clears history, positions, logs, reader assignments and tag assignments; keeps stations and settings) |
| `--smoke` | Opens and closes the main window to check it starts |
| `--shot <file.png> [--page <name>] [--tab <n>]` | Saves a screenshot of the window |

---

## 13. Troubleshooting and FAQ

| Question / problem | Answer |
|---|---|
| **A tap doesn't appear on the Dashboard** | Check the reader is plugged in and shown against its station in *Settings › Stations*, that the station is *Enabled*, and that the *Tag Assignment* page is not open |
| **The reader's light stays red and nothing happens** | The reader is not reading the card. The card is the wrong type for the reader (see [What you need](#what-you-need)). Test in Notepad: a working card makes the reader beep and type a number |
| **A reader's scans show at the wrong station** | Open *Settings › Stations*, select the right station, press *Assign reader* and tap a card on that reader |
| **A scan shows NG** | The tag was tapped at the final station before the first one ("Out of sequence"). An administrator deletes that scan in *Scan Information*, then the tag is tapped in the right order |
| **I tapped twice but only one scan shows** | A repeat tap at the same station is ignored on purpose. It shows as **DUP** in the Live Scan Log |
| **The log says "Tag is not assigned to a work order"** | Open *Tag Assignment*, tap the tag, type its work order number and press *Assign* |
| **The log says "Blocked - delete the rejected scan"** | The work order has an NG scan on record. An administrator deletes it in *Scan Information* |
| **A tag was tapped at the final station by mistake** | An administrator selects the work order in *Scan Information* and presses *Delete last scan*. It goes back to the first station |
| **A work order never shows Completed** | It must be scanned at the station marked *Final* in Settings › Stations |
| **I can't edit anything in Settings** | Settings are locked. Enter the password at the top right and press Unlock |
| **I forgot the password** | Restore a backup whose password you know, or ask IT to reset the password value in the database |
| **Where are my exported reports?** | Documents › Nasmyth Traceability › Exports |
| **Can I use the normal keyboard to scan?** | No. The keyboard is deliberately ignored, and there is no manual scan screen. Every scan comes from a tag tapped on a reader |
| **Does it need the internet?** | No, it runs entirely on the PC |
| **The database file was deleted** | Start the application again. It creates a new empty database and carries on. Use *Settings › Database › Restore* to bring back a backup |

---

## 14. Limitations and suggested next steps

- **Not yet tried with real readers and tags.** Reader assignment, tag assignment, the sequence rules and the delete functions pass the automatic self-test, but have not been tried with physical readers and matching tags. Check on site: *Assign reader* should pick up each reader, a tag ID should appear on the *Tag Assignment* page when tapped, and the tag number should not appear in the search box.
- **Two identical readers.** Both readers are the same model, so Windows tells them apart only by the USB port. Keep each in its own port, and assign it again if it is moved.
- **Fixed display settings.** Company name, date format, font size and full-screen start-up can no longer be changed in the app. They keep their current values.
- **One PC only.** Data lives on one computer. Other offices can't view it live, and regular off-PC backups are essential. *Suggestion:* scheduled automatic backups, or a shared network database if several PCs are needed later.
- **Export totals.** The Excel/PDF exports still include the overall totals that were removed from the Reports screen.

---

## 15. Glossary

| Term | Meaning |
|---|---|
| **Work order** | A production job, identified by its work order number |
| **Tag** | The RFID card or key fob that travels with a work order. Its ID is linked to the work order number |
| **Tag assignment** | The link between a tag and a work order, made on the Tag Assignment page |
| **Station** | A workstation on the shop floor where a work order is processed and scanned |
| **Final station** | The last station. A scan here marks the work order as Completed |
| **Scan** | Tapping a work order's tag on a station's reader (or entering the work order by hand) |
| **OK / NG** | Result of a scan: OK is accepted, NG ("not good") is rejected |
| **In Progress / Completed / Rejected** | Work order status: still moving, finished at the final station, or last scan rejected |
| **Traceability** | Being able to show where an item has been, and when |
| **Duplicate (DUP)** | The same work order tapped again at the station it is already at, ignored so it isn't counted twice |
| **Log** | The technical record of every scan event, including the ones set aside |
| **Backup / Restore** | Saving a copy of all data, and later putting that copy back |
| **Database** | The single file on the PC where all records are kept |
| **Export** | Saving a report as an Excel or PDF file |
