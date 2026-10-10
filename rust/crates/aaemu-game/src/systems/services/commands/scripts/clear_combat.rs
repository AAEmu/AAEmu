//! `Scripts/Commands/ClearCombat.cs`.

use super::prelude::*;
use crate::systems::skills::packets::SCCombatClearedPacket;

/// `ClearCombat`.
pub struct ClearCombat;

impl ClearCombat {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ClearCombat {
    fn command_names(&self) -> Vec<String> {
        names(&["clearcombat", "clear_combat", "cc"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let character = ctx.player()?;
        character.send(SCCombatClearedPacket {
            obj_id: character.obj_id,
        });
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Force sends a clear combat packet. Does not actually clear the combat flag on the server!"
            .into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn cc_sends_combat_cleared_to_self() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        gm.p.data().unit.is_in_battle = true;
        let (handled, out) = harness::run(&server, &gm.p, "cc").await;
        assert!(handled);
        assert!(out.is_empty());
        // SCCombatClearedPacket(bc obj id 0x101)
        assert_eq!(
            gm.packets(),
            vec![(sc::SCCombatClearedPacket, vec![0x01, 0x01, 0x00])]
        );
        // The server flag stays set.
        assert!(gm.p.data().unit.is_in_battle);
    }
}
