namespace NasmythTraceability.Services.Scanning;

/// <summary>A completed read from one reader at a production station.</summary>
public sealed class BarcodeScannedEventArgs : EventArgs
{
    public BarcodeScannedEventArgs(string barcode, string deviceKey, string deviceName, DateTime timestamp)
    {
        Barcode = barcode;
        DeviceKey = deviceKey;
        DeviceName = deviceName;
        Timestamp = timestamp;
    }

    /// <summary>What identifies the tag: the card uid for an RFID reader.</summary>
    public string Barcode { get; }

    /// <summary>Identity of the reader (its MAC, or its address until the MAC is known).</summary>
    public string DeviceKey { get; }

    public string DeviceName { get; }

    /// <summary>When the tag was presented.</summary>
    public DateTime Timestamp { get; }

    /// <summary>Station the reader belongs to.</summary>
    public int? StationId { get; init; }

    /// <summary>Text stored on the card (normally its work order); informational only.</summary>
    public string CardData { get; init; } = "";

    /// <summary>Why the card's data block could not be read; the uid is still valid.</summary>
    public string ReadError { get; init; } = "";
}

/// <summary>Source of tag reads (network readers or a stand-in).</summary>
public interface IScannerService : IDisposable
{
    event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;

    /// <summary>True once the service is listening for input.</summary>
    bool IsRunning { get; }
}
