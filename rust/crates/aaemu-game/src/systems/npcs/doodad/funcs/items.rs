//! Item / loot funcs: `DoodadFuncLootItem`, `LootPack`, `RecoverItem`, `RemoveItem`,
//! `Purchase`, `BuyFish`, `ConvertFish`, `ConsumeChanger`.

use super::{
    economy, error_message as err, is_player, item_task, random_next, send_error,
    set_to_next_phase, ITEM_COINS,
};
use crate::models::container::{equip_slot, slot_type};
use crate::models::doodad::doodad_owner_type;
use crate::systems::npcs::doodad::runtime::DoodadCtx;
use crate::systems::npcs::doodad::templates::{
    DoodadFuncBuyFish, DoodadFuncConsumeChanger, DoodadFuncLootItem, DoodadFuncLootPack,
    DoodadFuncPurchase, DoodadFuncRecoverItem, DoodadFuncRemoveItem,
};
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::world::{PlayerRef, WorldObject};

/// `SCUnitEquipmentsChangedPacket`: one definition per opcode (`crate::systems::economy::item_packets::SCUnitEquipmentsChangedPacket`).
pub use crate::systems::economy::item_packets::SCUnitEquipmentsChangedPacket;

/// `Inventory.GetEquippedBySlot(Backpack)`: (item id, template id).
fn equipped_backpack(player: &PlayerRef) -> Option<(u64, u32)> {
    let equipment = player.data().inventory.equipment().clone();
    let c = equipment.lock().expect("container poisoned");
    c.get_item_by_slot(equip_slot::BACKPACK)
        .map(|i| (i.id, i.template_id))
}

/// `(Character)caster`: `Err(())` where the C# cast throws (a non-character caster), `Ok(None)`
/// for a null caster.
fn cast_character(caster: Option<&WorldObject>) -> Result<Option<&PlayerRef>, ()> {
    match caster {
        None => Ok(None),
        Some(c) => c.as_player().map(Some).ok_or(()),
    }
}

/// `DoodadFuncLootItem.Use`.
pub fn loot_item(
    f: &DoodadFuncLootItem,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
    next_phase: i32,
) {
    let msg = format!(
        "DoodadFuncLootItem: skillId {skill_id}, nextPhase {next_phase},  ItemId {}, CountMin {}, CountMax {},  Percent {}, RemainTime {}, GroupId {}",
        f.item_id, f.count_min, f.count_max, f.percent, f.remain_time, f.group_id
    );
    if is_player(caster) {
        tracing::debug!("{msg}");
    } else {
        tracing::trace!("{msg}");
    }
    // `(Character)caster` throws for other units; `character == null` returns.
    let Ok(Some(player)) = cast_character(caster) else {
        return;
    };
    let chance = random_next(0, 10000).unwrap_or(0);
    if chance > f.percent {
        return;
    }
    let Some(count) = random_next(f.count_min, f.count_max) else {
        tracing::error!(
            "DoodadFuncLootItem {}: CountMin {} > CountMax {}",
            f.id,
            f.count_min,
            f.count_max
        );
        return;
    };
    let eco = economy(ctx.server);
    let res = if f.item_id == ITEM_COINS {
        count >= 0
            && eco.change_money(
                ctx.server,
                player,
                i64::from(count),
                item_task::DEPOSIT_MONEY,
            )
    } else {
        // The economy bridge handles trade packs in one item transaction, including slot
        // validation and glider replacement. Failure must leave this doodad lootable.
        eco.acquire_item(
            ctx.server,
            player,
            item_task::RECOVER_DOODAD_ITEM,
            f.item_id,
            count,
            -1,
        )
    };
    if !res {
        send_error(player, err::BAG_INVALID_ITEM);
    } else if f.item_id != ITEM_COINS {
        // GET_LOOTITEM: the items granted by this loot roll.
        crate::systems::report_achievement(
            ctx.server,
            player,
            AchievementInput::Increment {
                kind_id: char_record_kind::GET_LOOTITEM,
                amount: i64::from(count),
                matcher: RecordMatch::Value1(f.item_id),
            },
        );
    }
    // Move to next phase only when loot was actually granted.
    set_to_next_phase(ctx, res);
}

/// `DoodadFuncLootPack.Use`.
pub fn loot_pack(
    f: &DoodadFuncLootPack,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
) {
    let Some(player) = DoodadCtx::as_player(caster) else {
        return;
    };
    match economy(ctx.server).give_doodad_loot_pack(
        ctx.server,
        player,
        f.loot_pack_id,
        skill_id,
        item_task::DOODAD_INTERACTION,
    ) {
        Some(true) => set_to_next_phase(ctx, true),
        // TODO (C#): make sure the doodad is marked as loot-able when not enough inventory space
        Some(false) => send_error(player, err::BAG_FULL),
        None => tracing::error!(
            "DoodadFuncLootPack {}: loot pack {} not found",
            f.id,
            f.loot_pack_id
        ),
    }
}

/// `DoodadFuncRecoverItem.Use`.
pub fn recover_item(
    f: &DoodadFuncRecoverItem,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
    next_phase: i32,
) {
    // `caster.Name` / `(Character)caster` throw for a null / non-character caster.
    let Some(player) = DoodadCtx::as_player(caster) else {
        return;
    };
    let _operation = ctx
        .server
        .systems
        .housing
        .operation_lock
        .lock()
        .unwrap_or_else(|e| e.into_inner());
    if ctx.doodad.data().phase.deleted {
        return;
    }
    let (item_id, item_template_id, owner_db_id, owner_type, template_id) = {
        let d = ctx.doodad.data();
        (
            d.item_id,
            d.item_template_id,
            d.owner_db_id,
            d.owner_type,
            d.template_id,
        )
    };
    // Recovery can also be reached through an InteractionEffect, outside the phase
    // machine's normal result reset. A failed recovery must never reuse an old success.
    set_to_next_phase(ctx, false);
    if owner_type == doodad_owner_type::HOUSING && owner_db_id > 0 {
        let allowed =
            crate::systems::housing::house_by_id(ctx.server, owner_db_id).is_some_and(|house| {
                let (owner, allow_recover) = {
                    let h = house.data();
                    (h.unit.owner_id, h.allow_recover)
                };
                player.character_id == owner
                    || (allow_recover
                        && crate::systems::housing::allowed_to_interact(ctx.server, &house, player))
            });
        if !allowed {
            send_error(player, err::INTERACTION_PERMISSION_DENY);
            return;
        }
    }
    tracing::debug!(
        "DoodadFuncRecoverItem({}) - Caster:{} - DoodadOwner Template:{} - SkillId:{} - Nextphase:{}",
        f.id,
        player.data().unit.name,
        template_id,
        skill_id,
        next_phase
    );
    let server = ctx.server;
    let eco = economy(server);
    let mut added_item = false;
    let item = server.items.item_by_id(item_id);
    if item_id > 0 {
        if let Some(item) = &item {
            // Recoverable doodads should reference an item in a System container; otherwise it
            // was already picked up by somebody else.
            if item.slot_type != slot_type::SYSTEM {
                set_to_next_phase(ctx, false);
                send_error(player, err::INTERACTION_RECOVER_PARENT);
                return;
            }
            if eco.is_auto_equip_trade_pack(server, item.template_id) {
                if eco.takeoff_backpack(server, player, item_task::RECOVER_DOODAD_ITEM, true)
                    && eco.move_item_to_backpack_slot(
                        server,
                        player,
                        item_task::RECOVER_DOODAD_ITEM,
                        item.id,
                    )
                {
                    added_item = true;
                }
            } else if eco.move_item_to_bag(server, player, item_task::RECOVER_DOODAD_ITEM, item.id)
            {
                added_item = true;
            }
        }
    } else if item_template_id > 0 {
        added_item = eco.acquire_item(
            server,
            player,
            item_task::RECOVER_DOODAD_ITEM,
            item_template_id,
            1,
            -1,
        );
    } else {
        // No itemId was provided with the doodad.
        tracing::warn!(
            "DoodadFuncRecoverItem: Doodad {} has no item information attached to it",
            ctx.doodad.obj_id
        );
        added_item = true; // fake it to get rid of the error state
    }
    if added_item && item.is_some() {
        if let Some(moved) = server.items.item_by_id(item_id) {
            if moved.slot_type == slot_type::EQUIPMENT {
                let packet = SCUnitEquipmentsChangedPacket {
                    obj_id: player.obj_id,
                    items: vec![(moved.slot as u8, Some(moved))],
                };
                crate::world::broadcast(&ctx.world, player, &packet, false);
            }
        }
    }
    let mut d = ctx.doodad.data();
    if added_item {
        // remove the old reference
        d.item_id = 0;
        d.item_template_id = 0;
    }
    d.phase.to_next_phase = added_item;
}

/// `DoodadFuncRemoveItem.Use`.
pub fn remove_item(f: &DoodadFuncRemoveItem, ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>) {
    tracing::trace!(
        "DoodadFuncRemoveItem: ItemId {}, Count {}",
        f.item_id,
        f.count
    );
    let Ok(player) = cast_character(caster) else {
        return; // InvalidCastException
    };
    // `character?.Inventory.Bag.ConsumeItem(...)`: null > 0 is false.
    let balance = player.map_or(0, |p| {
        economy(ctx.server).bag_consume_item(
            ctx.server,
            p,
            item_task::DOODAD_ITEM_CHANGER,
            f.item_id,
            f.count,
        )
    });
    set_to_next_phase(ctx, balance > 0);
}

/// `DoodadFuncPurchase.Use`.
pub fn purchase(f: &DoodadFuncPurchase, ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>) {
    let Some(player) = DoodadCtx::as_player(caster) else {
        return;
    };
    let server = ctx.server;
    let eco = economy(server);
    if eco.bag_space_left_for_item(server, player, f.item_id) < f.count {
        send_error(player, err::BAG_FULL);
        return;
    }
    if eco.bag_consume_item(
        server,
        player,
        item_task::DOODAD_INTERACTION,
        f.coin_item_id,
        f.coin_count,
    ) <= 0
    {
        send_error(player, err::NOT_ENOUGH_ITEM);
        return;
    }
    let name = player.data().unit.name.clone();
    if eco.is_auto_equip_trade_pack(server, f.item_id) {
        if !eco.try_equip_new_backpack(
            server,
            player,
            item_task::QUEST_SUPPLY_ITEMS,
            f.item_id,
            f.count,
        ) {
            tracing::error!(
                "DoodadFuncPurchase: Failed to auto-equip backpack item {} for player {}",
                f.item_id,
                name
            );
            send_error(player, err::BACKPACK_OCCUPIED);
        }
    } else if !eco.acquire_item(
        server,
        player,
        item_task::DOODAD_INTERACTION,
        f.item_id,
        f.count,
        -1,
    ) {
        tracing::error!(
            "DoodadFuncPurchase: Failed to create item {} for player {}",
            f.item_id,
            name
        );
    }
}

/// `DoodadFuncBuyFish.Use`.
pub fn buy_fish(f: &DoodadFuncBuyFish, ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>) {
    tracing::trace!("DoodadFuncBuyFish");
    set_to_next_phase(ctx, false);
    let Some(player) = DoodadCtx::as_player(caster) else {
        return;
    };
    let Some((backpack_id, backpack_template)) = equipped_backpack(player) else {
        send_error(player, err::STORE_BACKPACK_NOGOODS);
        return;
    };
    let eco = economy(ctx.server);
    if eco.try_sell_equipped_fish(ctx.server, player, backpack_id, backpack_template, f.id) {
        // The sale animation is entered only after payment and consumption succeeded.
        ctx.doodad.data().item_template_id = backpack_template;
        set_to_next_phase(ctx, true);
    }
}

/// `DoodadFuncConvertFish.Use`.
pub fn convert_fish(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>) {
    set_to_next_phase(ctx, false);
    let group = ctx.doodad.data().func_group_id;
    let func_id = ctx
        .data()
        .funcs_for_group(group)
        .iter()
        .find(|f| f.func_type == "DoodadFuncConvertFish")
        .map(|f| f.func_id);
    if let Some(func_id) = func_id {
        convert_fish_with_id(ctx, caster, func_id);
    }
}

pub fn convert_fish_with_id(
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    convert_func_id: u32,
) {
    tracing::trace!("DoodadFuncConvertFish");
    set_to_next_phase(ctx, false);
    let Some(player) = DoodadCtx::as_player(caster) else {
        return;
    };
    let Some((backpack_id, backpack_template)) = equipped_backpack(player) else {
        send_error(player, err::STORE_BACKPACK_NOGOODS);
        return;
    };
    let eco = economy(ctx.server);
    eco.try_convert_equipped_fish(
        ctx.server,
        player,
        backpack_id,
        backpack_template,
        convert_func_id,
        item_task::FISHING,
    );
    // Conversion's 1.2 successor is -1: remain at the stand instead of deleting it.
}

/// `DoodadFuncConsumeChanger.Use`: stores a trade pack into a trade-pack storage.
pub fn consume_changer(
    f: &DoodadFuncConsumeChanger,
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
) {
    tracing::trace!("DoodadFuncConsumeChanger");
    let Some(player) = DoodadCtx::as_player(caster) else {
        return;
    };
    let source = {
        let equipment = player.data().inventory.equipment().clone();
        let c = equipment.lock().expect("container poisoned");
        c.get_item_by_slot(f.slot_id as i32)
            .map(|i| (i.id, i.template_id))
    };
    let Some((source_id, source_template)) = source else {
        send_error(player, err::UNKNOWN_ITEM);
        return;
    };
    // doodad_func_consume_changer_items
    if !ctx
        .data()
        .consume_changer_items(f.id)
        .contains(&source_template)
    {
        send_error(player, err::STORE_INVALID_ITEM);
        return;
    }
    let (template_id, owner_id) = {
        let d = ctx.doodad.data();
        (d.template_id, d.owner_id)
    };
    // TODO (C#): if this item count would be different from 1, this would cause issues
    if f.count > 1 {
        tracing::warn!(
            "DoodadFuncConsumeChanger, expected source count is higher than one ({}) for slot {} on doodad type {} ({}) from player {}",
            f.count,
            f.slot_id,
            template_id,
            owner_id,
            player.data().unit.name
        );
    }
    // Move the actual item to the owner's SystemContainer
    if !economy(ctx.server).move_item_to_system_container(
        ctx.server,
        player,
        item_task::DOODAD_ITEM_CHANGER,
        source_id,
    ) {
        set_to_next_phase(ctx, false);
        send_error(player, err::STORE_INVALID_ITEM);
        return;
    }
    {
        let mut d = ctx.doodad.data();
        d.item_id = source_id;
        d.item_template_id = source_template;
    }
    set_to_next_phase(ctx, true);
}
