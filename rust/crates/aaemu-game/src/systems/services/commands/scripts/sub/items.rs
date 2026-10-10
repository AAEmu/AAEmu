//! `Scripts/SubCommands/Items/**` and `Scripts/Commands/ItemCmd.cs`.

use chrono::{DateTime, Utc};

use super::super::prelude::*;
use crate::data::items::bind_type;
use crate::models::container::equip_slot;
use crate::models::item::{ITEM_FLAG_SECURE, ITEM_FLAG_SOUL_BOUND, ITEM_FLAG_UNPACKED};
use crate::models::item_task::{task_type, ItemTask};
use crate::systems::economy::inventory::{self, Inv, ItemTxn};
use crate::systems::economy::item_packets::{SCItemTaskSuccessPacket, SCSyncItemLifespanPacket};
use crate::systems::services::commands::output::PlayerCharacter;
use crate::utils::chat::convert_as_chat_message_reference;

/// `ItemGrade.Crude` / `ItemGrade.Mythic`.
const ITEM_GRADE_CRUDE: u8 = 0;
const ITEM_GRADE_MYTHIC: u8 = 11;

/// `DateTime.UtcNow.AddMinutes(minutes)` (`AddMinutes` rounds to whole milliseconds).
fn utc_now_add_minutes(now: DateTime<Utc>, minutes: f64) -> DateTime<Utc> {
    now + chrono::Duration::milliseconds((minutes * 60_000.0).round() as i64)
}

fn base_with(
    title: &str,
    description: &str,
    call_prefix: &str,
    params: Vec<Box<dyn SubCommandParameter>>,
) -> SubCommandBase {
    let mut base = SubCommandBase::new(title, description, call_prefix);
    for p in params {
        base.add_parameter(p).expect("valid parameter order");
    }
    base
}

/// `ItemAddSubCommand`.
pub struct ItemAddSubCommand {
    base: SubCommandBase,
}

impl Default for ItemAddSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Item]",
                "Adds to self or a player name or a selected target an amount of a specific item template of a specific [grade].",
                &format!("{COMMAND_PREFIX}item add"),
                vec![
                    Box::new(StringSubCommandParameter::new(
                        "target",
                        Some("player name||target||self"),
                        true,
                        &[],
                    )),
                    Box::new(NumericSubCommandParameter::<u32>::new(
                        "templateId",
                        Some("template id"),
                        true,
                    )),
                    Box::new(
                        NumericSubCommandParameter::<i32>::with_range(
                            "amount",
                            Some("amount=1"),
                            false,
                            None,
                            1,
                            1000,
                        )
                        .expect("valid range")
                        .with_default(ParameterValue::Int32(1)),
                    ),
                    Box::new(
                        NumericSubCommandParameter::<u8>::with_range(
                            "grade",
                            Some("item grade=0"),
                            false,
                            None,
                            ITEM_GRADE_CRUDE,
                            ITEM_GRADE_MYTHIC,
                        )
                        .expect("valid range")
                        .with_default(ParameterValue::Byte(ITEM_GRADE_CRUDE)),
                    ),
                ],
            ),
        }
    }
}

impl SubCommand for ItemAddSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        // `(Character)character`.
        let self_character = ctx.player()?;

        let first_argument = parameters.require("target")?.as_str()?.to_string();
        let add_target = if first_argument == "target" {
            match current_target(server, &self_character).and_then(|t| t.as_player().cloned()) {
                Some(p) => p,
                None => {
                    self.base.send_color_message(
                        out,
                        Color::RED,
                        "Please select a valid character player",
                    );
                    return Ok(());
                }
            }
        } else if first_argument == "self" {
            self_character.clone()
        } else {
            match server.world.character_by_name(&first_argument) {
                Some(p) => p,
                None => {
                    self.base.send_color_message(
                        out,
                        Color::RED,
                        &format!("Player: {first_argument} was not found."),
                    );
                    return Ok(());
                }
            }
        };

        let template_id = parameters.require("templateId")?.as_u32()?;
        let item_amount = parameters.require("amount")?.as_i32()?;
        let item_grade = parameters.require("grade")?.as_u8()?;

        let data = &server.game_data.items;
        if data.template(template_id).is_none() {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Item template id {template_id} does not exist!"),
            );
            return Ok(());
        }

        let inv = Inv::of(&add_target);
        if inventory::is_auto_equip_trade_pack(data, template_id) {
            // Speciality Packs or Tradepacks
            if inv.equipped(equip_slot::BACKPACK).is_some() {
                self.base.send_color_message(
                    out,
                    Color::RED,
                    "No room on the backpack slot to place a tradepack!",
                );
                return Ok(());
            }
            let mut txn = ItemTxn::begin(server);
            let ok = inv
                .equipment()
                .lock()
                .expect("container poisoned")
                .acquire_default_item(
                    task_type::GM,
                    template_id,
                    item_amount,
                    i32::from(item_grade),
                    0,
                    &mut txn.env,
                );
            drop(txn);
            if !ok {
                self.base
                    .send_color_message(out, Color::RED, "Tradepack could not be created!");
                return Ok(());
            }
        } else {
            let mut txn = ItemTxn::begin(server);
            let ok = inv
                .bag()
                .lock()
                .expect("container poisoned")
                .acquire_default_item(
                    task_type::GM,
                    template_id,
                    item_amount,
                    i32::from(item_grade),
                    0,
                    &mut txn.env,
                );
            drop(txn);
            if !ok {
                self.base
                    .send_color_message(out, Color::RED, "Item could not be created!");
                return Ok(());
            }
        }

        if is_other(&self_character, &add_target) {
            let reference = convert_as_chat_message_reference(template_id, item_grade, 100);
            self.base.send_message(
                out,
                &format!(
                    "Added item {reference} to {}'s inventory",
                    name_of(&add_target)
                ),
            );
            self.base.send_message_to(
                &PlayerCharacter(add_target.clone()),
                out,
                &format!(
                    "[GM] {} added {reference} to your inventory",
                    name_of(&self_character)
                ),
            );
        }
        Ok(())
    }
}

/// `ItemExpireSubCommand`.
pub struct ItemExpireSubCommand {
    base: SubCommandBase,
}

impl Default for ItemExpireSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Item]",
                "Set target to expire after a specific amount of minutes, or 30 seconds if ommited.",
                &format!("{COMMAND_PREFIX}item expire"),
                vec![
                    Box::new(NumericSubCommandParameter::<u64>::new(
                        "itemId",
                        Some("item id"),
                        true,
                    )),
                    Box::new(
                        NumericSubCommandParameter::<f32>::with_range(
                            "minutes",
                            Some("minutes=0.5"),
                            false,
                            None,
                            -1.0,
                            1_000_000.0,
                        )
                        .expect("valid range")
                        .with_default(ParameterValue::Single(0.5)),
                    ),
                ],
            ),
        }
    }
}

impl SubCommand for ItemExpireSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        // `(Character)character`.
        let self_character = ctx.player()?;

        let item_id = parameters.require("itemId")?.as_u64()?;
        let minutes = optional_parameter(parameters, "minutes", -1.0f32, |v| v.as_f32())?;

        let Some(item) = server.items.item_by_id(item_id) else {
            ctx.character
                .send_message(&format!("No item could be found with Id: {item_id}"));
            return Ok(());
        };

        let new_time = utc_now_add_minutes(Utc::now(), f64::from(minutes));
        if minutes >= 0.0 {
            let t = new_time.timestamp();
            server.items.update_item(item_id, |i| {
                if i.expiration_time != t {
                    i.expiration_time = t;
                    i.dirty = true;
                }
            });
            self_character.send(SCSyncItemLifespanPacket {
                added: true,
                item_id: item.id,
                template_id: item.template_id,
                expire_time: t,
            });
            ctx.character.send_message(&format!(
                "Item @ITEM_NAME({})'s expire time updated to {}",
                item.template_id,
                date_time_string(new_time)
            ));
        } else {
            server.items.update_item(item_id, |i| {
                if i.expiration_time != 0 {
                    i.expiration_time = 0; // DateTime.MinValue
                    i.dirty = true;
                }
            });
            self_character.send(SCSyncItemLifespanPacket {
                added: false,
                item_id: item.id,
                template_id: item.template_id,
                expire_time: 0,
            });
            ctx.character.send_message(&format!(
                "Item @ITEM_NAME({})'s expire set to invalid",
                item.template_id
            ));
        }
        Ok(())
    }
}

/// `ItemUnwrapSubCommand`.
pub struct ItemUnwrapSubCommand {
    base: SubCommandBase,
}

impl Default for ItemUnwrapSubCommand {
    fn default() -> Self {
        Self {
            base: base_with(
                "[Item]",
                "Set target item's unwrap time to now, or to expire a specified amount of minutes from now.",
                &format!("{COMMAND_PREFIX}item unwrap"),
                vec![
                    Box::new(NumericSubCommandParameter::<u64>::new(
                        "itemId",
                        Some("item id"),
                        true,
                    )),
                    Box::new(
                        NumericSubCommandParameter::<f32>::with_range(
                            "minutes",
                            Some("minutes=-1"),
                            false,
                            None,
                            -1.0,
                            1_000_000.0,
                        )
                        .expect("valid range")
                        .with_default(ParameterValue::Single(-1.0)),
                    ),
                ],
            ),
        }
    }
}

impl SubCommand for ItemUnwrapSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        // `(Character)character`.
        let self_character = ctx.player()?;

        let item_id = parameters.require("itemId")?.as_u64()?;
        let minutes = optional_parameter(parameters, "minutes", -1.0f32, |v| v.as_f32())?;

        let Some(item) = server.items.item_by_id(item_id) else {
            ctx.character
                .send_message(&format!("No item could be found with Id: {item_id}"));
            return Ok(());
        };

        let template = server.game_data.items.template(item.template_id);
        let Some(equip_item_template) = template.and_then(|t| t.equip()).copied() else {
            ctx.character.send_message(&format!(
                "Item @ITEM_NAME({}) is not a equipment item, Id: {item_id}",
                item.template_id
            ));
            return Ok(());
        };
        let bind_on_unpack = template.is_some_and(|t| t.bind_type == bind_type::BIND_ON_UNPACK);

        let mut new_time = Utc::now();
        ctx.character
            .send_message(&format!("Item unwrap using {}", f32s(minutes)));
        if minutes >= 0.0 && equip_item_template.charge_lifetime > 0 {
            new_time = utc_now_add_minutes(
                utc_now_add_minutes(
                    new_time,
                    f64::from(equip_item_template.charge_lifetime.wrapping_neg()),
                ),
                f64::from(minutes),
            );
        }

        let task = server.items.update_item(item_id, |i| {
            i.unpack_time = new_time.timestamp();
            i.flags |= ITEM_FLAG_UNPACKED;
            if bind_on_unpack {
                i.flags |= ITEM_FLAG_SOUL_BOUND;
            }
            i.dirty = true;
            // C#: `item.HasFlag(ItemFlag.Secure)` is passed for both secure arguments.
            let secure = i.flags & ITEM_FLAG_SECURE != 0;
            (
                ItemTask::update_security(
                    i,
                    i.flags,
                    secure,
                    secure,
                    i.flags & ITEM_FLAG_UNPACKED != 0,
                ),
                i.unpack_time,
            )
        });
        let Some((update_item_task, unpack_time)) = task else {
            return Ok(());
        };
        self_character.send(SCItemTaskSuccessPacket {
            action: task_type::ITEM_TASK_THISTIME_UNPACK,
            tasks: vec![update_item_task],
            force_remove: Vec::new(),
        });
        if equip_item_template.charge_lifetime > 0 {
            self_character.send(SCSyncItemLifespanPacket {
                added: true,
                item_id: item.id,
                template_id: item.template_id,
                expire_time: unpack_time,
            });
        }

        ctx.character.send_message(&format!(
            "Item @ITEM_NAME({}) unwrap time set to {}",
            item.template_id,
            date_time_string(new_time)
        ));
        Ok(())
    }
}

/// `ItemCmd`.
pub fn item_cmd() -> RootCommand {
    RootCommand::new(&["item"], "[Item]", "Root command to manage Items", "")
        .register(Arc::new(ItemAddSubCommand::default()), &["add"])
        .register(Arc::new(ItemExpireSubCommand::default()), &["expire"])
        .register(Arc::new(ItemUnwrapSubCommand::default()), &["unwrap"])
}

#[cfg(test)]
mod tests {
    use super::super::super::harness;
    use crate::data::items::{EquipTemplate, ItemTemplate, TemplateKind};
    use crate::data::GameData;
    use crate::models::container::slot_type;
    use crate::models::item::ITEM_FLAG_UNPACKED;
    use crate::opcodes::sc;
    use crate::systems::economy::inventory::Inv;
    use crate::utils::chat::convert_as_chat_message_reference;

    fn data() -> GameData {
        let mut data = GameData::empty();
        data.items
            .templates
            .insert(1000, ItemTemplate::plain(1000, 1000));
        data.items.templates.insert(
            2000,
            ItemTemplate {
                kind: TemplateKind::Armor(EquipTemplate {
                    slot_type_id: 3,
                    charge_lifetime: 60,
                    ..EquipTemplate::default()
                }),
                ..ItemTemplate::plain(2000, 1)
            },
        );
        data
    }

    #[tokio::test]
    async fn item_add() {
        let server = harness::server_with(data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);

        let (_, out) = harness::run(&server, &gm.p, "item add self 1000 5").await;
        assert!(out.is_empty());
        assert_eq!(
            Inv::of(&gm.p).items_count_in(slot_type::INVENTORY, 1000, -1),
            5
        );
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCItemTaskSuccessPacket));

        let reference = convert_as_chat_message_reference(1000, 2, 100);
        let (_, out) = harness::run(&server, &gm.p, "item add Bob 1000 1 2").await;
        assert_eq!(
            out,
            [
                format!("[Item] Added item {reference} to Bob's inventory"),
                format!("Target: Bob - [Item] [GM] Gm added {reference} to your inventory"),
            ]
        );
        assert_eq!(
            bob.chat(),
            [format!(
                "[Item] [GM] Gm added {reference} to your inventory"
            )]
        );

        let (_, out) = harness::run(&server, &gm.p, "item add self 999").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Item] Item template id 999 does not exist!|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "item add self 1000 0").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Item] The number 0 should be between 1 and 1000 for parameter: amount=1|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "item add target 1000").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Item] Please select a valid character player|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "item add nobody 1000").await;
        assert_eq!(out, ["|cFFFF0000[Item] Player: nobody was not found.|r"]);
        let (_, out) = harness::run(&server, &gm.p, "item").await;
        assert_eq!(
            out,
            [
                "|cFF7CFC00[Item] Root command to manage Items|r",
                "|cFF7CFC00[Item] Supported subcommands: <add||expire||unwrap>|r",
                "|cFF7CFC00[Item] For more details use /<command> <subcommand> help.|r"
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, "item add help").await;
        assert_eq!(
            out[1],
            "|cFF7CFC00[Item] /item add <player name||target||self> <template id> [amount=1] [item grade=0]|r"
        );
    }

    #[tokio::test]
    async fn item_expire_and_unwrap() {
        let server = harness::server_with(data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        // Make the player's containers known to the ItemManager (`GetItemByItemId`).
        for (_, c) in Inv::of(&gm.p).containers {
            server.items.add_container(c);
        }
        harness::run(&server, &gm.p, "item add self 2000").await;
        let item = Inv::of(&gm.p).bag().lock().unwrap().items[0].clone();
        gm.frames();

        harness::run(&server, &gm.p, "item expire 424242").await;
        assert_eq!(gm.chat(), ["No item could be found with Id: 424242"]);

        let before = chrono::Utc::now().timestamp();
        harness::run(&server, &gm.p, &format!("item expire {}", item.id)).await;
        let exp = server.items.item_by_id(item.id).unwrap().expiration_time;
        assert!((before + 29..=before + 31).contains(&exp), "{exp}");
        let frames = gm.frames();
        assert_eq!(frames[0].0, sc::SCSyncItemLifespanPacket);
        assert_eq!(frames[0].1[0], 1);
        assert!(harness::chat_text(&frames[1].1)
            .starts_with("Item @ITEM_NAME(2000)'s expire time updated to "));

        harness::run(&server, &gm.p, &format!("item expire {} -1", item.id)).await;
        assert_eq!(server.items.item_by_id(item.id).unwrap().expiration_time, 0);
        assert_eq!(gm.chat(), ["Item @ITEM_NAME(2000)'s expire set to invalid"]);

        harness::run(&server, &gm.p, &format!("item unwrap {}", item.id)).await;
        let unwrapped = server.items.item_by_id(item.id).unwrap();
        assert_ne!(unwrapped.flags & ITEM_FLAG_UNPACKED, 0);
        let frames = gm.frames();
        let ops: Vec<u16> = frames.iter().map(|(op, _)| *op).collect();
        assert_eq!(
            ops,
            [
                sc::SCChatMessagePacket,
                sc::SCItemTaskSuccessPacket,
                sc::SCSyncItemLifespanPacket,
                sc::SCChatMessagePacket
            ]
        );
        assert_eq!(harness::chat_text(&frames[0].1), "Item unwrap using -1");
        assert!(harness::chat_text(&frames[3].1)
            .starts_with("Item @ITEM_NAME(2000) unwrap time set to "));

        harness::run(&server, &gm.p, "item add self 1000").await;
        let stack = Inv::of(&gm.p)
            .bag()
            .lock()
            .unwrap()
            .items
            .iter()
            .find(|i| i.template_id == 1000)
            .unwrap()
            .id;
        gm.frames();
        harness::run(&server, &gm.p, &format!("item unwrap {stack}")).await;
        assert_eq!(
            gm.chat(),
            [format!(
                "Item @ITEM_NAME(1000) is not a equipment item, Id: {stack}"
            )]
        );
    }
}
