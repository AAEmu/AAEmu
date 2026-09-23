using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Residents;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Resident point and charge settlement.
///
/// Service points and local/hunting charge accumulate per character and zone group in
/// <c>character_resident_state</c>. Stockpile notice text is not a contribution threshold, and a
/// settlement does not move tribute doodads — those advance through their own devote chain.
/// </summary>
public class ResidentManager : Singleton<ResidentManager>, ILoadable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _lock = new();
    private Dictionary<(uint Owner, ushort ZoneGroup), CharacterResidentState> _states = [];
    private IResidentStateStore _store = new InMemoryResidentStateStore();

    /// <summary>Reads the persisted settlement.</summary>
    public void Load()
    {
        lock (_lock)
        {
            _store = new MySqlResidentStateStore();
            LoadFromStoreNoLock();
        }
    }

    /// <summary>For tests / boot: swaps the store (null resets to in-memory) — the store itself is not reloaded.</summary>
    internal void UseStore(IResidentStateStore store)
    {
        lock (_lock)
            _store = store ?? new InMemoryResidentStateStore();
    }

    /// <summary>Replaces the in-memory state with what the current store holds — a restart round-trip.</summary>
    internal void LoadFromStore()
    {
        lock (_lock)
            LoadFromStoreNoLock();
    }

    /// <summary>For tests: back to an empty in-memory store.</summary>
    internal void ResetForTest()
    {
        lock (_lock)
        {
            _store = new InMemoryResidentStateStore();
            _states = [];
        }
    }

    private void LoadFromStoreNoLock()
    {
        _states = [];
        foreach (var row in _store.LoadAll())
            _states[(row.OwnerId, row.ZoneGroupId)] = row;
        Logger.Info("Resident state: loaded {0} character row(s)", _states.Count);
    }

    /// <summary>One character's settled row for a zone group, or null when nothing has been contributed yet.</summary>
    public CharacterResidentState GetState(uint characterId, ushort zoneGroup)
    {
        lock (_lock)
            return _states.GetValueOrDefault((characterId, zoneGroup));
    }

    public uint GetServicePoint(uint characterId, ushort zoneGroup) =>
        GetState(characterId, zoneGroup)?.ServicePoint ?? 0u;

    public ulong GetCharge(uint characterId, ushort zoneGroup) =>
        GetState(characterId, zoneGroup)?.Charge ?? 0ul;

    /// <summary>Zone aggregate contribution — what the board thresholds are measured against.</summary>
    public uint GetZonePointSum(ushort zoneGroup)
    {
        lock (_lock)
        {
            ulong sum = 0;
            foreach (var (key, row) in _states)
                if (key.ZoneGroup == zoneGroup)
                    sum += row.ServicePoint;
            return (uint)Math.Min(uint.MaxValue, sum);
        }
    }

    /// <summary>Zone aggregate local charge — shop and craft fees, not the hunting pool.</summary>
    public ulong GetZoneChargeSum(ushort zoneGroup)
    {
        lock (_lock)
        {
            ulong sum = 0;
            foreach (var (key, row) in _states)
                if (key.ZoneGroup == zoneGroup)
                    sum = row.Charge > ulong.MaxValue - sum ? ulong.MaxValue : sum + row.Charge;
            return sum;
        }
    }

    /// <summary>Zone aggregate hunting charge. The townhall's second money field is this pool.</summary>
    public ulong GetZoneHuntingChargeSum(ushort zoneGroup)
    {
        lock (_lock)
        {
            ulong sum = 0;
            foreach (var (key, row) in _states)
                if (key.ZoneGroup == zoneGroup)
                    sum = row.HuntingCharge > ulong.MaxValue - sum ? ulong.MaxValue : sum + row.HuntingCharge;
            return sum;
        }
    }

    /// <summary>Every settled row for one zone group (the Residents tab joins these with house owners).</summary>
    public IReadOnlyList<CharacterResidentState> GetZoneMembers(ushort zoneGroup)
    {
        lock (_lock)
            return _states.Where(pair => pair.Key.ZoneGroup == zoneGroup)
                .Select(pair => pair.Value)
                .ToList();
    }

    /// <summary>Settles resident service points for one character and zone group.</summary>
    public ResidentSettleStatus AddServicePoint(uint characterId, short zoneGroupId, uint point)
    {
        if (!IsValidZoneGroup(zoneGroupId))
        {
            Logger.Warn("Resident settlement: zone group {0} is not a valid zone group; {1} point(s) for character {2} refused",
                zoneGroupId, point, characterId);
            return ResidentSettleStatus.Refused;
        }

        var zoneGroup = (ushort)zoneGroupId;
        UpsertCharacterRow(characterId, zoneGroup, row => row with
        {
            ServicePoint = (uint)Math.Min(uint.MaxValue, (ulong)row.ServicePoint + point),
        });

        return ResidentSettleStatus.Settled;
    }

    /// <summary>
    /// Settles one charge (copper) into the character's resident balance for a zone group.
    /// </summary>
    /// <remarks>
    /// <paramref name="moneyAmount"/> is local charge and <paramref name="moneyAmount2"/> is hunting
    /// charge. <paramref name="type2"/> is still unresolved: a non-zero value refuses the settlement
    /// and writes nothing.
    /// </remarks>
    public ResidentSettleStatus AddCharge(uint characterId, short zoneGroupId, ulong type2, ulong moneyAmount, ulong moneyAmount2)
    {
        if (!IsValidZoneGroup(zoneGroupId))
        {
            Logger.Warn("Resident settlement: zone group {0} is not a valid zone group; {1} charge for character {2} refused",
                zoneGroupId, moneyAmount, characterId);
            return ResidentSettleStatus.Refused;
        }

        if (type2 != 0)
        {
            Logger.Warn("Resident charge: type2 {0} has no modelled meaning (unresolved 10.0.2.13 semantics); charge of {1} copper for zone group {2} refused",
                type2, moneyAmount, zoneGroupId);
            return ResidentSettleStatus.Refused;
        }

        if (moneyAmount == 0 && moneyAmount2 == 0)
            return ResidentSettleStatus.Settled;

        var zoneGroup = (ushort)zoneGroupId;
        UpsertCharacterRow(characterId, zoneGroup, row => row with
        {
            Charge = moneyAmount > ulong.MaxValue - row.Charge ? ulong.MaxValue : row.Charge + moneyAmount,
            HuntingCharge = moneyAmount2 > ulong.MaxValue - row.HuntingCharge
                ? ulong.MaxValue
                : row.HuntingCharge + moneyAmount2,
        });

        // The charge settles into the balance; it does not move the development level.
        return ResidentSettleStatus.Settled;
    }

    private void UpsertCharacterRow(uint characterId, ushort zoneGroup, Func<CharacterResidentState, CharacterResidentState> mutate)
    {
        lock (_lock)
        {
            var key = (characterId, zoneGroup);
            var current = _states.GetValueOrDefault(key) ??
                          new CharacterResidentState(characterId, zoneGroup, 0, 0, 0, ServerCalendarNow());
            var row = mutate(current) with { UpdatedAt = ServerCalendarNow() };
            _states[key] = row;
            if (!_store.UpsertCharacterState(row))
                Logger.Error("Resident settlement: character {0} zone group {1} could not be persisted (see SQL/updates/2026-09-23_aaemu_game_resident_state.sql)",
                    characterId, zoneGroup);
        }
    }

    private static bool IsValidZoneGroup(short zoneGroupId) => zoneGroupId > 0;

    private static DateTime ServerCalendarNow() => ServerCalendar.AsUtc(DateTime.UtcNow);
}
