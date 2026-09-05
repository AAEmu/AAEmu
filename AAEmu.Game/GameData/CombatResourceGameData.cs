using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>enum_buff_conditions — when <see cref="CombatResource.BuffId"/> is meant to be on the unit.</summary>
public enum CombatResourceBuffCondition
{
    Invalid = 1,
    Max = 2,
    Min = 3,
    Active = 4
}

/// <summary>
/// One combat resource — the combo-point style pools an ability accumulates (광란, 착취, 근성 and the rest).
/// </summary>
public class CombatResource
{
    public int Id { get; init; }
    public string Name { get; init; }

    /// <summary>Cap on the accumulated amount; 5 for most, 5000 for 근성.</summary>
    public int Max { get; init; }

    /// <summary>Amount a unit starts with, non-zero only for 죽음의 낙인, 기쁨 and 슬픔.</summary>
    public int DefaultPoint { get; init; }

    /// <summary>enum_combat_resource_send_types: 1 Self, 2 Broadcast.</summary>
    public int SendTypeId { get; init; }

    /// <summary>
    /// Milliseconds between two decay steps. 0 means the pool never drains on its own
    /// (기쁨 / 슬픔), which is why the tick has to treat it as "no timer" rather than "every tick".
    /// </summary>
    public int RecoveryCycle { get; init; }

    /// <summary>Applied every <see cref="RecoveryCycle"/> while the unit is out of combat. Negative throughout the shipped data.</summary>
    public int PeaceRecoveryAmount { get; init; }

    /// <summary>Applied every <see cref="RecoveryCycle"/> while the unit is in combat.</summary>
    public int CombatRecoveryAmount { get; init; }

    /// <summary>enum_recovery_states: 1 invalid, 2 always, 5 nomoving. Every shipped row is 1, so nothing acts on it yet.</summary>
    public int EtcRecoveryStateId { get; init; }

    /// <summary>Companion amount to <see cref="EtcRecoveryStateId"/>; 0 throughout the shipped data.</summary>
    public int EtcRecoveryAmount { get; init; }

    /// <summary>
    /// Buff carrying the client-side resource bar. Held while <see cref="BuffCondition"/> is satisfied;
    /// without it the pip UI never appears even though the point packets arrive.
    /// </summary>
    public uint BuffId { get; init; }

    /// <summary>When <see cref="BuffId"/> should be on the unit.</summary>
    public CombatResourceBuffCondition BuffCondition { get; init; }

    /// <summary>Amount applied per decay step for a unit in the given combat state.</summary>
    public int RecoveryAmountFor(bool inCombat) => inCombat ? CombatRecoveryAmount : PeaceRecoveryAmount;

    /// <summary>True when <paramref name="amount"/> satisfies this resource's bar-buff condition.</summary>
    public bool ShouldHoldBuff(int amount) => BuffId != 0 && BuffCondition switch
    {
        CombatResourceBuffCondition.Active => amount > 0,
        CombatResourceBuffCondition.Max => Max > 0 && amount >= Max,
        CombatResourceBuffCondition.Min => amount <= 0,
        _ => false
    };
}

/// <summary>
/// The combat resource definitions from <c>combat_resources</c>. Needed by CombatResourceEffect to clamp a
/// grant against the resource's own ceiling rather than letting a pool run past what the UI can show.
/// </summary>
[GameData]
public class CombatResourceGameData : Singleton<CombatResourceGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<int, CombatResource> _resources;
    private Dictionary<int, HashSet<int>> _resourceIdsByAbility;

    public void Load(SqliteConnection connection)
    {
        _resources = [];
        _resourceIdsByAbility = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM combat_resources";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var resource = new CombatResource
                {
                    Id = reader.GetInt32("id"),
                    Name = reader.GetString("name"),
                    Max = reader.GetInt32("max"),
                    DefaultPoint = reader.GetInt32("default_point"),
                    // Column is spelled "resouece_send_type_id" in the shipped schema.
                    SendTypeId = reader.GetInt32("resouece_send_type_id"),
                    RecoveryCycle = reader.GetInt32("recovery_cycle", 0),
                    PeaceRecoveryAmount = reader.GetInt32("peace_recovery_amount", 0),
                    CombatRecoveryAmount = reader.GetInt32("combat_recovery_amount", 0),
                    EtcRecoveryStateId = reader.GetInt32("etc_recovery_state_id", 1),
                    EtcRecoveryAmount = reader.GetInt32("etc_recovery_amount", 0),
                    BuffId = reader.GetUInt32("buff_id", 0),
                    BuffCondition = (CombatResourceBuffCondition)reader.GetInt32("resource_buff_condition_id", 1)
                };

                _resources[resource.Id] = resource;
            }
        }

        using (var groupCommand = connection.CreateCommand())
        {
            groupCommand.CommandText =
                "SELECT ability_id, combat_resource_1_id, combat_resource_2_id, " +
                "change_combat_resource_1_id, change_combat_resource_2_id " +
                "FROM combat_resource_groups";
            groupCommand.Prepare();
            using var sqliteReader = groupCommand.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var abilityId = reader.GetInt32("ability_id", 0);
                if (abilityId <= 0)
                    continue;
                if (!_resourceIdsByAbility.TryGetValue(abilityId, out var owned))
                {
                    owned = [];
                    _resourceIdsByAbility[abilityId] = owned;
                }

                CombatResourceSeedRules.AddGroupResourceIds(
                    owned,
                    reader.GetInt32("combat_resource_1_id", 0),
                    reader.GetInt32("combat_resource_2_id", 0),
                    reader.GetInt32("change_combat_resource_1_id", 0),
                    reader.GetInt32("change_combat_resource_2_id", 0));
            }
        }

        Logger.Info("Loaded {0} combat resources", _resources.Count);
    }

    public void PostLoad()
    {
    }

    public CombatResource Get(int id) => _resources?.GetValueOrDefault(id);

    /// <summary>Ceiling for a resource, 0 when the id is unknown.</summary>
    public int GetMax(int id) => Get(id)?.Max ?? 0;

    /// <summary>Every defined resource — used to seed a unit's pools and to drive the decay tick.</summary>
    public IEnumerable<CombatResource> All => _resources?.Values ?? Enumerable.Empty<CombatResource>();

    /// <summary>Resources that start non-empty, so a unit only has to be seeded with those.</summary>
    public IEnumerable<CombatResource> WithDefaultPoint => All.Where(r => r.DefaultPoint > 0);

    /// <summary>
    /// Resource ids listed on <c>combat_resource_groups</c> for the given abilities,
    /// including the change-resource columns (Death's brand sits there, not on column 1).
    /// </summary>
    public IReadOnlySet<int> ResourceIdsForAbilities(params int[] abilityIds)
    {
        var owned = new HashSet<int>();
        if (_resourceIdsByAbility == null || abilityIds == null)
            return owned;
        foreach (var abilityId in abilityIds)
        {
            if (_resourceIdsByAbility.TryGetValue(abilityId, out var ids))
                owned.UnionWith(ids);
        }

        return owned;
    }
}
