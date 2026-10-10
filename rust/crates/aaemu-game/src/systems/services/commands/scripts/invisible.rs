//! `Scripts/Commands/Invisible.cs`.

use super::prelude::*;
use crate::systems::skills::combat;

/// `Invisible`.
pub struct Invisible;

impl Invisible {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Invisible {
    fn command_names(&self) -> Vec<String> {
        names(&["invisible"])
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
            let character = ctx.player()?;
            combat::set_invisible(ctx.server()?, &WorldObject::from(&character), value);
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<true||false>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Sets yourself as invisible to other players".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn invisible_broadcasts_the_state() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut near = harness::pc(&server, 2, "Near", 105.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "invisible true").await;
        assert!(out.is_empty());
        assert!(gm.p.data().unit.invisible);
        let expected = (sc::SCUnitInvisiblePacket, vec![0x01, 0x01, 0x00, 1]);
        assert_eq!(gm.packets().last(), Some(&expected));
        assert_eq!(near.packets().last(), Some(&expected));
        harness::run(&server, &gm.p, "invisible x").await;
        assert!(gm.p.data().unit.invisible);
        harness::run(&server, &gm.p, "invisible false").await;
        assert!(!gm.p.data().unit.invisible);
    }
}
