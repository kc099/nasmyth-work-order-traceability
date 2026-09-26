namespace NasmythTraceability.Services.Scanning;

/// <summary>
/// Stand-in scanner used when no hardware is attached (developer machines, the
/// self-test, and the dashboard "simulate scan" helper). Reads are pushed in
/// manually via <see cref="Emit"/>.
/// </summary>
public sealed class SimulatedScannerService : IScannerService
{
    public event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Emit(string barcode, string deviceKey, string deviceName)
        => BarcodeScanned?.Invoke(this,
            new BarcodeScannedEventArgs(barcode, deviceKey, deviceName, DateTime.Now));

    public void Dispose() => IsRunning = false;
}
