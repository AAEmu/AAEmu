using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using MySql.Data.MySqlClient;
using NLog;

using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterSkills(Character owner)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private enum SkillType : byte
    {
        Skill = 1,
        Buff = 2
    }

    private readonly List<uint> _removed = [];
    public Dictionary<uint, Skill> Skills { get; } = [];
    public Dictionary<uint, PassiveBuff> PassiveBuffs { get; } = [];
    private Character Owner { get; } = owner;

    /// <summary>
    /// Try to learn a new Skill
    /// </summary>
    /// <param name="skillId"></param>
    public void AddSkill(uint skillId)
    {
        // Successors are selected through CSActivateHeirSkill and remain separate from the learned
        // base-skill table. Accepting one here would persist it as an ordinary learned skill.
        if (HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillId, out _, out _))
        {
            Logger.Warn("LearnSkill reject {0}: {1} is a Heir successor", Owner.Name, skillId);
            return;
        }

        // Check if what we want to learn is part of an active skill tree (or not part of one)
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template.AbilityId > 0 &&
            template.AbilityId != Owner.Ability1 &&
            template.AbilityId != Owner.Ability2 &&
            template.AbilityId != Owner.Ability3)
            return;

        // Get total skill points for the player's level
        var points = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level);

        // Deduct the amount of skill points already used
        points -= GetUsedSkillPoints(AbilityType.General);

        // Check if we have enough remaining to learn this Skill
        if (template.SkillPoints > points)
        {
            Logger.Info(
                "LearnSkill reject {0}: skill={1} cost={2} remaining={3} (levelBudget={4})",
                Owner.Name, skillId, template.SkillPoints, points,
                ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level));
            return;
        }

        // Check if we already learned it
        if (Skills.TryGetValue(skillId, out var skill))
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
        else
            AddSkill(template, 1, true);
    }

    /// <summary>
    /// Adds a Skill and optionally sends a SCSkillLearnedPacket
    /// </summary>
    /// <param name="template"></param>
    /// <param name="level"></param>
    /// <param name="packet"></param>
    public void AddSkill(SkillTemplate template, byte level, bool packet)
    {
        var skill = new Skill
        {
            Id = template.Id,
            Template = template,
            Level = template.LevelStep > 0 ? (byte)((Owner.GetAbLevel(template.AbilityId) - template.AbilityLevel) / template.LevelStep + 1) : (byte)1
        };
        Skills.Add(skill.Id, skill);

        if (packet)
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
    }

    /// <summary>
    /// Try to learn a Passive Skill.
    /// </summary>
    /// <param name="buffId"></param>
    /// <param name="notify">
    /// When true, broadcasts <c>SCBuffLearned</c> (client chat "Learned …"). Use false for
    /// skillsaver snapshot restore — the client rebuilds passives from AllInfo / AbilitySetUpdated.
    /// </param>
    public void AddBuff(uint buffId, bool notify = true)
    {
        // Check if what we want to learn is part of an active skill tree (or not part of one)
        var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
        // buffId comes straight off CSLearnBuffPacket, so an unknown one has to be refused rather than
        // dereferenced: passive_buffs 51, 268, 274 and 289 name a buff that is not in the table.
        if (template == null)
            return;
        if (template.AbilityId > 0 &&
           template.AbilityId != Owner.Ability1 &&
           template.AbilityId != Owner.Ability2 &&
           template.AbilityId != Owner.Ability3)
            return;

        // Get total skill points for the player's level
        var points = ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level);

        // Deduct the amount of skill points already used
        points -= GetUsedSkillPoints(AbilityType.General);

        // Check if we have enough remaining (passive_buffs.skill_points — often 0 for early passives)
        if (template.SkillPoints > points)
            return;

        // Check if there are enough points already invested in this tree to allow learning this Passive
        if (GetUsedSkillPoints(template.AbilityId) < template.ReqPoints)
            return;

        // Check if we already learned it
        if (PassiveBuffs.ContainsKey(buffId))
            return;

        // Add Passive Buff
        var buff = new PassiveBuff { Id = buffId, Template = template };
        PassiveBuffs.Add(buff.Id, buff);
        if (notify)
            Owner.BroadcastPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id), true);
        buff.Apply(Owner);
    }

    /// <summary>
    /// Re-send every learned skill/passive to this character. Needed after <c>SCAbilitySwapped</c>
    /// when the client wiped trees and there is no <c>SCAbilitySetUpdated(Changed)</c> path to
    /// restore them from a saved set (e.g. NPC <see cref="CharacterAbilities.Swap"/>).
    /// Each <c>SCSkillLearned</c> raises chat <c>SKILL_LEARNED</c> — do not use on skillsaver activate.
    /// Temporary grants are re-sent too: the client has just wiped the same list they were added to.
    /// </summary>
    public void ResendLearnedToOwner()
    {
        foreach (var skill in Skills.Values)
            if (!ReplacedSkillIds.Contains(skill.Id))
                Owner.SendPacket(new SCSkillLearnedPacket(skill));
        foreach (var skill in TemporarySkills.Values)
            if (!ReplacedSkillIds.Contains(skill.Id))
                Owner.SendPacket(new SCSkillLearnedPacket(skill));
        foreach (var buff in PassiveBuffs.Values)
            Owner.SendPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id));
    }

    #region buff-granted skills

    /// <summary>
    /// Skills the character holds only while a buff grants them (<c>buff_skills</c>,
    /// <c>buff_mount_skills</c>, and the replacement side of <c>buff_swap_skills</c>).
    /// </summary>
    /// <remarks>
    /// Deliberately a separate dictionary from <see cref="Skills"/>: that one is the saved skill list —
    /// <see cref="Save"/> writes exactly its members and <see cref="Load"/> reads them back — so keeping
    /// temporary grants out of it is what stops a buff-granted skill from becoming a learned one on the
    /// next logout, and stops it from eating skill points while it is up.
    /// </remarks>
    public Dictionary<uint, Skill> TemporarySkills { get; } = [];

    /// <summary>
    /// Learned skills a live <c>buff_swap_skills</c> row has taken off the bar. The skill stays learned —
    /// only the list the client is shown drops it, and it comes back when the swap ends.
    /// </summary>
    public HashSet<uint> ReplacedSkillIds { get; } = [];

    // Which active buff grants what, so that two buffs granting the same skill ("one ends, the other
    // still holds it") are reconciled together instead of one of them revoking the other's grant.
    private readonly Dictionary<Buff, BuffGrantSet> _buffGrantHolders = [];
    private readonly Dictionary<uint, PassiveBuff> _grantedPassives = [];

    /// <summary>
    /// Registers what <paramref name="buff"/> grants and reconciles the character's temporary set
    /// against every other active grant. Called from <c>BuffTemplate.Start</c>, which also runs on a
    /// refresh and on stack growth, so this has to be idempotent: the buff's entry is replaced, not
    /// added to.
    /// </summary>
    public void ApplyBuffGrants(Buff buff, BuffGrantSet grants)
    {
        if (buff == null || grants == null || grants.IsEmpty)
            return;

        _buffGrantHolders[buff] = grants;
        ReconcileBuffGrants();
    }

    /// <summary>
    /// Drops what <paramref name="buff"/> granted and reconciles the rest. Called from
    /// <c>BuffTemplate.Dispel</c>, i.e. on every way a buff can end — dispel, timeout, removal, death —
    /// and twice on the <c>Buffs.RemoveBuff</c> path, which is why an unknown buff is simply ignored.
    /// </summary>
    public void RevokeBuffGrants(Buff buff)
    {
        if (buff == null || !_buffGrantHolders.Remove(buff))
            return;

        ReconcileBuffGrants();
    }

    /// <summary>Skill ids the client is shown for this character: learned skills plus temporary grants,
    /// minus the entries a live swap has replaced. This is the list <c>SCUnitState</c> carries.</summary>
    public IReadOnlyList<uint> LiveSkillIds()
    {
        var ids = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var skill in Skills.Values)
            if (!ReplacedSkillIds.Contains(skill.Id) && seen.Add(skill.Id))
                ids.Add(skill.Id);
        foreach (var skill in TemporarySkills.Values)
            if (!ReplacedSkillIds.Contains(skill.Id) && seen.Add(skill.Id))
                ids.Add(skill.Id);
        return ids;
    }

    /// <summary>Whether the character holds <paramref name="skillId"/>, learned or granted.</summary>
    public bool HasSkill(uint skillId) =>
        Skills.ContainsKey(skillId) || TemporarySkills.ContainsKey(skillId);

    private void ReconcileBuffGrants()
    {
        var holders = _buffGrantHolders.Values.ToList();

        var added = BuffGrantRules.AddedSkills([.. TemporarySkills.Keys], holders);
        foreach (var skillId in added)
            AddTemporarySkill(skillId);

        var released = BuffGrantRules.ReleasedSkills([.. TemporarySkills.Keys], holders);
        foreach (var skillId in released)
            TemporarySkills.Remove(skillId);

        var replaced = BuffGrantRules.ReplacedOrigins(holders);
        ReplacedSkillIds.Clear();
        foreach (var skillId in replaced)
            ReplacedSkillIds.Add(skillId);

        ReconcileGrantedPassives(holders);

        if (added.Count > 0 || released.Count > 0 || replaced.Count > 0)
            Logger.Info("BuffGrant {0}: +[{1}] -[{2}] replaced=[{3}]",
                Owner.Name,
                string.Join(",", added),
                string.Join(",", released),
                string.Join(",", replaced));
    }

    private void AddTemporarySkill(uint skillId)
    {
        // Already learned: there is nothing temporary to add, and nothing to take away afterwards.
        if (Skills.ContainsKey(skillId))
            return;

        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
            return;

        // buff_skills carries no level, so a grant sits at level 1 for as long as it lasts.
        var skill = new Skill { Id = template.Id, Template = template, Level = 1 };
        if (!TemporarySkills.TryAdd(skillId, skill))
            return;

        // A passive buff applies during character load, before the client is in the world —
        // Character.Connection is set on character select — and the grant goes out with the login skill
        // list (SCUnitState) instead. This is the same reason BuffTemplate.Start skips SCBuffCreated for
        // passives. SCSkillLearned is the packet the learn path (and CharacterSkills.ResendLearnedToOwner)
        // already uses for a skill the character has: 10.0.2.13 reads the single skill id it writes.
        if (Owner.Connection != null)
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
    }

    private void ReconcileGrantedPassives(List<BuffGrantSet> holders)
    {
        // A passive the character already learned is left to the learned instance: PassiveBuff.Remove
        // removes the buff family, which would take the learned one down with the grant.
        var wanted = BuffGrantRules.HeldPassives(holders)
            .Where(passiveBuffId => !PassiveBuffs.ContainsKey(passiveBuffId))
            .ToList();

        foreach (var passiveBuffId in wanted.Where(id => !_grantedPassives.ContainsKey(id)).ToList())
        {
            var template = SkillManager.Instance.GetPassiveBuffTemplate(passiveBuffId);
            if (template == null)
                continue;

            // The same application path a learned passive uses (CharacterSkills.AddBuff), only kept out
            // of PassiveBuffs so that it is neither saved nor charged skill points.
            var passive = new PassiveBuff { Id = passiveBuffId, Template = template };
            _grantedPassives[passiveBuffId] = passive;
            passive.Apply(Owner);
            Logger.Info("BuffGrant apply {0}: passive={1}", Owner.Name, passiveBuffId);
        }

        foreach (var passiveBuffId in _grantedPassives.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            if (_grantedPassives.Remove(passiveBuffId, out var passive))
            {
                passive.Remove(Owner);
                Logger.Info("BuffGrant revoke {0}: passive={1}", Owner.Name, passiveBuffId);
            }
        }
    }

    #endregion

    /// <summary>
    /// Resets all skills from a specific ability Skill Tree
    /// </summary>
    /// <param name="abilityId"></param>
    /// <param name="notifyClient">
    /// When false, only mutates server state. Used before <c>SCAbilitySwapped</c> so a following
    /// <c>SCSkillsReset</c> does not cancel the client's learn-ability banner queue.
    /// </param>
    public void Reset(AbilityType abilityId, bool notifyClient = true)
    {
        // TODO: with price...
        foreach (var skill in new List<Skill>(Skills.Values))
        {
            if (skill.Template.AbilityId != abilityId)
                continue;
            Skills.Remove(skill.Id);
            _removed.Add(skill.Id);
        }

        foreach (var buff in new List<PassiveBuff>(PassiveBuffs.Values))
        {
            if (buff.Template.AbilityId != abilityId)
                continue;
            buff.Remove(Owner);
            PassiveBuffs.Remove(buff.Id);
            _removed.Add(buff.Id);
        }

        Owner.HeirSkills?.RemoveByAbility(abilityId, notifyClient: notifyClient);
        if (notifyClient)
            Owner.BroadcastPacket(new SCSkillsResetPacket(Owner.ObjId, abilityId), true);
    }

    /// <summary>
    /// Get skill points invested in total or for a specific tree
    /// </summary>
    /// <param name="ability">Ability whose Skill Tree to check. Use AbilityType.General if you want the total for all learned skills</param>
    /// <returns>Number of skill points invested</returns>
    private int GetUsedSkillPoints(AbilityType ability)
    {
        var points = 0;

        // Count points for Active Skills
        foreach (var skill in Skills.Values)
            if (ability == AbilityType.General || skill.Template.AbilityId == ability)
                points += skill.Template.SkillPoints;

        // Passives use their own skill_points column (client-matching); many early ones cost 0.
        foreach (var buff in PassiveBuffs.Values)
            if (ability == AbilityType.General || buff.Template.AbilityId == ability)
                points += buff.Template?.SkillPoints ?? 0;

        return points;
    }

    /// <summary>
    /// A successor is castable only when the character selected that exact content-defined Heir
    /// replacement. Matching only ability and ability-level allowed unrelated skills to bypass the
    /// authoritative selection map.
    /// </summary>
    public bool IsActiveHeirSuccessor(uint skillId) => Owner.HeirSkills?.IsActiveSuccessor(skillId) == true;

    #region database
    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM skills WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var type = Enum.Parse<SkillType>(reader.GetString("type"), true);
                    switch (type)
                    {
                        case SkillType.Skill:
                            var skill = new Skill
                            {
                                Id = reader.GetUInt32("id"),
                                Level = reader.GetByte("level")
                            };
                            AddSkill(skill.Id);
                            break;
                        case SkillType.Buff:
                            var buffId = reader.GetUInt32("id");
                            // A saved passive whose template is gone is skipped rather than dereferenced:
                            // the loader already dropped those rows, and PassiveBuff.Apply would have
                            // thrown on the null template.
                            var passiveTemplate = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
                            if (passiveTemplate == null)
                                break;
                            var buff = new PassiveBuff { Id = buffId, Template = passiveTemplate };
                            PassiveBuffs.Add(buff.Id, buff);
                            buff.Apply(Owner);
                            break;
                    }
                }
            }
        }

        foreach (var skill in Skills.Values)
            if (skill != null)
                skill.Template = SkillManager.Instance.GetSkillTemplate(skill.Id);
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removed.Count > 0)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "DELETE FROM skills WHERE owner = @owner AND id IN(" + string.Join(",", _removed) + ")";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            command.ExecuteNonQuery();
            _removed.Clear();
        }

        foreach (var skill in Skills.Values)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES (@id, @level, @type, @owner)";
            command.Parameters.AddWithValue("@id", skill.Id);
            command.Parameters.AddWithValue("@level", skill.Level);
            command.Parameters.AddWithValue("@type", (byte)SkillType.Skill);
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.ExecuteNonQuery();
        }

        foreach (var buff in PassiveBuffs.Values)
        {
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.Transaction = transaction;

            command.CommandText = "REPLACE INTO skills(`id`,`level`,`type`,`owner`) VALUES(@id,@level,@type,@owner)";
            command.Parameters.AddWithValue("@id", buff.Id);
            command.Parameters.AddWithValue("@level", 1);
            command.Parameters.AddWithValue("@type", (byte)SkillType.Buff);
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.ExecuteNonQuery();
        }
    }

    #endregion
}
