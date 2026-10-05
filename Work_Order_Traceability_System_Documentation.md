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

Each work order travels with an RFID tag, and each station on the shop floor has a network RFID reader. A separate *work order assigning station* writes the work order onto the tag once. After that, tapping the tag on a station's reader records the station and the time, and the screens update straight away. Supervisors can look up any work order, see where every job currently sits, and export reports to Excel or PDF.

| | |
|---|---|
| **Who uses it** | Operators (assign tags and tap them at the stations), supervisors and managers (monitor progress, look up jobs, run reports), an administrator (sets up stations and readers, corrects wrong scans, keeps the data safe) |
| **Where it runs** | One Windows 10/11 PC on the shop floor, on the same network (Wi-Fi / LAN) as the RFID readers. No internet or server is needed |
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
| **One reader per station** | Each station has its own network RFID reader, set up by its IP address. The system knows which reader (and so which station) every tap came from |
| **Work order assigning station** | A separate reader writes the work order onto the tag and links the tag to it, in one step |
| **Never stops scanning** | Every reader is polled all the time, whatever page is open. Taps made while a reader was unreachable, or while the program was closed, are picked up afterwards |
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

A work order travels with an RFID tag. There are three steps: the work order is written to the tag once at the assigning station, then the tag is tapped at each station.

```
 ┌───────────────────┐   ┌────────────────────┐   ┌─────────────────────┐
 │ 1. ASSIGN         │──▶│ 2. FIRST STATION   │──▶│ 3. FINAL STATION    │
 │ Type the work     │   │ Tap the tag on     │   │ Tap the tag on      │
 │ order, press Write│   │ reader 1.          │   │ the final reader.   │
 │ and tap the tag.  │   │ Entry is logged.   │   │ Exit from station 1 │
 │ (Tag Assignment)  │   │ Status: In Progress│   │ and entry here are  │
 └───────────────────┘   └────────────────────┘   │ logged. Completed.  │
                                                  └─────────────────────┘
```

Step by step:

1. **Assign the tag.** On the *Tag Assignment* page, type the work order number and press **Write to tag**, then present the tag on the *work order assigning station's* reader. The reader writes the work order onto the tag, reads it back, and the tag (by its serial number, the UID) is linked to the work order. From then on, wherever that tag is tapped, the system knows which work order it is.
2. **Tap at the first station.** Each station has its own network reader, polled by its IP address, so the system knows which station the tap came from. The entry is saved with date and time, and the work order shows **In Progress** at that station.
3. **Tap at the final station.** The system saves the entry at the final station and, at the same moment, logs the **exit** from the station the work order came from. The work order becomes **Completed**.
4. **Every screen refreshes.** The live log, the work order list and the reports show each scan straight away.

**The right order is: first station, then the final station.** The final station is always last, because it is the one that marks a job as done. If there are more than two stations, the ones before the final station may be visited in any order.

**Example.** Tag *04A1B2C3* is assigned to work order *WO-1001A*, tapped at ST01 at 09:05 and at the final station ST02 at 11:45:

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

This is where a work order is written to an RFID tag. It works **only with the work order assigning station's reader**, set up in *Settings › Assignment Station*. The production station readers are not involved: **they keep recording station scans while this page is open**. **The page is locked until the administrator password is entered** at the top right (the same password as Settings). It locks itself again when it is left or times out.

| Part of the screen | What it shows / does |
|---|---|
| **Reader bar** | The assigning station's reader and whether it is online. If no assigning station is set up, it says so |
| **Work order number** | Type the work order. Letters and digits, with `-` `_` `/` `.` allowed inside, 4 to 16 characters (a tag holds 16). It is stored in capital letters. It can be typed while the page is locked; the line under the box says straight away if the text is not a valid work order (green when it is) |
| **Write to tag** | Arms the assigning reader to write the work order to the next tag presented (Enter does the same). Then present the tag on that reader. If the page is still locked, the status box says so and nothing is written |
| **Status box** | *Waiting for tag…* with a countdown while the reader is armed; *Tag written* with the tag's UID once the reader has written and read back the work order; *Not written* with the reason otherwise |
| **Cancel** | Stops waiting and puts the reader back in read mode |
| **Last tag at the assignment station** | A tag presented when no write is pending is only read: its UID, the text on the card and the work order it is assigned to are shown here. Use this to check a tag |
| **Assigned Tags** | Every tag (by UID) and its work order, with the work order's status, its current station and when the tag was assigned. Use **Search** to find a tag or work order |
| **Remove assignment** | Unlinks the selected tag. Scans already recorded for the work order are kept |

How a write works:

1. **Write to tag** sends the work order to the assigning reader as a one-shot write.
2. The next tag presented is written and read back. If that works, the tag's UID is linked to the work order and the reader returns to read mode by itself.
3. If the write fails (the tag was lifted too early, or it is not a MIFARE Classic card), the reason is shown and **the reader stays armed**, so the tag can simply be presented again.
4. If no tag is presented in time (30 seconds, setting `rfid.writeTimeoutSeconds`), or **Cancel** is pressed, or the page is left or locked, the reader is put back in read mode, so it never overwrites a tag by accident later.

Rules for assigning:

- **One tag, one work order.** A tag carries one work order at a time, and a work order has one tag.
- **The tag is identified by its UID**, its factory serial number. The work order text written on the card is a convenience label: the UID is what the stations look up.
- **Tags are reused.** When a work order is completed, its tag can be written with the next work order. Because the card now physically carries the new work order, the link follows the card. If the tag was still in use by an unfinished work order, the status box says so and that work order is left without a tag.
- **Giving a work order a different tag** asks for confirmation; the old tag is released.
- **A completed work order cannot be written to a tag again.** Delete its scans in *Scan Information* first if it really has to be run again.

### 5.3 Scan Information

The answer to "what is where right now?" for the whole factory. The **Station** drop-down and the search box (part of a work order number or tag ID) filter both tabs.

**Scanned Work Orders** tab:

- Lists every work order the system knows about, with these columns:
  - **Work Order**, the code
  - **Station**, where it is now
  - **Status**: In Progress, Completed or Rejected (coloured badge)
  - **Result**, the result of its last scan (OK/NG)
  - **Scans**, how many times it has been scanned in total
  - **Last Scan**, date and time
- Click any row to see that work order's details and full history on the right (**Scan Details** and **Scan History**). The history shows the time in and the time out of each station.
- **Delete last scan** removes the most recent scan of the selected work order, for example a tap at the wrong reader. The work order goes back to the station before.
- **Delete all scans of this work order** removes everything recorded for it, so it can be scanned again from the first station. Its tag assignment is kept.
- Both delete buttons ask for the administrator password and say exactly what will be deleted.

**Invalid & Repeat Scans** tab (the number in brackets is how many are listed):

- Every tap that was *not* recorded as a station visit, newest first: time, station, tag, work order (when the tag has one), type and reason.
- **Invalid** (red): the tag is not assigned to a work order, the work order was tapped at the final station first (out of sequence), it is blocked by a rejected scan, or it is already completed.
- **Repeat** (yellow): a valid work order tapped again at the station it is already at.
- **Show** picks invalid and repeat, invalid only, or repeat only. The list updates as taps happen.

### 5.4 Reports & Analytics

For supervisors and managers who want the bigger picture over a period of time.

- **Date range.** Choose *From* and *To* dates and press **Apply**. The page opens on the last 7 days by default.
- **Scans by Station.** For each station, three bars side by side: **Valid** (green, recorded as a station visit), **Invalid** (red) and **Repeat** (yellow), with the totals at the top right. With many stations the chart scrolls sideways.
- **Export to Excel / Export to PDF.** These buttons sit on the same row as the dates. Each saves a report for the chosen dates containing **only valid scans**: invalid and repeat scans are left out, and the report says so. See [section 9](#9-reports-and-exports).

The page is recalculated every time it is opened and when **Apply** is pressed.

### 5.5 Settings (password protected)

Anyone can *look* at Settings, but making any change needs the administrator password.

- **Locked (normal state):** the top right shows "Locked – enter password to edit". Type the password and press **Unlock** (or Enter). Edit buttons are greyed out while locked.
- **Unlocked:** the top right shows "Unlocked", a **New password** box with a **Change password** button, and a **Lock** button.
- Settings **lock again automatically** as soon as you leave the page. Unsaved edits in the Stations table are thrown away.

It has three tabs:

**Stations**

Each production station has one network RFID reader (ESP32 + RC522), reached by its IP address. The table shows the stations, the address of each one's reader and whether that reader is online right now.

| Column / button | Purpose |
|---|---|
| Code, Name, Sequence | Station identifier (e.g. ST01), display name, and display order |
| Reader IP address | The address of the station's reader, e.g. `192.168.1.57`. Leave it empty for a station that has no reader yet. Edit it here and press **Save changes** |
| Reader status | Live: *Online – RFID-RW-E5F6*, *Offline – reason*, *Connecting…*, *No reader IP* or *Station disabled – not polled* |
| Enabled | Switches the station on or off. A switched-off station's reader is not polled and its taps are not recorded |
| Final | Shows which station is the final one (only one at a time) |
| **Save changes** | Saves edits made in the table. Addresses are checked first: each must be a valid IP address and no reader may serve two stations |
| **Set as final** | Makes the selected station the final station. A scan there completes the work order |
| **Delete** | Deletes the selected station. If it already has scans, you are asked to confirm, because its history is deleted too |
| **Test reader** | Asks the selected station's reader for its status and shows its name, MAC address, firmware and mode (works while locked) |
| **Add station** box | Code, Name and **Reader IP address** for a new station, with **Test** to check the address first and **Add station** to create it. The new reader is polled straight away |

Changes take effect immediately: a reader whose address is changed is polled at the new address without restarting the program. Give every reader a **DHCP reservation** in the router so its address never changes.

**Assignment Station**

The work order assigning station: a separate reader used only by the *Tag Assignment* page to write work orders to tags. It is never part of the production sequence and its taps are never recorded as station scans.

| Item | Purpose |
|---|---|
| Reader IP address | The assigning reader's address. It must not be the address of a production station's reader. **Save** applies it (needs unlock) |
| **Test reader** | Asks the reader for its status |
| Status, Reader MAC | Live connection status, and the MAC address the reader reported |

**How the readers are watched**

- Every reader (all enabled stations and the assigning station) is polled about twice a second (setting `rfid.pollIntervalMs`, default 500 ms), from start-up until the program is closed, **whatever page is open**.
- Production readers are always kept in read mode. If one is found in write mode (for example set from its web page), it is switched back, because in write mode it would overwrite every tag presented. The change is logged.
- If a reader cannot be reached, it is shown offline and polling carries on; when it comes back, the taps it stored in the meantime (up to its last 16) are recorded with the time they were made.
- Taps made while the program was closed are recorded on the next start, using the last event recorded for each reader.
- If a reader restarts, or a different reader (another MAC address) answers at a station's address, it is noticed: a restart is handled automatically, and a different reader is logged as an error so it can be checked.
- A reader whose firmware reports no MAC address (`00:00:00:00:00:00`) is identified by its IP address instead; this is logged once.

**Database**

| Item | Purpose |
|---|---|
| Database file | Shows where the data file is stored |
| **Backup…** | Saves a copy of all data to a file you choose (works while locked) |
| **Restore…** | Replaces **all** current data with a chosen backup (needs unlock, asks to confirm) |
| **Open folder** | Opens the folder containing the database file |

**About the logs.** The system still keeps a technical log of every scan event, including repeats, rejected reads, readers going online or offline, and tag writes. It is stored in the database (the `scan_logs` table) for a technician to inspect. The application no longer has a screen for it; recorded scans are shown on the Scan Information page.

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
| Tag presented at the work order assigning station when no write is pending | Only read, and shown on the *Tag Assignment* page. Never recorded as a station scan | Nothing |
| Station scans while the *Tag Assignment* page is open | Recorded as usual. Assigning tags never pauses the stations | Nothing |
| A tag write fails (tag lifted too early, wrong card type) | The reason is shown and the assigning reader stays armed | Present the tag again, or press **Cancel** |
| No tag is presented for a pending write | After 30 seconds the write is cancelled and the reader goes back to read mode | Press **Write to tag** again |
| A station's reader cannot be reached | Shown offline in *Settings › Stations*, and logged. Polling carries on; taps the reader stored meanwhile are recorded when it is back | Check the reader's power and Wi-Fi; use **Test reader** |
| A production reader is in write mode | Switched back to read mode at once, and logged | Do not change reader modes from the reader's own web page |
| A different reader answers at a station's address | Its taps are still recorded for that station, its new MAC is stored, and an error is logged | Check that the right reader has that address (DHCP reservation) |
| Typing on the PC keyboard | Has no effect on scanning: scans come only from the readers | Nothing |
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

1. For a new work order, open *Tag Assignment* (unlock it), type the work order number, press **Write to tag** and present a free tag on the assigning station's reader. Wait for **Tag written**.
2. Tap the tag on the first station's reader. The scan appears at the top of the Live Scan Log with **OK**.
3. When the work is done, tap the tag on the final station's reader. The work order is completed and the tag is free for the next work order.
4. If a tag won't read, write the work order to a different tag on the *Tag Assignment* page and tap that one.

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
6. **Set up the readers:** enter each station reader's IP address in *Settings › Stations*, and the assigning reader's address in *Settings › Assignment Station*. Check each with **Test reader**; the *Reader status* column in *Settings › Stations* should show them all online.

---

## 8. Installation and first run

### What you need

| | |
|---|---|
| Computer | Windows 10 or 11 (64-bit) |
| Software to run | Microsoft .NET 8 Desktop Runtime (free from Microsoft) |
| Software to build from source | .NET SDK 8.0 or later |
| Readers | One network RFID reader/writer (ESP32 + RC522, firmware with the HTTP API in README.md) per station, plus one for the work order assigning station. All on the same network as the PC, each with a fixed address (DHCP reservation) |
| Cards | MIFARE Classic 13.56 MHz cards or fobs (1K / 4K / Mini) with the factory key. Ultralight, NTAG and DESFire cards cannot be written |

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
- No readers are polled until their IP addresses are entered in *Settings*.
- To see the screens filled with sample data for training, run the program once with `--seed-demo` (see [section 12](#12-under-the-hood-technical-reference)).

---

## 9. Reports and exports

Reports are created from **Reports & Analytics** for the selected date range. **They contain valid scans only** - every station visit that was recorded. Invalid scans (unassigned tag, out of sequence, blocked, already completed) and repeat scans are left out; they can be seen on screen in *Scan Information* and in the Reports chart. Each report carries the note "Valid scans only. Invalid and repeat scans are not included."

| | Excel (.xlsx) | PDF |
|---|---|---|
| Best for | Further analysis, filtering, sharing numbers | Printing, emailing, archiving |
| Contents | Sheets: **Summary** (valid scans, work orders), **Scans by Station** (valid scans per station), **Scans** (every valid station visit in the period, with time in and time out) | Company header, summary figures, valid scans by station, and scan detail pages |

- Files are saved to **Documents › Nasmyth Traceability › Exports**.
- File names include the date range and time created, for example
  `Traceability_20260920_20260926_101530.xlsx`.
- After saving, the folder opens automatically with the new file selected.

---

## 10. Data, backups and security

- **Where data lives:** one database file on the shop-floor PC (`C:\ProgramData\Nasmyth\Traceability\traceability.db`). Nothing is sent over the internet.
- **Reader network:** the readers have no password of their own (see README.md): anyone on the same network can change their mode. Keep them on a trusted shop-floor network and close their web pages in production.
- **What is stored:** stations with the IP address and MAC of each station's reader, the assigning station's reader, tag-to-work-order assignments, every scan with its entry and exit time (history), the current position of each work order, logs, and settings.
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
| RFID readers | The USB keyboard-type readers are replaced by **network RFID reader/writers** (ESP32 + RC522). Each station's reader is set up by its IP address in *Settings › Stations*, including in the new **Add station** box |
| Scanning | Every reader is polled continuously, whatever page is open. Taps made while a reader was unreachable, or while the program was closed, are recorded afterwards. Production readers are kept in read mode |
| Scanning | A tag is identified by its UID. The work order text on the card is only shown, never trusted on its own |
| Tag Assignment | Works only with the new **work order assigning station**: type the work order, press **Write to tag**, present the tag; once the reader confirms the write, the tag is linked. The production readers keep scanning while the page is open |
| Settings | New **Assignment Station** tab, **Test reader** buttons and a live reader status column. *Assign reader*, *Remove reader* and automatic reader linking are gone |
| Work orders | At most 16 characters, the capacity of a tag |
| Database | Upgraded automatically to version 3: reader address and MAC per station, and a table remembering the last event read from each reader |
| Dashboard | Reader status chips removed (they do not scale to hundreds of readers). Reader status is shown in *Settings* only |
| Scan Information | New **Invalid & Repeat Scans** tab: unassigned tags, out-of-sequence, blocked and completed work orders, and repeat taps |
| Reports & Analytics | The chart shows **valid, invalid and repeat** scans per station side by side, and scrolls sideways for many stations |
| Excel / PDF export | Reports contain **valid scans only**; invalid and repeat scans are left out, and the report says so |
| Reports & Analytics | The date pickers' weekday names and month arrows are now clearly visible |
| Tag Assignment | The work order can be typed while the page is locked, with an immediate check of what is typed; text boxes that cannot be used now look greyed out |
| RFID readers | Readers whose firmware reports MAC `00:00:00:00:00:00` are identified by their IP address instead |
| Database | Upgraded automatically to version 4: each scan in the log records what it came to (valid / invalid / repeat) and the tag |

---

## 12. Under the hood (technical reference)

### Technology

| Topic | Detail |
|---|---|
| Language / framework | C# on .NET 8, WPF desktop user interface, MVVM pattern (CommunityToolkit.Mvvm 8.4) |
| Database | SQLite (Microsoft.Data.Sqlite 8.0) |
| Excel export | ClosedXML 0.104 |
| PDF export | PdfSharp 6.1 |
| Readers | Network RFID reader/writers (ESP32 + RC522), HTTP + JSON on port 80, API in README.md. One polling loop per reader (`GET /api/events?since=`, every `rfid.pollIntervalMs`, default 500 ms, 4 s timeout). Requests to one reader never overlap. Events are timestamped from the reader's uptime |
| Tag writing | `POST /api/mode` with `{"mode":"write","data":"<work order>","once":true}`, then wait for a `write` event newer than the reply's `lastEventId`. Timeout `rfid.writeTimeoutSeconds` (default 30), then `{"mode":"read"}` |
| Idle timeout | Pages other than the Dashboard close after the number of seconds in the `security.pageTimeoutSeconds` setting (default 120; 0 switches it off). The warning shows for the last 30 seconds |
| Configuration | `config.json` next to the program: company name, and an optional `databasePath` to move the database |

### Database tables

| Table | Holds |
|---|---|
| `stations` | Stations, their order, on/off state, which is final, and the IP address (`reader_ip`) and MAC (`reader_mac`) of each one's reader |
| `station_devices` | Not used since version 3 (it held the USB reader of each station). Kept so older databases open unchanged |
| `reader_cursors` | Per reader MAC: the boot id and last event processed, so taps made while the program was closed are caught up exactly once |
| `tag_assignments` | Which RFID tag carries which work order |
| `trace_history` | Every station visit: work order, station, result, entry time (`scanned_at`) and exit time (`exited_at`) |
| `current_trace` | Latest position and status for each work order |
| `scan_logs` | Every scan event and reader status line. Since version 4, `outcome` says what a station scan came to (Accepted = valid, Rejected / Error = invalid, Duplicate = repeat; empty for lines that are not scans) and `tag_id` holds the tag |
| `settings` | Key/value settings, including the password hash |
| `routes`, `route_stations` | Kept from the original design, not used in scanning |

Note: the work order code is stored in columns named `barcode`. Only the screens were renamed.

### Main program parts

| Part | File | Job |
|---|---|---|
| Network readers | `Services/Rfid/NetworkReaderService.cs`, `ReaderPoller.cs`, `RfidReaderClient.cs` | Poll every reader, keep production readers in read mode, report status |
| Tag writer | `Services/Rfid/TagWriter.cs` | Writes a work order to a tag at the assigning station and links the tag |
| Scan coordinator | `Services/ScanCoordinator.cs` | Turns a tag into its work order, applies the sequence rules, and decides what happens to each scan |
| Trace service | `Services/TraceService.cs` | Saves history, current positions, logs and search |
| Station service | `Services/StationService.cs` | Stations and the address of each one's reader |
| Tag service | `Services/TagService.cs` | Tag to work order assignments |
| Report service | `Services/ReportService.cs` | Report figures and chart data |
| Database service | `Data/DatabaseService.cs` | Database access, backup, restore |
| Settings service | `Data/SettingsService.cs` | Settings and password |
| Screens | `Views/*.xaml` + `ViewModels/*.cs` | Dashboard, Tag Assignment, Scan Information, Reports, Settings |

### Command-line options

| Option | Purpose |
|---|---|
| *(none)* | Normal start |
| `--selftest` | Automatic check: builds a temporary database, runs test scans, reports, exports and backup, and drives the reader polling and tag writing against simulated network readers. Prints PASS or FAIL |
| `--seed-demo [units] [days] [--append]` | Fills the database with sample data (default 40 work orders over 7 days) for training or demonstration |
| `--wipe-data` | Resets to a clean state (clears history, positions, logs and tag assignments; keeps stations, reader addresses and settings) |
| `--smoke` | Opens and closes the main window to check it starts |
| `--shot <file.png> [--page <name>] [--tab <n>]` | Saves a screenshot of the window |

---

## 13. Troubleshooting and FAQ

| Question / problem | Answer |
|---|---|
| **A tap doesn't appear on the Dashboard** | Check the station's *Reader status* in *Settings › Stations* says Online. If it is offline, check the reader's power and Wi-Fi and its address in *Settings › Stations* (**Test reader**). Also check the station is *Enabled*. Taps made while a reader was offline are recorded once it is back |
| **A reader shows offline but it is switched on** | Its address may have changed. Open its web page or check the router, enter the right address, and give it a DHCP reservation so it never changes. The PC and the readers must be on the same network, and the router network must not be `192.168.4.x` |
| **A reader's scans show at the wrong station** | Two stations have each other's reader addresses. Correct the IP addresses in *Settings › Stations* and press *Save changes*. **Test reader** shows the reader's name (RFID-RW-xxxx), printed on its own web page |
| **Write to tag says the assignment station is not set up** | Enter the assigning reader's address in *Settings › Assignment Station* and press *Save* |
| **The write keeps failing with "authentication failed"** | The card is not a MIFARE Classic card with the factory key (Ultralight, NTAG and DESFire cards cannot be written). Use a supported card |
| **The status box says "No tag was presented in time"** | The reader was armed for 30 seconds and no tag came. Press **Write to tag** again and present the tag within the countdown |
| **A scan shows NG** | The tag was tapped at the final station before the first one ("Out of sequence"). An administrator deletes that scan in *Scan Information*, then the tag is tapped in the right order |
| **I tapped twice but only one scan shows** | A repeat tap at the same station is ignored on purpose. It shows as **DUP** in the Live Scan Log |
| **Test reader shows "no MAC reported (00:00:00:00:00:00)"** | The reader firmware most likely reads its MAC address before Wi-Fi has started, so every such reader reports zeros (and is named RFID-RW-0000). The program copes: it identifies these readers by their IP address. Fixing the firmware (read the MAC after `WiFi.mode(...)`) is still recommended, so the readers can be told apart |
| **The log says "Tag is not assigned to a work order"** | Write the work order to the tag on the *Tag Assignment* page (the log shows the text found on the card, if any) |
| **The log says "Blocked - delete the rejected scan"** | The work order has an NG scan on record. An administrator deletes it in *Scan Information* |
| **A tag was tapped at the final station by mistake** | An administrator selects the work order in *Scan Information* and presses *Delete last scan*. It goes back to the first station |
| **A work order never shows Completed** | It must be scanned at the station marked *Final* in Settings › Stations |
| **I can't edit anything in Settings** | Settings are locked. Enter the password at the top right and press Unlock |
| **I forgot the password** | Restore a backup whose password you know, or ask IT to reset the password value in the database |
| **Where are my exported reports?** | Documents › Nasmyth Traceability › Exports |
| **Can I use the normal keyboard to scan?** | No. There is no manual scan screen. Every scan comes from a tag tapped on a reader |
| **Does it need the internet?** | No. It runs on the PC and talks only to the readers on the local network |
| **The database file was deleted** | Start the application again. It creates a new empty database and carries on. Use *Settings › Database › Restore* to bring back a backup |

---

## 14. Limitations and suggested next steps

- **Not yet tried with the physical readers.** Polling, tag writing, reader restarts and outages pass the automatic self-test against simulated readers that follow the reader API (README.md), and against a simulated reader over real HTTP, but not yet against the ESP32 readers themselves. Check on site: each reader shows online, a tap at each station appears on the Dashboard at the right station, and **Write to tag** writes and links a tag at the assigning station.
- **Up to 16 taps between two polls.** A reader keeps only its last 16 events. If more than 16 tags are presented while a reader cannot be reached (or while the program is closed), the oldest are lost; this is logged.
- **Readers are not secured.** The reader API has no password. Keep the readers on a trusted network.
- **Fixed display settings.** Company name, date format, font size and full-screen start-up can no longer be changed in the app. They keep their current values.
- **One PC only.** Data lives on one computer. Other offices can't view it live, and regular off-PC backups are essential. *Suggestion:* scheduled automatic backups, or a shared network database if several PCs are needed later.
- **Export totals.** The Excel/PDF exports still include the overall totals that were removed from the Reports screen.

---

## 15. Glossary

| Term | Meaning |
|---|---|
| **Work order** | A production job, identified by its work order number |
| **Tag** | The RFID card or key fob that travels with a work order. Its UID is linked to the work order number, and the work order is also written on it |
| **UID** | A tag's factory serial number. It cannot be changed, so it is what identifies the tag |
| **Work order assigning station** | The reader used only on the Tag Assignment page, to write work orders to tags |
| **Tag assignment** | The link between a tag and a work order, made by writing the work order to the tag on the Tag Assignment page |
| **Station** | A workstation on the shop floor where a work order is processed and scanned |
| **Final station** | The last station. A scan here marks the work order as Completed |
| **Scan** | Tapping a work order's tag on a station's reader |
| **OK / NG** | Result of a scan: OK is accepted, NG ("not good") is rejected |
| **In Progress / Completed / Rejected** | Work order status: still moving, finished at the final station, or last scan rejected |
| **Traceability** | Being able to show where an item has been, and when |
| **Duplicate (DUP)** | The same work order tapped again at the station it is already at, ignored so it isn't counted twice |
| **Log** | The technical record of every scan event, including the ones set aside |
| **Backup / Restore** | Saving a copy of all data, and later putting that copy back |
| **Database** | The single file on the PC where all records are kept |
| **Export** | Saving a report as an Excel or PDF file |
