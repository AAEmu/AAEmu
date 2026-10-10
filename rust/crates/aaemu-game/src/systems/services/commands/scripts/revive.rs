//! `Scripts/Commands/Revive.cs`.

use super::prelude::*;
use crate::packets::world::SCUnitPointsPacket;
use crate::systems::skills::combat;
use crate::systems::skills::packets_special::SCCharacterResurrectedPacket;
use crate::systems::skills::types::KillReason;

/// `Revive`.
pub struct Revive;

impl Revive {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Revive {
    fn command_names(&self) -> Vec<String> {
        names(&["revive"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        // `GetTargetOrSelf` never returns null: the "Cannot revive this target" branch of the
        // C# is unreachable.
        let (target, _) = get_target_or_self(server, &character, args.first().map(String::as_str));
        let obj = WorldObject::from(&target);
        let hp = target.data().unit.hp;
        if hp == 0 {
            let (hp, mp) = {
                let mut c = target.data();
                c.unit.hp = c.unit.max_hp;
                c.unit.mp = c.unit.max_mp;
                (c.unit.hp, c.unit.mp)
            };
            let world_pos = match parent_world(server, &target) {
                Some(w) => w.world_pos_rot(&obj),
                None => obj.transform().local(),
            };
            crate::systems::skills::broadcast(
                server,
                &obj,
                &SCCharacterResurrectedPacket {
                    unit_id: target.obj_id,
                    x: world_pos.x,
                    y: world_pos.y,
                    z: world_pos.z,
                    z_rot: world_pos.rot_z,
                },
                true,
            );
            crate::systems::skills::broadcast(
                server,
                &obj,
                &SCUnitPointsPacket {
                    obj_id: target.obj_id,
                    hp,
                    mp,
                },
                true,
            );
            combat::post_update_current_hp(server, &obj, Some(&obj), 0, hp, KillReason::Unknown);
        } else {
            ctx.character.send_message("Target is already alive");
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target)".into()
    }

    fn get_command_help_text(&self) -> String {
        "Revives target".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn revive_resurrects_dead_players() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        let (_, out) = harness::run(&server, &gm.p, "revive").await;
        assert!(out.is_empty());
        assert_eq!(gm.chat(), ["Target is already alive"]);

        {
            let mut c = bob.p.data();
            c.unit.hp = 0;
            c.unit.mp = 0;
        }
        harness::run(&server, &gm.p, "revive bob").await;
        assert_eq!(
            {
                let c = bob.p.data();
                (c.unit.hp, c.unit.mp)
            },
            (5_000, 1_000)
        );
        let ops: Vec<u16> = bob.packets().into_iter().map(|(op, _)| op).collect();
        assert_eq!(
            ops,
            [sc::SCCharacterResurrectedPacket, sc::SCUnitPointsPacket]
        );
    }
}
