//! `Scripts/Commands/AddBadges.cs`.

use super::prelude::*;
use crate::systems::skills::progress;

/// `GamePointKind` 1 (`Vocation`).
const GAME_POINT_KIND_VOCATION: u8 = 1;

/// `AddBadges`.
pub struct AddBadges;

impl AddBadges {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddBadges {
    fn command_names(&self) -> Vec<String> {
        names(&["vocation", "vocationpoints", "add_vp", "add_vb"])
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

        let vp_to_add = try_parse::<i32>(arg_at(args, first_arg)?).unwrap_or(0);
        if vp_to_add != 0 {
            progress::change_game_points(server, &target, GAME_POINT_KIND_VOCATION, vp_to_add);
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) <VocationPoints>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Adds VocationPoints (to target player)".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn vocation_points_are_added() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        let (_, out) = harness::run(&server, &gm.p, "vocation 100").await;
        assert!(out.is_empty());
        assert_eq!(gm.p.data().vocation_point, 100);
        // SCGamePointChangedPacket(kind 1, amount 100)
        let mut body = vec![1u8];
        body.extend(100i32.to_le_bytes());
        assert_eq!(
            gm.packets(),
            vec![(sc::SCGamePointChangedPacket, body.clone())]
        );

        let (_, out) = harness::run(&server, &gm.p, "add_vp bob -40").await;
        assert!(out.is_empty());
        assert_eq!(bob.p.data().vocation_point, -40);
        assert_eq!(bob.packets().len(), 1);
        // Unparsable amount: 0, nothing changes.
        harness::run(&server, &gm.p, "vocation x").await;
        assert_eq!(gm.p.data().vocation_point, 100);
        assert!(gm.packets().is_empty());
        let (_, out) = harness::run(&server, &gm.p, "vocation").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/vocation|r |cFFEEEEAA(target) <VocationPoints>|r\n|cFF888888Adds VocationPoints (to target player)|r"]
        );
    }
}
