//! Repair and durability: `Character.DoRepair`, `CSRepairSingleEquipmentPacket`,
//! `CSRepairAllEquipmentsPacket`, `Character.ApplyDurabilityLossToEquipment` (+
//! `DurabilityLossTargets`), the durability part of `Character.ReduceCurrentHp` and of
//! `Character.DoDie` (for the skills / combat subsystem).

use aaemu_commons::PacketStream;

use crate::data::items::equipment_item_slot_type;
use crate::error::Result;
use crate::models::container::{equip_slot, slot_type, ContainerRef};
use crate::models::item::{Item, ItemClass};
use crate::models::item_task::{task_type, ItemTask};
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::server::GameServer;
use crate::systems::social::achievement::char_record_kind;
use crate::systems::social::achievement_hooks::{AchievementInput, RecordMatch};
use crate::world::{PlayerRef, WorldObject};

use super::error_message;
use super::inventory::{self, Inv, ItemTxn};
use super::item_handlers::{active, sync_client_packet};
use super::item_packets::SCItemTaskSuccessPacket;
use super::loot::{LootRng, SharedRandom};

/// Where an item to repair was found (`Inventory.GetItem(slotType, slot)` /
/// `Equipment.Items`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct RepairTarget {
    pub item_id: u64,
}

/// `Character.DoRepair(items)` (`None` entries are the C# null items).
pub fn do_repair(server: &GameServer, player: &PlayerRef, items: &[Option<RepairTarget>]) {
    let inv = Inv::of(player);
    let repaired = repair_in_containers(server, player, items, &[inv.bag(), inv.equipment()]);
    report_repaired(server, player, repaired.len());
    if let Some(hooks) = server.systems.economy.hooks.gear.get() {
        for (item, was_destroyed) in repaired {
            if was_destroyed && item.slot_type == slot_type::EQUIPMENT {
                hooks.update_gear_bonuses(server, &WorldObject::from(player), Some(&item), None);
            }
        }
    }
}

/// ITEM_FIX achievement credit: `count` items were actually repaired.
fn report_repaired(server: &GameServer, player: &PlayerRef, count: usize) {
    crate::systems::report_achievement(
        server,
        player,
        AchievementInput::Increment {
            kind_id: char_record_kind::ITEM_FIX,
            amount: count as i64,
            matcher: RecordMatch::Any,
        },
    );
}

fn repair_in_containers(
    server: &GameServer,
    player: &PlayerRef,
    items: &[Option<RepairTarget>],
    containers: &[ContainerRef],
) -> Vec<(Item, bool)> {
    let data = &server.game_data.items;
    let mut txn = ItemTxn::begin(server);
    let mut tasks = Vec::new();
    let mut repair_cost = 0i32;
    let mut repaired = Vec::new();
    for target in items.iter().flatten() {
        let id = target.item_id;
        let Some(container) = containers
            .iter()
            .find(|c| {
                c.lock()
                    .expect("container poisoned")
                    .get_item_by_id(id)
                    .is_some()
            })
            .cloned()
        else {
            tracing::warn!(
                "Attempting to repair an item that isn't in your inventory or equipment, Item: {id}"
            );
            continue;
        };
        let item = container
            .lock()
            .expect("container poisoned")
            .get_item_by_id(id)
            .cloned()
            .expect("checked above");
        let is_equip_template = data
            .template(item.template_id)
            .is_some_and(|t| t.is_equip());
        if !(item.is_equip() && is_equip_template) {
            tracing::warn!("Attempting to repair a non-equipment item, Item: {id}");
            continue;
        }
        let max = item.max_durability(data);
        if item.details.durability >= max {
            tracing::warn!("Attempting to repair an item that has max durability, Item: {id}");
            continue;
        }
        // CurrentInteractionObject is not Npc npc -> continue
        let interaction = player.data().unit.current_interaction_object;
        let Some(world) = server.world.world_of(&WorldObject::from(player)) else {
            continue;
        };
        let Some(npc) = world.npc(interaction) else {
            continue;
        };
        let blacksmith = server
            .systems
            .economy
            .hooks
            .npcs
            .get()
            .and_then(|h| h.shop_info(server, &npc))
            .is_some_and(|i| i.blacksmith);
        if !blacksmith {
            tracing::warn!(
                "Attempting to repair an item while not at a blacksmith, Item: {id}, NPC: {}",
                npc.obj_id
            );
            continue;
        }
        let me = world.world_pos_rot(&WorldObject::from(player));
        let them = world.world_pos_rot(&WorldObject::from(&npc));
        if me.distance_to(&them, false) > 5.0 {
            inventory::send_error_message_to(&mut txn.out(), player, error_message::TOO_FAR_AWAY);
            continue;
        }
        let Some(cost) = item.repair_cost(data) else {
            // C#: NullReferenceException (missing grade template)
            tracing::error!("RepairCost failed for item {id}");
            continue;
        };
        let money = player.data().money;
        let Some(total_cost) = repair_cost.checked_add(cost).filter(|_| cost >= 0) else {
            continue;
        };
        if money < i64::from(total_cost) {
            tracing::warn!(
                "Not enough money to repair, Item: {id}, Money: {money}, RepairCost: {cost}"
            );
            continue;
        }
        let mut c = container.lock().expect("container poisoned");
        if let Some(i) = c.get_item_by_id_mut(id) {
            let was_destroyed = i.details.durability == 0;
            i.details.durability = max;
            i.dirty = true;
            tasks.push(ItemTask::update(i));
            repaired.push((i.clone(), was_destroyed));
        }
        drop(c);
        repair_cost = total_cost;
    }
    if repair_cost > 0 {
        // ChangeMoney(SlotType.Inventory, -repairCost) (default task DepositMoney)
        inventory::change_money_at(
            &mut txn.out(),
            player,
            slot_type::INVENTORY,
            -repair_cost,
            task_type::DEPOSIT_MONEY,
        );
    }
    txn.out().send(
        player,
        SCItemTaskSuccessPacket {
            action: task_type::REPAIR,
            tasks,
            force_remove: Vec::new(),
        },
    );
    repaired
}

/// `DurabilityLossTargets`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DurabilityLossTargets {
    /// Applies to all equipped items
    All,
    /// Only armor pieces
    AllArmor,
    /// Only weapons and shields
    AllWeapons,
    /// Only the main and offhand equipped slots
    AllMainWeapons,
    /// Only the equipped main weapon
    PrimaryWeapon,
    /// Only the equipped offhand weapon or shield
    SecondaryWeapon,
    /// Only the weapon in the ranged slot
    RangedWeapon,
    /// Only apply to shields
    Shield,
    /// Only apply to feet
    Feet,
}

/// `Character.ApplyDurabilityLossToEquipment(durabilityLoss, targets, durabilityRate)`:
/// `durability_rate` < 0 affects every target item, otherwise each item loses durability with
/// `durability_rate`% chance. Returns the ids of the destroyed (durability 0) items.
pub fn apply_durability_loss_to_equipment(
    server: &GameServer,
    player: &PlayerRef,
    durability_loss: u8,
    targets: DurabilityLossTargets,
    durability_rate: f32,
    rng: &mut dyn LootRng,
) -> Vec<u64> {
    let data = &server.game_data.items;
    let mut destroyed: Vec<Item> = Vec::new();
    {
        let mut txn = ItemTxn::begin(server);
        let mut update_tasks = Vec::new();
        let equipment = Inv::of(player).equipment();
        let mut c = equipment.lock().expect("container poisoned");
        for item in c.items.iter_mut() {
            if !item.is_equip() {
                continue;
            }
            // Ignore if no durability
            if item.max_durability(data) == 0 {
                continue;
            }
            // Ignore already destroyed items
            if !item.is_not_destroyed(data) {
                continue;
            }
            let keep = match targets {
                DurabilityLossTargets::All => true,
                DurabilityLossTargets::AllArmor => item.class == ItemClass::Armor,
                DurabilityLossTargets::AllWeapons => item.class == ItemClass::Weapon,
                DurabilityLossTargets::AllMainWeapons => {
                    item.class == ItemClass::Weapon
                        && (item.slot == equip_slot::MAINHAND || item.slot == equip_slot::OFFHAND)
                }
                DurabilityLossTargets::PrimaryWeapon => item.slot == equip_slot::MAINHAND,
                DurabilityLossTargets::SecondaryWeapon => item.slot == equip_slot::OFFHAND,
                DurabilityLossTargets::RangedWeapon => item.slot == equip_slot::RANGED,
                DurabilityLossTargets::Shield => data
                    .template(item.template_id)
                    .and_then(|t| data.holdable_of(t))
                    .is_some_and(|h| h.slot_type_id == equipment_item_slot_type::SHIELD),
                DurabilityLossTargets::Feet => item.slot == equip_slot::FEET,
            };
            if !keep {
                continue;
            }
            // If durability rate is set, apply randomness
            if durability_rate >= 0.0 && rng.next_f32() * 100f32 > durability_rate {
                continue;
            }
            if item.details.durability <= durability_loss {
                item.details.durability = 0;
                destroyed.push(item.clone());
                tracing::debug!(
                    "Equipment Item: {} ({}) was destroyed",
                    item.id,
                    item.template_id
                );
            } else {
                item.details.durability -= durability_loss;
                tracing::debug!(
                    "Equipment Item: {} ({}) lost {durability_loss} durability",
                    item.id,
                    item.template_id
                );
            }
            item.dirty = true;
            update_tasks.push(ItemTask::update(item));
        }
        drop(c);
        if !update_tasks.is_empty() {
            txn.out().send(
                player,
                SCItemTaskSuccessPacket {
                    action: task_type::DURABILITY_LOSS,
                    tasks: update_tasks,
                    force_remove: Vec::new(),
                },
            );
        }
    }
    // Reparse equipment buffs if needed because of getting destroyed items
    if let Some(h) = server.systems.economy.hooks.gear.get() {
        for item in &destroyed {
            h.update_gear_bonuses(server, &WorldObject::from(player), None, Some(item));
        }
    }
    destroyed.iter().map(|i| i.id).collect()
}

/// `KillReason` values the durability code distinguishes.
pub mod kill_reason {
    pub const UNKNOWN: u8 = 0;
    pub const DAMAGE: u8 = 1;
    pub const PVP_SIEGE: u8 = 2;
    pub const PVP_BATTLE_FIELD: u8 = 3;
    pub const PVP_HONOR_WAR: u8 = 4;
    pub const PVP_ENEMY: u8 = 5;
    pub const PVP_ALLY: u8 = 6;
}

/// The armor wear of `Character.ReduceCurrentHp(attacker, value, killReason)` (call after the
/// HP change): PvP / PvE rate by kill reason, scaled by `DurabilityDecrementChance`, 1 point on
/// all armor.
pub fn on_damage_taken(
    server: &GameServer,
    player: &PlayerRef,
    kill_reason: u8,
    attacker_is_character: bool,
    rng: &mut dyn LootRng,
) {
    let w = &server.config.world;
    let mut rate = match kill_reason {
        kill_reason::PVP_SIEGE
        | kill_reason::PVP_BATTLE_FIELD
        | kill_reason::PVP_HONOR_WAR
        | kill_reason::PVP_ENEMY
        | kill_reason::PVP_ALLY => w.pvp_durability_loss_rate,
        kill_reason::UNKNOWN | kill_reason::DAMAGE => {
            if attacker_is_character {
                w.pvp_durability_loss_rate
            } else {
                w.pve_durability_loss_rate
            }
        }
        _ => 0.0,
    };
    if rate > 0.0 {
        // DurabilityDecrementChance in the item_config is very likely in %
        rate *= server.game_data.items.config.durability_decrement_chance;
        apply_durability_loss_to_equipment(
            server,
            player,
            1,
            DurabilityLossTargets::AllArmor,
            rate,
            rng,
        );
    }
}

/// `Character.LastDurabilityLoss` at death: `GetDeathDurabilityLossRatio()` when the level is at
/// least `MinimumExpLossLevel`, else 0.
pub fn death_durability_loss(server: &GameServer, level_qualifies: bool) -> u8 {
    if level_qualifies {
        server.game_data.items.config.death_durability_loss_ratio
    } else {
        0
    }
}

/// The durability part of `Character.DoDie` (after `base.DoDie`): every equipped item loses
/// `LastDurabilityLoss` points.
pub fn on_death(server: &GameServer, player: &PlayerRef, last_durability_loss: u8) {
    if last_durability_loss > 0 {
        apply_durability_loss_to_equipment(
            server,
            player,
            last_durability_loss,
            DurabilityLossTargets::All,
            -1.0,
            &mut SharedRandom,
        );
    }
}

/// `CSRepairSingleEquipmentPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRepairSingleEquipmentPacket {
    pub slot_type: u8,
    pub slot: u8,
    pub auto_use_aa_point: bool,
}

impl CSRepairSingleEquipmentPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            slot_type: s.read_u8()?,
            slot: s.read_u8()?,
            auto_use_aa_point: s.read_bool()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(player) = active(conn) else { return };
        tracing::debug!(
            "RepairSingleEquipment, SlotType: {}, Slot: {}, AutoUseAAPoint: {}",
            self.slot_type,
            self.slot,
            self.auto_use_aa_point
        );
        let item = Inv::of(&player).item_at(self.slot_type, self.slot);
        do_repair(
            server,
            &player,
            &[item.map(|i| RepairTarget { item_id: i.id })],
        );
    }
}
sync_client_packet!(CSRepairSingleEquipmentPacket);

/// `CSRepairAllEquipmentsPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRepairAllEquipmentsPacket {
    pub auto_use_aa_point: bool,
}

impl CSRepairAllEquipmentsPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            auto_use_aa_point: s.read_bool()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(player) = active(conn) else { return };
        tracing::debug!(
            "RepairAllEquipments, AutoUseAAPoint: {}",
            self.auto_use_aa_point
        );
        let items: Vec<Option<RepairTarget>> = Inv::of(&player)
            .equipment()
            .lock()
            .expect("container poisoned")
            .items
            .iter()
            .map(|i| Some(RepairTarget { item_id: i.id }))
            .collect();
        do_repair(server, &player, &items);
    }
}
sync_client_packet!(CSRepairAllEquipmentsPacket);

/// Registers the repair packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSRepairSingleEquipmentPacket>(LEVEL_PLAIN, cs::CSRepairSingleEquipmentPacket);
    reg.register::<CSRepairAllEquipmentsPacket>(LEVEL_PLAIN, cs::CSRepairAllEquipmentsPacket);
}
