//! `DoodadFuncTemplate.Use` of every (non-phase) func of `Models/Game/DoodadObj/Funcs`.
//!
//! [`use_func`] is the C# virtual dispatch (`DoodadFunc.Use` -> `template.Use(caster, owner,
//! skillId, nextPhase)`). Funcs signal a phase change through `doodad.phase.to_next_phase` /
//! `override_phase`, which the phase machine (`Doodad.Use` / `DoFunc`) consumes.
//!
//! | module | funcs |
//! |---|---|
//! | [`items`] | `LootItem`, `LootPack`, `RecoverItem`, `RemoveItem`, `Purchase`, `BuyFish`, `ConvertFish`, `ConsumeChanger` |
//! | [`skills`] | `Use`, `FakeUse`, `ConditionalUse`, `SkillHit`, `Buff`, `Quest` |
//! | [`world`] | `Binding`, `Attachment`, `EnterInstance`, `EnterSysInstance`, `ExitIndun`, `RemoveInstance` |
//! | this file | the funcs whose C# body only logs and / or sets `ToNextPhase` |
//!
//! Cross-subsystem effects (items, money, loot packs, skills, buffs, quests, housing) go
//! through `systems::npcs::hooks`; without an installed bridge the trait defaults apply.

pub mod items;
pub mod skills;
pub mod world;

#[cfg(test)]
mod tests;

use std::sync::Arc;

use super::runtime::DoodadCtx;
use super::templates::FuncTemplate;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::systems::npcs::hooks::{EconomyBridge, QuestsBridge, SkillsBridge};
use crate::world::{PlayerRef, WorldObject};

/// `ItemTaskType` values used by the funcs / interactions.
pub mod item_task {
    pub const LOOT: u8 = 9;
    pub const DOODAD_ITEM_CHANGER: u8 = 16;
    pub const DOODAD_INTERACTION: u8 = 17;
    pub const RECOVER_DOODAD_ITEM: u8 = 25;
    pub const QUEST_SUPPLY_ITEMS: u8 = 38;
    pub const SKILL_EFFECT_CONSUMPTION: u8 = 41;
    pub const DEPOSIT_MONEY: u8 = 83;
    pub const FISHING: u8 = 90;
}

/// `ErrorMessageType` values used by the funcs / interactions / coffers.
pub mod error_message {
    pub const NO_INTERACTION_AVAILABLE: i16 = 3;
    pub const CANNOT_HARVEST_YET: i16 = 7;
    pub const INTERACTION_RECOVER_PARENT: i16 = 14;
    pub const BAG_FULL: i16 = 25;
    pub const NOT_ENOUGH_ITEM: i16 = 27;
    pub const STORE_INVALID_ITEM: i16 = 87;
    pub const BAG_INVALID_ITEM: i16 = 181;
    pub const FAILED_TO_USE_ITEM: i16 = 207;
    pub const UNKNOWN_ITEM: i16 = 213;
    pub const INTERACTION_PERMISSION_DENY: i16 = 232;
    pub const INVALID_RETURN_POS_INSTANCE: i16 = 257;
    pub const BACKPACK_OCCUPIED: i16 = 315;
    pub const COFFER_IN_USE: i16 = 329;
    pub const STORE_BACKPACK_NOGOODS: i16 = 361;
}

/// `Item.Coins` (`DoodadFuncLootItem.ItemId == 500` gives money).
pub const ITEM_COINS: u32 = 500;

/// `DoodadFunc.Use(caster, owner, skillId, nextPhase)` → the template's `Use`. Phase func
/// templates (`is_phase_func`) have no `DoodadFuncTemplate.Use` and are ignored.
pub fn use_func(
    t: &FuncTemplate,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
    next_phase: i32,
) {
    use FuncTemplate as F;
    match t {
        // --- items / loot ---
        F::LootItem(f) => items::loot_item(f, ctx, caster, skill_id, next_phase),
        F::LootPack(f) => items::loot_pack(f, ctx, caster, skill_id),
        F::RecoverItem(f) => items::recover_item(f, ctx, caster, skill_id, next_phase),
        F::RemoveItem(f) => items::remove_item(f, ctx, caster),
        F::Purchase(f) => items::purchase(f, ctx, caster),
        F::BuyFish(f) => items::buy_fish(f, ctx, caster),
        F::ConvertFish(f) => items::convert_fish_with_id(ctx, caster, f.id),
        F::ConsumeChanger(f) => items::consume_changer(f, ctx, caster),
        // --- skills / quests ---
        F::Use(f) => skills::use_(f, ctx, caster, skill_id, next_phase),
        F::FakeUse(f) => skills::fake_use(f, ctx, caster, skill_id, next_phase),
        F::ConditionalUse(f) => skills::conditional_use(f, ctx, caster),
        F::SkillHit(f) => skills::skill_hit(f, ctx, caster, skill_id),
        F::Buff(f) => skills::buff(f, ctx, caster),
        F::Quest(f) => skills::quest(f, ctx, caster, skill_id),
        // --- world ---
        F::Binding(f) => world::binding(f, ctx, caster),
        F::Attachment(f) => world::attachment(f, ctx, caster),
        F::EnterInstance(f) => world::enter_instance(f, ctx, caster),
        F::EnterSysInstance(f) => world::enter_sys_instance(f, ctx, caster),
        F::ExitIndun(f) => world::exit_indun(f, ctx, caster),
        F::RemoveInstance(f) => world::remove_instance(f, ctx, caster),
        // --- `ToNextPhase = true` ---
        F::Cutdown(_) => {
            tracing::trace!("DoodadFuncCutdown");
            set_to_next_phase(ctx, true);
        }
        F::Cutdowning(_) => {
            if is_player(caster) {
                tracing::debug!("DoodadFuncCutdowning");
            } else {
                tracing::trace!("DoodadFuncCutdowning");
            }
            set_to_next_phase(ctx, true);
        }
        F::Harvest(_) => {
            tracing::trace!("DoodadFuncHarvest");
            set_to_next_phase(ctx, true);
        }
        F::OpenFarmInfo(f) => {
            // 1.2 commonfarm/info.alb opens on OPEN_COMMON_FARM_INFO. The packet's
            // count describes the position array, not the farm's planting capacity
            // (10.x serializer 0x39C27A00 also writes exactly `count` positions).
            if let Some(player) = DoodadCtx::as_player(caster) {
                if ctx
                    .server
                    .game_data
                    .systems
                    .npcs
                    .public_farm
                    .common_farms
                    .iter()
                    .any(|farm| farm.id == f.farm_id)
                {
                    let pos = ctx.world.world_pos_rot(&ctx.obj());
                    player.send(crate::systems::npcs::public_farm::SCShowCommonFarmPacket {
                        farm_type: f.farm_id as i32,
                        count: 1,
                        x: pos.x,
                        y: pos.y,
                        z: pos.z,
                    });
                }
            }
            set_to_next_phase(ctx, true);
        }
        F::PuzzleRoll(_) => {
            tracing::trace!("DoodadFuncPuzzleRoll");
            set_to_next_phase(ctx, true);
        }
        // --- C# stubs (logging only) ---
        F::EvidenceItemLoot(_) => tracing::warn!("DoodadFuncEvidenceItemLoot"),
        F::TreeByProductsCollect(_) => tracing::trace!("DoodadFuncTreeByproductsCollect"),
        F::AreaTrigger(_)
        | F::AuctionUi(_)
        | F::BankUi(_)
        | F::Bubble(_)
        | F::Butcher(_)
        | F::Catch(_)
        | F::CerealHarvest(_)
        | F::CleanupLogicLink(_)
        | F::Climb(_)
        | F::CofferPerm(_)
        | F::CraftAct(_)
        | F::CraftCancel(_)
        | F::CraftGetItem(_)
        | F::CraftInfo(_)
        | F::CraftPack(_)
        | F::CraftStart(_)
        | F::CropHarvest(_)
        | F::CrystalCollect(_)
        | F::DairyCollect(_)
        | F::Dig(_)
        | F::DigTerrain(_)
        | F::DyeingredientCollect(_)
        | F::Feed(_)
        | F::FiberCollect(_)
        | F::FruitPick(_)
        | F::GassExtract(_)
        | F::HousingArea(_)
        | F::InsertCounter(_)
        | F::MachinePartsCollect(_)
        | F::MedicalingredientMine(_)
        | F::Mow(_)
        | F::NaviMarkPosToMap(_)
        | F::NaviNaming(_)
        | F::NaviOpenBounty(_)
        | F::NaviOpenMailbox(_)
        | F::NaviOpenPortal(_)
        | F::NaviRemove(_)
        | F::NaviTeleport(_)
        | F::OpenPaper(_)
        | F::OreMine(_)
        | F::ParentInfo(_)
        | F::PlantCollect(_)
        | F::RenewItem(_)
        | F::RockMine(_)
        | F::SeedCollect(_)
        | F::Shear(_)
        | F::SkinOff(_)
        | F::SoilCollect(_)
        | F::SpiceCollect(_)
        | F::StampMaker(_)
        | F::StoreUi(_)
        | F::UccImprint(_) => tracing::trace!("{}", t.func_type()),
        // Phase funcs (`DoodadPhaseFuncTemplate`): no `DoodadFuncTemplate.Use`.
        _ => {
            debug_assert!(t.is_phase_func());
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Shared helpers
// ---------------------------------------------------------------------------------------------

/// `owner.ToNextPhase = v`.
pub(crate) fn set_to_next_phase(ctx: &DoodadCtx<'_>, v: bool) {
    ctx.doodad.data().phase.to_next_phase = v;
}

/// `caster is Character`.
pub(crate) fn is_player(caster: Option<&WorldObject>) -> bool {
    caster.is_some_and(|c| c.as_player().is_some())
}

/// `Character.SendErrorMessage(type)`.
pub(crate) fn send_error(player: &PlayerRef, error: i16) {
    player.send(SCErrorMsgPacket::new(error, 0, true));
}

/// `Unit.SendErrorMessage(type)`: only characters have a connection.
pub(crate) fn send_error_unit(unit: &WorldObject, error: i16) {
    if let Some(p) = unit.as_player() {
        send_error(p, error);
    }
}

/// `Character.SendDebugMessage(message)`: a system chat message when `DebugInfo` is on and the
/// character's access level reaches `DebugInfoLevel`. Deviation: the account access level
/// (`GetEffectiveAccessLevel` reads the account details from the DB) is not consulted.
pub(crate) fn send_debug_message(server: &GameServer, player: &PlayerRef, message: &str) {
    if !server.config.debug_info {
        return;
    }
    let level = player.data().access_level;
    if level < 0 || (level as u32) < server.config.debug_info_level {
        return;
    }
    player.send(crate::packets::world::SCChatMessagePacket {
        chat_type: -2, // ChatType.System
        sender: None,
        message: message.to_string(),
        ability: 0,
        language_type: 0,
    });
}

struct NoBridge;
impl EconomyBridge for NoBridge {}
impl SkillsBridge for NoBridge {}
impl QuestsBridge for NoBridge {}

/// The installed `EconomyBridge` (or the trait defaults).
pub(crate) fn economy(server: &GameServer) -> Arc<dyn EconomyBridge> {
    server
        .systems
        .npcs
        .hooks
        .economy
        .get()
        .unwrap_or_else(|| Arc::new(NoBridge))
}

/// The installed `SkillsBridge` (or the trait defaults).
pub(crate) fn skills_hook(server: &GameServer) -> Arc<dyn SkillsBridge> {
    server
        .systems
        .npcs
        .hooks
        .skills
        .get()
        .unwrap_or_else(|| Arc::new(NoBridge))
}

/// The installed `QuestsBridge` (or the trait defaults).
pub(crate) fn quests_hook(server: &GameServer) -> Arc<dyn QuestsBridge> {
    server
        .systems
        .npcs
        .hooks
        .quests
        .get()
        .unwrap_or_else(|| Arc::new(NoBridge))
}

/// `Random.Shared.Next(min, max)` (`max` exclusive, `min` when equal). `None` where the C#
/// throws (`min > max`).
pub(crate) fn random_next(min: i32, max: i32) -> Option<i32> {
    use rand::Rng;
    match min.cmp(&max) {
        std::cmp::Ordering::Greater => None,
        std::cmp::Ordering::Equal => Some(min),
        std::cmp::Ordering::Less => Some(rand::thread_rng().gen_range(min..max)),
    }
}
