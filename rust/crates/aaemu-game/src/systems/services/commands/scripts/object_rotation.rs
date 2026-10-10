//! `Scripts/Commands/ObjectRotation.cs`.

use super::object_position::axis_value;
use super::prelude::*;
use crate::utils::numeric::{deg_to_rad_f32, rad_to_deg_f32};

/// `ObjectRotation`.
pub struct ObjectRotation;

impl ObjectRotation {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ObjectRotation {
    fn command_names(&self) -> Vec<String> {
        names(&["setrot", "npcrot", "doodadrot"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.len() < 4 {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        let Some(id) = try_parse::<u32>(&args[0]) else {
            CommandManager::send_error_text(self, out, "ObjId parse error");
            return Ok(());
        };
        let server = ctx.server()?;
        let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
        let Some(game_object) = world.object(id) else {
            CommandManager::send_error_text(self, out, &format!("ObjId: {id} not found"));
            return Ok(());
        };

        let t = game_object.transform();
        let old_x = rad_to_deg_f32(t.rot_x);
        let old_y = rad_to_deg_f32(t.rot_y);
        let old_z = rad_to_deg_f32(t.rot_z);
        let (mut roll, mut pitch, mut yaw) = (old_x, old_y, old_z);
        if let Some(v) = axis_value(&args[1], "x") {
            roll = v;
        }
        if let Some(v) = axis_value(&args[2], "y") {
            pitch = v;
        }
        if let Some(v) = axis_value(&args[3], "z") {
            yaw = v;
        }

        // Local.SetRotationDegree(roll, pitch, yaw)
        game_object.with_transform(|t| {
            t.rot_x = deg_to_rad_f32(roll);
            t.rot_y = deg_to_rad_f32(pitch);
            t.rot_z = deg_to_rad_f32(yaw);
        });

        hide_show(server, &world, &game_object);
        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "ObjId: r{}, p{}, y{} => r{}, p{}, y{}",
                f32s(old_x),
                f32s(old_y),
                f32s(old_z),
                f32s(roll),
                f32s(pitch),
                f32s(yaw)
            ),
        );
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(ObjID) <x> <y> <z> (can use x y z or * instead of a value to keep current angle)".into()
    }

    fn get_command_help_text(&self) -> String {
        "Change a object's local rotation in degrees".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn setrot_rotates_any_object() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let d = ts::spawn_doodad(&server, ts::DOODAD, 102.0, 100.0);
        let (_, out) =
            harness::run(&server, &gm.p, &format!("doodadrot {} * y 90", d.obj_id)).await;
        assert_eq!(
            out,
            [harness::normal(
                "setrot",
                "ObjId: r0, p0, y0 => r0, p0, y90"
            )]
        );
        let rz = d.data().transform.rot_z;
        assert!((rz - std::f32::consts::FRAC_PI_2).abs() < 1e-6);
        let (_, out) = harness::run(&server, &gm.p, "setrot 77 1 2 3").await;
        assert_eq!(out, [harness::error("setrot", "ObjId: 77 not found")]);
    }
}
