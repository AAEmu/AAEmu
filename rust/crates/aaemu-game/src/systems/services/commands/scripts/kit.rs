//! `Scripts/Commands/Kit.cs` (class `AddKit`, + `Scripts/Commands/kits.json`).

use serde::Deserialize;

use super::prelude::*;
use crate::models::item_task::task_type;
use crate::systems::economy::inventory::{Inv, ItemTxn};

/// `GMItemKitItem`.
#[derive(Debug, Clone, PartialEq, Eq, Deserialize)]
#[serde(default)]
pub struct GMItemKitItem {
    #[serde(rename = "names")]
    pub kitnames: Vec<String>,
    #[serde(rename = "id")]
    pub item_id: u32,
    #[serde(rename = "grade")]
    pub item_grade: u8,
    #[serde(rename = "count")]
    pub item_count: i32,
}

impl Default for GMItemKitItem {
    fn default() -> Self {
        Self {
            kitnames: Vec::new(),
            item_id: 0,
            item_grade: 0,
            item_count: 1,
        }
    }
}

/// `GMItemKitConfig`.
#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
pub struct GMItemKitConfig {
    #[serde(default)]
    pub itemkits: Vec<GMItemKitItem>,
    #[serde(skip)]
    pub itemkitnames: Vec<String>,
}

/// `AddKit`.
pub struct AddKit {
    pub kitconfig: GMItemKitConfig,
}

impl AddKit {
    /// The command with its `OnLoad` (`InitKits`) done.
    pub fn new(env: &ScriptEnv) -> Self {
        Self {
            kitconfig: Self::init_kits(env),
        }
    }

    /// `InitKits()`: `<AppPath>/Scripts/Commands/kits.json` (Newtonsoft.Json: comments and
    /// trailing commas allowed); errors are logged and leave the list empty.
    pub fn init_kits(env: &ScriptEnv) -> GMItemKitConfig {
        let mut kitconfig = GMItemKitConfig::default();
        let file_path = env
            .app_path
            .join("Scripts")
            .join("Commands")
            .join("kits.json");
        let loaded = (|| -> anyhow::Result<GMItemKitConfig> {
            let contents = std::fs::read_to_string(&file_path).unwrap_or_default();
            if contents.trim().is_empty() {
                anyhow::bail!("File {} doesn't exists or is empty.", file_path.display());
            }
            let contents = contents.trim_start_matches('\u{feff}');
            let json = crate::data::world_templates::strip_json_extensions(contents);
            Ok(serde_json::from_str(&json)?)
        })();
        match loaded {
            Ok(json_kit) => kitconfig.itemkits.extend(json_kit.itemkits),
            Err(x) => tracing::error!("Exception: {x}"),
        }

        // Create a enum for our "/kit ?" help command
        for kit in &kitconfig.itemkits {
            for kn in &kit.kitnames {
                if !kitconfig.itemkitnames.contains(kn) {
                    kitconfig.itemkitnames.push(kn.clone());
                }
            }
        }
        // C#: `List<string>.Sort()` (culture aware, invariant culture: case-insensitive first).
        kitconfig
            .itemkitnames
            .sort_by(|a, b| a.to_lowercase().cmp(&b.to_lowercase()).then(b.cmp(a)));
        kitconfig
    }
}

impl Command for AddKit {
    fn command_names(&self) -> Vec<String> {
        names(&["kit", "addkit", "add_kit"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.is_empty() {
            ctx.character
                .send_message(&format!("[Items] {COMMAND_PREFIX}kit (target) <kitname>"));
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "{} kits have been loaded, use {COMMAND_PREFIX}kit ? to get a list of kits",
                    self.kitconfig.itemkitnames.len()
                ),
            );
            return Ok(());
        }
        let server = ctx.server()?;
        let character = ctx.player()?;
        let (target, first_arg) = get_target_or_self(server, &character, Some(&args[0]));

        let kit_name = args
            .get(first_arg)
            .map(|a| a.to_lowercase())
            .unwrap_or_default();
        let mut items_added = 0;

        if kit_name == "?" {
            ctx.character.send_message(&format!(
                "[Items] {COMMAND_PREFIX}kit has the following kits registered:"
            ));
            let mut s = String::new();
            for n in &self.kitconfig.itemkitnames {
                s += n;
                s += "  ";
            }
            ctx.character.send_message(&format!("|cFFFFFFFF{s}|r"));
            return Ok(());
        }

        for kit in &self.kitconfig.itemkits {
            if !kit.kitnames.contains(&kit_name) {
                continue;
            }
            if server.game_data.items.template(kit.item_id).is_none() {
                ctx.character.send_message_typed(
                    chat_type::SYSTEM,
                    &format!("Item could not be created, ID: {} !", kit.item_id),
                    None,
                );
                tracing::error!("itemId not found: {}", kit.item_id);
                continue;
            }
            let acquired = {
                let mut txn = ItemTxn::begin(server);
                let bag = Inv::of(&target).bag();
                let ok = bag
                    .lock()
                    .expect("container poisoned")
                    .acquire_default_item(
                        task_type::GM,
                        kit.item_id,
                        kit.item_count,
                        i32::from(kit.item_grade),
                        0,
                        &mut txn.env,
                    );
                // Flush the item effects once the bag lock is released.
                drop(txn);
                ok
            };
            if !acquired {
                ctx.character.send_message_typed(
                    chat_type::SYSTEM,
                    "Item could not be created!",
                    Some(Color::RED),
                );
                continue;
            }
            if is_other(&character, &target) {
                ctx.character.send_message(&format!(
                    "[Items] added item {} to {}'s inventory",
                    kit.item_id,
                    name_of(&target)
                ));
                send_player_message(
                    &target,
                    &format!(
                        "[GM] {} has added a item to your inventory",
                        name_of(&character)
                    ),
                );
            }
            items_added += 1;
        }

        if items_added > 0 {
            if is_other(&character, &target) {
                ctx.character.send_message(&format!(
                    "[Items] added {items_added} items to {}'s inventory",
                    name_of(&target)
                ));
                send_player_message(
                    &target,
                    &format!(
                        "[GM] {} has added a {items_added} item to your inventory",
                        name_of(&character)
                    ),
                );
            }
        } else {
            ctx.character
                .send_message(&format!("[Items] No items in kit \"{kit_name}\""));
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) <kitname>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Adds a set of items based on a kit name to target player.".into()
    }
}

#[cfg(test)]
mod tests {
    use std::sync::atomic::{AtomicU64, Ordering};
    use std::sync::Arc;

    use super::super::harness;
    use super::*;
    use crate::data::items::ItemTemplate;
    use crate::data::GameData;
    use crate::models::container::slot_type;
    use crate::systems::economy::inventory::Inv;

    fn env_with_kits(json: &str) -> ScriptEnv {
        static NEXT_ENV: AtomicU64 = AtomicU64::new(0);
        let dir = std::env::temp_dir().join(format!(
            "aaemu-kit-test-{}-{}",
            std::process::id(),
            NEXT_ENV.fetch_add(1, Ordering::Relaxed)
        ));
        let cmd_dir = dir.join("Scripts").join("Commands");
        std::fs::create_dir_all(&cmd_dir).unwrap();
        std::fs::write(cmd_dir.join("kits.json"), json).unwrap();
        ScriptEnv { app_path: dir }
    }

    const KITS: &str = r#"{
    "itemkits": [
        // Stack
        { "names": [ "starter", "potions" ], "id": 1000, "count": 5 },
        { "names": [ "starter" ], "id": 2000, "grade": 2, },
        { "names": [ "broken" ], "id": 9999 },
    ]
}"#;

    #[test]
    fn kits_json_is_loaded_at_load() {
        let kit = AddKit::new(&env_with_kits(KITS));
        assert_eq!(kit.kitconfig.itemkits.len(), 3);
        assert_eq!(
            kit.kitconfig.itemkits[1],
            GMItemKitItem {
                kitnames: vec!["starter".into()],
                item_id: 2000,
                item_grade: 2,
                item_count: 1,
            }
        );
        assert_eq!(kit.kitconfig.itemkitnames, ["broken", "potions", "starter"]);
        // A missing file: an empty list.
        let none = AddKit::new(&ScriptEnv {
            app_path: std::env::temp_dir().join("aaemu-kit-test-missing"),
        });
        assert!(none.kitconfig.itemkits.is_empty());
    }

    #[tokio::test]
    async fn kit_adds_the_kit_items() {
        let mut data = GameData::empty();
        data.items
            .templates
            .insert(1000, ItemTemplate::plain(1000, 20));
        data.items
            .templates
            .insert(2000, ItemTemplate::plain(2000, 1));
        let server = harness::server_with(data);
        // Replace the loaded `kit` with one that reads the test kits.
        server.systems.services.commands.register_names(
            &["kit", "addkit", "add_kit"],
            Arc::new(AddKit::new(&env_with_kits(KITS))),
        );
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);

        let (_, out) = harness::run(&server, &gm.p, "kit").await;
        assert_eq!(
            out,
            ["[kit] 3 kits have been loaded, use /kit ? to get a list of kits"]
        );
        assert_eq!(
            gm.chat(),
            [
                "[Items] /kit (target) <kitname>",
                "[kit] 3 kits have been loaded, use /kit ? to get a list of kits"
            ]
        );
        harness::run(&server, &gm.p, "kit ?").await;
        assert_eq!(
            gm.chat(),
            [
                "[Items] /kit has the following kits registered:",
                "|cFFFFFFFFbroken  potions  starter  |r"
            ]
        );

        harness::run(&server, &gm.p, "kit STARTER").await;
        assert!(gm.chat().is_empty());
        let inv = Inv::of(&gm.p);
        assert_eq!(inv.items_count_in(slot_type::INVENTORY, 1000, -1), 5);
        assert_eq!(inv.items_count_in(slot_type::INVENTORY, 2000, -1), 1);

        harness::run(&server, &gm.p, "addkit Bob potions").await;
        assert_eq!(
            gm.chat(),
            [
                "[Items] added item 1000 to Bob's inventory",
                "[Items] added 1 items to Bob's inventory"
            ]
        );
        assert_eq!(
            bob.chat(),
            [
                "[GM] Gm has added a item to your inventory",
                "[GM] Gm has added a 1 item to your inventory"
            ]
        );
        assert_eq!(
            Inv::of(&bob.p).items_count_in(slot_type::INVENTORY, 1000, -1),
            5
        );

        harness::run(&server, &gm.p, "kit broken").await;
        assert_eq!(
            gm.chat(),
            [
                "Item could not be created, ID: 9999 !",
                "[Items] No items in kit \"broken\""
            ]
        );
    }
}
