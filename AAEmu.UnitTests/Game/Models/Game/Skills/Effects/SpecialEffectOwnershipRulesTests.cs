using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// Pins the ownership table against the content and the classes. An action class under
/// SpecialEffects used to count as "implemented" even when it only logged, which charged players for
/// casts that did nothing. These tests keep the table, the classes and the cost gate in step.
/// </summary>
public class SpecialEffectOwnershipRulesTests
{
    /// <summary>
    /// Every special_effect_type_id with at least one special_effects row in game_decrypted.sqlite3
    /// (10.0.2.13): 161 types. A row that reaches the dispatcher must have an explicit answer, not
    /// the fallback.
    /// </summary>
    private static readonly SpecialType[] PopulatedTypes =
    [
        SpecialType.Charge, SpecialType.DisturbCasting, SpecialType.LoseTarget, SpecialType.Interaction,
        SpecialType.Blink, SpecialType.KnockBack, SpecialType.SpawnDoodad, SpecialType.BuffSteal,
        SpecialType.FakeDeath, SpecialType.Resurrection, SpecialType.CapturePet, SpecialType.SpawnPet,
        SpecialType.Return, SpecialType.GainItem, SpecialType.AddExp, SpecialType.AddLaborPower,
        SpecialType.SavePortal, SpecialType.SkillUse, SpecialType.Anim, SpecialType.FxGroup,
        SpecialType.FxGroupAnim, SpecialType.Projectile, SpecialType.ProjectileAnim, SpecialType.ManaCost,
        SpecialType.Cooldown, SpecialType.GlobalCooldown, SpecialType.OpacityControl, SpecialType.ResetCooldown,
        SpecialType.RedeemBuff, SpecialType.GainItemWithEmblemImprint, SpecialType.ExplodeBuff,
        SpecialType.TeleportToUnit, SpecialType.Combo, SpecialType.ItemConversion, SpecialType.DeclareDominion,
        SpecialType.MateMakeGetUp, SpecialType.StopManaRegen, SpecialType.AttachTo, SpecialType.AddBreath,
        SpecialType.Detach, SpecialType.HealPet, SpecialType.RemoveDoodad, SpecialType.CancelStealth,
        SpecialType.SpawnSlave, SpecialType.CancelOngoingBuff, SpecialType.CombatText, SpecialType.TeleportToSiegeHq,
        SpecialType.AutoAttack, SpecialType.CombatDice, SpecialType.ApplyReagents, SpecialType.SextantPos,
        SpecialType.NotifyQuest, SpecialType.DestroyAndSpawnSlave, SpecialType.MoveToGround, SpecialType.Escape,
        SpecialType.ReportBot, SpecialType.ClearProjectile, SpecialType.RetrieveProjectile,
        SpecialType.AddFxToProjectile, SpecialType.FishingLoot, SpecialType.StopChanneling,
        SpecialType.FinishChanneling, SpecialType.SetVariable, SpecialType.ReportBotExpired,
        SpecialType.ReportBotArrested, SpecialType.EngraveOnGuardTower, SpecialType.ConsumeLaborPower,
        SpecialType.GiveLivingPoint, SpecialType.ApplyBotTrial, SpecialType.ArrestBot, SpecialType.EscapeMySlave,
        SpecialType.AddCharacterSlot, SpecialType.GradeEnchant, SpecialType.PlayUserMusic, SpecialType.PauseUserMusic,
        SpecialType.RechargeItemBuff, SpecialType.ExpToItem, SpecialType.UserMusicSaveNotes, SpecialType.Dyeing,
        SpecialType.GiveBmMileage, SpecialType.GiveHonorPoint, SpecialType.GiveCrimePoint, SpecialType.AggroCopy,
        SpecialType.AggroReset, SpecialType.ItemSocketing, SpecialType.Skinize, SpecialType.NpcDespawn,
        SpecialType.StartDominionNonPvpDuration, SpecialType.GiveAppellation, SpecialType.ExitArchemall,
        SpecialType.WeaponDisplay, SpecialType.AuctionPostAuthority, SpecialType.GiveCashPoint,
        SpecialType.RevertItemLook, SpecialType.RechargeItemSkill, SpecialType.RebuildHousing,
        SpecialType.ItemEvolving, SpecialType.RechargeItemRndAttrUnitModifier, SpecialType.ItemRefurbishment,
        SpecialType.ProtectionForExpedition, SpecialType.ExpeditionSummon, SpecialType.RemoveDoodadGroup,
        SpecialType.AddExpeditionExp, SpecialType.AddExpeditionContributionPoint, SpecialType.GainGachaLootPackItem,
        SpecialType.ChangeSkillActiveType, SpecialType.ActivateSavedAbilitySet, SpecialType.ItemEvolvingReRoll,
        SpecialType.ExpandDecoLimit, SpecialType.ExpeditionLevelChange, SpecialType.RemoveAllDoodad,
        SpecialType.LearnSpecialAbility, SpecialType.ResidentServicePoint, SpecialType.ChangeTarget,
        SpecialType.LoseTargetingTheTarget, SpecialType.PlayAttachmentAnim, SpecialType.ItemSmelting,
        SpecialType.ReduceCooldown, SpecialType.FamilyLevelChange, SpecialType.MakeCraftOrderSheet,
        SpecialType.RestoreDisableEnchant, SpecialType.ChangeBuffToleranceStep, SpecialType.ChargeCooldown,
        SpecialType.RestoreCraftOrderSheet, SpecialType.ProcessCraftOrder, SpecialType.EquipSlotReinforceAddExp,
        SpecialType.BlessUthstinSelectPage, SpecialType.EquipSlotReinforceChangeLevelEffect,
        SpecialType.RechargeItemProcLifetime, SpecialType.ItemChangeMapping, SpecialType.ChangeChargeSkillCount,
        SpecialType.ChangeChargeCooldown, SpecialType.ProcessCraftOrderInstant, SpecialType.ItemSocketChange,
        SpecialType.ZoneConflictChange, SpecialType.TeamSummon, SpecialType.MoveToSavedPos, SpecialType.Ensemble,
        SpecialType.EnsembleSuggest, SpecialType.GiveLeadershipPoint, SpecialType.ReduceBuffTime,
        SpecialType.GiveFactionCompetitionPoint, SpecialType.AddArchePassPoint, SpecialType.ResetFactionChangeCooldown,
        SpecialType.BuyPremium, SpecialType.ChangeZoneScore, SpecialType.TeleportToIntegrationWorld,
        SpecialType.ItemElement, SpecialType.ZonePermissionCheck, SpecialType.ButlerProductionCostCharge,
        SpecialType.ButlerAddExp, SpecialType.ItemEvolvingSelectReRoll, SpecialType.BindFactionRezDistrict,
        SpecialType.ChangeVisualRace, SpecialType.ChangeVisualRaceExpiredTime, SpecialType.SaveExpeditionPortal,
        SpecialType.TeleportExpeditionPortal, SpecialType.VariableCashCharge, SpecialType.AdditionalSkillPoint,
        SpecialType.ChangeForceAttackState, SpecialType.GainAppellationStampLifespan,
    ];

    /// <summary>
    /// Gameplay types the cast pipeline executes without an action class: combat_dice is rolled by
    /// PlotCondition, the two butler types by Skill.TryHandleButlerConsumable.
    /// </summary>
    private static readonly SpecialType[] PipelineOwned =
    [
        SpecialType.CombatDice, SpecialType.ButlerProductionCostCharge, SpecialType.ButlerAddExp,
    ];

    /// <summary>
    /// Unsupported types that still have an action class, every one of them log-only. Implementing one
    /// means flipping its table entry to Gameplay and removing it here, or the cast stays uncharged.
    /// </summary>
    private static readonly SpecialType[] LogOnlyStubs =
    [
        SpecialType.Interaction, SpecialType.RedeemBuff, SpecialType.MateMakeGetUp, SpecialType.CombatText,
        SpecialType.SextantPos, SpecialType.NotifyQuest, SpecialType.DestroyAndSpawnSlave, SpecialType.ReportBot,
        SpecialType.ReportBotExpired, SpecialType.ReportBotArrested, SpecialType.EngraveOnGuardTower,
        SpecialType.ArrestBot, SpecialType.AddCharacterSlot, SpecialType.ExpToItem,
        SpecialType.StartDominionNonPvpDuration, SpecialType.AuctionPostAuthority, SpecialType.RevertItemLook,
        SpecialType.RechargeItemSkill, SpecialType.RechargeItemRndAttrUnitModifier,
        SpecialType.GainGachaLootPackItem, SpecialType.ExpandDecoLimit, SpecialType.LearnSpecialAbility,
        SpecialType.ResidentServicePoint, SpecialType.ItemSmelting,
    ];

    [Test]
    public async Task EveryPopulatedType_IsClassifiedExplicitly()
    {
        var unclassified = PopulatedTypes.Where(type => !SpecialEffectOwnershipRules.IsClassified(type)).ToList();

        await Assert.That(PopulatedTypes.Length).IsEqualTo(161);
        await Assert.That(unclassified).IsEmpty();
    }

    [Test]
    public async Task EveryGameplayType_HasAnActionClass_OrThePipelineOwnsIt()
    {
        var orphans = SpecialEffectOwnershipRules.TypesWith(SpecialEffectOwnership.Gameplay)
            .Where(type => !SpecialEffect.HasActionClass(type) && !PipelineOwned.Contains(type))
            .ToList();

        await Assert.That(orphans).IsEmpty();
    }

    [Test]
    public async Task NoClientVisualType_HasAnActionClass()
    {
        // The dispatcher returns before resolving a class for these, so a class would be dead code
        // that reads as an implementation.
        var withClass = SpecialEffectOwnershipRules.TypesWith(SpecialEffectOwnership.ClientVisual)
            .Where(SpecialEffect.HasActionClass)
            .ToList();

        await Assert.That(withClass).IsEmpty();
    }

    [Test]
    public async Task UnsupportedTypesWithAClass_AreExactlyTheLogOnlyStubs()
    {
        var withClass = SpecialEffectOwnershipRules.TypesWith(SpecialEffectOwnership.Unsupported)
            .Where(SpecialEffect.HasActionClass)
            .OrderBy(type => type)
            .ToList();

        await Assert.That(withClass).IsEquivalentTo(LogOnlyStubs.OrderBy(type => type).ToList());
    }

    [Test]
    public async Task Classify_UnlistedValueIsUnsupported()
    {
        await Assert.That(SpecialEffectOwnershipRules.Classify((SpecialType)(-1)))
            .IsEqualTo(SpecialEffectOwnership.Unsupported);
        // Declared in the enum, no row in content, no table entry: the fallback, not an answer.
        await Assert.That(SpecialEffectOwnershipRules.IsClassified(SpecialType.Unk9)).IsFalse();
        await Assert.That(SpecialEffectOwnershipRules.Classify(SpecialType.Unk9))
            .IsEqualTo(SpecialEffectOwnership.Unsupported);
    }

    [Test]
    public async Task IsImplemented_FollowsTheTable_NotTheClassList()
    {
        await Assert.That(SpecialEffect.IsImplemented(SpecialType.StopChanneling)).IsTrue();
        await Assert.That(SpecialEffect.IsImplemented(SpecialType.Anim)).IsTrue();
        await Assert.That(SpecialEffect.IsImplemented(SpecialType.ItemSmelting)).IsFalse();
        // The audit's case: a class exists for 10 interaction and only logs.
        await Assert.That(SpecialEffect.HasActionClass(SpecialType.Interaction)).IsTrue();
        await Assert.That(SpecialEffect.IsImplemented(SpecialType.Interaction)).IsFalse();
    }

    [Test]
    public async Task TheNamedTypes_AreOwnedAsTheContentSays()
    {
        await Assert.That(SpecialEffectOwnershipRules.Classify(SpecialType.StopChanneling))
            .IsEqualTo(SpecialEffectOwnership.Gameplay);
        await Assert.That(SpecialEffectOwnershipRules.Classify(SpecialType.RebuildHousing))
            .IsEqualTo(SpecialEffectOwnership.Gameplay);
        // 77 is the fishing line: 18 of its 19 rows sit on fishing plot success events, the
        // nineteenth on another plot projectile event, and the server has no projectile object.
        await Assert.That(SpecialEffectOwnershipRules.Classify(SpecialType.RetrieveProjectile))
            .IsEqualTo(SpecialEffectOwnership.ClientVisual);
    }

    [Test]
    public async Task IsPureUnsupportedCast_EmptyListIsNotACast()
    {
        await Assert.That(SpecialEffectOwnershipRules.IsPureUnsupportedCast([])).IsFalse();
    }

    [Test]
    public async Task IsPureUnsupportedCast_OnlyUnsupportedTypes()
    {
        await Assert.That(SpecialEffectOwnershipRules.IsPureUnsupportedCast([SpecialType.ItemSmelting])).IsTrue();
        await Assert.That(SpecialEffectOwnershipRules.IsPureUnsupportedCast(
            [SpecialType.Interaction, SpecialType.ItemSmelting])).IsTrue();
    }

    [Test]
    public async Task IsPureUnsupportedCast_OneExecutedEffectKeepsTheCost()
    {
        // A non-special effect (null), a gameplay type, or a client-visual type each make the cast real.
        await Assert.That(SpecialEffectOwnershipRules.IsPureUnsupportedCast([SpecialType.ItemSmelting, null])).IsFalse();
        await Assert.That(SpecialEffectOwnershipRules.IsPureUnsupportedCast(
            [SpecialType.ItemSmelting, SpecialType.GainItem])).IsFalse();
        await Assert.That(SpecialEffectOwnershipRules.IsPureUnsupportedCast([SpecialType.OpacityControl])).IsFalse();
    }

    [Test]
    public async Task CountsAsExecuted_ClientVisualIsChargedAndUnsupportedIsNot()
    {
        await Assert.That(SpecialEffectOwnershipRules.CountsAsExecuted(SpecialEffectOwnership.Gameplay)).IsTrue();
        await Assert.That(SpecialEffectOwnershipRules.CountsAsExecuted(SpecialEffectOwnership.ClientVisual)).IsTrue();
        await Assert.That(SpecialEffectOwnershipRules.CountsAsExecuted(SpecialEffectOwnership.Unsupported)).IsFalse();
    }
}
