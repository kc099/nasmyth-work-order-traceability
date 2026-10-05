namespace NasmythTraceability.Services.Scanning;

/// <summary>
/// Stand-in scanner used when no hardware is attached (the self-test, the data tools).
/// Reads are pushed in manually via <see cref="Emit"/>.
/// </summary>
public sealed class SimulatedScannerService : IScannerService
{
    public event EventHandler<BarcodeScannedEventArgs>? BarcodeScanned;

    public bool IsRunning { get; private set; }

    public void Start() => IsRunning = true;

    public void Emit(string tagId, int stationId, string deviceName = "Simulated reader", string cardData = "")
        => BarcodeScanned?.Invoke(this,
            new BarcodeScannedEventArgs(tagId, "SIM-" + stationId, deviceName, DateTime.Now)
            {
                StationId = stationId,
                CardData = cardData,
            });

    public void Dispose() => IsRunning = false;
}
