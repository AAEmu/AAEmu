//! `Scripts/Commands/AddLabor.cs`.

use super::prelude::*;
use crate::systems::skills::progress;

/// `AddLabor`.
pub struct AddLabor;

impl AddLabor {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddLabor {
    fn command_names(&self) -> Vec<String> {
        names(&["labor", "addlabor", "add_labor"])
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

        let amount = args
            .get(first_arg)
            .and_then(|a| try_parse::<i16>(a))
            .unwrap_or(0);
        let vocation_skill_id = args
            .get(first_arg + 1)
            .and_then(|a| try_parse::<i32>(a))
            .unwrap_or(0);

        // C#: `Actability.Actabilities[(uint)actabilityId]` throws for an unknown id.
        if vocation_skill_id > 0
            && !target
                .data()
                .actability
                .actabilities
                .iter()
                .any(|a| a.id == vocation_skill_id as u32)
        {
            anyhow::bail!(
                "The given key '{}' was not present in the dictionary.",
                vocation_skill_id as u32
            );
        }
        progress::change_labor(server, &target, amount, vocation_skill_id);
        if is_other(&character, &target) {
            CommandManager::send_normal_text(
                self,
                out,
                &format!("added {amount} labor to {}", name_of(&target)),
            );
            send_player_message(
                &target,
                &format!(
                    "[GM] {} has changed your labor by {amount}",
                    name_of(&character)
                ),
            );
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) <amount> [vocationSkillId]".into()
    }

    fn get_command_help_text(&self) -> String {
        // Optional TODO: Add the values by extracting them from actability_groups ?
        concat!(
            "Add or remove <amount> of labor. If [vocationSkillId] is provided, then target vocation skill also gains a amount of points.\n",
            "(1)Alchemy, (2)Construction, (3)Cooking, (4)Handicrafts, (5)Husbandry, (6)Farming, (7)Fishing, (8)Logging, (9)Gathering, (10)Machining, ",
            "(11)Metalwork, (12)Printing, (13)Mining, (14)Masonry, (15)Tailoring, (16)Leatherwork, (17)Weaponry, (18)Carpentry, (20)Larceny, ",
            "(21)Nuian, (22)Elven, (23)Dwarven, (25)Harani, (26)Firran, (27)Warborn, (29)Nuia Dialect, (30)Haranya Dialect, ",
            "(31)Commerce, (33)Artistry, (34)Exploration"
        )
        .into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn labor_changes_labor_points() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        gm.p.data().labor_power = 10;
        bob.p.data().labor_power = 10;

        let (_, out) = harness::run(&server, &gm.p, "labor 100").await;
        assert!(out.is_empty());
        assert_eq!(gm.p.data().labor_power, 110);
        // SCCharacterLaborPowerChangedPacket(100, 0, 0, 0)
        let mut body = 100i32.to_le_bytes().to_vec();
        body.extend(0i32.to_le_bytes());
        body.extend(0i32.to_le_bytes());
        body.push(0);
        assert_eq!(
            gm.packets(),
            vec![(sc::SCCharacterLaborPowerChangedPacket, body)]
        );

        let (_, out) = harness::run(&server, &gm.p, "add_labor Bob 5").await;
        assert_eq!(out, ["[labor] added 5 labor to Bob"]);
        assert_eq!(bob.p.data().labor_power, 15);
        assert_eq!(bob.chat(), ["[GM] Gm has changed your labor by 5"]);

        // Out of short range: 0.
        harness::run(&server, &gm.p, "labor 40000").await;
        assert_eq!(gm.p.data().labor_power, 110);

        // Unknown vocation skill: the C# KeyNotFoundException.
        gm.packets();
        harness::run(&server, &gm.p, "labor 1 999").await;
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000The given key '999' was not present in the dictionary.|r"]
        );
        assert_eq!(gm.p.data().labor_power, 110);
    }
}
