//! `Scripts/Commands/AddGold.cs`.
//!
//! Note: `GoldCmd` (loaded after this file) replaces this command under `gold`, and the
//! `addgold` / `add_gold` aliases then resolve to `GoldCmd` as well, so this command is only
//! reachable directly (as in C#).

use super::prelude::*;
use crate::models::item_task::{task_type, ItemTask};
use crate::systems::economy::item_packets::SCItemTaskSuccessPacket;

/// `targetPlayer.Money += amount` + `SCItemTaskSuccessPacket(AutoLootDoodadItem,
/// [MoneyChange(amount)], [])` (shared with `GoldSetSubCommand`).
pub fn adjust_money(target: &PlayerRef, amount: i32) {
    target.data().money += i64::from(amount);
    target.send(SCItemTaskSuccessPacket {
        action: task_type::AUTO_LOOT_DOODAD_ITEM,
        tasks: vec![ItemTask::MoneyChange(amount)],
        force_remove: Vec::new(),
    });
}

/// `AddGold`.
pub struct AddGold;

impl AddGold {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddGold {
    fn command_names(&self) -> Vec<String> {
        names(&["gold", "addgold", "add_gold"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        let server = ctx.server()?;
        let character = ctx.player()?;
        let (target, first_arg) = get_target_or_self(server, &character, Some(&args[0]));

        let parse = |i: usize| {
            args.get(first_arg + i)
                .and_then(|a| try_parse::<i32>(a))
                .unwrap_or(0)
        };
        let arg_gold = parse(0);
        let arg_silver = parse(1);
        let arg_copper = parse(2);

        // C#: unchecked int arithmetic.
        let arg_total = arg_copper
            .wrapping_add(arg_silver.wrapping_mul(100))
            .wrapping_add(arg_gold.wrapping_mul(10_000));

        if arg_total != 0 {
            adjust_money(&target, arg_total);
            if is_other(&character, &target) {
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "changed {}'s money by {arg_gold}g {arg_silver}s {arg_copper}c",
                        name_of(&target)
                    ),
                );
                send_player_message(
                    &target,
                    &format!("[GM] {} has adjusted your money", name_of(&character)),
                );
            }
        } else {
            CommandManager::send_error_text(self, out, "No valid amount provided ...");
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) <gold> [silver] [copper]>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Adds X amount of money to target (can be negative values).".into()
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use crate::opcodes::sc;
    use crate::systems::services::commands::output::{CharacterMessageOutput, PlayerCharacter};

    use super::super::harness;

    fn run(server: &GameServer, p: &PlayerRef, line: &str) -> Vec<String> {
        let ctx = CommandContext {
            server: Some(server),
            character: PlayerCharacter::arc(p),
        };
        let args: Vec<String> = line
            .split(' ')
            .filter(|a| !a.is_empty())
            .map(String::from)
            .collect();
        let mut out = CharacterMessageOutput::new(ctx.character.clone());
        AddGold::new(&ScriptEnv::default())
            .execute(&ctx, &args, &mut out)
            .expect("no exception");
        out.messages().to_vec()
    }

    /// `SCItemTaskSuccessPacket(AutoLootDoodadItem, [MoneyChange(amount)], [])`.
    pub fn money_body(amount: i32) -> Vec<u8> {
        let mut s = aaemu_commons::PacketStream::new();
        s.write_u8(task_type::AUTO_LOOT_DOODAD_ITEM).write_u8(1);
        aaemu_commons::Marshal::write(&ItemTask::MoneyChange(amount), &mut s);
        s.write_u8(0).write_u32(0);
        s.into_bytes()
    }

    #[tokio::test]
    async fn add_gold_changes_money() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        gm.p.data().money = 0;
        bob.p.data().money = 0;

        assert!(run(&server, &gm.p, "1 2 3").is_empty());
        assert_eq!(gm.p.data().money, 10_203);
        assert_eq!(
            gm.packets(),
            vec![(sc::SCItemTaskSuccessPacket, money_body(10_203))]
        );

        let out = run(&server, &gm.p, "Bob -1");
        assert_eq!(out, ["[gold] changed Bob's money by -1g 0s 0c"]);
        assert_eq!(bob.p.data().money, -10_000);
        assert_eq!(bob.chat(), ["[GM] Gm has adjusted your money"]);

        let out = run(&server, &gm.p, "Bob x");
        assert_eq!(
            out,
            ["|cFFFFFFFF[gold]|r |cFFFF0000No valid amount provided ...|r"]
        );
        let out = run(&server, &gm.p, "");
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/gold|r |cFFEEEEAA(target) <gold> [silver] [copper]>|r\n|cFF888888Adds X amount of money to target (can be negative values).|r"]
        );
    }
}
