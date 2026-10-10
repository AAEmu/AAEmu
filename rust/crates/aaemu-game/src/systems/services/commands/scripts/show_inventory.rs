//! `Scripts/Commands/ShowInventory.cs`.

use super::prelude::*;
use crate::models::container::slot_type;
use crate::models::item::Item;
use crate::models::item_task::task_type;
use crate::systems::economy::inventory::{self, Inv, ItemTxn};

/// `OrderBy(x => x.Slot).ThenBy(x => x.CreateTime)` (stable).
fn sorted(mut items: Vec<Item>) -> Vec<Item> {
    items.sort_by_key(|i| (i.slot, i.create_time));
    items
}

/// `((EquipmentItemSlot)slot).ToString()` (the int is truncated to the byte enum).
fn equipment_slot_name(slot: i32) -> String {
    enum_member_name(i64::from(slot as u8), EQUIPMENT_ITEM_SLOT)
}

/// `ShowInventory`.
pub struct ShowInventory;

impl ShowInventory {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }

    /// The `unit.Equipment` listing of a non-character unit target.
    fn show_unit_equipment(&self, unit: &WorldObject, out: &mut dyn MessageOutput) {
        // `Unit.Equipment`: NPCs and mates have equipment, the other units an empty
        // `EquipmentContainer`; mates use `SlotType.EquipmentMate`.
        let (items, container_type, npc_template) = match unit {
            WorldObject::Npc(n) => {
                let d = n.data();
                (
                    d.equipment.iter().flatten().cloned().collect::<Vec<_>>(),
                    slot_type::EQUIPMENT,
                    Some(d.unit.template_id),
                )
            }
            WorldObject::Mate(m) => (
                m.data().equipment.iter().flatten().cloned().collect(),
                slot_type::EQUIPMENT_MATE,
                None,
            ),
            _ => (Vec::new(), slot_type::EQUIPMENT, None),
        };
        let count = items.len();
        let mut template_name = "Unit".to_string();
        for item in sorted(items) {
            if let Some(template_id) = npc_template {
                template_name = format!("|nc;@NPC_NAME({template_id})|r");
            }
            let slot_name = equipment_slot_name(item.slot);
            let count_name = if item.count == 1 {
                String::new()
            } else {
                format!("|ng;{}|r x ", item.count)
            };
            out.send_message(&format!(
                "[{template_name}][{slot_name}] {count_name}|nn;{}|r = @ITEM_NAME({})",
                item.template_id, item.template_id
            ));
        }
        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "[{template_name}][{}] {count} entries",
                enum_member_name(i64::from(container_type), SLOT_TYPE)
            ),
        );
    }
}

impl Command for ShowInventory {
    fn command_names(&self) -> Vec<String> {
        names(&[
            "inventory",
            "showinv",
            "show_inv",
            "showinventory",
            "show_inventory",
        ])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        if let Some(unit) =
            current_target(server, &character).filter(|t| t.is_unit() && t.as_player().is_none())
        {
            self.show_unit_equipment(&unit, out);
            return Ok(());
        }

        let mut target = character.clone();
        let mut first_arg = 0;
        if !args.is_empty() {
            (target, first_arg) = get_target_or_self(server, &character, Some(&args[0]));
        }

        let mut container_id = i64::from(slot_type::INVENTORY);
        if let Some(arg_container_id) = args
            .get(first_arg)
            .and_then(|a| enum_try_parse_ci(a, SLOT_TYPE, 0, i64::from(u8::MAX)))
        {
            if arg_container_id <= i64::from(slot_type::MAIL)
                || arg_container_id == i64::from(slot_type::SYSTEM)
            {
                container_id = arg_container_id;
            } else {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!(
                        "Invalid ContainerType Id {}",
                        enum_member_name(arg_container_id, SLOT_TYPE)
                    ),
                );
                return Ok(());
            }
        }

        let do_try_fix = args
            .get(first_arg + 1)
            .is_some_and(|a| a.eq_ignore_ascii_case("fix"));
        let mut invalid_items: Vec<u64> = Vec::new();
        let target_name = name_of(&target);

        let inv = Inv::of(&target);
        let target_container = inv.get(container_id as u8);
        let mut container_type = 0u8;
        match &target_container {
            Some(c) => {
                let (ctype, items) = {
                    let g = c.lock().expect("container poisoned");
                    (g.slot_type, g.items.clone())
                };
                container_type = ctype;
                let ctype_name = enum_member_name(i64::from(ctype), SLOT_TYPE);
                let count = items.len();
                let show_warnings = matches!(
                    ctype,
                    slot_type::EQUIPMENT
                        | slot_type::EQUIPMENT_MATE
                        | slot_type::INVENTORY
                        | slot_type::BANK
                );
                let mut last_slot_number = -1;
                let mut has_slot_errors = 0;
                for item in sorted(items) {
                    let mut additional_errors = String::new();
                    let mut slot_name = format!("{ctype_name}-{}", item.slot);
                    if item.slot_type == slot_type::EQUIPMENT {
                        slot_name = equipment_slot_name(item.slot);
                    }

                    if last_slot_number == item.slot && show_warnings {
                        slot_name = format!("|cFFFF0000**{slot_name}**|r");
                        invalid_items.push(item.id);
                        has_slot_errors += 1;
                    } else if item.slot_type != ctype {
                        // `item.SlotType != item._holdingContainer?.ContainerType` (the holding
                        // container is the listed one).
                        additional_errors += "|cFFFF0000**Container Error**|r";
                        invalid_items.push(item.id);
                        has_slot_errors += 1;
                    }

                    let count_name = if item.count == 1 {
                        String::new()
                    } else {
                        format!("|ng;{}|r x ", item.count)
                    };
                    out.send_message(&format!(
                        "[|nd;{target_name}|r][{slot_name}] |nb;{}|r {count_name}|nn;{}|r = @ITEM_NAME({}){additional_errors}",
                        item.id, item.template_id, item.template_id
                    ));
                    last_slot_number = item.slot;
                }

                if has_slot_errors > 0 {
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "[|nd;{target_name}|r] |cFFFF0000{ctype_name} contains {has_slot_errors} slot number related errors, please manually fix these!|r"
                        ),
                    );
                }
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("[|nd;{target_name}|r][{ctype_name}] {count} entries"),
                );
            }
            None => CommandManager::send_error_text(
                self,
                out,
                &format!(
                    "Unused container Id {}",
                    enum_member_name(container_id, SLOT_TYPE)
                ),
            ),
        }

        let Some(target_container) = target_container else {
            return Ok(());
        };
        if !do_try_fix || invalid_items.is_empty() {
            return Ok(());
        }
        let mut fixed_count = 0;
        match container_type {
            slot_type::EQUIPMENT | slot_type::EQUIPMENT_MATE => {
                // Fix equipment by moving it to inventory
                let bag = inv.bag();
                for item_id in &invalid_items {
                    let template_id = target_container
                        .lock()
                        .expect("container poisoned")
                        .get_item_by_id(*item_id)
                        .map_or(0, |i| i.template_id);
                    let next_slot = bag.lock().expect("container poisoned").get_unused_slot(-1);
                    let moved = next_slot >= 0 && {
                        let mut txn = ItemTxn::begin(server);
                        let data = &server.game_data.items;
                        let (mut b, src) = inventory::lock_pair(&bag, &target_container);
                        let ok = match src {
                            Some(mut src) => b.move_item_from(
                                &mut src,
                                task_type::INVALID,
                                *item_id,
                                -1,
                                data,
                                &mut txn.env.fx,
                            ),
                            None => false,
                        };
                        if ok {
                            if let Some(i) = b.get_item_by_id_mut(*item_id) {
                                i.slot = next_slot;
                                i.dirty = true;
                            }
                        }
                        drop(b);
                        drop(txn);
                        ok
                    };
                    if !moved {
                        CommandManager::send_error_text(
                            self,
                            out,
                            &format!(
                                "Unable to fix {template_id} ({template_id}) ItemId: {item_id}, no more room in your inventory to move this item!"
                            ),
                        );
                        continue;
                    }
                    fixed_count += 1;
                }
            }
            slot_type::INVENTORY | slot_type::BANK => {
                // Fix things
                for item_id in &invalid_items {
                    let mut c = target_container.lock().expect("container poisoned");
                    let next_slot = c.get_unused_slot(-1);
                    let template_id = c.get_item_by_id(*item_id).map_or(0, |i| i.template_id);
                    if next_slot < 0 {
                        drop(c);
                        CommandManager::send_error_text(
                            self,
                            out,
                            &format!(
                                "Unable to fix {template_id} ({template_id}) ItemId: {item_id}, no more room!"
                            ),
                        );
                        continue;
                    }
                    if let Some(i) = c.get_item_by_id_mut(*item_id) {
                        i.slot = next_slot;
                        i.dirty = true;
                    }
                    fixed_count += 1;
                }
            }
            _ => {
                // Can't or doesn't need fixing
            }
        }

        if fixed_count > 0 {
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "{fixed_count} items have been re-slotted, fully |ni;reboot your game client|r for the fixes to take affect"
                ),
            );
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) [containerId] [fix]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Show content of target's item container.\rEquipment = 1, Inventory = 2 (default), Bank = 3, Trade = 4, Mail = 5".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::spawn_npc;
    use super::super::harness;
    use crate::data::items::ItemTemplate;
    use crate::data::GameData;
    use crate::models::container::slot_type;
    use crate::models::item::Item;
    use crate::server::GameServer;
    use crate::systems::economy::inventory::Inv;
    use crate::world::PlayerRef;

    fn new_item(server: &GameServer, template: u32, count: i32) -> Item {
        server
            .items
            .create(
                template,
                count,
                0,
                1,
                &server.game_data.items,
                &server.ids.item,
                0,
            )
            .expect("item")
    }

    fn put(
        server: &GameServer,
        p: &PlayerRef,
        st: u8,
        template: u32,
        count: i32,
        slot: i32,
    ) -> u64 {
        let mut item = new_item(server, template, count);
        item.slot = slot;
        item.slot_type = st;
        let id = item.id;
        Inv::of(p).get(st).unwrap().lock().unwrap().items.push(item);
        id
    }

    #[tokio::test]
    async fn inventory_lists_and_fixes_containers() {
        let mut data = GameData::empty();
        data.items
            .templates
            .insert(1000, ItemTemplate::plain(1000, 20));
        let server = harness::server_with(data);
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let _bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);

        let a = put(&server, &gm.p, slot_type::INVENTORY, 1000, 3, 0);
        let b = put(&server, &gm.p, slot_type::INVENTORY, 1000, 1, 0);
        let (_, out) = harness::run(&server, &gm.p, "inventory").await;
        assert_eq!(
            out,
            [
                format!("[|nd;Gm|r][Inventory-0] |nb;{a}|r |ng;3|r x |nn;1000|r = @ITEM_NAME(1000)"),
                format!("[|nd;Gm|r][|cFFFF0000**Inventory-0**|r] |nb;{b}|r |nn;1000|r = @ITEM_NAME(1000)"),
                "[inventory] [|nd;Gm|r] |cFFFF0000Inventory contains 1 slot number related errors, please manually fix these!|r".to_string(),
                "[inventory] [|nd;Gm|r][Inventory] 2 entries".to_string(),
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, "showinv Gm inventory FIX").await;
        assert_eq!(
            out.last().unwrap(),
            "[inventory] 1 items have been re-slotted, fully |ni;reboot your game client|r for the fixes to take affect"
        );
        let slots: Vec<i32> = Inv::of(&gm.p)
            .bag()
            .lock()
            .unwrap()
            .items
            .iter()
            .map(|i| i.slot)
            .collect();
        assert_eq!(slots, [0, 1]);

        let (_, out) = harness::run(&server, &gm.p, "inventory Bob 3").await;
        assert_eq!(out, ["[inventory] [|nd;Bob|r][Bank] 0 entries"]);
        let (_, out) = harness::run(&server, &gm.p, "inventory auction").await;
        assert_eq!(
            out,
            [harness::error(
                "inventory",
                "Invalid ContainerType Id Auction"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "inventory system").await;
        assert_eq!(out.len(), 1);

        // A NPC target: its equipment.
        let npc = spawn_npc(&server, 0x500, 10, 105.0, 100.0, 1_000);
        gm.p.data().unit.current_target = npc.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "inventory").await;
        assert_eq!(out, ["[inventory] [Unit][Equipment] 0 entries"]);
        let mut sword = new_item(&server, 1000, 1);
        sword.slot = 15;
        npc.data().equipment[15] = Some(sword);
        let (_, out) = harness::run(&server, &gm.p, "inventory").await;
        assert_eq!(
            out,
            [
                "[|nc;@NPC_NAME(10)|r][Mainhand] |nn;1000|r = @ITEM_NAME(1000)",
                "[inventory] [|nc;@NPC_NAME(10)|r][Equipment] 1 entries"
            ]
        );
    }
}
