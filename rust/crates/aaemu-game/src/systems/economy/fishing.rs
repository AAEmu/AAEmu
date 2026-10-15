//! Fish-stand inventory operations. The 1.2 allowed-item rows and `items.refund` are
//! authoritative here; a retail weight-dependent sale formula has not been established.

use crate::data::items::TemplateKind;
use crate::models::container::{equip_slot, slot_type};
use crate::models::item::{Item, ItemClass};
use crate::models::item_task::{task_type, ItemFx, ItemTask};
use crate::server::GameServer;
use crate::systems::npcs::doodad::templates::FuncTemplate;
use crate::world::{PlayerRef, WorldObject};

use super::inventory::{lock_pair, Inv, ItemTxn};
use super::loot::{loot_convert_fish_from_pack, SharedRandom};

/// Conservative fishing safety adaptation: an active FISH status that requests
/// creator immunity permits character damage only from its owner, using a skill
/// in its configured exception tag. The 1.2 FISH description allows fishing only.
/// The creator is the NPC's character owner, not its self-cast buff caster.
/// This does not infer general creator/skill-tag exception rules for other buffs.
pub(crate) fn blocks_character_fish_damage(
    server: &GameServer,
    target: &WorldObject,
    caster: &WorldObject,
    source_skill_id: Option<u32>,
) -> bool {
    let (Some(npc), Some(player)) = (target.as_npc(), caster.as_player()) else {
        return false;
    };
    let (owner_id, buff_ids) = {
        let npc = npc.data();
        (
            npc.unit.owner_id,
            npc.unit
                .buffs
                .effects
                .iter()
                .map(|b| b.template.id)
                .collect::<Vec<_>>(),
        )
    };
    if owner_id == 0 {
        return false;
    }
    let skills = &server.game_data.systems.skills;
    let fish_buffs = skills
        .buffs_by_tag(crate::systems::ai::npc::tags::FISH)
        .unwrap_or(&[]);
    buff_ids.into_iter().any(|id| {
        fish_buffs.contains(&id)
            && skills.buff(id).is_some_and(|b| {
                b.immune_except_creator
                    && (owner_id != player.character_id
                        || (b.immune_except_skill_tag_id != 0
                            && !source_skill_id.is_some_and(|skill_id| {
                                skills
                                    .skill_tags(skill_id)
                                    .contains(&b.immune_except_skill_tag_id)
                            })))
            })
    })
}

fn is_equipped_fish(
    server: &GameServer,
    item: &Item,
    owner_id: u32,
    item_id: u64,
    template_id: u32,
) -> bool {
    item.id == item_id
        && item.template_id == template_id
        && item.owner_id == u64::from(owner_id)
        && item.slot_type == slot_type::EQUIPMENT
        && item.slot == equip_slot::BACKPACK
        && item.count == 1
        && item.class == ItemClass::Backpack
        && server
            .game_data
            .items
            .template(template_id)
            .is_some_and(|t| matches!(t.kind, TemplateKind::Backpack { .. }))
}

pub(crate) fn sell_equipped_fish(
    server: &GameServer,
    player: &PlayerRef,
    item_id: u64,
    template_id: u32,
    buy_func_id: u32,
) -> bool {
    let allowed = server
        .game_data
        .systems
        .npcs
        .doodad
        .phase_func_templates
        .get("DoodadFuncBuyFishItem")
        .is_some_and(|rows| {
            rows.values().any(|row| {
                matches!(row, FuncTemplate::BuyFishItem(f)
                    if f.doodad_func_buy_fish_id == buy_func_id && f.item_id == template_id)
            })
        });
    if !allowed {
        return false;
    }
    let Some(refund) = server
        .game_data
        .items
        .template(template_id)
        .map(|t| t.refund)
        .filter(|refund| *refund >= 0)
    else {
        return false;
    };
    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let equipment_ref = inv.equipment();
    // Character before container: container locks are leaves.
    let mut character = player.data();
    let Some(balance) = character.money.checked_add(i64::from(refund)) else {
        return false;
    };
    let mut equipment = equipment_ref.lock().expect("container poisoned");
    let Some(item) = equipment.get_item_by_slot(equip_slot::BACKPACK) else {
        return false;
    };
    if !is_equipped_fish(server, item, inv.character_id, item_id, template_id)
        || !txn.env.rules.can_destroy(item)
    {
        return false;
    }
    if equipment
        .remove_item_task(
            task_type::SKILL_EFFECT_CONSUMPTION,
            item_id,
            true,
            &mut txn.env,
        )
        .is_none()
    {
        return false;
    }
    character.money = balance;
    txn.env.fx.tasks(
        inv.character_id,
        task_type::DEPOSIT_MONEY,
        vec![ItemTask::MoneyChange(refund)],
    );
    true
}

/// Converts the equipped fish into its trophy and, once every container lock is released,
/// offers the catch to the ranking boards (`systems::social::ranking_manager`).
pub(crate) fn convert_equipped_fish(
    server: &GameServer,
    player: &PlayerRef,
    item_id: u64,
    template_id: u32,
    convert_func_id: u32,
    task: u8,
) -> bool {
    let Some((length, weight)) =
        convert_equipped_fish_inner(server, player, item_id, template_id, convert_func_id, task)
    else {
        return false;
    };
    record_catch(server, player, length, weight);
    true
}

/// Offers one landed trophy fish (`BigFish` length / weight) to the fishing ranking boards. The
/// boards are bound to a zone group, so the catch counts where the player stands.
pub(crate) fn record_catch(server: &GameServer, player: &PlayerRef, length: f32, weight: f32) {
    let zone_key = player.data().unit.transform.zone_id;
    let zone_group = super::craft::zone_group_id(server, zone_key);
    server.systems.social.ranking.record(
        server,
        player,
        crate::systems::social::ranking_runtime::RankEvent::Catch { length, weight },
        zone_group,
        chrono::Utc::now(),
    );
}

fn convert_equipped_fish_inner(
    server: &GameServer,
    player: &PlayerRef,
    item_id: u64,
    template_id: u32,
    convert_func_id: u32,
    task: u8,
) -> Option<(f32, f32)> {
    let convert = server
        .game_data
        .items
        .loot_packs_by_fish(template_id)
        .iter()
        .find(|row| row.doodad_func_convert_fish_id == convert_func_id)?;
    let econ = &server.game_data.systems.economy;
    let details = econ.items.fish_details.get(&template_id)?;
    if details.min_length < 0
        || details.max_length <= 0
        || details.max_length < details.min_length
        || details.min_weight < 0
        || details.max_weight < details.min_weight
    {
        return None;
    }
    let mut txn = ItemTxn::begin(server);
    let inv = Inv::of(player);
    let equipment_ref = inv.equipment();
    let bag_ref = inv.bag();
    let (mut equipment, bag) = lock_pair(&equipment_ref, &bag_ref);
    let mut bag = bag?;
    let source = equipment.get_item_by_slot(equip_slot::BACKPACK)?;
    if !is_equipped_fish(server, source, inv.character_id, item_id, template_id)
        || !txn.env.rules.can_destroy(source)
    {
        return None;
    }
    let mut rng = SharedRandom;
    let trophies =
        loot_convert_fish_from_pack(server, template_id, convert.loot_pack_id, &mut rng)?;
    // This lottery allocates only its single selected row (it breaks after selection).
    let trophy = trophies.into_iter().next()?;
    let Some(fish) = econ.items.create_big_fish(
        &server.game_data.items,
        &trophy,
        server.config.id,
        &mut rng,
        txn.env.now,
    ) else {
        txn.env.fx.push(ItemFx::ReleaseId(trophy.id));
        return None;
    };
    let caught = (fish.details.fish_length, fish.details.fish_weight);
    let data = txn.data();
    if bag.place(&fish, -1, data).is_none() {
        txn.env.fx.push(ItemFx::ReleaseId(fish.id));
        return None;
    }
    let before_equipment = equipment.clone();
    let fx_before = txn.env.fx.len();
    if equipment
        .remove_item_task(task, item_id, true, &mut txn.env)
        .is_none()
    {
        txn.env.fx.push(ItemFx::ReleaseId(fish.id));
        return None;
    }
    // Both containers stay locked throughout placement and consumption. Restore the
    // source and discard its queued effects if delivery nonetheless refuses the item.
    if let Err(fish) = bag.add_item(task, fish, -1, data, &mut txn.env.fx) {
        *equipment = before_equipment;
        txn.env.fx.0.truncate(fx_before);
        txn.env.fx.push(ItemFx::ReleaseId(fish.id));
        return None;
    }
    Some(caught)
}

#[cfg(test)]
mod tests;
