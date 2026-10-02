using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace NasmythTraceability.Services.Scanning;

/// <summary>
/// Reads USB "keyboard wedge" readers (RFID card readers, barcode scanners) through the
/// Win32 Raw Input API. Each keystroke is attributed to the exact HID device it came from,
/// so multiple readers on one PC can be told apart and mapped to different stations.
/// A read completes on Enter or Tab.
/// </summary>
public sealed class RawInputScannerService : IScannerService
{
    private const int WM_INPUT = 0x00FF;
    private const int RID_INPUT = 0x10000003;
    private const int RIDI_DEVICENAME = 0x20000007;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const int RIM_TYPEKEYBOARD = 1;
    private const ushort RI_KEY_BREAK = 0x01;

    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;

    private const int MaxBarcodeLength = 128;

    // A reader sends a whole card number in a few milliseconds per key; a person cannot
    // type that fast. An unlinked device is treated as a reader only when every key of a
    // read arrives within this gap and the read is at least this long.
    private const int MaxReaderKeyGapMs = 50;
    private const int MinAutoDetectLength = 4;

    // Key events that reach the window this soon after a reader keystroke are the
    // reader's own, see IsReaderTyping.
    private const int ReaderKeyEchoMs = 50;

    private readonly Dictionary<IntPtr, StringBuilder> _buffers = new();
    private readonly Dictionary<IntPtr, string> _deviceNames = new();
    private readonly Dictionary<IntPtr, int> _lastKeyAt = new();
    private readonly HashSet<IntPtr> _shiftDown = new();

    // These HID devices are linked readers. The regular keyboard is never in this set.
    // Other devices are only listened to for auto-detection of a new reader.
    private readonly HashSet<string> _allowedDeviceKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _knownReaderIds = new();
    private readonly object _allowedLock = new();
    private long _lastReaderKeyTick = long.MinValue / 2;

    private HwndSource? _source;
    private bool _disposed;

    public event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// When true, a read from a device that is not linked yet but types like a reader is
    /// raised with <see cref="BarcodeScannedEventArgs.IsNewDevice"/> set.
    /// </summary>
    public bool AutoDetect { get; set; } = true;

    /// <summary>
    /// True while a reader is sending a read. A keyboard-type reader also types into
    /// whatever has focus; the window uses this to drop those keystrokes.
    /// </summary>
    public bool IsReaderTyping => Environment.TickCount64 - _lastReaderKeyTick <= ReaderKeyEchoMs;

    /// <summary>Replaces the set of scanner device paths that are read as scanners.</summary>
    public void SetAllowedDevices(IEnumerable<string> deviceKeys)
    {
        lock (_allowedLock)
        {
            _allowedDeviceKeys.Clear();
            foreach (var key in deviceKeys)
                if (!string.IsNullOrEmpty(key))
                    _allowedDeviceKeys.Add(key);
        }
    }

    /// <summary>
    /// Hardware ids (e.g. "VID_FFFF&amp;PID_0035") of reader models that are always treated
    /// as readers, without the typing-speed check.
    /// </summary>
    public void SetKnownReaderIds(IEnumerable<string> ids)
    {
        lock (_allowedLock)
        {
            _knownReaderIds.Clear();
            foreach (var id in ids)
                if (!string.IsNullOrWhiteSpace(id))
                    _knownReaderIds.Add(id.Trim());
        }
    }

    private bool IsAllowed(string deviceKey)
    {
        lock (_allowedLock)
            return _allowedDeviceKeys.Contains(deviceKey);
    }

    private bool IsKnownReader(string deviceKey)
    {
        lock (_allowedLock)
            return _knownReaderIds.Any(id => deviceKey.Contains(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Hooks the given top-level window handle and registers for keyboard raw input.</summary>
    public void Attach(IntPtr hwnd)
    {
        if (IsRunning || hwnd == IntPtr.Zero)
            return;

        _source = HwndSource.FromHwnd(hwnd);
        if (_source is null)
            throw new InvalidOperationException("Window handle is not an HwndSource.");

        _source.AddHook(WndProc);

        var rid = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01, // generic desktop
            usUsage = 0x06,     // keyboard
            dwFlags = RIDEV_INPUTSINK,
            hwndTarget = hwnd,
        };

        if (!RegisterRawInputDevices(new[] { rid }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new InvalidOperationException(
                $"RegisterRawInputDevices failed (Win32 {Marshal.GetLastWin32Error()}).");

        IsRunning = true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_INPUT)
            ProcessRawInput(lParam);
        return IntPtr.Zero;
    }

    private void ProcessRawInput(IntPtr lParam)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
            return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size)
                return;

            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (raw.header.dwType != RIM_TYPEKEYBOARD)
                return;

            var kb = raw.keyboard;
            var isBreak = (kb.Flags & RI_KEY_BREAK) != 0;
            var device = raw.header.hDevice;

            var deviceKey = ResolveDeviceName(device);
            if (string.IsNullOrEmpty(deviceKey))
                return;

            // Linked readers are always read. Anything else (the PC keyboard, typing in
            // other apps, a reader nobody has linked yet) is only watched for auto-detection.
            var linked = IsAllowed(deviceKey);
            if (!linked && !AutoDetect)
                return;

            // An unlinked device only counts as a reader while its keys arrive faster than a
            // person can type, unless it is a known reader model.
            var bySpeed = !linked && !IsKnownReader(deviceKey);
            if (!bySpeed)
                _lastReaderKeyTick = Environment.TickCount64;

            // Track shift per device (keyboard wedge sends its own shifts).
            if (kb.VKey is VK_SHIFT or VK_LSHIFT or VK_RSHIFT)
            {
                if (isBreak) _shiftDown.Remove(device);
                else _shiftDown.Add(device);
                return;
            }

            if (isBreak || kb.VKey == 0xFF)
                return;

            _buffers.TryGetValue(device, out var sb);

            // A slow key starts the read over, so ordinary typing never builds up here.
            if (bySpeed)
            {
                var now = GetMessageTime();
                if (_lastKeyAt.TryGetValue(device, out var last) && unchecked(now - last) > MaxReaderKeyGapMs)
                    sb?.Clear();
                _lastKeyAt[device] = now;
            }

            if (kb.VKey is VK_RETURN or VK_TAB)
            {
                if (sb is null || sb.Length == 0)
                    return;

                if (bySpeed && sb.Length < MinAutoDetectLength)
                {
                    sb.Clear();
                    return;
                }

                // Now known to be a reader: keep this Enter from submitting the focused box.
                _lastReaderKeyTick = Environment.TickCount64;
                Flush(sb, deviceKey, isNewDevice: !linked);
                return;
            }

            var ch = MapKey(kb.VKey, _shiftDown.Contains(device));
            if (ch == '\0')
                return;

            if (sb is null)
                _buffers[device] = sb = new StringBuilder();

            if (sb.Length < MaxBarcodeLength)
                sb.Append(ch);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void Flush(StringBuilder sb, string deviceKey, bool isNewDevice)
    {
        var barcode = sb.ToString();
        sb.Clear();

        BarcodeScanned?.Invoke(this,
            new BarcodeScannedEventArgs(barcode, deviceKey, FriendlyName(deviceKey), DateTime.Now)
            {
                IsNewDevice = isNewDevice,
            });
    }

    private string ResolveDeviceName(IntPtr device)
    {
        if (device == IntPtr.Zero)
            return "";

        if (_deviceNames.TryGetValue(device, out var cached))
            return cached;

        uint size = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size == 0)
        {
            _deviceNames[device] = "";
            return "";
        }

        var buffer = Marshal.AllocHGlobal((int)size * 2);
        try
        {
            var written = GetRawInputDeviceInfo(device, RIDI_DEVICENAME, buffer, ref size);
            var name = written > 0 ? Marshal.PtrToStringUni(buffer) ?? "" : "";
            _deviceNames[device] = name;
            return name;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Turns a device path into something readable, e.g. "HID VID_05E0 PID_1200".</summary>
    public static string FriendlyName(string deviceKey)
    {
        if (string.IsNullOrEmpty(deviceKey))
            return "Unknown device";

        var vid = ExtractToken(deviceKey, "VID_");
        var pid = ExtractToken(deviceKey, "PID_");
        if (vid.Length > 0 || pid.Length > 0)
            return $"HID VID_{vid} PID_{pid}".Trim();

        return deviceKey.Length > 40 ? deviceKey[^40..] : deviceKey;
    }

    private static string ExtractToken(string s, string prefix)
    {
        var i = s.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        i += prefix.Length;
        var end = i;
        while (end < s.Length && Uri.IsHexDigit(s[end])) end++;
        return s[i..end];
    }

    private static char MapKey(ushort vk, bool shift)
    {
        switch (vk)
        {
            case >= 0x30 and <= 0x39: return (char)vk;                         // 0-9
            case >= 0x60 and <= 0x69: return (char)('0' + (vk - 0x60));        // numpad 0-9
            case >= 0x41 and <= 0x5A:                                          // A-Z
                var baseChar = shift ? 'A' : 'a';
                return (char)(baseChar + (vk - 0x41));
            case 0x20: return ' ';
            case 0x6F: return '/';   // numpad divide
            case 0x6A: return '*';   // numpad multiply
            case 0x6D: return '-';   // numpad minus
            case 0x6B: return '+';   // numpad plus
            case 0x6E: return '.';   // numpad decimal
            case 0xBD: return shift ? '_' : '-';
            case 0xBB: return shift ? '+' : '=';
            case 0xBE: return shift ? '>' : '.';
            case 0xBC: return shift ? '<' : ',';
            case 0xBF: return shift ? '?' : '/';
            case 0xC0: return shift ? '~' : '`';
            case 0xDB: return shift ? '{' : '[';
            case 0xDD: return shift ? '}' : ']';
            case 0xDC: return shift ? '|' : '\\';
            case 0xBA: return shift ? ':' : ';';
            case 0xDE: return shift ? '"' : '\'';
            default: return '\0';
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        IsRunning = false;
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    // ------------------------------------------------------------ interop

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWINPUT
    {
        [FieldOffset(0)] public RAWINPUTHEADER header;
        [FieldOffset(24)] public RAWKEYBOARD keyboard; // 64-bit RAWINPUTHEADER is 24 bytes
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray)] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

    [DllImport("user32.dll")]
    private static extern int GetMessageTime();
}
