using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace NasmythTraceability.Services.Scanning;

/// <summary>
/// Reads USB "keyboard wedge" barcode scanners through the Win32 Raw Input API.
/// Each keystroke is attributed to the exact HID device it came from, so multiple
/// scanners on one PC can be told apart and mapped to different stations.
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

    private readonly Dictionary<IntPtr, StringBuilder> _buffers = new();
    private readonly Dictionary<IntPtr, string> _deviceNames = new();
    private readonly HashSet<IntPtr> _shiftDown = new();

    // Only these HID devices are read as scanners. The regular keyboard is never in
    // this set, so typing in other apps is ignored. Detection mode lifts the filter
    // so a new scanner can be identified in Settings.
    private readonly HashSet<string> _allowedDeviceKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _allowedLock = new();
    private volatile bool _detectionMode;

    private HwndSource? _source;
    private bool _disposed;

    public event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;

    public bool IsRunning { get; private set; }

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

    /// <summary>When true, every keyboard device is captured (used by the Settings "Detect" flow).</summary>
    public void SetDetectionMode(bool on)
    {
        _detectionMode = on;
        if (!on)
            _buffers.Clear();
    }

    private bool ShouldCapture(IntPtr device, out string deviceKey)
    {
        deviceKey = ResolveDeviceName(device);
        if (_detectionMode)
            return true;
        if (string.IsNullOrEmpty(deviceKey))
            return false;
        lock (_allowedLock)
            return _allowedDeviceKeys.Contains(deviceKey);
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

            // Ignore everything from devices that are not mapped scanners (the PC keyboard,
            // typing in other apps, etc.) unless we are in detection mode.
            if (!ShouldCapture(device, out var deviceKey))
                return;

            // Track shift per device (keyboard wedge sends its own shifts).
            if (kb.VKey is VK_SHIFT or VK_LSHIFT or VK_RSHIFT)
            {
                if (isBreak) _shiftDown.Remove(device);
                else _shiftDown.Add(device);
                return;
            }

            if (isBreak || kb.VKey == 0xFF)
                return;

            if (kb.VKey is VK_RETURN or VK_TAB)
            {
                Flush(device, deviceKey);
                return;
            }

            var ch = MapKey(kb.VKey, _shiftDown.Contains(device));
            if (ch == '\0')
                return;

            if (!_buffers.TryGetValue(device, out var sb))
                _buffers[device] = sb = new StringBuilder();

            if (sb.Length < MaxBarcodeLength)
                sb.Append(ch);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void Flush(IntPtr device, string deviceKey)
    {
        if (!_buffers.TryGetValue(device, out var sb) || sb.Length == 0)
            return;

        var barcode = sb.ToString();
        sb.Clear();

        BarcodeScanned?.Invoke(this,
            new BarcodeScannedEventArgs(barcode, deviceKey, FriendlyName(deviceKey), DateTime.Now));
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
}
