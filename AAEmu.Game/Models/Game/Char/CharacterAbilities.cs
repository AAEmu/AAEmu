using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterAbilities
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public Dictionary<AbilityType, Ability> Abilities { get; set; }
    public Character Owner { get; set; }

    public CharacterAbilities(Character owner)
    {
        Owner = owner;
        Abilities = [];
        // One entry per real skillset (1..29); None (30) is the empty-slot sentinel, General (0) is not chosen.
        for (var i = 1; i < (int)AbilityType.None; i++)
        {
            var id = (AbilityType)i;
            Abilities[id] = new Ability(id);
        }
    }

    public IEnumerable<Ability> Values => Abilities.Values;

    public void SetAbility(AbilityType id, byte order)
    {
        if (Abilities.TryGetValue(id, out var ability))
            ability.Order = order;
    }

    public List<AbilityType> GetActiveAbilities()
    {
        var list = new List<AbilityType>();
        if (Owner.Ability1 != AbilityType.None)
            list.Add(Owner.Ability1);
        if (Owner.Ability2 != AbilityType.None)
            list.Add(Owner.Ability2);
        if (Owner.Ability3 != AbilityType.None)
            list.Add(Owner.Ability3);
        return list;
    }

    public void AddExp(AbilityType type, int exp)
    {
        if (exp == 0 || !Abilities.TryGetValue(type, out var ability))
            return;

        ApplyExpDelta(ability, exp);

        // server mirror first, then notify the client so reconnect/save paths observe the same state.
        Owner.SendPacket(new SCAbilityExpChangedPacket(Owner.ObjId, type, exp));
    }

    public void AddActiveExp(int exp)
    {
        if (Owner.Ability1 != AbilityType.None)
            ApplyExpDelta(Abilities[Owner.Ability1], exp);
        if (Owner.Ability2 != AbilityType.None)
            ApplyExpDelta(Abilities[Owner.Ability2], exp);
        if (Owner.Ability3 != AbilityType.None)
            ApplyExpDelta(Abilities[Owner.Ability3], exp);
    }

    /// <summary>
    /// de-level the ability, and cannot advance beyond the owner's current character level.
    /// </summary>
    private void ApplyExpDelta(Ability ability, int exp)
    {
        var experienceManager = ExperienceManager.Instance;
        var abilityLevel = experienceManager.GetLevelFromExp(Math.Max(0, ability.Exp), out _);
        if (abilityLevel > Owner.Level)
            return;

        var levelFloor = experienceManager.GetExpForLevel(abilityLevel);
        var nextOwnerLevelExp = experienceManager.GetExpForLevel((byte)(Owner.Level + 1));
        var ownerLevelCeiling = nextOwnerLevelExp > 0
            ? nextOwnerLevelExp - 1
            : experienceManager.GetExpForLevel(Owner.Level);
        var globalLevelCeiling = experienceManager.GetExpForLevel(experienceManager.MaxPlayerLevel);
        var levelCeiling = Math.Min(ownerLevelCeiling, globalLevelCeiling);

        var updatedExp = (long)ability.Exp + exp;
        ability.Exp = (int)Math.Clamp(updatedExp, levelFloor, levelCeiling);
    }

    public void Swap(AbilityType oldAbilityId, AbilityType abilityId)
    {
        // Adding into an empty slot sends oldAbilityId = None (30). There is nothing to wipe, and
        // SCSkillsReset(None) makes the client look up ability name 30 → "invalid ability".
        var isAdding = oldAbilityId is AbilityType.None or AbilityType.General;

        // Client CanBuyAbilityChange() always shows formula 41 gold — charge before mutating.
        if (Owner.Ability1 != oldAbilityId &&
            Owner.Ability2 != oldAbilityId &&
            Owner.Ability3 != oldAbilityId)
        {
            Logger.Warn(
                "SwapAbility: no slot matched old={0} new={1} (slots {2}/{3}/{4})",
                oldAbilityId, abilityId, Owner.Ability1, Owner.Ability2, Owner.Ability3);
            return;
        }

        if (!AbilityChangeCosts.TryChargeSwapAbility(Owner))
            return;

        // 0x147 reports every slot's before/after pair, so snapshot all three before mutating.
        AbilityType[] before = [Owner.Ability1, Owner.Ability2, Owner.Ability3];
        if (Owner.Ability1 == oldAbilityId)
        {
            Owner.Ability1 = abilityId;
            Abilities[abilityId].Order = 0;
        }
        else if (Owner.Ability2 == oldAbilityId)
        {
            Owner.Ability2 = abilityId;
            Abilities[abilityId].Order = 1;

            // Keep ability level in sync with ability1 when filling an empty slot.
            if (isAdding)
                Abilities[Owner.Ability2].Exp = Abilities[Owner.Ability1].Exp;
        }
        else if (Owner.Ability3 == oldAbilityId)
        {
            Owner.Ability3 = abilityId;
            Abilities[abilityId].Order = 2;

            if (isAdding)
            {
                Abilities[Owner.Ability3].Exp = Abilities[Owner.Ability1].Exp;

                // Unchosen trees default to level 10 so spillover exp cannot desync them.
                var c = GetActiveAbilities();
                for (var i = 1; i < Abilities.Count; i++)
                {
                    var id = (AbilityType)i;
                    if (!c.Contains(Abilities[id].Id))
                        Abilities[id].Exp = 42000;
                }
            }
        }

        if (!isAdding)
            Abilities[oldAbilityId].Order = 255;

        Logger.Info(
            "SwapAbility {0}: {1}/{2}/{3} → {4}/{5}/{6} (old={7} new={8} adding={9})",
            Owner.Name,
            before[0], before[1], before[2],
            Owner.Ability1, Owner.Ability2, Owner.Ability3,
            oldAbilityId, abilityId, isAdding);

        // Wipe server book for the old tree (no SCSkillsReset — that cancels the banner queue).
        if (!isAdding)
            Owner.Skills.Reset(oldAbilityId, notifyClient: false);

        // Client 0x147 swap path only fires ABILITY_CHANGED (msg_swap_ability + learn-ability
        // banner) when a single leading news[] ability is valid. A full triad (all three
        // equipped) always skips that event — which is why NPC swaps looked broken.
        // Pad with General(0): still three wire pairs (reader requires them), r13==1.
        // Single-slot apply replaces old→new by ability id; only that tree is wiped client-side.
        AbilityType[] wireOld =
        [
            isAdding ? AbilityType.General : oldAbilityId,
            AbilityType.General,
            AbilityType.General
        ];
        AbilityType[] wireNew = [abilityId, AbilityType.General, AbilityType.General];

        Owner.BroadcastPacket(new SCAbilitySwappedPacket(Owner.ObjId, wireOld, wireNew), true);

        // Only the swapped tree was cleared on the client — do not ResendLearned (chat spam).
        // When unlocking a slot, push starters with notify so the client learns just those.
        if (isAdding && abilityId is not AbilityType.None and not AbilityType.General)
        {
            foreach (var template in SkillManager.Instance.GetStartAbilitySkills(abilityId))
                Owner.Skills.AddSkill(template, 1, true);
        }
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM abilities WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var ability = new Ability
                    {
                        Id = (AbilityType)reader.GetByte("id"),
                        Exp = reader.GetInt32("exp")
                    };
                    if (ability.Id == Owner.Ability1)
                        ability.Order = 0;
                    if (ability.Id == Owner.Ability2)
                        ability.Order = 1;
                    if (ability.Id == Owner.Ability3)
                        ability.Order = 2;
                    Abilities[ability.Id] = ability;
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        foreach (var ability in Abilities.Values)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO abilities(`id`,`exp`,`owner`) VALUES (@id, @exp, @owner)";
                command.Parameters.AddWithValue("@id", (byte)ability.Id);
                command.Parameters.AddWithValue("@exp", ability.Exp);
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.ExecuteNonQuery();
            }
        }
    }

    public byte GetAbilityLevel(AbilityType abilityType)
    {
        return Abilities.TryGetValue(abilityType, out var ability) ? ExperienceManager.Instance.GetLevelFromExp(ability.Exp, out _) : (byte)0;
    }
}
