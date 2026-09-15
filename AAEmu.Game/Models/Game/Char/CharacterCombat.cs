using System.Collections.Concurrent;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Char;

public partial class Character
{
    /// <summary>Time window in seconds for PvP assist credit</summary>
    private const int PvpAssistWindowSeconds = 30;

    /// <summary>War-zone death honor penalty (clamped to victim's current honor).</summary>
    private const int WarZoneHonorLoss = 10;

    /// <summary>Escalating death respawn wait times in seconds. Resets after 5 min without dying.</summary>
    private static readonly int[] DeathWaitTimesSeconds = [15, 30, 60, 90, 120, 150, 180, 210, 240];
    private const int DeathCountResetMinutes = 5;
    private int _consecutiveDeathCount;
    private DateTime _lastDeathTime = DateTime.MinValue;

    public int LastDeathLostExp { get; private set; }
    public byte LastDeathDurabilityLossRatio { get; private set; }

    public uint ResurrectHpPercent { get; set; } = 1;
    public uint ResurrectMpPercent { get; set; } = 1;
    public uint HostileFactionKills { get; set; }
    public uint HonorGainedInCombat { get; set; }

    /// <summary>True if last death was a PvP kill in a War zone (Leech debuff on temple-revive).</summary>
    public bool DiedInPvpWarZone { get; set; }
    /// <summary>True if last death was a PvP kill (any zone — skips Weakened Body debuff on temple-revive).</summary>
    public bool DiedInPvp { get; set; }

    /// <summary>Char IDs that recently damaged us. Cleared on death.</summary>
    private readonly ConcurrentDictionary<uint, DateTime> _pvpDamageHistory = new();
    /// <summary>Char IDs that recently healed us. NOT cleared on death (so heal-assists carry across the healer's next kill).</summary>
    private readonly ConcurrentDictionary<uint, DateTime> _pvpHealHistory = new();

    /// <summary>Record that a player dealt damage to us (for assist tracking).</summary>
    public void RecordPvpDamageFrom(Character attacker)
    {
        _pvpDamageHistory[attacker.Id] = DateTime.UtcNow;
    }

    /// <summary>Record that a player healed us (for assist tracking on our kills).</summary>
    public void RecordPvpHealFrom(Character healer)
    {
        _pvpHealHistory[healer.Id] = DateTime.UtcNow;
    }

    public override void DoDie(BaseUnit killer, KillReason killReason)
    {
        try
        {
            try
            {
                // Escalating respawn timer — runs BEFORE base.DoDie sends SCUnitDeathPacket
                ComputeDeathWaitTime();
                ApplyDeathPenalties();
                base.DoDie(killer, killReason);
            }
            finally
            {
                // The equipped pack is part of death, even if a penalty/event callback fails.
                // Run before optional PvP processing, for NPC and environmental deaths too.
                DropBackpackOnDeath();
            }

            // A wanted character's death is the courthouse's own summons: the resurrection lands them
            // there as the defendant instead of at a temple (see JusticeManager.TakeDefendantCourt).
            if (ArrestRules.IsWanted(this))
                JusticeManager.Instance.OnWantedDeath(this);

            ProcessPvpDeath(killer);
        }
        finally
        {
            // Heal history intentionally survives death.
            _pvpDamageHistory.Clear();
            ClearAllAggro();
            if (WorldIntegration.ZoneAuthority)
                WorldIntegration.RelayUnitDeathToZone?.Invoke(ObjId);
        }
    }

    private void ProcessPvpDeath(BaseUnit killer)
    {
        if (killer is not Character enemy || ReferenceEquals(killer, this))
            return;

        // Resolve the victim's zone-conflict state once for both PvP-honor award and War-zone honor-loss
        var victimZone = ZoneManager.Instance.GetZoneByKey(Transform.ZoneId);
        var conflictData = victimZone != null
            ? ZoneManager.Instance.GetConflicts().FirstOrDefault(c => c.ZoneGroupId == victimZone.GroupId)
            : null;
        var zoneState = conflictData?.CurrentZoneState ?? ZoneConflictType.Peace;

        var relationState = killer.GetRelationStateTo(this);
        if (relationState != RelationState.Friendly)
        {
            enemy.HostileFactionKills++;
            AwardPvpHonor(enemy, victimZone, conflictData, zoneState);
            ExpeditionManager.Instance.RegisterWarKill(enemy, this);

            // Mark victim as PvP death (prevents Weakened Body debuff on temple-revive)
            DiedInPvp = true;
            if (zoneState == ZoneConflictType.War)
                DiedInPvpWarZone = true;

            // Broadcast PvP stats: kind=0 → HonorGainedInCombat, kind=1 → HostileFactionKills
            enemy.BroadcastPacket(new SCUnitPvPPointsChangedPacket(enemy.ObjId, 0, (int)enemy.HonorGainedInCombat), true);
            enemy.BroadcastPacket(new SCUnitPvPPointsChangedPacket(enemy.ObjId, 1, (int)enemy.HostileFactionKills), true);

            // Victim loses honor in War zone (clamped >= 0)
            if (zoneState == ZoneConflictType.War && HonorPoint > 0)
            {
                var loss = Math.Min(WarZoneHonorLoss, HonorPoint);
                ChangeGamePoints(GamePointKind.Honor, -loss);
                Logger.Debug($"PvP Death: {Name} lost {loss} honor (War zone death)");
            }
        }
        else
        {
            // Friendly-fire kill → generate crime evidence (unless retaliation)
            var killerOwner = killer.GetOwnerCharacter();
            if (killerOwner != null && !AssaultedBy.Contains(killerOwner.Id))
                _ = CrimeManager.Instance.GenerateEvidenceFromKill(killer, this);
        }
    }

    /// <summary>
    /// Computes the escalating death wait time and stores it in RezWaitDuration.
    /// After 5 minutes without dying, the counter resets.
    /// </summary>
    private void ComputeDeathWaitTime()
    {
        if (_lastDeathTime != DateTime.MinValue &&
            (DateTime.UtcNow - _lastDeathTime).TotalMinutes >= DeathCountResetMinutes)
        {
            _consecutiveDeathCount = 0;
        }

        var index = Math.Min(_consecutiveDeathCount, DeathWaitTimesSeconds.Length - 1);
        var waitSeconds = DeathWaitTimesSeconds[index];

        RezWaitDuration = waitSeconds * 1000;
        DeadTime = DateTime.UtcNow;

        _consecutiveDeathCount++;
        _lastDeathTime = DateTime.UtcNow;

        Logger.Debug($"Death #{_consecutiveDeathCount} for {Name}: respawn wait = {waitSeconds}s");
    }

    private void ApplyDeathPenalties()
    {
        LastDeathLostExp = 0;
        LastDeathDurabilityLossRatio = 0;

        var ratio = ItemManager.Instance.GetDeathDurabilityLossRatio();
        var durMul = (int)CalculateWithBonuses(0, UnitAttribute.DeathDurabilityLossRatioMul);
        LastDeathDurabilityLossRatio = (byte)Math.Clamp(
            AttributeGainRules.ApplyPercentPoints(Math.Max(0, ratio), durMul), 0, byte.MaxValue);

        if (Equipment != null)
        {
            foreach (var item in Equipment.Items)
            {
                if (item is not EquipItem gear || gear.MaxDurability == 0 || gear.Durability == 0)
                    continue;
                var loss = CharacterDeathRules.DurabilityLoss(gear.Durability, gear.MaxDurability, ratio, durMul);
                if (loss <= 0)
                    continue;
                gear.Durability = (byte)Math.Max(0, gear.Durability - loss);
                SendPacket(new SCItemDetailUpdatedPacket(gear));
            }
        }

        var floor = ExperienceManager.Instance.GetExpForLevel(Level);
        var intoLevel = Math.Max(0, Experience - floor);
        var parameters = new Dictionary<string, double> { ["experience"] = intoLevel };
        var penaltyFormula = FormulaManager.Instance.GetFormula((uint)FormulaKind.PenaltyExp);
        var recoverFormula = FormulaManager.Instance.GetFormula((uint)FormulaKind.RecoverableExp);
        var rawPenalty = penaltyFormula != null
            ? (int)Math.Max(0, Math.Round(penaltyFormula.Evaluate(parameters)))
            : 0;
        var rawRecover = recoverFormula != null
            ? (int)Math.Max(0, Math.Round(recoverFormula.Evaluate(parameters)))
            : 0;
        var lost = CharacterDeathRules.ClampLostExp(
            Experience,
            floor,
            AttributeGainRules.ApplyPercentPoints(rawPenalty, (int)CalculateWithBonuses(0, UnitAttribute.PenaltyExpMul)));
        var recoverable = AttributeGainRules.ApplyPercentPoints(
            rawRecover,
            (int)CalculateWithBonuses(0, UnitAttribute.RecoverableExpMul));

        if (lost > 0)
        {
            Experience -= lost;
            LastDeathLostExp = lost;
            SendPacket(new SCExpChangedPacket(ObjId, -lost, false));
        }

        if (recoverable > 0)
            RecoverableExp += recoverable;
        if (lost > 0 || recoverable > 0)
            SendPacket(new SCRecoverableExpPacket(ObjId, RecoverableExp, lost, 0));
    }

    private static int ApplyWarHonor(Character killer, int honor) =>
        AttributeGainRules.ApplyGain(
            honor,
            (int)killer.CalculateWithBonuses(0, UnitAttribute.HonorPointGainWar),
            (int)killer.CalculateWithBonuses(0, UnitAttribute.HonorPointGainWarMul));

    /// <summary>
    /// Awards PvP honor to the killer (and assists) based on zone conflict state.
    /// Conflict: 10 solo (6 killer + 4 each assist). War: 20 solo (16 killer + 4 each assist).
    /// Also registers the kill in the zone conflict system.
    /// </summary>
    private void AwardPvpHonor(Character killer, Zone victimZone, ZoneConflict conflictData, ZoneConflictType zoneState)
    {
        int soloHonor, killerShareHonor, assistShareHonor;
        switch (zoneState)
        {
            case ZoneConflictType.Conflict:
                soloHonor = 10;
                killerShareHonor = 6;
                assistShareHonor = 4;
                break;
            case ZoneConflictType.War:
                soloHonor = 20;
                killerShareHonor = 16;
                assistShareHonor = 4;
                break;
            default:
                // No honor outside Conflict/War zones
                return;
        }

        // Register zone kill (drives zone state escalation)
        conflictData?.AddZoneKill();

        var pvpRate = AppConfiguration.Instance.World.PvpHonorRate;
        var assistIds = CollectAssists(killer);

        // Resolve to online assistants *before* choosing the kill path. The recorded
        // assist IDs are from the rolling damage/heal/CC window and may all be offline
        // by the time the victim dies. If none of them are reachable, fall back to
        // the solo award — otherwise the killer would only get killerShareHonor and
        // the unawarded assist share would be silently discarded (e.g. War solo with
        // one offline assist: 20 solo − 16 killer-share = 14 honor lost).
        var onlineAssists = new List<Character>(assistIds.Count);
        foreach (var assistId in assistIds)
        {
            var assistant = WorldManager.Instance.GetCharacterById(assistId);
            if (assistant is { IsOnline: true })
                onlineAssists.Add(assistant);
        }

        if (onlineAssists.Count > 0)
        {
            var killerHonor = ApplyWarHonor(killer, (int)Math.Round(killerShareHonor * pvpRate));
            if (killerHonor > 0)
            {
                killer.ChangeGamePoints(GamePointKind.Honor, killerHonor);
                killer.HonorGainedInCombat += (uint)killerHonor;
                Logger.Debug($"PvP Kill: {killer.Name} killed {Name} in {zoneState} zone — {killerHonor} honor (killer share)");
            }

            var assistHonor = (int)Math.Round(assistShareHonor * pvpRate);
            if (assistHonor > 0)
            {
                foreach (var assistant in onlineAssists)
                {
                    var granted = ApplyWarHonor(assistant, assistHonor);
                    if (granted <= 0)
                        continue;
                    assistant.ChangeGamePoints(GamePointKind.Honor, granted);
                    assistant.HonorGainedInCombat += (uint)granted;
                    Logger.Debug($"PvP Assist: {assistant.Name} assisted {killer.Name} killing {Name} — {granted} honor");
                    assistant.BroadcastPacket(new SCUnitPvPPointsChangedPacket(assistant.ObjId, 0, (int)assistant.HonorGainedInCombat), true);
                }
            }
        }
        else
        {
            var honor = ApplyWarHonor(killer, (int)Math.Round(soloHonor * pvpRate));
            if (honor > 0)
            {
                killer.ChangeGamePoints(GamePointKind.Honor, honor);
                killer.HonorGainedInCombat += (uint)honor;
                Logger.Debug($"PvP Solo Kill: {killer.Name} killed {Name} in {zoneState} zone — {honor} honor");
            }
        }

        if (victimZone != null)
        {
            // The conflict-zone honor UI keys on ZoneGroupId (same identifier the
            // sister SCConflictZoneStatePacket carries), not on the per-cell ZoneKey.
            // Sending ZoneKey here makes the client unable to match the packet to
            // any conflict zone, so the displayed honor never updates after a kill.
            killer.SendPacket(new SCConflictZoneHonorPointSumPacket((ushort)victimZone.GroupId, (int)killer.HonorGainedInCombat));
        }
    }

    /// <summary>
    /// Collect assist contributors: recent damage on victim + recent heals on killer + active CC casters on victim.
    /// </summary>
    public HashSet<uint> CollectAssists(Character killer)
    {
        var assists = new HashSet<uint>();
        var cutoff = DateTime.UtcNow.AddSeconds(-PvpAssistWindowSeconds);

        // 1) Players who damaged the victim recently (excluding the killer)
        foreach (var (charId, time) in _pvpDamageHistory)
        {
            if (charId != killer.Id && time >= cutoff)
                assists.Add(charId);
        }

        // 2) Players who healed the killer recently (excluding the killer themselves)
        foreach (var (charId, time) in killer._pvpHealHistory)
        {
            if (charId != killer.Id && time >= cutoff)
                assists.Add(charId);
        }

        // 3) Active CC-debuff casters on the victim
        var goodBuffs = new List<Buff>();
        var badBuffs = new List<Buff>();
        var hiddenBuffs = new List<Buff>();
        Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs, false);

        foreach (var buff in badBuffs)
        {
            if (buff.Caster is not Character ccCaster || ccCaster.Id == killer.Id)
                continue;

            var template = buff.Template;
            if (template.Stun || template.Root || template.Sleep || template.Silence || template.Cripled)
                assists.Add(ccCaster.Id);
        }

        return assists;
    }

    /// <summary>
    /// Force-drop an equipped backpack with an authored physical put-down effect.
    /// </summary>
    protected virtual void DropBackpackOnDeath()
    {
        try
        {
            new CharacterBackpackDrop(this).TryDropOnDeath();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Backpack death-drop failed for character {0}; death cleanup continues", Id);
        }
    }

    public override void ClearAllAggro()
    {
        base.ClearAllAggro();
        AggroTable.Clear();
        ClearAssaultList();
    }

    public void ClearAssaultList()
    {
        foreach (var criminalPlayerId in AssaultedBy)
        {
            var criminal = WorldManager.Instance.GetCharacterById(criminalPlayerId);
            if (criminal == null)
                continue;
            criminal.AssaultOn.Remove(this.Id);
        }
        foreach (var victimPlayerId in AssaultOn)
        {
            var victim = WorldManager.Instance.GetCharacterById(victimPlayerId);
            if (victim == null)
                continue;
            victim.AssaultedBy.Remove(this.Id);
        }
        AssaultedBy.Clear();
        AssaultOn.Clear();
    }

    /// <summary>
    /// Checks if Wanted and/or pirate buffs need to be applied.
    /// </summary>
    public void CheckWantedThreshold()
    {
        // Check wanted status
        if (CrimeRecord >= CrimeManager.PirateCrimePointThreshold)
        {
            // Add wanted
            if (!Buffs.CheckBuff((uint)BuffConstants.Wanted))
            {
                Buffs.AddBuff((uint)BuffConstants.Wanted, this);
            }
            if (!Buffs.CheckBuff((uint)BuffConstants.Contemptuous))
            {
                Buffs.AddBuff((uint)BuffConstants.Contemptuous, this);
            }
            // Set pirate faction
            if (Faction.Id != FactionsEnum.Pirate)
            {
                SetFaction(FactionsEnum.Pirate);
                if (Expedition != null && Expedition.MotherId != FactionsEnum.Pirate)
                {
                    ExpeditionManager.Instance.Kick(this.Connection, this.Id);
                }
                if (InParty)
                {
                    TeamManager.Instance.MemberRemoveFromTeam(this, this, RiskyAction.Kick);
                }
            }
        }
        else
        if (CrimePoint >= CrimeManager.WantedCrimePointThreshold)
        {
            if (!Buffs.CheckBuff((uint)BuffConstants.Wanted))
            {
                Buffs.AddBuff((uint)BuffConstants.Wanted, this);
            }
        }
        else
        {
            if (Buffs.CheckBuff((uint)BuffConstants.Wanted))
            {
                Buffs.RemoveBuff((uint)BuffConstants.Wanted);
            }
        }
    }
}
