namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Who executes a <c>special_effects</c> row of a given <c>special_effect_type_id</c>.
/// </summary>
public enum SpecialEffectOwnership
{
    /// <summary>
    /// Nothing on this server runs it yet. A cast made only of these changes nothing, so it is not
    /// charged its mana, labor, reagents, cooldown or charges. The default for any type not in the table.
    /// </summary>
    Unsupported = 0,

    /// <summary>World executes it, directly or through a zone relay, and the cast is charged.</summary>
    Gameplay = 1,

    /// <summary>
    /// The client plays it from the SCSkillFired or SCPlotEvent it receives. The server is right to do
    /// nothing, and the cast is still charged.
    /// </summary>
    ClientVisual = 2,
}

/// <summary>
/// The one table that says, for every special effect type shipped in content, whether the server
/// executes it, the client owns it, or nobody does yet. An action class under <c>SpecialEffects</c>
/// is not proof of anything: 24 of them only log. <see cref="SpecialEffect.IsImplemented"/> and
/// <see cref="Skill"/>'s cost gates read this table, and SpecialEffectOwnershipRulesTests pins it
/// against the classes that exist.
/// </summary>
/// <remarks>
/// Counts are from game_decrypted.sqlite3 (10.0.2.13), <c>special_effects</c> joined to
/// <c>effects</c> (actual_type SpecialEffect), <c>skill_effects</c> (enable = 't'),
/// <c>buff_triggers</c> and <c>plot_effects</c>. 161 types have rows. "rows" is special_effects rows,
/// "skills" is distinct enabled skill links, "plot" is plot_effects rows.

/// Client-visual is decided by shape and by what the server owns: those rows sit on plot_effects,
/// which the client replays from the SCPlotEvent it receives, or on skill_effects every viewer
/// replays from SCSkillFired; the server has no animation, fx or projectile object to act on, and
/// Carries its own SpecialEffectDesc executor (RTTI, vftable)
/// for exactly that. The per-type client handler list was not recovered, so a type is only marked
/// client-visual when its name, its values and its placement all say so.
/// </remarks>
public static class SpecialEffectOwnershipRules
{
    private static readonly Dictionary<SpecialType, SpecialEffectOwnership> Table = new()
    {
        // Gameplay: the action class does real work, or the cast pipeline executes the type itself.
        [SpecialType.Charge] = SpecialEffectOwnership.Gameplay, // 1: 174 rows, 26 skills
        [SpecialType.DisturbCasting] = SpecialEffectOwnership.Gameplay, // 5: 151 rows, 14 skills, 108 buff triggers
        [SpecialType.LoseTarget] = SpecialEffectOwnership.Gameplay, // 8: 10 rows, skill 31183
        [SpecialType.Blink] = SpecialEffectOwnership.Gameplay, // 11: 99 rows, 36 skills
        [SpecialType.KnockBack] = SpecialEffectOwnership.Gameplay, // 13: 1082 rows, 314 skills
        [SpecialType.SpawnDoodad] = SpecialEffectOwnership.Gameplay, // 15: 382 rows, 233 skills
        [SpecialType.BuffSteal] = SpecialEffectOwnership.Gameplay, // 16 buff_control: 14 rows, 8 skills
        [SpecialType.FakeDeath] = SpecialEffectOwnership.Gameplay, // 18: 23 rows, 5 skills
        [SpecialType.Resurrection] = SpecialEffectOwnership.Gameplay, // 22: 42 rows, 9 skills
        [SpecialType.CapturePet] = SpecialEffectOwnership.Gameplay, // 23: 16 rows, 13 skills
        [SpecialType.SpawnPet] = SpecialEffectOwnership.Gameplay, // 24: 83 rows, 83 skills
        [SpecialType.Return] = SpecialEffectOwnership.Gameplay, // 25: 606 rows, 500 skills
        [SpecialType.GainItem] = SpecialEffectOwnership.Gameplay, // 27: 936 rows, 660 skills
        [SpecialType.AddExp] = SpecialEffectOwnership.Gameplay, // 28: 37 rows, 31 skills
        [SpecialType.AddLaborPower] = SpecialEffectOwnership.Gameplay, // 29: 214 rows, 109 skills
        [SpecialType.SavePortal] = SpecialEffectOwnership.Gameplay, // 30: 3 rows, 3 skills
        [SpecialType.SkillUse] = SpecialEffectOwnership.Gameplay, // 33 skill: 3975 rows, 697 skills, 1731 buff triggers
        [SpecialType.ManaCost] = SpecialEffectOwnership.Gameplay, // 39: 645 rows, all on plots
        [SpecialType.Cooldown] = SpecialEffectOwnership.Gameplay, // 40: 1435 rows, all on plots
        [SpecialType.GlobalCooldown] = SpecialEffectOwnership.Gameplay, // 41: 2870 rows, all on plots
        [SpecialType.ResetCooldown] = SpecialEffectOwnership.Gameplay, // 43: 209 rows, 39 skills
        [SpecialType.GainItemWithEmblemImprint] = SpecialEffectOwnership.Gameplay, // 45: 2 rows, 2 skills
        [SpecialType.ExplodeBuff] = SpecialEffectOwnership.Gameplay, // 46: 3 rows, 2 skills
        [SpecialType.TeleportToUnit] = SpecialEffectOwnership.Gameplay, // 47: 142 rows, 41 skills
        [SpecialType.ItemConversion] = SpecialEffectOwnership.Gameplay, // 49: 14 rows, 13 skills
        [SpecialType.DeclareDominion] = SpecialEffectOwnership.Gameplay, // 50: 2 rows, 1 skill
        [SpecialType.StopManaRegen] = SpecialEffectOwnership.Gameplay, // 52: 710 rows, all on plots
        [SpecialType.AttachTo] = SpecialEffectOwnership.Gameplay, // 53: 40 rows, 40 skills
        [SpecialType.AddBreath] = SpecialEffectOwnership.Gameplay, // 54: 39 rows, 1 skill
        [SpecialType.Detach] = SpecialEffectOwnership.Gameplay, // 55: 94 rows, 92 on plots
        [SpecialType.HealPet] = SpecialEffectOwnership.Gameplay, // 56: 9 rows, 6 skills (PetHealRules)
        [SpecialType.RemoveDoodad] = SpecialEffectOwnership.Gameplay, // 58: 862 rows, 69 skills
        [SpecialType.CancelStealth] = SpecialEffectOwnership.Gameplay, // 59: 1096 rows, 1086 on plots
        [SpecialType.SpawnSlave] = SpecialEffectOwnership.Gameplay, // 60: 14 rows, 14 skills
        [SpecialType.CancelOngoingBuff] = SpecialEffectOwnership.Gameplay, // 61: 995 rows, all on plots
        [SpecialType.TeleportToSiegeHq] = SpecialEffectOwnership.Gameplay, // 65: 2 rows, 2 skills
        [SpecialType.AutoAttack] = SpecialEffectOwnership.Gameplay, // 66: 261 rows, all on plots
        // 67 combat_dice: 368 rows, all on plot damage events. The roll is made by
        // PlotCondition.ConditionCombatDiceResult (Skill.RollCombatDice); the row is the marker the
        // plot carries for it, so the class is deliberately empty.
        [SpecialType.CombatDice] = SpecialEffectOwnership.Gameplay,
        [SpecialType.ApplyReagents] = SpecialEffectOwnership.Gameplay, // 68: 90 rows, all on plots
        [SpecialType.MoveToGround] = SpecialEffectOwnership.Gameplay, // 73: 38 rows, 11 skills
        [SpecialType.Escape] = SpecialEffectOwnership.Gameplay, // 74: 1 row, 1 skill
        [SpecialType.FishingLoot] = SpecialEffectOwnership.Gameplay, // 79: 4 rows, all on fishing plots
        [SpecialType.StopChanneling] = SpecialEffectOwnership.Gameplay, // 80: 4 rows, 3 on plots, buff 27645
        [SpecialType.FinishChanneling] = SpecialEffectOwnership.Gameplay, // 81: 38 rows, all on plots
        [SpecialType.SetVariable] = SpecialEffectOwnership.Gameplay, // 82: 621 rows, all on plots
        [SpecialType.ConsumeLaborPower] = SpecialEffectOwnership.Gameplay, // 86: 71 rows, all on plots
        [SpecialType.GiveLivingPoint] = SpecialEffectOwnership.Gameplay, // 87: 53 rows, 29 skills
        [SpecialType.ApplyBotTrial] = SpecialEffectOwnership.Gameplay, // 88: 1 row, 1 skill
        [SpecialType.EscapeMySlave] = SpecialEffectOwnership.Gameplay, // 90: 6 rows, 6 skills
        [SpecialType.GradeEnchant] = SpecialEffectOwnership.Gameplay, // 92: 12 rows, 12 skills
        [SpecialType.PlayUserMusic] = SpecialEffectOwnership.Gameplay, // 93: 1 row, 1 skill
        [SpecialType.PauseUserMusic] = SpecialEffectOwnership.Gameplay, // 94: 14 rows, 2 skills
        [SpecialType.RechargeItemBuff] = SpecialEffectOwnership.Gameplay, // 95: 9 rows, 9 skills
        [SpecialType.UserMusicSaveNotes] = SpecialEffectOwnership.Gameplay, // 97: 4 rows, 4 skills
        [SpecialType.Dyeing] = SpecialEffectOwnership.Gameplay, // 98: 1 row, 1 skill
        [SpecialType.GiveBmMileage] = SpecialEffectOwnership.Gameplay, // 99: 36 rows, 26 skills
        [SpecialType.GiveHonorPoint] = SpecialEffectOwnership.Gameplay, // 100: 119 rows, 118 skills
        [SpecialType.GiveCrimePoint] = SpecialEffectOwnership.Gameplay, // 102: 11 rows, 10 skills
        [SpecialType.AggroCopy] = SpecialEffectOwnership.Gameplay, // 104: 20 rows, 6 skills
        [SpecialType.AggroReset] = SpecialEffectOwnership.Gameplay, // 105: 163 rows, 83 skills
        [SpecialType.ItemSocketing] = SpecialEffectOwnership.Gameplay, // 106: 7 rows, 7 skills
        [SpecialType.Skinize] = SpecialEffectOwnership.Gameplay, // 108: 1 row, 1 skill
        [SpecialType.NpcDespawn] = SpecialEffectOwnership.Gameplay, // 109: 495 rows, 48 skills
        [SpecialType.GiveAppellation] = SpecialEffectOwnership.Gameplay, // 114: 594 rows, 594 skills
        [SpecialType.ExitArchemall] = SpecialEffectOwnership.Gameplay, // 115 exit_indun: 6 rows, 4 skills
        [SpecialType.GiveCashPoint] = SpecialEffectOwnership.Gameplay, // 118: 35 rows, 35 skills
        [SpecialType.RebuildHousing] = SpecialEffectOwnership.Gameplay, // 122: 2 rows, 2 skills
        [SpecialType.ItemEvolving] = SpecialEffectOwnership.Gameplay, // 123: 1 row, 1 skill
        [SpecialType.ItemRefurbishment] = SpecialEffectOwnership.Gameplay, // 126: 15 rows, 15 skills
        [SpecialType.ProtectionForExpedition] = SpecialEffectOwnership.Gameplay, // 127: 1 row, 1 skill
        [SpecialType.ExpeditionSummon] = SpecialEffectOwnership.Gameplay, // 128: 1 row, 1 skill
        [SpecialType.RemoveDoodadGroup] = SpecialEffectOwnership.Gameplay, // 130: 2 rows, both on plots
        [SpecialType.AddExpeditionExp] = SpecialEffectOwnership.Gameplay, // 131: 2 rows, 2 skills
        [SpecialType.AddExpeditionContributionPoint] = SpecialEffectOwnership.Gameplay, // 132: 3 rows, 2 skills
        [SpecialType.ChangeSkillActiveType] = SpecialEffectOwnership.Gameplay, // 134: 216 rows, 140 skills
        [SpecialType.ActivateSavedAbilitySet] = SpecialEffectOwnership.Gameplay, // 135: 1 row, 1 skill
        [SpecialType.ItemEvolvingReRoll] = SpecialEffectOwnership.Gameplay, // 136: 2 rows, 2 skills
        [SpecialType.ExpeditionLevelChange] = SpecialEffectOwnership.Gameplay, // 139: 7 rows, 7 skills
        [SpecialType.RemoveAllDoodad] = SpecialEffectOwnership.Gameplay, // 140: 52 rows, 3 skills
        [SpecialType.ChangeTarget] = SpecialEffectOwnership.Gameplay, // 145: 23 rows, 4 skills
        [SpecialType.LoseTargetingTheTarget] = SpecialEffectOwnership.Gameplay, // 146: 65 rows, 6 skills
        [SpecialType.ReduceCooldown] = SpecialEffectOwnership.Gameplay, // 153: 101 rows, 6 skills
        [SpecialType.FamilyLevelChange] = SpecialEffectOwnership.Gameplay, // 154: 2 rows, 2 skills
        [SpecialType.MakeCraftOrderSheet] = SpecialEffectOwnership.Gameplay, // 155: 1 row, 1 skill
        [SpecialType.RestoreDisableEnchant] = SpecialEffectOwnership.Gameplay, // 156: 2 rows, 2 skills
        [SpecialType.ChangeBuffToleranceStep] = SpecialEffectOwnership.Gameplay, // 157: 19 rows, 1 skill
        [SpecialType.ChargeCooldown] = SpecialEffectOwnership.Gameplay, // 158: 13 rows, all on plots
        [SpecialType.RestoreCraftOrderSheet] = SpecialEffectOwnership.Gameplay, // 159: 1 row, 1 skill
        [SpecialType.ProcessCraftOrder] = SpecialEffectOwnership.Gameplay, // 160: 1 row, 1 skill
        [SpecialType.EquipSlotReinforceAddExp] = SpecialEffectOwnership.Gameplay, // 161: 2 rows, 1 skill
        [SpecialType.BlessUthstinSelectPage] = SpecialEffectOwnership.Gameplay, // 162: 1 row, 1 skill
        [SpecialType.EquipSlotReinforceChangeLevelEffect] = SpecialEffectOwnership.Gameplay, // 163: 1 row, 1 skill
        [SpecialType.ItemChangeMapping] = SpecialEffectOwnership.Gameplay, // 165: 322 rows, 322 skills
        [SpecialType.ChangeChargeSkillCount] = SpecialEffectOwnership.Gameplay, // 166: 5 rows, 1 skill
        [SpecialType.ChangeChargeCooldown] = SpecialEffectOwnership.Gameplay, // 167: 1 row, 1 skill
        [SpecialType.ProcessCraftOrderInstant] = SpecialEffectOwnership.Gameplay, // 168: 1 row, 1 skill
        [SpecialType.ItemSocketChange] = SpecialEffectOwnership.Gameplay, // 169: 17 rows, 17 skills
        [SpecialType.ZoneConflictChange] = SpecialEffectOwnership.Gameplay, // 170: 22 rows, 19 skills
        [SpecialType.MoveToSavedPos] = SpecialEffectOwnership.Gameplay, // 172: 9 rows, 9 skills
        [SpecialType.Ensemble] = SpecialEffectOwnership.Gameplay, // 173: 1 row, 1 skill
        [SpecialType.EnsembleSuggest] = SpecialEffectOwnership.Gameplay, // 174: 1 row, 1 skill
        [SpecialType.GiveLeadershipPoint] = SpecialEffectOwnership.Gameplay, // 175: 2 rows, 2 skills
        [SpecialType.ReduceBuffTime] = SpecialEffectOwnership.Gameplay, // 176: 47 rows, 13 skills
        [SpecialType.AddArchePassPoint] = SpecialEffectOwnership.Gameplay, // 178: 17 rows, 17 skills
        [SpecialType.ItemElement] = SpecialEffectOwnership.Gameplay, // 183: 1 row, 1 skill
        // 185/186: Skill.TryHandleButlerConsumable executes both through IButlerChargeService before
        // the effect list is walked, so no action class is needed.
        [SpecialType.ButlerProductionCostCharge] = SpecialEffectOwnership.Gameplay, // 185: 2 rows, 2 skills
        [SpecialType.ButlerAddExp] = SpecialEffectOwnership.Gameplay, // 186: 1 row, 1 skill
        [SpecialType.ItemEvolvingSelectReRoll] = SpecialEffectOwnership.Gameplay, // 187: 2 rows, 2 skills
        [SpecialType.SaveExpeditionPortal] = SpecialEffectOwnership.Gameplay, // 191: 3 rows, 3 skills
        [SpecialType.TeleportExpeditionPortal] = SpecialEffectOwnership.Gameplay, // 192: 1 row, 1 skill
        [SpecialType.ChangeForceAttackState] = SpecialEffectOwnership.Gameplay, // 195: 1 row, 1 skill

        // Client-visual: no server object exists for what the row names.
        [SpecialType.Anim] = SpecialEffectOwnership.ClientVisual, // 34: 9439 rows, all on plots; value1 is an animations id the server reads only for timing
        [SpecialType.FxGroup] = SpecialEffectOwnership.ClientVisual, // 35: 5349 rows, 5300 on plots, 0 enabled skill links
        [SpecialType.FxGroupAnim] = SpecialEffectOwnership.ClientVisual, // 36: 1856 rows, all on plots
        [SpecialType.Projectile] = SpecialEffectOwnership.ClientVisual, // 37: 4990 rows, 4968 on plots
        [SpecialType.ProjectileAnim] = SpecialEffectOwnership.ClientVisual, // 38: 550 rows, all on plots
        [SpecialType.OpacityControl] = SpecialEffectOwnership.ClientVisual, // 42: 121 rows, 73 skills; every viewer replays the fired skill
        // 48 combo: 149 rows, 146 skills. value1 names the next hold-hit and value2 its window; the
        // client starts that skill as an ordinary cast, which World validates like any other. Whether
        // World must also gate the follow-up window is an open question (SKILL_TASKS E8).
        [SpecialType.Combo] = SpecialEffectOwnership.ClientVisual,
        // 76/77/78: the fishing line. 76 sits on the fail and hook events of fishing plots (52 rows,
        // 30 plots), 77 on their success and box events (19 rows, 15 plots) and 78 adds fx to it. The
        // server has no projectile object; the fish and the loot are 109 and 79.
        [SpecialType.ClearProjectile] = SpecialEffectOwnership.ClientVisual,
        [SpecialType.RetrieveProjectile] = SpecialEffectOwnership.ClientVisual,
        [SpecialType.AddFxToProjectile] = SpecialEffectOwnership.ClientVisual, // 78: 17 rows, all on plots
        [SpecialType.WeaponDisplay] = SpecialEffectOwnership.ClientVisual, // 116: 115 rows, all on plots (show or hide the weapon model)
        [SpecialType.PlayAttachmentAnim] = SpecialEffectOwnership.ClientVisual, // 149: 3 rows, all on plots

        // Unsupported: an action class may exist but only logs, or no class exists. Fail closed.
        [SpecialType.Interaction] = SpecialEffectOwnership.Unsupported, // 10: 19 rows, 19 skills; value1 is an interaction kind with no server model
        [SpecialType.RedeemBuff] = SpecialEffectOwnership.Unsupported, // 44: 1 row, skill 11988
        [SpecialType.MateMakeGetUp] = SpecialEffectOwnership.Unsupported, // 51: 1 row, skill 13719 (mate recovery note)
        [SpecialType.CombatText] = SpecialEffectOwnership.Unsupported, // 63: 331 rows, 1 skill (combat text note)
        [SpecialType.SextantPos] = SpecialEffectOwnership.Unsupported, // 69: 2 rows, 2 skills; no packet known
        [SpecialType.NotifyQuest] = SpecialEffectOwnership.Unsupported, // 70: 10 rows, all on plots, all values zero
        [SpecialType.DestroyAndSpawnSlave] = SpecialEffectOwnership.Unsupported, // 72: 1 row, skill 19165, all values zero
        [SpecialType.ReportBot] = SpecialEffectOwnership.Unsupported, // 75: 1 row; CharacterBotCheck has no report path
        [SpecialType.ReportBotExpired] = SpecialEffectOwnership.Unsupported, // 83: 4 rows, 2 skills
        [SpecialType.ReportBotArrested] = SpecialEffectOwnership.Unsupported, // 84: 1 row, buff trigger only
        [SpecialType.EngraveOnGuardTower] = SpecialEffectOwnership.Unsupported, // 85: 10 rows, 8 skills; no guard tower model
        [SpecialType.ArrestBot] = SpecialEffectOwnership.Unsupported, // 89: 1 row, 1 skill
        [SpecialType.AddCharacterSlot] = SpecialEffectOwnership.Unsupported, // 91: 2 rows; slots are config, no per-account count
        [SpecialType.ExpToItem] = SpecialEffectOwnership.Unsupported, // 96: 1 row, skill 22580
        [SpecialType.StartDominionNonPvpDuration] = SpecialEffectOwnership.Unsupported, // 110: 1 row, skill 23992
        [SpecialType.AuctionPostAuthority] = SpecialEffectOwnership.Unsupported, // 117: 1 row, buff trigger only
        [SpecialType.RevertItemLook] = SpecialEffectOwnership.Unsupported, // 119: 2 rows, 2 skills
        [SpecialType.RechargeItemSkill] = SpecialEffectOwnership.Unsupported, // 120: 3 rows, 3 skills
        [SpecialType.RechargeItemRndAttrUnitModifier] = SpecialEffectOwnership.Unsupported, // 124: 4 rows, 4 skills
        [SpecialType.GainGachaLootPackItem] = SpecialEffectOwnership.Unsupported, // 133: 3 rows, 3 skills, all values zero
        [SpecialType.ExpandDecoLimit] = SpecialEffectOwnership.Unsupported, // 137: 2 rows; SCHousingDecoLimitExpanded exists, no house limit model
        [SpecialType.LearnSpecialAbility] = SpecialEffectOwnership.Unsupported, // 141: 1 row, skill 33995
        [SpecialType.ResidentServicePoint] = SpecialEffectOwnership.Unsupported, // 143: 31 rows, 31 skills; no resident model
        [SpecialType.ItemSmelting] = SpecialEffectOwnership.Unsupported, // 151: 3 rows, 3 skills; SCItemSmeltingResult exists, no smelting model
        [SpecialType.RechargeItemProcLifetime] = SpecialEffectOwnership.Unsupported, // 164: 1 row, no class
        [SpecialType.TeamSummon] = SpecialEffectOwnership.Unsupported, // 171: 1 row, skill 39700, no class
        [SpecialType.GiveFactionCompetitionPoint] = SpecialEffectOwnership.Unsupported, // 177: 72 rows, 6 skills, no class
        [SpecialType.ResetFactionChangeCooldown] = SpecialEffectOwnership.Unsupported, // 179: 1 row, skill 43380, no class
        [SpecialType.BuyPremium] = SpecialEffectOwnership.Unsupported, // 180: 13 rows, 13 skills, cash flow, no class
        [SpecialType.ChangeZoneScore] = SpecialEffectOwnership.Unsupported, // 181: 107 rows, 12 skills, no class
        [SpecialType.TeleportToIntegrationWorld] = SpecialEffectOwnership.Unsupported, // 182: 3 rows, 3 skills, no class
        [SpecialType.ZonePermissionCheck] = SpecialEffectOwnership.Unsupported, // 184: 5 rows, 5 skills, no class
        [SpecialType.BindFactionRezDistrict] = SpecialEffectOwnership.Unsupported, // 188: 1 row, skill 46607, no class
        [SpecialType.ChangeVisualRace] = SpecialEffectOwnership.Unsupported, // 189: 10 rows, 10 skills, no class
        [SpecialType.ChangeVisualRaceExpiredTime] = SpecialEffectOwnership.Unsupported, // 190: 1 row, no class
        [SpecialType.VariableCashCharge] = SpecialEffectOwnership.Unsupported, // 193: 1 row, cash flow, no class
        [SpecialType.AdditionalSkillPoint] = SpecialEffectOwnership.Unsupported, // 194: 2 rows; SCUpdateAdditionalSkillPoint only ever sends 0
        [SpecialType.GainAppellationStampLifespan] = SpecialEffectOwnership.Unsupported, // 196: 2 rows, 2 skills, no class
    };

    /// <summary>The table's answer, or Unsupported for a type it does not list.</summary>
    public static SpecialEffectOwnership Classify(SpecialType type) =>
        Table.TryGetValue(type, out var ownership) ? ownership : SpecialEffectOwnership.Unsupported;

    /// <summary>Whether the table names the type at all, as opposed to falling back to Unsupported.</summary>
    public static bool IsClassified(SpecialType type) => Table.ContainsKey(type);

    /// <summary>
    /// Whether a cast of this ownership does something the caster should pay for. Client-visual counts:
    /// the player sees the effect, so the cost stands even though World ran nothing.
    /// </summary>
    public static bool CountsAsExecuted(SpecialEffectOwnership ownership) =>
        ownership != SpecialEffectOwnership.Unsupported;

    /// <summary>
    /// Whether a cast is made only of unsupported special effects. Each entry is the special type of
    /// one effect, or null for an effect of any other kind. An empty list is not a cast at all and
    /// answers false, and one non-special or executed effect is enough to keep the normal cost path.
    /// </summary>
    public static bool IsPureUnsupportedCast(IEnumerable<SpecialType?> effectTypes)
    {
        var sawEffect = false;
        foreach (var effectType in effectTypes)
        {
            if (effectType == null || CountsAsExecuted(Classify(effectType.Value)))
                return false;
            sawEffect = true;
        }

        return sawEffect;
    }

    /// <summary>Every type the table gives the ownership to, for the tests that pin it against the classes.</summary>
    public static IEnumerable<SpecialType> TypesWith(SpecialEffectOwnership ownership) =>
        Table.Where(entry => entry.Value == ownership).Select(entry => entry.Key);
}
