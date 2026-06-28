using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using cAlgo.API;

namespace cAlgo.Robots;

// Controls whether the bot does nothing, exports current pending orders, or restores them from JSON.
public enum SnapshotMode
{
    None,
    Save,
    Restore
}

[Robot(AccessRights = AccessRights.FullAccess, AddIndicators = false)]
public class OrderManager : Robot
{
    // Choose how the bot behaves when it starts.
    [Parameter("Mode", DefaultValue = SnapshotMode.None)]
    public SnapshotMode Mode { get; set; }

    // JSON file name. Relative paths are stored under the user's Documents\cTrader\OrderManager folder.
    [Parameter("Snapshot file", DefaultValue = "OrderManager-pending-orders.json")]
    public string SnapshotFileName { get; set; }

    // If true, the bot removes all current pending orders before restoring the snapshot.
    [Parameter("Clear existing pending orders before restore", DefaultValue = false)]
    public bool ClearExistingPendingOrdersBeforeRestore { get; set; }

    // When enabled, each successfully restored order is written to the log.
    [Parameter("Log restored orders", DefaultValue = true)]
    public bool LogRestoredOrders { get; set; }

    protected override void OnStart()
    {
        // Keep startup logic simple: save once, restore once, or idle.
        switch (Mode)
        {
            case SnapshotMode.Save:
                SavePendingOrders();
                Stop();
                return;
            case SnapshotMode.Restore:
                RestorePendingOrders();
                Stop();
                return;
            default:
                Print("OrderManager started. Use Mode = Save to export pending orders or Mode = Restore to load them back.");
                return;
        }
    }

    protected override void OnTick()
    {
    }

    protected override void OnStop()
    {
    }

    private void SavePendingOrders()
    {
        // Capture every current pending order into a serializable snapshot.
        var snapshot = PendingOrders.Select(order => PendingOrderSnapshot.From(order)).ToList();

        // Write the snapshot to disk in a human-readable JSON format.
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        System.IO.File.WriteAllText(GetSnapshotPath(), json);

        Print("Saved {0} pending order(s) to {1}", snapshot.Count, GetSnapshotPath());
    }

    private void RestorePendingOrders()
    {
        var snapshotPath = GetSnapshotPath();

        // If no file exists, do nothing instead of creating fake orders.
        if (!System.IO.File.Exists(snapshotPath))
        {
            Print("Snapshot file not found: {0}", snapshotPath);
            return;
        }

        // Read the JSON snapshot and rebuild the list of pending orders.
        var json = System.IO.File.ReadAllText(snapshotPath);
        var snapshot = JsonSerializer.Deserialize<List<PendingOrderSnapshot>>(json, JsonOptions) ?? new List<PendingOrderSnapshot>();

        // Optional cleanup: remove all live pending orders before restoring the snapshot.
        if (ClearExistingPendingOrdersBeforeRestore)
        {
            foreach (var pendingOrder in PendingOrders.ToArray())
            {
                CancelPendingOrder(pendingOrder);
            }
        }

        var restoredCount = 0;

        // Restore each snapshot entry individually so one failure does not block the rest.
        foreach (var pendingOrder in snapshot)
        {
            if (TryRestorePendingOrder(pendingOrder))
            {
                restoredCount++;
            }
        }

        Print("Restored {0} pending order(s) from {1}", restoredCount, snapshotPath);
    }

    private bool TryRestorePendingOrder(PendingOrderSnapshot snapshot)
    {
        // Validate the symbol first so we can skip stale snapshot entries safely.
        var symbol = Symbols.GetSymbol(snapshot.SymbolName);

        if (symbol == null)
        {
            Print("Skipped order {0}: symbol not found ({1})", snapshot.Label, snapshot.SymbolName);
            return false;
        }

        TradeResult result;

        // Restore using the matching pending-order type and the closest available overload.
        if (snapshot.OrderType == PendingOrderType.Limit)
        {
            result = PlaceLimitOrder(
                snapshot.TradeType,
                snapshot.SymbolName,
                snapshot.VolumeInUnits,
                snapshot.TargetPrice,
                snapshot.Label,
                snapshot.StopLossPips,
                snapshot.TakeProfitPips,
                GetProtectionType(snapshot),
                snapshot.ExpirationTime,
                snapshot.Comment,
                snapshot.HasTrailingStop,
                snapshot.StopLossTriggerMethod);
        }
        else if (snapshot.OrderType == PendingOrderType.Stop)
        {
            result = PlaceStopOrder(
                snapshot.TradeType,
                snapshot.SymbolName,
                snapshot.VolumeInUnits,
                snapshot.TargetPrice,
                snapshot.Label,
                snapshot.StopLossPips,
                snapshot.TakeProfitPips,
                GetProtectionType(snapshot),
                snapshot.ExpirationTime,
                snapshot.Comment,
                snapshot.HasTrailingStop,
                snapshot.StopLossTriggerMethod);
        }
        else if (snapshot.OrderType == PendingOrderType.StopLimit)
        {
            result = PlaceStopLimitOrder(
                snapshot.TradeType,
                snapshot.SymbolName,
                snapshot.VolumeInUnits,
                snapshot.TargetPrice,
                snapshot.StopLimitRangePips ?? 0,
                snapshot.Label,
                snapshot.StopLossPips,
                snapshot.TakeProfitPips,
                GetProtectionType(snapshot),
                snapshot.ExpirationTime,
                snapshot.Comment,
                snapshot.HasTrailingStop,
                snapshot.StopLossTriggerMethod,
                snapshot.StopOrderTriggerMethod ?? StopTriggerMethod.Trade);
        }
        else
        {
            Print("Skipped order {0}: unsupported order type {1}", snapshot.Label, snapshot.OrderType);
            return false;
        }

        if (!result.IsSuccessful)
        {
            Print("Failed to restore order {0}: {1}", snapshot.Label, result.Error);
            return false;
        }

        if (LogRestoredOrders)
        {
            Print("Restored order {0}", snapshot.Label);
        }

        return true;
    }

    private string GetSnapshotPath()
    {
        // Allow advanced users to set an absolute path; otherwise keep the file in a stable app folder.
        if (Path.IsPathRooted(SnapshotFileName))
        {
            return SnapshotFileName;
        }

        // Keep snapshots away from the robot source folder so they survive rebuilds and are easy to find.
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "cTrader",
            "OrderManager");

        Directory.CreateDirectory(baseDirectory);

        return Path.Combine(baseDirectory, SnapshotFileName);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private static ProtectionType? GetProtectionType(PendingOrderSnapshot snapshot)
    {
        // cTrader uses protection type to decide whether stop-loss/take-profit values are relative or omitted.
        return snapshot.StopLossPips.HasValue || snapshot.TakeProfitPips.HasValue ? ProtectionType.Relative : null;
    }

    // A small JSON-friendly shape that keeps only the values needed to rebuild a pending order later.
    private sealed class PendingOrderSnapshot
    {
        public PendingOrderType OrderType { get; set; }

        public TradeType TradeType { get; set; }

        public string SymbolName { get; set; }

        public double VolumeInUnits { get; set; }

        public double TargetPrice { get; set; }

        public string Label { get; set; }

        public double? StopLossPips { get; set; }

        public double? TakeProfitPips { get; set; }

        public string Comment { get; set; }

        public DateTime? ExpirationTime { get; set; }

        public bool HasTrailingStop { get; set; }

        public StopTriggerMethod? StopLossTriggerMethod { get; set; }

        public StopTriggerMethod? StopOrderTriggerMethod { get; set; }

        public double? StopLimitRangePips { get; set; }

        public static PendingOrderSnapshot From(PendingOrder order)
        {
            // Copy the live order into a serializable representation.
            return new PendingOrderSnapshot
            {
                OrderType = order.OrderType,
                TradeType = order.TradeType,
                SymbolName = order.SymbolName,
                VolumeInUnits = order.VolumeInUnits,
                TargetPrice = order.TargetPrice,
                Label = order.Label,
                StopLossPips = order.StopLossPips,
                TakeProfitPips = order.TakeProfitPips,
                Comment = order.Comment,
                ExpirationTime = order.ExpirationTime,
                HasTrailingStop = order.HasTrailingStop,
                StopLossTriggerMethod = order.StopLossTriggerMethod,
                StopOrderTriggerMethod = order.StopOrderTriggerMethod,
                StopLimitRangePips = order.StopLimitRangePips
            };
        }
    }
}