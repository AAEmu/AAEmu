using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Persistent, character-owned farmhand state.</summary>
public sealed class CharacterButler(uint characterId)
{
    private readonly Dictionary<sbyte, ulong> _permanentDatas = [];
    private readonly Dictionary<long, ButlerHarvestJob> _harvestJobs = [];
    private readonly Dictionary<ulong, ButlerStoredItem> _storedItems = [];

    /// <summary>
    /// Serializes durable farmhand operations for this character. Callers acquire this before
    /// <see cref="SyncRoot"/> and must not hold ButlerManager's registry lock while waiting.
    /// </summary>
    internal object OperationSyncRoot { get; } = new();
    internal object SyncRoot { get; } = new();
    internal bool IsDeleted { get; set; }

    public uint CharacterId { get; } = characterId;
    public uint HouseId { get; internal set; }
    public string Name { get; internal set; } = string.Empty;
    public uint LaborPower { get; internal set; }
    public ushort LpChargedAmount { get; internal set; }
    public long LpChargeResetTime { get; internal set; }
    public ushort RemainProductionCost { get; internal set; }

    public IReadOnlyDictionary<sbyte, ulong> PermanentDatas => _permanentDatas;
    public IReadOnlyDictionary<long, ButlerHarvestJob> HarvestJobs => _harvestJobs;
    public IReadOnlyDictionary<ulong, ButlerStoredItem> StoredItems => _storedItems;

    internal CharacterButlerRecord Snapshot() =>
        new(CharacterId, HouseId, Name, LaborPower, LpChargedAmount, RemainProductionCost,
            LpChargeResetTime);

    internal void Apply(CharacterButlerRecord record)
    {
        HouseId = record.HouseId;
        Name = record.Name ?? string.Empty;
        LaborPower = record.LaborPower;
        LpChargedAmount = record.LpChargedAmount;
        LpChargeResetTime = record.LpChargeResetTime;
        RemainProductionCost = record.RemainProductionCost;
    }

    internal void ApplyLoadedState(CharacterButlerStateRecord state)
    {
        Apply(state.Butler);
        _permanentDatas.Clear();
        foreach (var (key, value) in state.PermanentDatas)
            _permanentDatas[key] = value;
        _harvestJobs.Clear();
        foreach (var job in state.HarvestJobs)
            _harvestJobs.Add(job.JobId, job);
        _storedItems.Clear();
        foreach (var item in state.StoredItems ?? Array.Empty<ButlerStoredItem>())
            _storedItems.Add(item.ItemId, item);
    }

    internal IReadOnlyDictionary<sbyte, ulong> SnapshotPermanentDatas() =>
        new Dictionary<sbyte, ulong>(_permanentDatas);

    internal IReadOnlyList<ButlerHarvestJob> SnapshotHarvestJobs() => [.. _harvestJobs.Values];

    internal void ApplyPermanentData(sbyte key, ulong value) => _permanentDatas[key] = value;

    internal void RemovePermanentData(sbyte key) => _permanentDatas.Remove(key);

    internal void ApplyHarvestJob(ButlerHarvestJob job) => _harvestJobs[job.JobId] = job;

    internal bool RemoveHarvestJob(long jobId) => _harvestJobs.Remove(jobId);

    internal void ClearHarvestJobs() => _harvestJobs.Clear();

    internal IReadOnlyList<ButlerStoredItem> SnapshotStoredItems() => [.. _storedItems.Values];

    internal void ApplyStoredItem(ButlerStoredItem item) => _storedItems[item.ItemId] = item;

    internal bool RemoveStoredItem(ulong itemId) => _storedItems.Remove(itemId);

    internal void ClearStoredItems() => _storedItems.Clear();

    public void Save(MySql.Data.MySqlClient.MySqlConnection connection,
        MySql.Data.MySqlClient.MySqlTransaction transaction) =>
        ButlerManager.Instance.Save(this, connection, transaction);

    public static ButlerInfoWire ResetWire =>
        ButlerInfoWire.Empty(0, CharacterBlocked.LocalWorldId, string.Empty, 0, 0, 0, 0);

    internal ButlerInfoWire FreeWire =>
        ButlerInfoWire.Empty(0, CharacterBlocked.LocalWorldId, Name, 0, LaborPower, LpChargedAmount, 0);
}

public readonly record struct CharacterButlerRecord(
    uint CharacterId,
    uint HouseId,
    string Name,
    uint LaborPower,
    ushort LpChargedAmount,
    ushort RemainProductionCost,
    long LpChargeResetTime = 0);

public readonly record struct CharacterButlerStateRecord(
    CharacterButlerRecord Butler,
    IReadOnlyDictionary<sbyte, ulong> PermanentDatas,
    IReadOnlyList<ButlerHarvestJob> HarvestJobs,
    IReadOnlyList<ButlerStoredItem> StoredItems = null);

/// <summary>
/// Logical farmhand item location. The actual persistent item remains in the character's System container.
/// </summary>
public readonly record struct ButlerStoredItem(byte Type, ulong ItemId);
