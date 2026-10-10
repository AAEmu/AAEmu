//! `Scripts/Commands/CofferActions.cs`.

use super::prelude::*;
use crate::models::container::slot_type;

/// `CofferActions`.
pub struct CofferActions;

impl CofferActions {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `SlotType.ToString()`.
fn slot_type_name(v: u8) -> String {
    match v {
        slot_type::NONE => "None".into(),
        slot_type::EQUIPMENT => "Equipment".into(),
        slot_type::INVENTORY => "Inventory".into(),
        slot_type::BANK => "Bank".into(),
        slot_type::TRADE => "Trade".into(),
        slot_type::MAIL => "Mail".into(),
        slot_type::AUCTION => "Auction".into(),
        slot_type::EQUIPMENT_MATE => "EquipmentMate".into(),
        slot_type::SYSTEM => "System".into(),
        other => other.to_string(),
    }
}

impl Command for CofferActions {
    fn command_names(&self) -> Vec<String> {
        names(&["coffer", "chest"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let check_radius = 4f32;
        let action = args
            .first()
            .map_or_else(|| "help".to_string(), |a| a.to_lowercase());
        let doodad_obj_id_str = args.get(1).map_or("0", String::as_str);

        if action == "help" {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        let Some(doodad_obj_id) = try_parse::<u32>(doodad_obj_id_str) else {
            CommandManager::send_error_text(self, out, "Error parsing doodadObjId !");
            return Ok(());
        };

        let server = ctx.server()?;
        let character = ctx.player()?;
        let coffer = if doodad_obj_id == 0 {
            let doodads = around_of(server, &character, check_radius, |o| {
                o.as_doodad().filter(|d| d.data().coffer.is_some()).cloned()
            });
            match doodads.into_iter().next() {
                Some(d) => d,
                None => {
                    CommandManager::send_error_text(self, out, "No coffers found nearby !");
                    return Ok(());
                }
            }
        } else {
            let world = parent_world(server, &character).ok_or_else(nre)?;
            match world
                .doodad(doodad_obj_id)
                .filter(|d| d.data().coffer.is_some())
            {
                Some(d) => d,
                None => {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("No coffers found with objId: {doodad_obj_id} !"),
                    );
                    return Ok(());
                }
            }
        };

        let (db_id, template_id, state) = {
            let d = coffer.data();
            (d.db_id, d.template_id, d.coffer.clone().unwrap_or_default())
        };
        let container = (state.container_id != 0)
            .then(|| server.items.container_by_id(state.container_id))
            .flatten();
        // C#: `coffer.ItemContainer.ContainerId` (a coffer always has its container).
        let Some(container) = container else {
            return Err(nre());
        };
        let (container_id, container_type, items) = {
            let c = container.lock().expect("container poisoned");
            (c.container_id, c.slot_type, c.items.clone())
        };
        let used_by = state
            .opened_by
            .and_then(|id| server.world.character_by_id(id))
            .map_or_else(|| "<nobody>".to_string(), |p| name_of(&p));

        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "objId: {}, DoodadDbId: {db_id}, ItemContainerDbId: {container_id}, UsedBy: {used_by}",
                coffer.obj_id
            ),
        );

        match action.as_str() {
            "view" => {
                for item in &items {
                    let slot_name = item.slot.to_string();
                    let mut count_name = format!("|ng;{}|r x ", item.count);
                    if item.count == 1 {
                        count_name = String::new();
                    }
                    ctx.character.send_message(&format!(
                        "[|nd;@DOODAD_NAME({template_id})|r][{slot_name}] |nb;{}|r {count_name}|nn;{}|r = @ITEM_NAME({})",
                        item.id, item.template_id, item.template_id
                    ));
                }

                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "[|nd;@DOODAD_NAME({template_id})|r][{}] {} entries",
                        slot_type_name(container_type),
                        items.len()
                    ),
                );
            }
            "close" => {
                // C#: `DoodadManager.CloseCofferDoodad(null, coffer.ObjId)` dereferences the null
                // character (`character.ParentWorld`): always a NullReferenceException.
                return Err(nre());
            }
            _ => {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!("Unknown sub-command {action}"),
                );
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<action> [doodadObjId]".into()
    }

    fn get_command_help_text(&self) -> String {
        "View or manipulate a coffer doodad, using one of the following actions:\nclose - Force-Closes a coffer so it can be opened again by other people.\nview - View the coffer contents\n\nIf doodadObjId is omitted, the first found coffer in a 4m radius will be used.\n".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;
    use crate::data::items::ItemTemplate;
    use crate::models::item::Item;

    #[tokio::test]
    async fn coffer_view_and_close() {
        let server = ts::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let coffer = ts::spawn_doodad(&server, ts::DOODAD, 101.0, 100.0);
        let container = server.items.new_coffer_container(1, &server.ids.container);
        let container_id = container.lock().unwrap().container_id;
        {
            let t = ItemTemplate::plain(500, 1);
            let mut a = Item::create(77, &t, 3, 0, 1, &server.game_data.items, 0);
            a.slot = 0;
            let mut b = Item::create(78, &t, 1, 0, 1, &server.game_data.items, 0);
            b.slot = 1;
            let mut c = container.lock().unwrap();
            c.items.push(a);
            c.items.push(b);
        }
        coffer.data().coffer = Some(crate::models::doodad::DoodadCofferState {
            capacity: 10,
            container_id,
            opened_by: Some(1),
        });
        coffer.data().db_id = 9;
        gm.chat();

        let (_, out) = harness::run(&server, &gm.p, "coffer view").await;
        let head = format!(
            "objId: {}, DoodadDbId: 9, ItemContainerDbId: {container_id}, UsedBy: Gm",
            coffer.obj_id
        );
        let tail = format!("[|nd;@DOODAD_NAME({})|r][Trade] 2 entries", ts::DOODAD);
        assert_eq!(
            out,
            [
                harness::normal("coffer", &head),
                harness::normal("coffer", &tail)
            ]
        );
        let chat = gm.chat();
        assert_eq!(
            chat[1],
            format!(
                "[|nd;@DOODAD_NAME({})|r][0] |nb;77|r |ng;3|r x |nn;500|r = @ITEM_NAME(500)",
                ts::DOODAD
            )
        );
        assert_eq!(
            chat[2],
            format!(
                "[|nd;@DOODAD_NAME({})|r][1] |nb;78|r |nn;500|r = @ITEM_NAME(500)",
                ts::DOODAD
            )
        );

        // C# NullReferenceException (CloseCofferDoodad(null, ..)).
        let (_, out) =
            harness::run(&server, &gm.p, &format!("chest close {}", coffer.obj_id)).await;
        assert_eq!(out.len(), 1);
        assert!(gm.chat().contains(
            &"|cFFFF0000Object reference not set to an instance of an object.|r".to_string()
        ));

        let (_, out) = harness::run(&server, &gm.p, "coffer bogus").await;
        assert_eq!(
            out[1],
            harness::error("coffer", "Unknown sub-command bogus")
        );
        let (_, out) = harness::run(&server, &gm.p, "coffer view 1").await;
        assert_eq!(
            out,
            [harness::error("coffer", "No coffers found with objId: 1 !")]
        );
        let (_, out) = harness::run(&server, &gm.p, "coffer view x").await;
        assert_eq!(
            out,
            [harness::error("coffer", "Error parsing doodadObjId !")]
        );
        coffer.data().coffer = None;
        let (_, out) = harness::run(&server, &gm.p, "coffer view").await;
        assert_eq!(out, [harness::error("coffer", "No coffers found nearby !")]);
    }
}
