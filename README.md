# RFID Reader/Writer (ESP32 + RC522) — integration guide

Each reader is an ESP32 with an MFRC522 (RC522) module. It reads MIFARE Classic
cards, can write 16 characters of custom data to them, and exposes a web UI and
a JSON HTTP API on port 80. This document is for developing PC software that
talks to one or more readers.

## 1. Quick facts

| Item | Value |
|---|---|
| Transport | HTTP, port 80, JSON, no authentication |
| Card data | 1–16 printable ASCII characters (0x20–0x7E), stored in block 2 |
| Supported cards | MIFARE Classic (1K/4K/Mini) with factory key `FFFFFFFFFFFF` |
| Mode at boot | Always `read`. Mode and write data are never saved to flash |
| Event history | Last 16 events, in RAM, lost on reboot |
| Concurrency | The reader handles one HTTP request at a time |

## 2. Finding and identifying a reader

Every reader runs two Wi-Fi interfaces at once, with the same UI and API on both:

- **Access point** — SSID `RFID-RW-XXXX` (XXXX = last two MAC bytes), password
  `rfid12345`, address `http://192.168.4.1`. Use this for first-time setup.
  Every reader has the same AP address, so the AP is not usable for talking to
  several readers from one PC.
- **Station (your router)** — set the router SSID/password in the UI or with
  `POST /api/wifi`. The reader gets an address from DHCP and is also reachable at
  `http://rfid-rw-xxxx.local` (mDNS, lower-case suffix).

For a multi-reader installation, poll each reader by its station IP address.
Give each reader a **DHCP reservation** in the router so the address never
changes. Windows resolves `.local` names only on some setups, so do not depend
on mDNS in production.

To confirm which physical reader is behind an address, read `device` and `mac`
from `GET /api/status`. `mac` is the permanent identity; store it next to the
IP in your configuration and warn if it changes.

## 3. API reference

All responses are `application/json`. `POST` bodies may be JSON
(`Content-Type: application/json`) or form encoded. Errors return HTTP 400/404
with `{"error":"reason"}`. CORS is enabled for browser-based clients.

### GET /api/status

```json
{
  "mode": "read",
  "data": "TOOL-0042",
  "once": false,
  "device": "RFID-RW-E5F6",
  "firmware": "1.2.0",
  "mac": "A1:B2:C3:D4:E5:F6",
  "hostname": "rfid-rw-e5f6.local",
  "bootId": 2873461123,
  "block": 2,
  "readerOk": true,
  "uptimeMs": 934211,
  "ap":  {"ssid": "RFID-RW-E5F6", "ip": "192.168.4.1", "clients": 0},
  "sta": {"ssid": "Factory", "connected": true, "ip": "192.168.1.57", "rssi": -58},
  "webhook": {"url": "", "lastCode": 0},
  "lastEventId": 9,
  "lastEvent": { "...": "same shape as an event, see below; absent until the first scan" }
}
```

| Field | Meaning |
|---|---|
| `mode` | `read` or `write` |
| `data` | Data that write mode assigns to cards (may be set while in read mode) |
| `once` | `true` if write mode will return to read after one successful write |
| `firmware` | Firmware version (`FW_VERSION` in the sketch) |
| `bootId` | Random number chosen at each boot. A change means the reader restarted |
| `readerOk` | `false` if the RC522 module was not detected at boot (wiring fault) |
| `uptimeMs` | Milliseconds since boot (wraps after about 49.7 days) |
| `webhook.lastCode` | HTTP status of the last webhook POST; negative = connection error; 0 = none sent |

### GET /api/mode

```json
{"mode": "write", "data": "TOOL-0042", "once": true}
```

### POST /api/mode

All fields are optional, but at least one must be present.

| Field | Type | Meaning |
|---|---|---|
| `mode` | `"read"` / `"write"` | Mode to switch to |
| `data` | string | 1–16 printable ASCII characters to assign |
| `once` | bool | With `mode:"write"`: write one card, then return to read mode |

| Request body | Result |
|---|---|
| `{"mode":"write","data":"TOOL-0042","once":true}` | One-shot: the next card successfully written gets the data, then the reader returns to read mode |
| `{"mode":"write","data":"TOOL-0042"}` | Continuous: every card presented gets the data until you send `{"mode":"read"}` |
| `{"data":"TOOL-0043"}` | Changes the data only; mode and `once` are unchanged |
| `{"mode":"write"}` | Continuous write using the data sent earlier; 400 if none was sent |
| `{"mode":"read"}` | Back to read mode (also cancels a pending one-shot) |

Notes:

- Sending `mode` without `once` means `once:false`. A one-shot must be re-armed
  for every card.
- A one-shot stays armed until a write **succeeds**. A failed attempt (wrong
  card type, card removed too early) produces a failed `write` event and the
  reader keeps waiting, so the operator can retry. Send `{"mode":"read"}` to cancel.
- A successful response is the same JSON as `GET /api/status`.

### GET /api/events?since=ID

Returns events with `id` greater than `since`, oldest first. Use `since=0` to
get everything still in memory.

```json
{
  "device": "RFID-RW-E5F6",
  "bootId": 2873461123,
  "uptimeMs": 934211,
  "lastEventId": 9,
  "events": [
    {"id": 8, "ms": 923456, "type": "read", "ok": true, "uid": "A1B2C3D4",
     "cardType": "MIFARE 1KB", "data": "TOOL-0042",
     "hex": "544F4F4C2D3030343200000000000000", "error": ""},
    {"id": 9, "ms": 925010, "type": "write", "ok": false, "uid": "04A1B2C3D4E5F6",
     "cardType": "MIFARE Ultralight or Ultralight C", "data": "", "hex": "",
     "error": "authentication failed: Timeout in communication."}
  ]
}
```

| Event field | Meaning |
|---|---|
| `id` | Increases by 1 for every card presented, starting at 1 after each boot |
| `ms` | Reader uptime when the card was processed. Event age = `uptimeMs - ms` |
| `type` | `read` or `write` — the mode the reader was in |
| `ok` | `true` if the read, or the write plus read-back verification, succeeded |
| `uid` | Card serial number, upper-case hex, 4/7/10 bytes |
| `cardType` | Card type as detected by the reader |
| `data` | Block content as text, up to the first zero byte; non-ASCII bytes shown as `.`. For a write, this is what was read back from the card |
| `hex` | The full 16 bytes as hex |
| `error` | Reason when `ok` is `false`, otherwise empty |

### POST /api/wifi

`{"ssid":"Factory","password":"secret123"}` — saved to flash; the reader then
connects. An empty `ssid` forgets the router. If you send this over the station
interface, you lose the connection as the reader switches networks.

### POST /api/webhook

`{"url":"http://192.168.1.10:8000/rfid"}` — saved to flash. Empty `url` disables
it. See section 6.

## 4. Polling for scans

Run one polling loop per reader and keep two values per reader: `bootId` and
`since`.

1. `GET /api/events?since=<since>` every 300–1000 ms, with a 3–5 s timeout.
2. If `bootId` differs from the stored one, the reader restarted: store the new
   `bootId`, set `since = 0`, and poll again. Treat the reader as being in read
   mode with no data, and re-send any mode you still need.
3. Process `events` in order and set `since` to the last `id` processed.
4. On a timeout or connection error, mark the reader offline and keep polling.

Do not use `lastEvent` from `/api/status` as the scan feed: it holds only the
newest event, so two scans between polls would lose one.

Example (Python; the same logic applies in any language):

```python
import requests, time

BASE = "http://192.168.1.57"
boot_id, since = None, 0
while True:
    try:
        r = requests.get(f"{BASE}/api/events", params={"since": since}, timeout=5).json()
        if r["bootId"] != boot_id:        # first poll, or the reader restarted
            boot_id, since = r["bootId"], 0
            continue
        for e in r["events"]:
            print(r["device"], e["type"], e["uid"], e["data"] if e["ok"] else e["error"])
            since = e["id"]
    except requests.RequestException:
        pass                              # reader offline, keep trying
    time.sleep(0.5)
```

From PowerShell, for quick tests:

```powershell
Invoke-RestMethod http://192.168.1.57/api/status
Invoke-RestMethod http://192.168.1.57/api/events?since=0
Invoke-RestMethod http://192.168.1.57/api/mode -Method Post -ContentType 'application/json' `
  -Body '{"mode":"write","data":"TOOL-0042","once":true}'
```

## 5. Assigning data to cards

### One card at a time (recommended)

1. `POST /api/mode` with `{"mode":"write","data":"<value>","once":true}`.
   Remember `lastEventId` from the response.
2. Poll `/api/events`. Wait for an event with `type:"write"` and an `id` above
   the remembered one.
3. `ok:true` — the card now holds `data`; record `uid` against the value. The
   reader is already back in read mode.
4. `ok:false` — show `error` to the operator; the reader is still armed, so
   they can present the card again. To give up, send `{"mode":"read"}`.

Apply your own timeout: if no card arrives within, say, 30 s, send
`{"mode":"read"}` so the reader is not left armed.

### Batch with the same value

`{"mode":"write","data":"<value>"}`, present the cards, then `{"mode":"read"}`.

### Reader behaviour to design around

- A card left on the reader is processed once. It must be lifted and presented
  again to be read or written again.
- The same card presented again within 2 s is ignored (debounce).
- Any card presented while the reader is in write mode is overwritten. Keep the
  write window short and prefer `once`.

## 6. Webhook (optional push)

If a webhook URL is set, the reader sends each event as it happens:

```
POST <url>
Content-Type: application/json

{"id":8,"ms":923456,"type":"read","ok":true,"uid":"A1B2C3D4","cardType":"MIFARE 1KB",
 "data":"TOOL-0042","hex":"544F...","error":"",
 "device":"RFID-RW-E5F6","mac":"A1:B2:C3:D4:E5:F6","bootId":2873461123,"mode":"read"}
```

`mode` is the reader's mode after the event (after a successful one-shot write
it is already `read`).

- A failed POST is not retried. Keep polling as the source of truth and
  de-duplicate on `mac` + `bootId` + `id`.
- The reader does nothing else while the POST is in progress. If the PC is off
  or slow, card scanning and API responses stall for a few seconds per event.
  Clear the webhook URL when the listener is not running.
- The PC needs an inbound Windows Firewall rule for the listener port.
- `https://` URLs work, but the server certificate is not verified.

## 7. Firmware update over the network (OTA)

Both methods need the update credentials, set by `OTA_USER` / `OTA_PASSWORD` at
the top of the sketch (defaults `admin` / `rfid-ota` — change them before
deploying). The firmware that is being replaced decides which password applies.

**Getting the file:** in Arduino IDE use *Sketch → Export Compiled Binary* and
take `build/esp32.esp32.esp32/RFID_RW.ino.bin` (not the `bootloader`,
`partitions` or `merged` files). Bump `FW_VERSION` before building so you can
verify the update afterwards.

**Web UI:** open the reader's page, *Firmware* section, choose the `.bin`, enter
the password, *Upload & restart*.

**API** — `POST /api/update`, multipart form upload with HTTP basic auth:

```powershell
curl.exe -u admin:rfid-ota -F "firmware=@RFID_RW.ino.bin" http://192.168.1.57/api/update
```

| Response | Meaning |
|---|---|
| 200 `{"ok":true,"message":"firmware updated, rebooting"}` | Image accepted; the reader restarts about 1 s later |
| 401 `{"error":"wrong update user or password"}` | Nothing was written |
| 500 `{"error":"..."}` | Image rejected or upload incomplete; the old firmware keeps running |

After a 200, wait for the reader to come back (10–20 s), then check that
`bootId` changed and `firmware` shows the new version. To update many readers,
do them one at a time and verify each before moving on.

**Arduino IDE:** the reader appears under *Tools → Port → Network ports* as
`rfid-rw-xxxx`; upload as usual and enter the password when asked. The PC must
be on the same network and allow the IDE through Windows Firewall.

Notes:

- The first installation of an OTA-capable firmware must be done over USB.
- The board's partition scheme must have two app slots (the default
  "Default 4MB with spiffs" does; "Huge APP / No OTA" does not). The firmware
  currently uses about 89 % of a 1.3 MB slot.
- Saved Wi-Fi and webhook settings survive an update. Mode, write data and the
  event history do not (the reader reboots into read mode).
- The reader does not scan cards or answer other requests during the upload.
- If power is lost during an upload the old firmware stays in place. A new
  firmware that crashes at boot is not rolled back automatically and needs USB.
- The password travels unencrypted over HTTP; use OTA on a trusted network only.

## 8. Known limitations and things to watch

| Topic | Detail |
|---|---|
| No authentication | Anyone on the same network, or on the reader's AP, can change modes and overwrite cards. Keep readers on a trusted network and change `AP_PASSWORD` in the sketch |
| One controller per reader | The reader has a single mode and data value. If two programs (or the web UI and your software) set it, the last request wins. Read `/api/status` before assuming the mode |
| Router subnet | The router network must not be `192.168.4.x`; that range is used by the reader's own AP |
| Many readers on one site | Every reader broadcasts its own AP, on the same Wi-Fi channel as the router. This is harmless for a handful of readers |
| Router unreachable | The reader retries every 15 s. AP clients may see short interruptions during each retry |
| Open web UI pages | Each open UI tab polls the reader about twice a second and competes with your software. Close them in production |
| Card security | Cards use the factory default key and the data is stored unencrypted. Anyone with an NFC writer can read or change it. Treat `uid` as the trustworthy identifier and `data` as a convenience label |
| Unsupported cards | Ultralight/NTAG/DESFire cards and Classic cards with changed keys produce an event with `ok:false` and an authentication error; the `uid` is still reported |
| Timestamps | The reader has no clock. Timestamp events on the PC, or use `uptimeMs - ms` for the event's age |
| Missed events | More than 16 scans between two polls, or a reboot before the next poll, loses events |

## 9. Firmware source

- `RFID_RW.ino` — firmware; settings (AP password, block number, debounce,
  retry interval) are constants at the top.
- `index_html.h` — the web UI page.
- `originals/` — the two example sketches this was merged from.

Build with Arduino IDE: board "ESP32 Dev Module", esp32 core 3.x, libraries
`MFRC522v2` and `ArduinoJson` 7.

Wiring (default SPI pins): SDA/SS = GPIO 5, SCK = 18, MOSI = 23, MISO = 19,
3.3 V, GND.
