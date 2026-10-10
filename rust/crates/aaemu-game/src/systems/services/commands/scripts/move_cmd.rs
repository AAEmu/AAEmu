//! `Scripts/Commands/Move.cs`.

use super::super::output::PlayerCharacter;
use super::prelude::*;

/// `Move`.
pub struct Move;

impl Move {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Move {
    fn command_names(&self) -> Vec<String> {
        names(&["move"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut target_player = character.clone();
        let mut first_arg = 0;
        if !args.is_empty() {
            (target_player, first_arg) =
                get_target_or_self(server, &character, Some(args[0].as_str()));
        }

        let move_to_me = is_other(&target_player, &character)
            && args.len() == 2
            && args[1].to_lowercase() == "tome";

        if !move_to_me && args.len() < first_arg + 3 && !is_other(&target_player, &character) {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        if move_to_me {
            let me = world_pos(server, &WorldObject::from(&character));
            let my_x = me.x;
            let my_y = me.y;
            let my_z = me.z + 2.0; // drop them slightly above you to avoid weird collision stuff
            if is_other(&target_player, &character) {
                PlayerCharacter(target_player.clone()).send_message(&format!(
                    "[GM] |cFFFFFFFF{}|r has called upon your presence !",
                    name_of(&character)
                ));
            }

            teleport_player(&target_player, 0, my_x, my_y, my_z, 0.0);
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "Moved |cFFFFFFFF{}|r to your location.",
                    name_of(&target_player)
                ),
            );
            return Ok(());
        }

        if args.len() == first_arg && is_other(&target_player, &character) {
            let t = world_pos(server, &WorldObject::from(&target_player));
            let target_x = t.x;
            let target_y = t.y;
            let target_z = t.z + 2.0; // drop me slightly above them to avoid weird collision stuff
            teleport_player(&character, 0, target_x, target_y, target_z, 0.0);
            CommandManager::send_normal_text(
                self,
                out,
                &format!("Moved to |cFFFFFFFF{}|r.", name_of(&target_player)),
            );
            return Ok(());
        }

        let parsed = if args.len() == first_arg + 3 {
            match (
                parse_f32(&args[first_arg]),
                parse_f32(&args[first_arg + 1]),
                parse_f32(&args[first_arg + 2]),
            ) {
                (Some(x), Some(y), Some(z)) => Some((x, y, z)),
                _ => None,
            }
        } else {
            None
        };
        if let Some((new_x, new_y, new_z)) = parsed {
            let (sx, sy, sz) = (f32s(new_x), f32s(new_y), f32s(new_z));
            if is_other(&target_player, &character) {
                PlayerCharacter(target_player.clone()).send_message(&format!(
                    "[GM] |cFFFFFFFF{}|r has moved you do position X: {sx}, Y: {sy}, Z: {sz}",
                    name_of(&character)
                ));
            }

            teleport_player(&target_player, 0, new_x, new_y, new_z, 0.0);
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "|cFFFFFFFF{}|r moved to X: {sx}, Y: {sy}, Z: {sz}",
                    name_of(&target_player)
                ),
            );
        } else {
            CommandManager::send_error_text(self, out, "Position parse error");
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) [<x> <y> <z>||ToMe]".into()
    }

    fn get_command_help_text(&self) -> String {
        let n = &self.command_names()[0];
        format!(
            "Move yourself or (target) player to position <x> <y> <z>.\n\
             You can also use \"ToMe\" instead of coordinates to teleport (target) to your location.\n\
             Or you can only specify a target player to move to that player.\n\
             Examples:\n\
             {COMMAND_PREFIX}{n} 21000 12500 200\n\
             {COMMAND_PREFIX}{n} TargetPlayer 21000 12500 200\n\
             {COMMAND_PREFIX}{n} TargetPlayer ToMe\n\
             {COMMAND_PREFIX}{n} TargetPlayer"
        )
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use aaemu_commons::PacketStream;

    use super::super::harness;
    use crate::opcodes::sc;

    /// An `SCTeleportUnitPacket(reason, 0, x, y, z, z2)` body.
    pub(crate) fn teleport_body(reason: u8, x: f32, y: f32, z: f32, z2: f32) -> Vec<u8> {
        let mut s = PacketStream::new();
        s.write_u8(reason)
            .write_i16(0)
            .write_position(x, y, z)
            .write_f32(z2);
        s.into_bytes()
    }

    #[tokio::test]
    async fn move_self_target_and_to_me() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);

        // Own coordinates
        let (handled, out) = harness::run(&server, &gm.p, "move 1000 1200.5 50").await;
        assert!(handled);
        assert_eq!(
            out,
            [harness::normal(
                "move",
                "|cFFFFFFFFGm|r moved to X: 1000, Y: 1200.5, Z: 50"
            )]
        );
        assert!(gm.p.data().disabled_set_position);
        assert_eq!(
            gm.packets(),
            vec![(
                sc::SCTeleportUnitPacket,
                teleport_body(0, 1000.0, 1200.5, 50.0, 0.0)
            )]
        );

        // Another player to coordinates (they are told)
        let (_, out) = harness::run(&server, &gm.p, "move bob 10 20 30").await;
        assert_eq!(
            out,
            [harness::normal(
                "move",
                "|cFFFFFFFFBob|r moved to X: 10, Y: 20, Z: 30"
            )]
        );
        let frames = bob.frames();
        assert_eq!(frames.len(), 2);
        assert_eq!(
            harness::chat_text(&frames[0].1),
            "[GM] |cFFFFFFFFGm|r has moved you do position X: 10, Y: 20, Z: 30"
        );
        assert_eq!(
            frames[1],
            (
                sc::SCTeleportUnitPacket,
                teleport_body(0, 10.0, 20.0, 30.0, 0.0)
            )
        );

        // ToMe (any case)
        let (_, out) = harness::run(&server, &gm.p, "move Bob TOME").await;
        assert_eq!(
            out,
            [harness::normal(
                "move",
                "Moved |cFFFFFFFFBob|r to your location."
            )]
        );
        let frames = bob.frames();
        assert_eq!(
            harness::chat_text(&frames[0].1),
            "[GM] |cFFFFFFFFGm|r has called upon your presence !"
        );
        assert_eq!(
            frames[1],
            (
                sc::SCTeleportUnitPacket,
                teleport_body(0, 100.0, 200.0, 102.0, 0.0)
            )
        );

        // Only a target: move to them
        let (_, out) = harness::run(&server, &gm.p, "move bob").await;
        assert_eq!(out, [harness::normal("move", "Moved to |cFFFFFFFFBob|r.")]);
        assert_eq!(
            gm.packets(),
            vec![(
                sc::SCTeleportUnitPacket,
                teleport_body(0, 900.0, 900.0, 102.0, 0.0)
            )]
        );

        // Parse error / help
        let (_, out) = harness::run(&server, &gm.p, "move 1 2 x").await;
        assert_eq!(out, [harness::error("move", "Position parse error")]);
        let (_, out) = harness::run(&server, &gm.p, "move 1 2").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/move|r |cFFEEEEAA(target) [<x> <y> <z>||ToMe]|r\n|cFF888888Move yourself or (target) player to position <x> <y> <z>.\nYou can also use \"ToMe\" instead of coordinates to teleport (target) to your location.\nOr you can only specify a target player to move to that player.\nExamples:\n/move 21000 12500 200\n/move TargetPlayer 21000 12500 200\n/move TargetPlayer ToMe\n/move TargetPlayer|r"]
        );
    }
}
