//! `Scripts/Commands/MoveAll.cs`.

use super::prelude::*;

/// `TeleportReason.Gm`.
const TELEPORT_REASON_GM: u8 = 12;

/// `MoveAll`.
pub struct MoveAll;

impl MoveAll {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for MoveAll {
    fn command_names(&self) -> Vec<String> {
        names(&["moveall", "move_all"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        for other_char in server.world.all_characters() {
            if is_other(&other_char, &character) {
                let me = world_pos(server, &WorldObject::from(&character));
                teleport_player(&other_char, TELEPORT_REASON_GM, me.x, me.y, me.z + 1.0, 0.0);
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Moves every player to your location".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::move_cmd::tests::teleport_body;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn move_all_teleports_everyone_else() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let mut a = harness::pc(&server, 2, "Ann", 900.0, 900.0);
        let mut b = harness::pc(&server, 3, "Ben", 1500.0, 1500.0);
        let (handled, out) = harness::run(&server, &gm.p, "move_all").await;
        assert!(handled);
        assert!(out.is_empty());
        for pc in [&mut a, &mut b] {
            assert!(pc.p.data().disabled_set_position);
            assert_eq!(
                pc.frames(),
                vec![(
                    sc::SCTeleportUnitPacket,
                    teleport_body(12, 100.0, 200.0, 101.0, 0.0)
                )]
            );
        }
        assert!(gm.frames().is_empty());
        assert!(!gm.p.data().disabled_set_position);
    }
}
