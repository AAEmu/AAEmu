using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Residents;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Resident point/charge settlement and the local-development state machine.
///
/// Contribution (resident service points from CSAddResidentServicePoint and the
/// QuestActSupplyResidentPoint reward) accumulates per character and zone group in
/// <c>character_resident_state</c>. Every settlement then runs the zone group's development:
/// distinct board thresholds crossed (parsed from <c>local_developments_boards.show_text</c>) is
/// the development level, the level picks <c>doodad_phase_N</c>, and the spawned almighty/board
/// doodads are moved there with DoChangePhase — which broadcasts SCDoodadPhaseChanged itself.
/// The applied level/phases are persisted in <c>local_development_state</c>.
/// </summary>
public class ResidentManager : Singleton<ResidentManager>, ILoadable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _lock = new();
    private Dictionary<(uint Owner, ushort ZoneGroup), CharacterResidentState> _states = [];
    private Dictionary<ushort, LocalDevelopmentState> _developmentStates = [];
    private IResidentStateStore _store = new InMemoryResidentStateStore();

    /// <summary>
    /// Test seam for the phase apply step: tests record plans instead of needing spawned doodads.
    /// Null (production) applies the plan to the spawned almighty/board doodads, best-effort.
    /// </summary>
    internal Action<LocalDevelopmentDefinition, LocalDevelopmentPlan> PhaseApplier { get; set; }

    /// <summary>Reads the persisted settlement and development state.</summary>
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
            _developmentStates = [];
        }
    }

    private void LoadFromStoreNoLock()
    {
        _states = [];
        _developmentStates = [];
        foreach (var row in _store.LoadAll())
            _states[(row.OwnerId, row.ZoneGroupId)] = row;
        foreach (var state in _store.LoadDevelopmentStates())
            _developmentStates[state.ZoneGroupId] = state;
        Logger.Info("Resident state: loaded {0} character row(s), {1} development state(s)",
            _states.Count, _developmentStates.Count);
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

    /// <summary>Zone aggregate charge — the resident balance the townhall shows.</summary>
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

    /// <summary>Every settled row for one zone group (the Residents tab joins these with house owners).</summary>
    public IReadOnlyList<CharacterResidentState> GetZoneMembers(ushort zoneGroup)
    {
        lock (_lock)
            return _states.Where(pair => pair.Key.ZoneGroup == zoneGroup)
                .Select(pair => pair.Value)
                .ToList();
    }

    /// <summary>The last applied development state for a zone group, or null when nothing has been applied yet.</summary>
    public LocalDevelopmentState GetDevelopmentState(ushort zoneGroup)
    {
        lock (_lock)
            return _developmentStates.GetValueOrDefault(zoneGroup);
    }

    /// <summary>
    /// Settles resident service points for one character and zone group, then runs the
    /// development state machine for that zone group.
    /// </summary>
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

        // Charge does not feed the ladder; only points are contribution.
        return RunDevelopment(zoneGroup)
            ? ResidentSettleStatus.Settled
            : ResidentSettleStatus.SettledDevelopmentSkipped;
    }

    /// <summary>
    /// Settles one charge (copper) into the character's resident balance for a zone group.
    /// </summary>
    /// <remarks>
    /// <paramref name="type2"/> and <paramref name="moneyAmount2"/> are the two CSAddResidentCharge
    /// fields whose 10.0.2.13 meaning is not pinned. They are not guessed at: a non-zero value in
    /// either refuses the whole settlement, loudly, with nothing written.
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

        if (moneyAmount2 != 0)
        {
            Logger.Warn("Resident charge: second moneyAmount {0} has no modelled meaning (unresolved 10.0.2.13 semantics); charge of {1} copper for zone group {2} refused",
                moneyAmount2, moneyAmount, zoneGroupId);
            return ResidentSettleStatus.Refused;
        }

        if (moneyAmount == 0)
            return ResidentSettleStatus.Settled;

        var zoneGroup = (ushort)zoneGroupId;
        UpsertCharacterRow(characterId, zoneGroup, row => row with
        {
            Charge = moneyAmount > ulong.MaxValue - row.Charge ? ulong.MaxValue : row.Charge + moneyAmount,
        });

        // The charge settles into the balance; it does not move the development level.
        return ResidentSettleStatus.Settled;
    }

    /// <summary>
    /// The state machine: contribution -&gt; distinct board thresholds -&gt; level phase apply +
    /// persist. Returns false (loud skip) when <c>local_developments</c> has no row for the zone group.
    /// </summary>
    private bool RunDevelopment(ushort zoneGroup)
    {
        var definition = LocalDevelopmentGameData.Instance.GetByZoneGroup(zoneGroup);
        if (definition == null)
        {
            Logger.Warn("Local development: no local_developments row for zone group {0}; contribution phase skipped", zoneGroup);
            return false;
        }

        var plan = LocalDevelopmentRules.Evaluate(definition, GetZonePointSum(zoneGroup));
        if (plan.DoodadPhase == null)
            Logger.Warn("Local development {0} (zone group {1}): doodad_phase_{2} is not defined in local_developments; almighty phase skipped",
                definition.Id, zoneGroup, plan.Level);

        if (PhaseApplier != null)
            PhaseApplier(definition, plan);
        else
            ApplyToWorld(definition, plan);

        var next = new LocalDevelopmentState(
            zoneGroup,
            plan.Level,
            plan.DoodadPhase ?? 0u,
            plan.BoardPhase ?? 0u,
            ServerCalendarNow());

        lock (_lock)
        {
            var persisted = _developmentStates.GetValueOrDefault(zoneGroup);
            var changed = persisted == null ||
                          persisted.DevelopmentLevel != next.DevelopmentLevel ||
                          persisted.DoodadPhase != next.DoodadPhase ||
                          persisted.BoardPhase != next.BoardPhase;
            if (!changed)
                return true;

            _developmentStates[zoneGroup] = next;
            if (!_store.UpsertDevelopmentState(next))
                Logger.Error("Local development: could not persist development state for zone group {0} (see SQL/updates/2026-09-23_aaemu_game_resident_state.sql)",
                    zoneGroup);
        }

        Logger.Info("Local development {0} (zone group {1}): contribution {2} -> level {3}, doodad phase {4}, board phase {5}",
            definition.Id, zoneGroup, plan.Contribution, plan.Level, plan.DoodadPhase, plan.BoardPhase);
        return true;
    }

    /// <summary>
    /// Best-effort world apply: move every spawned almighty/board doodad that is not already in
    /// the target phase. DoChangePhase broadcasts SCDoodadPhaseChanged. A doodad that is not
    /// spawned (the new-zone almighties with no spawn rows) is a loud skip, never a failure.
    /// </summary>
    private void ApplyToWorld(LocalDevelopmentDefinition definition, LocalDevelopmentPlan plan)
    {
        WorldInstance[] worlds;
        try
        {
            worlds = WorldManager.Instance.GetWorlds();
        }
        catch (Exception ex)
        {
            Logger.Warn("Local development {0}: world not available to apply phase ({1}); phase skipped",
                definition.Id, ex.Message);
            return;
        }

        foreach (var world in worlds)
        {
            if (world == null)
                continue;
            ApplyToDoodads(world, definition.DoodadAlmightyId, plan.DoodadPhase, "development");
            if (definition.BoardDoodadId != 0)
                ApplyToDoodads(world, definition.BoardDoodadId, plan.BoardPhase, "board");
        }
    }

    private static void ApplyToDoodads(WorldInstance world, uint templateId, uint? targetPhase, string what)
    {
        if (targetPhase == null)
            return;
        var doodads = world.GetDoodadsByTemplateId(templateId);
        if (doodads.Count == 0)
        {
            Logger.Warn("Local development: {0} doodad {1} is not spawned in world {2}; phase {3} skipped",
                what, templateId, world.Id, targetPhase);
            return;
        }

        foreach (var doodad in doodads)
        {
            if (LocalDevelopmentRules.ShouldChangePhase(doodad.FuncGroupId, targetPhase.Value))
                doodad.DoChangePhase(null, (int)targetPhase.Value);
        }
    }

    private void UpsertCharacterRow(uint characterId, ushort zoneGroup, Func<CharacterResidentState, CharacterResidentState> mutate)
    {
        lock (_lock)
        {
            var key = (characterId, zoneGroup);
            var current = _states.GetValueOrDefault(key) ??
                          new CharacterResidentState(characterId, zoneGroup, 0, 0, ServerCalendarNow());
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
