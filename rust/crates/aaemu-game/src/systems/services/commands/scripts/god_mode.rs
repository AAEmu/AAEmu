//! `Scripts/Commands/GodMode.cs`.

use super::prelude::*;

/// `Unit.SetGodMode(value)`: `AppConfiguration.Instance.World.GodMode = value` (and the copy
/// the skills subsystem reads in `ReduceCurrentHp`).
pub fn set_god_mode(server: &GameServer, value: bool) {
    server.config.update(|c| c.world.god_mode = value);
    let mut cfg = server.systems.skills.config();
    cfg.god_mode = value;
    server.systems.skills.set_config(cfg);
}

/// `GodMode`.
pub struct GodMode;

impl GodMode {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for GodMode {
    fn command_names(&self) -> Vec<String> {
        names(&["godmode"])
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
        if let Some(value) = parse_bool(&args[0]) {
            set_god_mode(ctx.server()?, value);
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<true||false>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Makes himself immortal to other players".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn godmode_toggles_the_world_flag() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "godmode true").await;
        assert!(out.is_empty());
        assert!(server.config.world.god_mode);
        assert!(server.systems.skills.config().god_mode);
        // Damage is ignored now.
        harness::run(&server, &gm.p, "damage 100").await;
        assert_eq!(gm.p.data().unit.hp, 5_000);
        harness::run(&server, &gm.p, "godmode nope").await;
        assert!(server.config.world.god_mode);
        harness::run(&server, &gm.p, "godmode False").await;
        assert!(!server.config.world.god_mode);
        assert!(!server.systems.skills.config().god_mode);
        let (_, out) = harness::run(&server, &gm.p, "godmode").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/godmode|r |cFFEEEEAA<true||false>|r\n|cFF888888Makes himself immortal to other players|r"]
        );
    }
}
