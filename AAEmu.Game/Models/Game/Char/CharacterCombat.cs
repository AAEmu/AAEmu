using System.Collections.Concurrent;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
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

    public uint ResurrectHpPercent { get; set; } = 1;
    public uint ResurrectMpPercent { get; set; } = 1;

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
        // Escalating respawn timer — runs BEFORE base.DoDie sends SCUnitDeathPacket
        ComputeDeathWaitTime();

        LastDurabilityLoss = Level >= AppConfiguration.Instance.World.MinimumExpLossLevel
            ? ItemManager.Instance.GetDeathDurabilityLossRatio()
            : (byte)0;

        if (Level < ExperienceManager.Instance.MaxPlayerLevel && ParentWorld.Id == WorldManager.DefaultInstanceId)
        {
            if (Level >= AppConfiguration.Instance.World.MinimumExpLossLevel)
            {
                LastExpLoss = ExperienceManager.Instance.GetExpLoss(Level, AppConfiguration.Instance.World.ExpLossRateAtDeath);
                var thisLevelStartExp = ExperienceManager.Instance.GetExpForLevel(Level);
                var inThisLevelExp = Experience - thisLevelStartExp;
                LastExpLoss = Math.Min(LastExpLoss, inThisLevelExp);
                RecoverableExp = (int)Math.Round(LastExpLoss * 0.80f);
                SendDebugMessage($"Lost {LastExpLoss} exp, {LastDurabilityLoss} Durability. Can recover {RecoverableExp} exp");
            }
            else
            {
                // Free resurrect below level 10 
                SendPacket(new SCNotifyResurrectionPacket(new SkillCasterUnit(ObjId)));
                SendDebugMessage($"Free resurrect below level {AppConfiguration.Instance.World.MinimumExpLossLevel}");
            }
        }
        else
        {
            LastExpLoss = 0;
            SendDebugMessage($"No Exp Lost at max level, Lost {LastDurabilityLoss} Durability.");
        }

        if (LastExpLoss > 0 || RecoverableExp > 0)
        {
            Experience -= LastExpLoss;
            SendPacket(new SCRecoverableExpPacket(ObjId, RecoverableExp, LastExpLoss, (int)KillReason.Damage));
            if (LastExpLoss > 0)
            {
                SendPacket(new SCExpChangedPacket(ObjId, -LastExpLoss, false));
            }
        }

        base.DoDie(killer, killReason);

        if (LastDurabilityLoss > 0)
        {
            ApplyDurabilityLossToEquipment(LastDurabilityLoss, DurabilityLossTargets.All, -1f);
            LastDurabilityLoss = 0;
        }

        // Resolve the victim's zone-conflict state once for both PvP-honor award and War-zone honor-loss
        var victimZone = ZoneManager.Instance.GetZoneByKey(Transform.ZoneId);
        var conflictData = victimZone != null
            ? ZoneManager.Instance.GetConflicts().FirstOrDefault(c => c.ZoneGroupId == victimZone.GroupId)
            : null;
        var zoneState = conflictData?.CurrentZoneState ?? ZoneConflictType.Peace;

        var relationState = killer.GetRelationStateTo(this);
        var possibleArrest = false;
        Character arrestor = null;
        if (killer is Character enemy)
        {
            possibleArrest = true;
            arrestor = enemy;
            if (relationState != RelationState.Friendly)
            {
                enemy.HostileFactionKills++;
                AwardPvpHonor(enemy, victimZone, conflictData, zoneState);

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
                {
                    _ = CrimeManager.Instance.GenerateEvidenceFromKill(killer, this);
                }
            }
        }

        DropTradePackToFloor();
        ClearAllAggro();

        // Clear damage history on death (heal history is intentionally kept)
        _pvpDamageHistory.Clear();

        // Arrest if wanted
        if (possibleArrest && Buffs.CheckBuffTag((uint)BuffConstants.TagWanted))
        {
            if (!Buffs.CheckBuff((uint)BuffConstants.Contemptuous))
            {
                // If not a pirate arrest regardless
                TrialManager.Instance.ArrestCriminal(this, arrestor);
            }
            else if (!ZoneManager.Instance.IsPirateDesperadoZone(Transform.ZoneId))
            {
                // If a pirate, only arrest in faction zones
                TrialManager.Instance.ArrestCriminal(this, arrestor);
            }
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
            var killerHonor = (int)Math.Round(killerShareHonor * pvpRate);
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
                    assistant.ChangeGamePoints(GamePointKind.Honor, assistHonor);
                    assistant.HonorGainedInCombat += (uint)assistHonor;
                    Logger.Debug($"PvP Assist: {assistant.Name} assisted {killer.Name} killing {Name} — {assistHonor} honor");
                    assistant.BroadcastPacket(new SCUnitPvPPointsChangedPacket(assistant.ObjId, 0, (int)assistant.HonorGainedInCombat), true);
                }
            }
        }
        else
        {
            var honor = (int)Math.Round(soloHonor * pvpRate);
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
    /// Force drop player's trade-pack (if any) to the floor
    /// </summary>
    private void DropTradePackToFloor()
    {
        // check trade packs to drop
        var item = Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (item?.Template is BackpackTemplate { BackpackType: BackpackType.TradePack } backpackTemplate)
        {
            // Find the linked doodad of this item's put down effect
            var backpackDoodadId = 0u;
            var itemSkill = SkillManager.Instance.GetSkillTemplate(backpackTemplate.UseSkillId);
            foreach (var skillEffect in itemSkill.Effects)
            {
                if (skillEffect.Template is PutDownBackpackEffect putDownEffect)
                {
                    backpackDoodadId = putDownEffect.BackpackDoodadId;
                    break;
                }
            }

            if (backpackDoodadId > 0 && Inventory.SystemContainer.AddOrMoveExistingItem(Items.Actions.ItemTaskType.DropBackpack, item))
            {
                // Spawn doodad
                Logger.Trace("Spawn tradepack on floor on death");

                var doodad = DoodadManager.Instance.Create(ParentWorld, 0, backpackDoodadId, this, true);
                if (doodad == null)
                {
                    Logger.Warn($"Doodad {backpackDoodadId}, from BackpackDoodadId could not be created");
                    return;
                }

                doodad.IsPersistent = true;
                doodad.Transform = Transform.CloneDetached(doodad);
                doodad.Transform.Local.SetHeight(doodad.ParentWorld.Template.GeoData.GetHeight(doodad.Transform.World.Position));
                doodad.AttachPoint = AttachPointKind.None;
                doodad.ItemId = item.Id;
                doodad.ItemTemplateId = item.Template.Id;
                doodad.UccId = item.UccId;
                doodad.SetScale(1f);
                doodad.PlantTime = DateTime.UtcNow;
                doodad.InitDoodad();
                doodad.Spawn();
                doodad.Save();

                BroadcastPacket(new SCUnitEquipmentsChangedPacket(ObjId, (byte)EquipmentItemSlot.Backpack, null), false);
            }

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
        if (InfamyPoint >= CrimeManager.PirateCrimePointThreshold)
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
            }
        }
        else
        {
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
            // Remove pirate buff if on
            if (Buffs.CheckBuff((uint)BuffConstants.Contemptuous))
            {
                Buffs.RemoveBuff((uint)BuffConstants.Contemptuous);
            }
        }
    }

    /// <summary>
    /// Applied durability loss to items, sends related packets and updates bonuses if needed
    /// </summary>
    /// <param name="durabilityLoss"></param>
    /// <param name="targets"></param>
    /// <param name="durabilityRate">Percent value that an item can lose the durability, if set to &lt; 0, it will affect all target items</param>
    public void ApplyDurabilityLossToEquipment(byte durabilityLoss, DurabilityLossTargets targets, float durabilityRate)
    {
        var updateTasks = new List<ItemTask>();
        var destroyedItems = new List<Item>();
        foreach (var item in Equipment.Items)
        {
            if (item is not EquipItem equipItem)
                continue;

            // Ignore if no durability
            if (equipItem.MaxDurability <= 0)
                continue;

            // Ignore already destroyed items
            if (!equipItem.IsNotDestroyed)
                continue;

            // Filter out by type
            switch (targets)
            {
                case DurabilityLossTargets.All:
                    // No filter, normally only used for player death
                    break;
                case DurabilityLossTargets.AllArmor:
                    if (equipItem is not Items.Armor)
                        continue;
                    break;
                case DurabilityLossTargets.AllWeapons:
                    if (equipItem is not Weapon)
                        continue;
                    break;
                case DurabilityLossTargets.AllMainWeapons:
                    if (equipItem is not Weapon || (equipItem.Slot != (int)EquipmentItemSlot.Mainhand && equipItem.Slot != (int)EquipmentItemSlot.Offhand))
                        continue;
                    break;
                case DurabilityLossTargets.PrimaryWeapon:
                    if (equipItem.Slot != (int)EquipmentItemSlot.Mainhand)
                        continue;
                    break;
                case DurabilityLossTargets.SecondaryWeapon:
                    if (equipItem.Slot != (int)EquipmentItemSlot.Offhand)
                        continue;
                    break;
                case DurabilityLossTargets.RangedWeapon:
                    if (equipItem.Slot != (int)EquipmentItemSlot.Ranged)
                        continue;
                    break;
                case DurabilityLossTargets.Shield:
                    if (equipItem.Template is not WeaponTemplate shieldHoldable || shieldHoldable.HoldableTemplate.SlotTypeId != (uint)EquipmentItemSlotType.Shield)
                        continue;
                    break;
                case DurabilityLossTargets.Feet:
                    if (equipItem.Slot != (int)EquipmentItemSlot.Feet)
                        continue;
                    break;
                default:
                    continue;
            }

            // If durability rate is set, apply randomness
            if (durabilityRate >= 0f && ((Random.Shared.NextSingle() * 100f) > durabilityRate))
            {
                // random did not qualify for durability loss
                continue;
            }

            // Deduct durability
            if (equipItem.Durability <= durabilityLoss)
            {
                // Destroyed item
                equipItem.Durability = 0;
                destroyedItems.Add(equipItem);
                SendDebugMessage($"Equipment Item: {equipItem.Id} @ITEM_NAME({equipItem.TemplateId}) ({equipItem.TemplateId}) was |cFFFF0000destroyed|r");
            }
            else
            {
                equipItem.Durability -= durabilityLoss;
                SendDebugMessage($"Equipment Item: {equipItem.Id} @ITEM_NAME({equipItem.TemplateId}) ({equipItem.TemplateId}) lost {durabilityLoss} durability");
            }

            equipItem.IsDirty = true;
            updateTasks.Add(new ItemUpdate(equipItem));
        }

        if (updateTasks.Count > 0)
        {
            // NOTE: Max 30 items, but technically speaking, you should never be able to reach that amount
            // since you can not have that many items that have durability equipped at the same time.
            SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.DurabilityLoss, updateTasks, null));
        }

        // Reparse equipment buffs if needed because of getting destroyed items
        if (destroyedItems.Count > 0)
        {
            foreach (var item in destroyedItems)
            {
                UpdateGearBonuses(null, item);
            }
        }
    }
}

public enum DurabilityLossTargets
{
    /// <summary>Applies to all equipped items</summary>
    All,
    /// <summary>Only armor pieces</summary>
    AllArmor,
    /// <summary>Only weapons and shields</summary>
    AllWeapons,
    /// <summary>Only the main and offhand equipped slots</summary>
    AllMainWeapons,
    /// <summary>Only the equipped main weapon</summary>
    PrimaryWeapon,
    /// <summary>Only the equipped offhand weapon or shield</summary>
    SecondaryWeapon,
    /// <summary>Only the weapon in the ranged slot </summary>
    RangedWeapon,
    /// <summary>Only aplly to shields</summary>
    Shield,
    /// <summary>Only apply to feet (if you want to implement wear by walk distance for example)</summary>
    Feet
}
