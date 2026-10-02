namespace NasmythTraceability.Services.Scanning;

/// <summary>A completed read from one physical scanner.</summary>
public sealed class BarcodeScannedEventArgs : EventArgs
{
    public BarcodeScannedEventArgs(string barcode, string deviceKey, string deviceName, DateTime timestamp)
    {
        Barcode = barcode;
        DeviceKey = deviceKey;
        DeviceName = deviceName;
        Timestamp = timestamp;
    }

    public string Barcode { get; }

    /// <summary>Raw Input device path; empty when the source device could not be identified.</summary>
    public string DeviceKey { get; }

    public string DeviceName { get; }

    public DateTime Timestamp { get; }

    /// <summary>
    /// True when the read came from a device that is not linked to a station yet but
    /// behaves like a reader, so it can be linked automatically.
    /// </summary>
    public bool IsNewDevice { get; init; }
}

/// <summary>Source of barcode reads (real hardware or a stand-in).</summary>
public interface IScannerService : IDisposable
{
    event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;

    /// <summary>True once the service is listening for input.</summary>
    bool IsRunning { get; }
}
