//! `Scripts/SubCommands/Gold/**` and `Scripts/Commands/GoldCmd.cs`.

use super::super::add_gold::adjust_money;
use super::super::prelude::*;
use crate::systems::services::commands::output::PlayerCharacter;

/// `GoldSetSubCommand` (registered as `add`, `set` and `remove`).
pub struct GoldSetSubCommand {
    base: SubCommandBase,
}

impl Default for GoldSetSubCommand {
    fn default() -> Self {
        let mut base = SubCommandBase::new(
            "[Gold Set]",
            "Changes to self or a player name or a selected target an specific amount of gold, silver and copper.",
            &format!("{COMMAND_PREFIX}gold <add||set||remove>"),
        );
        let params: Vec<Box<dyn SubCommandParameter>> = vec![
            Box::new(StringSubCommandParameter::new(
                "target",
                Some("player name||target||self"),
                true,
                &[],
            )),
            Box::new(NumericSubCommandParameter::<i32>::new(
                "gold",
                Some("gold amount"),
                true,
            )),
            Box::new(NumericSubCommandParameter::<i32>::new(
                "silver",
                Some("silver amount"),
                false,
            )),
            Box::new(NumericSubCommandParameter::<i32>::new(
                "copper",
                Some("copper amount"),
                false,
            )),
        ];
        for p in params {
            base.add_parameter(p).expect("valid parameter order");
        }
        Self { base }
    }
}

impl SubCommand for GoldSetSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        // `(Character)character`.
        let self_character = ctx.player()?;

        let first_parameter = parameters.require("target")?.to_string();
        let target_character = if first_parameter == "target" {
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
        } else if first_parameter == "self" {
            self_character.clone()
        } else {
            match server.world.character_by_name(&first_parameter) {
                Some(p) => p,
                None => {
                    self.base.send_color_message(
                        out,
                        Color::RED,
                        &format!("Character player: {first_parameter} was not found."),
                    );
                    return Ok(());
                }
            }
        };

        let mut silver_amount = 0;
        let mut copper_amount = 0;
        let multiplier = if trigger == "remove" { -1 } else { 1 };
        let gold_amount = parameters.require("gold")?.as_i32()?;
        if let Some(p) = parameters.get("silver") {
            silver_amount = p.as_i32()?;
        }
        if let Some(p) = parameters.get("copper") {
            copper_amount = p.as_i32()?;
        }

        // C#: unchecked int arithmetic. `set` adds like `add`.
        let total_amount = copper_amount
            .wrapping_mul(multiplier)
            .wrapping_add(silver_amount.wrapping_mul(100).wrapping_mul(multiplier))
            .wrapping_add(gold_amount.wrapping_mul(10_000).wrapping_mul(multiplier));

        if total_amount != 0 {
            adjust_money(&target_character, total_amount);
            self.base.send_message(
                out,
                &format!(
                    "Changed {}'s money by {gold_amount}g {silver_amount}s {copper_amount}c",
                    name_of(&target_character)
                ),
            );
            if is_other(&self_character, &target_character) {
                self.base.send_message_to(
                    &PlayerCharacter(target_character.clone()),
                    out,
                    &format!("[GM] {} has changed your gold", name_of(&self_character)),
                );
            }
        } else {
            self.base
                .send_color_message(out, Color::RED, "No valid amount sum provided");
        }
        Ok(())
    }
}

/// `GoldCmd`.
pub fn gold_cmd() -> RootCommand {
    RootCommand::new(&["gold"], "[Gold]", "Root command to manage gold", "/")
        .register(Arc::new(GoldSetSubCommand::default()), &["add"])
        .register(Arc::new(GoldSetSubCommand::default()), &["set"])
        .register(Arc::new(GoldSetSubCommand::default()), &["remove"])
}

#[cfg(test)]
mod tests {
    use super::super::super::add_gold::tests::money_body;
    use super::super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn gold_add_set_remove() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        gm.p.data().money = 0;
        bob.p.data().money = 0;

        let (_, out) = harness::run(&server, &gm.p, "gold add self 1 2 3").await;
        assert_eq!(out, ["[Gold Set] Changed Gm's money by 1g 2s 3c"]);
        assert_eq!(gm.p.data().money, 10_203);
        assert_eq!(
            gm.packets(),
            vec![(sc::SCItemTaskSuccessPacket, money_body(10_203))]
        );

        // `set` adds as well (C#).
        harness::run(&server, &gm.p, "gold set self 1").await;
        assert_eq!(gm.p.data().money, 20_203);
        let (_, out) = harness::run(&server, &gm.p, "gold remove Bob 2 0 5").await;
        assert_eq!(
            out,
            [
                "[Gold Set] Changed Bob's money by 2g 0s 5c",
                "Target: Bob - [Gold Set] [GM] Gm has changed your gold"
            ]
        );
        assert_eq!(bob.p.data().money, -20_005);
        assert_eq!(bob.chat(), ["[Gold Set] [GM] Gm has changed your gold"]);

        let (_, out) = harness::run(&server, &gm.p, "gold add target 1").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Gold Set] Please select a valid character player|r"]
        );
        gm.p.data().unit.current_target = bob.p.obj_id;
        harness::run(&server, &gm.p, "gold add target 0 1").await;
        assert_eq!(bob.p.data().money, -19_905);
        let (_, out) = harness::run(&server, &gm.p, "gold add nobody 1").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Gold Set] Character player: nobody was not found.|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "gold add self 0").await;
        assert_eq!(out, ["|cFFFF0000[Gold Set] No valid amount sum provided|r"]);
        let (_, out) = harness::run(&server, &gm.p, "gold add self").await;
        assert_eq!(out, ["|cFFFF0000[Gold Set] Parameter gold is required|r"]);

        // The root: help and the call prefix.
        let (_, out) = harness::run(&server, &gm.p, "gold").await;
        assert_eq!(
            out,
            [
                "|cFF7CFC00[Gold] Root command to manage gold|r",
                "|cFF7CFC00[Gold] Supported subcommands: <add||set||remove>|r",
                "|cFF7CFC00[Gold] For more details use /<command> <subcommand> help.|r"
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, "gold add help").await;
        assert_eq!(
            out,
            [
                "|cFF7CFC00[Gold Set] Changes to self or a player name or a selected target an specific amount of gold, silver and copper.|r",
                "|cFF7CFC00[Gold Set] /gold <add||set||remove> <player name||target||self> <gold amount> [silver amount] [copper amount]|r"
            ]
        );
    }
}
