using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using MySql.Data.MySqlClient;
using NLog;

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
    /// Try to learn a new Skill.
    /// </summary>
    /// <returns>True when the skill is learned afterwards, including when it was already known.</returns>
    public bool AddSkill(uint skillId)
    {
        // Successors are selected through CSActivateHeirSkill and remain separate from the learned
        // base-skill table. Accepting one here would persist it as an ordinary learned skill.
        if (HeirGameData.Instance.TryGetHeirSkillForSuccessor(skillId, out _, out _))
        {
            Logger.Warn("LearnSkill reject {0}: {1} is a Heir successor", Owner.Name, skillId);
            return false;
        }

        // House remodel/finish skills are General and castable unlearned. Permanently learning them
        // spends their skill_points against the combat pool on both client and server.
        if (HousingGameData.Instance.IsHouseGrantedSkill(skillId))
        {
            Logger.Warn("LearnSkill reject {0}: {1} is a house-granted skill", Owner.Name, skillId);
            return false;
        }

        // Check if what we want to learn is part of an active skill tree (or not part of one)
        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
            return false;
        if (template.AbilityId > 0 &&
            template.AbilityId != Owner.Ability1 &&
            template.AbilityId != Owner.Ability2 &&
            template.AbilityId != Owner.Ability3)
            return false;

        // Level table + permanent AdditionalSkillPoint bonus, minus already spent.
        var points = GetRemainingSkillPoints();

        // Check if we have enough remaining to learn this Skill
        if (template.SkillPoints > points)
        {
            Logger.Info(
                "LearnSkill reject {0}: skill={1} cost={2} remaining={3} (levelBudget={4} additional={5})",
                Owner.Name, skillId, template.SkillPoints, points,
                ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level),
                Owner.AdditionalSkillPoint);
            return false;
        }

        // Check if we already learned it
        if (Skills.TryGetValue(skillId, out var skill))
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
        else
        {
            AddSkill(template, 1, true);
            // Tree passives key off skills unlocked in the tree — a player learn can cross req_points.
            // Not done inside AddSkill(template): Load / ability-seed / skillsaver restore with
            // packet=false and refresh once after the whole book is in (mid-Load refresh races saved
            // passives; ability-seed hosts often have no SkillManager yet).
            RefreshAutoGrantedPassives(notify: true);
        }

        return true;
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
            Level = SkillRankRules.ForUnit(template, Owner)
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

        // passive_buffs.level gates the learn: the 11 rows at 40-60 (the Predator/Trooper chains and two
        // level-60 general passives) were learnable at level 1, because CSLearnBuffPacket carries nothing
        // but the id and nothing else looked at the column.
        if (!PassiveBuffLevelRules.CanLearn(template, Owner.Level))
            return;

        if (template.AbilityId > 0 &&
           template.AbilityId != Owner.Ability1 &&
           template.AbilityId != Owner.Ability2 &&
           template.AbilityId != Owner.Ability3)
            return;

        // Tree passives (all 84 Fight…Pleasure rows) ship skill_points=0 — only skills spend the
        // shared pool. Gating a zero-cost row on remaining points locked every unlocked passive
        // once the player had spent the pool on skills (remaining 0, or negative if over-spent):
        // the client greys them out and a `SkillPoints > points` check with points < 0 refused the
        // server learn too. System actives (Active + req_points 0) also report a free pool cost —
        // see PassiveSkillPointSpendRules — even when the column is non-zero.
        var poolCost = PassiveSkillPointSpendRules.PoolCost(template);
        if (poolCost > 0)
        {
            if (poolCost > GetRemainingSkillPoints())
                return;
        }

        // Investment gate (passive_buffs.req_points):
        // - Combat trees (Fight…Pleasure): count of skills unlocked in that tree (twelve show
        //   skills at 1 point each; passives unlock at 3/4/5/6/7/8).
        // - General: total skill points spent across the book (legacy rows like req_points 2–8 /
        //   99). Skipping this for General auto-granted every req_points>0 general passive.
        if (template.ReqPoints > 0)
        {
            var invested = template.AbilityId == AbilityType.General
                ? GetUsedSkillPoints(AbilityType.General)
                : GetLearnedSkillCount(template.AbilityId);
            if (invested < template.ReqPoints)
                return;
        }

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
    /// Learns every auto-grantable passive the character now qualifies for and does not already
    /// hold (level, ability tree, skills unlocked in that tree, skill-point budget). Covers both
    /// <c>active='t'</c> rows and every tree passive with <c>req_points &gt; 0</c>.
    /// </summary>
    /// <param name="notify">
    /// When true, sends <c>SCBuffLearned</c> for each newly granted passive. Use after a
    /// player-initiated skill learn or level-up; keep false on skill-book load and skillsaver
    /// restore (those rebuild via AllInfo / AbilitySetUpdated). Never re-broadcasts already-held
    /// passives — that flooded the client on every skill unlock.
    /// </param>
    /// <remarks>
    /// Call sites: level-up rebuild, after learning a skill, after skill-book load, and after a
    /// skillsaver snapshot restore. <see cref="AddBuff"/> still owns every gate.
    /// </remarks>
    public void RefreshAutoGrantedPassives(bool notify = false)
    {
        // Ability-swap / combat-resource unit hosts never install SkillManager; Singleton throws
        // InvalidOperationException instead of returning null. Live World always has one.
        IReadOnlyList<uint> candidates;
        try
        {
            candidates = SkillManager.Instance.GetAutoGrantablePassiveBuffIds(Owner.Level, PassiveBuffs.Keys);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var passiveId in candidates)
            AddBuff(passiveId, notify);
    }

    /// <summary>
    /// Re-applies learned passives whose <c>Started</c> trigger is gated on an equip-stance tag, so
    /// the child buff matches the weapons currently worn. Call after the stance buff itself has
    /// been updated (<see cref="Units.Unit"/> gear path).
    /// </summary>
    public void ReapplyStanceGatedPassives()
    {
        foreach (var passive in PassiveBuffs.Values.ToList())
        {
            if (passive?.Template == null)
                continue;
            var triggers = SkillManager.Instance.GetBuffTriggerTemplates(passive.Template.BuffId);
            if (!PassiveBuffStanceRules.HasStanceGatedStartedTrigger(triggers))
                continue;
            passive.Remove(Owner);
            passive.Apply(Owner);
        }
    }

    /// <summary>
    /// Re-evaluates the passives a character holds after their level or ability level moved: every learned
    /// passive is rebuilt at its new ability level, and the <c>passive_buffs</c> rows marked
    /// <c>active='t'</c> that the character has just become old enough for are learned.
    /// </summary>
    /// <remarks>
    /// <c>Character.ApplyLevelUpBenefits</c> used to refresh HP/MP and nothing else, so a passive's
    /// bonuses stayed at the ability level they were first applied with (the <c>Buff.AbLevel</c> default
    /// of 1) for the whole life of the character. A rebuild is a remove plus an apply because the bonus
    /// values are snapshotted in <c>BuffTemplate.Start</c>; both halves are packet-silent for a passive —
    /// <c>Start</c> and <c>Dispel</c> only broadcast for a non-passive — so the client is not disturbed.
    /// </remarks>
    public void ReevaluatePassivesOnLevelUp()
    {
        foreach (var passive in PassiveBuffs.Values.ToList())
        {
            if (passive?.Template == null)
                continue;
            passive.Remove(Owner);
            passive.Apply(Owner);
        }

        RefreshAutoGrantedPassives(notify: true);
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

    /// <summary>
    /// Open Combo follow-up windows: follow-up skill id → UTC expiry. Intervening casts do not
    /// clear other entries (dash → attack → return must still accept the recall).
    /// </summary>
    private readonly Dictionary<uint, DateTime> _comboFollowUpExpires = [];

    /// <summary>
    /// Guards prune / consume / open / check of <see cref="_comboFollowUpExpires"/>. Packet
    /// <c>HasSkill</c> and scheduled cast accept (<c>NoteSkillFired</c>) can race.
    /// </summary>
    private readonly object _comboFollowUpLock = new();

    /// <summary>
    /// Records an accepted cast: consumes this id's follow-up window if any, and opens the next
    /// Combo link's window when this skill names one.
    /// </summary>
    public void NoteSkillFired(uint skillId)
    {
        if (skillId == 0)
            return;
        var now = DateTime.UtcNow;
        var castTimeMul = Owner.CastTimeMul;
        var globalCooldownMul = Owner.GlobalCooldownMul;
        var attackSpeedRating = Owner.AttackSpeedRating;
        lock (_comboFollowUpLock)
        {
            SkillComboRules.PruneExpiredFollowUps(_comboFollowUpExpires, now);
            SkillComboRules.ApplyAcceptedCast(
                skillId,
                now,
                _comboFollowUpExpires,
                SkillComboRules.NextFollowUp,
                id => SkillComboRules.ComboWindowMs(
                    id, ResolveEquippedWeaponGcdSpeed(id), castTimeMul, globalCooldownMul, attackSpeedRating));
        }
    }

    /// <summary>
    /// Hold-repeat of a <c>start_autoattack</c> root that still has an open Combo window is remapped
    /// onto that follow-up so the chain advances when the client keeps sending the root id.
    /// </summary>
    public uint RemapHoldRepeatCombo(uint skillId)
    {
        if (skillId == 0)
            return skillId;
        var template = SkillManager.Instance?.GetSkillTemplate(skillId);
        if (template is not { StartAutoAttack: true })
            return skillId;

        var now = DateTime.UtcNow;
        lock (_comboFollowUpLock)
        {
            SkillComboRules.PruneExpiredFollowUps(_comboFollowUpExpires, now);
            return SkillComboRules.RemapHoldRepeatToOpenFollowUp(
                skillId, startAutoAttack: true, SkillComboRules.NextFollowUp, _comboFollowUpExpires, now);
        }
    }

    /// <summary>
    /// Equipped holdable speed for the skill's <c>weapon_gcd_id</c> slot (15 Mainhand / 16 Offhand /
    /// 17 Ranged). Matches the press-armed shared GCD base.
    /// </summary>
    private int ResolveEquippedWeaponGcdSpeed(uint skillId)
    {
        var template = SkillManager.Instance?.GetSkillTemplate(skillId);
        if (template is not { WeaponGcdId: > 0 })
            return 0;

        var slot = (WeaponGcdId)template.WeaponGcdId switch
        {
            WeaponGcdId.Mainhand => EquipmentItemSlot.Mainhand,
            WeaponGcdId.Offhand => EquipmentItemSlot.Offhand,
            WeaponGcdId.Ranged => EquipmentItemSlot.Ranged,
            _ => (EquipmentItemSlot?)null
        };
        if (slot is null)
            return 0;

        var weapon = Owner.Equipment?.GetItemBySlot((int)slot.Value);
        return weapon?.Template is WeaponTemplate weaponTemplate
            ? weaponTemplate.HoldableTemplate?.Speed ?? 0
            : 0;
    }

    /// <summary>
    /// Closes the Combo follow-up window opened by an accepted cast that was later refunded.
    /// </summary>
    public void CancelComboOpenedBy(uint skillId)
    {
        if (skillId == 0)
            return;
        lock (_comboFollowUpLock)
        {
            SkillComboRules.CloseFollowUpOpenedBy(
                skillId, _comboFollowUpExpires, SkillComboRules.NextFollowUp);
        }
    }

    /// <summary>
    /// Whether the character holds <paramref name="skillId"/> — learned, buff-granted, the
    /// currently selected Heir successor, or an open Combo follow-up window for that id.
    /// </summary>
    /// <remarks>
    /// Combo follow-ups (special type 48) are never book entries: the client swaps the bar and sends
    /// an ordinary cast for the next id. Accepting every later hit of a learned root would let a
    /// short-cooldown follow-up be cast without its root — windows open only when the prior link
    /// is accepted. The window is <c>value2 + max(cast, shared GCD)</c> of that prior hit (both
    /// halves × cast speed; catalog <c>weapon_gcd_id</c> only when it decides the GCD base), and it
    /// survives other skills cast in between (blink return after an attack).
    /// </remarks>
    public bool HasSkill(uint skillId)
    {
        if (Skills.ContainsKey(skillId) || TemporarySkills.ContainsKey(skillId) || IsActiveHeirSuccessor(skillId))
            return true;

        var now = DateTime.UtcNow;
        lock (_comboFollowUpLock)
        {
            SkillComboRules.PruneExpiredFollowUps(_comboFollowUpExpires, now);
            return SkillComboRules.IsOpenComboFollowUp(skillId, _comboFollowUpExpires, now);
        }
    }

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

        // Do not send SCSkillLearned: each one posts chat SKILL_LEARNED, and a buff_swap / combo
        // grant is a bar replacement, not a permanent learn. The client already knows the skill id
        // from buff_swap_skills / buff_skills; TemporarySkills is the server-side HasSkill table.
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
    /// Removes a permanently learned skill without wiping the whole tree. Used to demote house
    /// remodel/finish skills that must not sit in the book (they spend <c>skill_points</c>).
    /// </summary>
    /// <param name="notifyClient">
    /// When true, sends <c>SCSkillsReset</c> for that skill's ability — which clears the client's
    /// entire ability book. Prefer <see cref="DemoteHouseGrantedSkills"/> for house demotes so
    /// remaining General skills are re-learned after one shared reset.
    /// </param>
    public bool ForgetPermanentSkill(uint skillId, bool notifyClient = true)
    {
        if (!Skills.Remove(skillId, out var skill))
            return false;

        if (!_removed.Contains(skillId))
            _removed.Add(skillId);

        TemporarySkills.Remove(skillId);
        if (notifyClient)
            Owner.BroadcastPacket(new SCSkillsResetPacket(Owner.ObjId, skill.Template.AbilityId), true);
        return true;
    }

    /// <summary>
    /// Drops wrongly permanent-learned house remodel/finish skills, then syncs the client General
    /// book: one <c>SCSkillsReset(General)</c> clears it, then every remaining General skill and
    /// passive is re-sent so permanent non-house General learns are not wiped.
    /// </summary>
    /// <remarks>
    /// Needed for a live World upgrade where the skill book still holds old house learns — after a
    /// normal relog, <see cref="Load"/> already demotes them and this path is a no-op. There is no
    /// per-skill unlearn packet; reset-then-restore is the only wire shape that keeps the client
    /// aligned without a relog.
    /// </remarks>
    /// <returns>How many house skills were removed from the permanent book.</returns>
    public int DemoteHouseGrantedSkills(IEnumerable<uint> houseSkillIds)
    {
        var demoted = 0;
        foreach (var skillId in houseSkillIds)
        {
            if (ForgetPermanentSkill(skillId, notifyClient: false))
                demoted++;
        }

        if (demoted == 0)
            return 0;

        Owner.BroadcastPacket(new SCSkillsResetPacket(Owner.ObjId, AbilityType.General), true);
        ResendLearnedForAbility(AbilityType.General);
        return demoted;
    }

    /// <summary>
    /// Re-sends learned skills and passives for one ability after <c>SCSkillsReset</c> cleared that
    /// tree on the client. Temporary grants are included: the reset wiped the same list.
    /// </summary>
    public void ResendLearnedForAbility(AbilityType ability)
    {
        foreach (var skill in Skills.Values)
        {
            if (skill.Template.AbilityId != ability || ReplacedSkillIds.Contains(skill.Id))
                continue;
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
        }

        foreach (var skill in TemporarySkills.Values)
        {
            if (skill.Template.AbilityId != ability || ReplacedSkillIds.Contains(skill.Id))
                continue;
            Owner.SendPacket(new SCSkillLearnedPacket(skill));
        }

        foreach (var buff in PassiveBuffs.Values)
        {
            if (buff.Template.AbilityId != ability)
                continue;
            Owner.SendPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id));
        }
    }

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
    /// Level <c>skill_points</c> plus permanent <see cref="Character.AdditionalSkillPoint"/>, minus spent.
    /// </summary>
    private int GetRemainingSkillPoints() =>
        AdditionalSkillPointBudgetRules.Remaining(
            ExperienceManager.Instance.GetSkillPointsForLevel(Owner.Level),
            Owner.AdditionalSkillPoint,
            GetUsedSkillPoints(AbilityType.General));

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

        // Passives: client GetSkillPoint used only charges skill-book rows. System actives
        // (Active + req_points 0) are free even when passive_buffs.skill_points is non-zero.
        foreach (var buff in PassiveBuffs.Values)
            if (ability == AbilityType.General || buff.Template.AbilityId == ability)
                points += PassiveSkillPointSpendRules.PoolCost(buff.Template);

        return points;
    }

    /// <summary>
    /// How many active skills are unlocked in one ability tree. Tree passives
    /// (<c>passive_buffs.req_points</c> 3–8) gate on this count — each show skill in a combat tree
    /// costs 1 and every tree ships twelve of them.
    /// </summary>
    private int GetLearnedSkillCount(AbilityType ability)
    {
        var count = 0;
        foreach (var skill in Skills.Values)
            if (skill.Template.AbilityId == ability)
                count++;
        return count;
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
                            var skillId = reader.GetUInt32("id");
                            // House remodel/finish skills were wrongly persisted by an older grant path.
                            // Drop them on load so they stop spending the combat skill-point pool; casts
                            // still work because General skills are accepted unlearned.
                            if (HousingGameData.Instance.IsHouseGrantedSkill(skillId))
                            {
                                if (!_removed.Contains(skillId))
                                    _removed.Add(skillId);
                                break;
                            }

                            // packet=false: restore only — do not auto-grant or spend the learn path mid-Load.
                            var skillTemplate = SkillManager.Instance.GetSkillTemplate(skillId);
                            if (skillTemplate != null)
                                AddSkill(skillTemplate, 1, packet: false);
                            break;
                        case SkillType.Buff:
                            var buffId = reader.GetUInt32("id");
                            // A saved passive whose template is gone is skipped rather than dereferenced:
                            // the loader already dropped those rows, and PassiveBuff.Apply would have
                            // thrown on the null template.
                            var passiveTemplate = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
                            if (passiveTemplate == null)
                                break;
                            if (PassiveBuffs.ContainsKey(buffId))
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

        // Saved books can predate the auto-grant path: learn anything the tree now qualifies for
        // that was never persisted (e.g. Empowered Malice after four Hatred points).
        RefreshAutoGrantedPassives();
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
