//! `SpecialEffect.Apply` and the `SpecialEffectAction`s (`Effects/SpecialEffects/*`).
//!
//! `execute` first offers the action to the other subsystems (`ext::ExtHooks::route_special`,
//! any of them may override an action), then runs the port of the C# class named after the
//! `SpecialType`:
//! * [`combat`] — buffs, cooldowns, mana, plots, auto attack, resurrection;
//! * [`movement`] — blink / teleport / knock back / escape / return (+ the NPC displacement
//!   shared with `ImpulseEffect`);
//! * [`items`] — item, loot, music and account point actions (item manager parts are hooks);
//! * [`misc`] — exp / labor / points, portals, doodads, mates / slaves, crime and the actions
//!   that only log in the C#.
//!
//! A `SpecialType` without a C# class (`Unk9` and the values the enum does not name) logs
//! "Unknown special effect", as `Type.GetType` returns null there.

pub mod combat;
pub mod items;
pub mod misc;
pub mod movement;

use crate::server::GameServer;

use super::super::data::effects::SpecialEffect;
use super::EffectArgs;

/// `SpecialType.Projectile` (client side; the C# class only logs).
pub const PROJECTILE: i32 = 37;

/// `SpecialType` names (`Effects/SpecialEffectType.cs`).
pub const SPECIAL_TYPES: &[(i32, &str)] = &[
    (1, "Charge"),
    (5, "DisturbCasting"),
    (8, "LoseTarget"),
    (9, "Unk9"),
    (10, "Interaction"),
    (11, "Blink"),
    (13, "KnockBack"),
    (15, "SpawnDoodad"),
    (16, "BuffSteal"),
    (17, "Track"),
    (18, "FakeDeath"),
    (19, "SpawnBomb"),
    (20, "PhysicalEnchantWeapon"),
    (21, "PhysicalEnchantArmor"),
    (22, "Resurrection"),
    (23, "CapturePet"),
    (24, "SpawnPet"),
    (25, "Return"),
    (27, "GainItem"),
    (28, "AddExp"),
    (29, "AddLaborPower"),
    (30, "SavePortal"),
    (31, "OpenPortal"),
    (32, "GainItemWithPosImprint"),
    (33, "SkillUse"),
    (34, "Anim"),
    (35, "FxGroup"),
    (36, "FxGroupAnim"),
    (37, "Projectile"),
    (38, "ProjectileAnim"),
    (39, "ManaCost"),
    (40, "Cooldown"),
    (41, "GlobalCooldown"),
    (42, "OpacityControl"),
    (43, "ResetCooldown"),
    (44, "RedeemBuff"),
    (45, "GainItemWithEmblemImprint"),
    (46, "ExplodeBuff"),
    (47, "TeleportToUnit"),
    (48, "Combo"),
    (49, "ItemConversion"),
    (50, "DeclareDominion"),
    (51, "MateMakeGetUp"),
    (52, "StopManaRegen"),
    (53, "AttachTo"),
    (54, "AddBreath"),
    (55, "Detach"),
    (56, "HealPet"),
    (57, "RenewEquipment"),
    (58, "RemoveDoodad"),
    (59, "CancelStealth"),
    (60, "SpawnSlave"),
    (61, "CancelOngoingBuff"),
    (62, "HealSlave"),
    (63, "CombatText"),
    (64, "GetSiegeTicket"),
    (65, "TeleportToSiegeHq"),
    (66, "AutoAttack"),
    (67, "CombatDice"),
    (68, "ApplyReagents"),
    (69, "SextantPos"),
    (70, "NotifyQuest"),
    (71, "DeclareIndependence"),
    (72, "DestroyAndSpawnSlave"),
    (73, "MoveToGround"),
    (74, "Escape"),
    (75, "ReportBot"),
    (76, "ClearProjectile"),
    (77, "RetrieveProjectile"),
    (78, "AddFxToProjectile"),
    (79, "FishingLoot"),
    (80, "StopChanneling"),
    (81, "FinishChanneling"),
    (82, "SetVariable"),
    (83, "ReportBotExpired"),
    (84, "ReportBotArrested"),
    (85, "EngraveOnGuardTower"),
    (86, "ConsumeLaborPower"),
    (87, "GiveLivingPoint"),
    (88, "ApplyBotTrial"),
    (89, "ArrestBot"),
    (90, "EscapeMySlave"),
    (91, "AddCharacterSlot"),
    (92, "GradeEnchant"),
    (93, "PlayUserMusic"),
    (94, "PauseUserMusic"),
    (95, "RechargeItemBuff"),
    (96, "ExpToItem"),
    (97, "UserMusicSaveNotes"),
    (98, "Dyeing"),
    (99, "GiveBmMileage"),
    (100, "GiveHonorPoint"),
    (101, "EnterBeautyshop"),
    (102, "GiveCrimePoint"),
    (103, "CleanupLookConvert"),
    (104, "AggroCopy"),
    (105, "AggroReset"),
    (106, "ItemSocketing"),
    (107, "GenderTransfer"),
    (108, "Skinize"),
    (109, "NpcDespawn"),
    (110, "StartDominionNonPvpDuration"),
    (111, "ItemCapScaleReset"),
    (112, "ItemCapScale"),
    (113, "AddPStat"),
    (114, "GiveAppellation"),
    (115, "ExitArchemall"),
    (116, "WeaponDisplay"),
    (117, "AuctionPostAuthority"),
    (118, "GiveCashPoint"),
    (119, "RevertItemLook"),
    (120, "RechargeItemSkill"),
    (121, "RepairAuthorityInBag"),
    (122, "RebuildHousing"),
    (123, "ItemEvolving"),
    (124, "RechargeItemRndAttrUnitModifier"),
    (125, "HudAuctionAuthority"),
    (126, "ItemRefurbishment"),
    (127, "ProtectionForExpedition"),
    (128, "ExpeditionSummon"),
    (129, "HudBattlefieldAuthority"),
    (130, "RemoveDoodadGroup"),
    (131, "AddExpeditionExp"),
    (132, "AddExpeditionContributionPoint"),
    (133, "GainGachaLootPackItem"),
    (134, "ChangeSkillActiveType"),
    (135, "ActivateSavedAbilitySet"),
    (136, "ItemEvolvingReRoll"),
    (137, "ExpandDecoLimit"),
    (138, "DominionTaxInKind"),
    (139, "ExpeditionLevelChange"),
    (140, "RemoveAllDoodad"),
    (141, "LearnSpecialAbility"),
    (142, "IncreaseFavoritePortalLimit"),
    (143, "ResidentServicePoint"),
    (144, "AddFamilyExp"),
    (145, "ChangeTarget"),
    (146, "LoseTargetingTheTarget"),
    (149, "PlayAttachmentAnim"),
    (150, "PlaySkillControllerAttachmentAnim"),
    (151, "ItemSmelting"),
];

/// `SpecialType.ToString()` (`None` for a value the enum does not name; the C# prints the
/// number then).
pub fn special_type_name(kind: i32) -> Option<&'static str> {
    SPECIAL_TYPES
        .iter()
        .find(|(k, _)| *k == kind)
        .map(|(_, n)| *n)
}

/// `SpecialEffect.Apply`: runs the action once, or `EffectRepeatCount` times
/// `EffectRepeatTick` ms apart (the C# sleeps on the calling thread; here the repetitions are
/// scheduled).
pub fn apply(server: &GameServer, e: &SpecialEffect, args: &EffectArgs<'_>) {
    let values = [e.value1, e.value2, e.value3, e.value4];
    let repeat = args
        .source
        .skill()
        .map_or(0, |s| s.template.effect_repeat_count);
    if repeat > 1 {
        let tick = args
            .source
            .skill()
            .map_or(0, |s| s.template.effect_repeat_tick);
        execute(server, e.special_effect_type_id, args, values);
        for i in 1..repeat {
            let owned = OwnedArgs::from(args);
            let kind = e.special_effect_type_id;
            super::super::tasks::schedule(server, i64::from(i) * i64::from(tick), move |srv| {
                let so = owned.skill_object.clone();
                let a = owned.to_args(&so);
                execute(&srv, kind, &a, values);
            });
        }
    } else {
        execute(server, e.special_effect_type_id, args, values);
    }
}

/// `EffectArgs` with an owned skill object (for scheduled repetitions / delayed actions).
#[derive(Debug, Clone)]
pub(crate) struct OwnedArgs {
    caster: Option<crate::world::WorldObject>,
    caster_caster: super::super::types::SkillCaster,
    target: super::super::geometry::Target,
    target_caster: super::super::types::SkillCastTarget,
    cast: super::super::types::CastAction,
    source: super::EffectSource,
    skill_object: super::super::types::SkillObject,
}

impl OwnedArgs {
    pub(crate) fn from(a: &EffectArgs<'_>) -> Self {
        Self {
            caster: a.caster.clone(),
            caster_caster: a.caster_caster,
            target: a.target.clone(),
            target_caster: a.target_caster,
            cast: a.cast,
            source: a.source.clone(),
            skill_object: a.skill_object.clone(),
        }
    }

    pub(crate) fn to_args<'a>(&self, so: &'a super::super::types::SkillObject) -> EffectArgs<'a> {
        EffectArgs {
            caster: self.caster.clone(),
            caster_caster: self.caster_caster,
            target: self.target.clone(),
            target_caster: self.target_caster,
            cast: self.cast,
            source: self.source.clone(),
            skill_object: so,
        }
    }
}

/// `caster is Character`.
pub(crate) fn caster_is_player(args: &EffectArgs<'_>) -> bool {
    args.caster
        .as_ref()
        .is_some_and(|c| c.as_player().is_some())
}

/// The `if (caster is Character) Logger.Debug("Special effects: X value1 ..., value4 ...")`
/// line every action starts with.
pub(crate) fn log_values(args: &EffectArgs<'_>, name: &str, v: [i32; 4]) {
    if caster_is_player(args) {
        tracing::debug!(
            "Special effects: {name} value1 {}, value2 {}, value3 {}, value4 {}",
            v[0],
            v[1],
            v[2],
            v[3]
        );
    }
}

/// `SpecialEffectAction.Execute` of one special type.
pub fn execute(server: &GameServer, kind: i32, args: &EffectArgs<'_>, values: [i32; 4]) {
    if server
        .systems
        .skills
        .ext
        .route_special(server, kind, args, values)
    {
        return;
    }
    let s = server;
    let a = args;
    let v = values;
    match kind {
        1 => combat::charge(s, a, v),
        5 => combat::disturb_casting(s, a, v),
        11 => movement::blink(s, a, v),
        13 => movement::knock_back(s, a, v),
        15 => misc::spawn_doodad(s, a, v),
        22 => combat::resurrection(s, a, v),
        23 => items::capture_pet(s, a, v),
        24 => misc::spawn_pet(s, a, v),
        25 => movement::return_(s, a, v),
        27 => items::gain_item(s, a, v),
        28 => misc::add_exp(s, a, v),
        29 => misc::add_labor_power(s, a, v),
        30 => misc::save_portal(s, a, v),
        33 => combat::skill_use(s, a, v),
        39 => combat::mana_cost(s, a, v),
        40 => combat::cooldown(s, a, v),
        41 => combat::global_cooldown(s, a, v),
        43 => combat::reset_cooldown(s, a, v),
        45 => items::gain_item_with_emblem_imprint(s, a, v),
        47 => movement::teleport_to_unit(s, a, v),
        49 => items::item_conversion(s, a, v),
        50 => misc::declare_dominion(s, a, v),
        52 => combat::stop_mana_regen(s, a, v),
        53 => misc::attach_to(s, a, v),
        54 => combat::add_breath(s, a, v),
        58 => misc::remove_doodad(s, a, v),
        59 => combat::cancel_stealth(s, a, v),
        60 => misc::spawn_slave(s, a, v),
        66 => combat::auto_attack(s, a, v),
        68 => items::apply_reagents(s, a, v),
        74 => movement::escape(s, a, v),
        75 => misc::report_bot(s, a, v),
        79 => items::fishing_loot(s, a, v),
        81 => combat::finish_channeling(s, a, v),
        82 => combat::set_variable(s, a, v),
        83 => misc::report_bot_expired(s, a, v),
        86 => combat::consume_labor_power(s, a, v),
        87 => misc::give_living_point(s, a, v),
        90 => misc::escape_my_slave(s, a, v),
        92 => items::grade_enchant(s, a, v),
        93 => items::play_user_music(s, a, v),
        94 => items::pause_user_music(s, a, v),
        97 => items::user_music_save_notes(s, a, v),
        98 => items::dyeing(s, a, v),
        99 => items::give_bm_mileage(s, a, v),
        100 => misc::give_honor_point(s, a, v),
        101 => misc::enter_beautyshop(s, a, v),
        102 => misc::give_crime_point(s, a, v),
        103 => items::cleanup_look_convert(s, a, v),
        106 => items::item_socketing(s, a, v),
        108 => items::skinize(s, a, v),
        111 => items::item_cap_scale_reset(s, a, v),
        112 => items::item_cap_scale(s, a, v),
        114 => misc::give_appellation(s, a, v),
        115 => misc::exit_archemall(s, a, v),
        118 => items::give_cash_point(s, a, v),
        // `Detach` (the C# body only logs; the unmount is commented out).
        55 => log_values(a, "Detach", v),
        // Every other class only logs (`if (caster is Character) Logger.Debug(...)`).
        _ => match special_type_name(kind) {
            Some("Unk9") | None => {
                // `Type.GetType` finds no class: logged unless it is a projectile.
                let name = special_type_name(kind).map_or_else(|| kind.to_string(), str::to_string);
                tracing::warn!("Unknown special effect: {name}");
            }
            Some(name) => log_values(a, name, v),
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn special_type_names_match_the_enum() {
        assert_eq!(special_type_name(PROJECTILE), Some("Projectile"));
        assert_eq!(special_type_name(33), Some("SkillUse"));
        assert_eq!(special_type_name(151), Some("ItemSmelting"));
        assert_eq!(special_type_name(147), None);
        assert_eq!(special_type_name(2), None);
        assert_eq!(SPECIAL_TYPES.len(), 141);
        let mut ids: Vec<i32> = SPECIAL_TYPES.iter().map(|(k, _)| *k).collect();
        ids.dedup();
        assert_eq!(ids.len(), SPECIAL_TYPES.len());
    }
}
