//! `Scripts/Commands/Nudge.cs`.

use super::prelude::*;
use crate::utils::numeric::deg_to_rad_f32;

/// `TeleportReason.Gm`.
const TELEPORT_REASON_GM: u8 = 12;

/// `Nudge`.
pub struct Nudge;

impl Nudge {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Nudge {
    fn command_names(&self) -> Vec<String> {
        names(&["nudge"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let mut distance = 5f32;
        // C#: the distance argument is only read when more than one argument is given
        // (`args.Length > 1 && !float.TryParse(args[0], ...)`), `/nudge 10` moves 5 m.
        if args.len() > 1 {
            match parse_f32(&args[0]) {
                Some(d) => distance = d,
                None => {
                    CommandManager::send_error_text(self, out, "Distance parse error");
                    return Ok(());
                }
            }
        }

        let server = ctx.server()?;
        let character = ctx.player()?;
        let obj = WorldObject::from(&character);
        // DisabledSetPosition first: FinalizeTransform then skips AddVisibleObject.
        character.data().disabled_set_position = true;
        // Transform.Local.AddDistanceToFront(distance, false)
        obj.with_transform(|t| {
            let (sin, cos) = t.rot_z.sin_cos();
            t.x += -distance * sin;
            t.y += distance * cos;
        });
        let w = world_pos(server, &obj);
        // C#: `World.Rotation.Z` is already in radians, `DegToRad()` converts it again.
        teleport_player(
            &character,
            TELEPORT_REASON_GM,
            w.x,
            w.y,
            w.z,
            deg_to_rad_f32(w.rot_z),
        );
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(distance)".into()
    }

    fn get_command_help_text(&self) -> String {
        let n = &self.command_names()[0];
        format!(
            "Move yourself forward by a given distance (of 5m by default).\n\
             Examples:\n\
             {COMMAND_PREFIX}{n}\n\
             {COMMAND_PREFIX}{n} 10"
        )
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::move_cmd::tests::teleport_body;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn nudge_moves_forward() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        // facing +Y (yaw 0): AddDistanceToFront adds (−d·sin, d·cos)
        let (handled, out) = harness::run(&server, &gm.p, "nudge").await;
        assert!(handled);
        assert!(out.is_empty());
        assert_eq!(gm.p.data().unit.transform.y, 205.0);
        assert!(gm.p.data().disabled_set_position);
        assert_eq!(
            gm.packets(),
            vec![(
                sc::SCTeleportUnitPacket,
                teleport_body(12, 100.0, 205.0, 100.0, 0.0)
            )]
        );
        // One argument is ignored (C#), two use the first
        harness::run(&server, &gm.p, "nudge 10").await;
        assert_eq!(gm.p.data().unit.transform.y, 210.0);
        harness::run(&server, &gm.p, "nudge 10 x").await;
        assert_eq!(gm.p.data().unit.transform.y, 220.0);
        gm.packets();
        // facing -X (yaw 90°): moves along -X, z2 = DegToRad(yaw in radians)
        let yaw = std::f32::consts::FRAC_PI_2;
        gm.p.data().unit.transform.rot_z = yaw;
        harness::run(&server, &gm.p, "nudge 2 2").await;
        let t = gm.p.data().unit.transform;
        assert_eq!(t.x, 100.0 - 2.0 * yaw.sin());
        assert_eq!(
            gm.packets(),
            vec![(
                sc::SCTeleportUnitPacket,
                teleport_body(12, t.x, t.y, 100.0, std::f32::consts::PI / 180.0 * yaw)
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "nudge x y").await;
        assert_eq!(out, [harness::error("nudge", "Distance parse error")]);
    }
}
