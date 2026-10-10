//! `Scripts/Commands/Dist.cs`.

use super::prelude::*;
use crate::systems::skills::geometry::{distance_to, Target};
use crate::utils::math::{
    calculate_angle_from, calculate_angle_from_objects, calculate_distance, clamp_deg_angle,
};

/// `Dist`.
pub struct Dist;

impl Dist {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Dist {
    fn command_names(&self) -> Vec<String> {
        names(&["distance", "dist"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let Some(mut target) = current_target(server, &character) else {
            CommandManager::send_normal_text(self, out, "No target selected");
            return Ok(());
        };

        if let Some(arg) = args.first() {
            match try_parse::<u32>(arg) {
                Some(target_obj_id) => {
                    // `go is BaseUnit bu` (every world object is a BaseUnit).
                    if let Some(go) =
                        parent_world(server, &character).and_then(|w| w.object(target_obj_id))
                    {
                        target = go;
                    }
                }
                None => {
                    CommandManager::send_error_text(self, out, "[objId] parse error");
                    return Ok(());
                }
            }
        }

        let me = WorldObject::from(&character);
        let a = world_pos(server, &me);
        let b = world_pos(server, &target);
        let (pa, pb) = ([a.x, a.y, a.z], [b.x, b.y, b.z]);
        let raw_distance = calculate_distance(pa, pb, false);
        let raw_distance_z = calculate_distance(pa, pb, true);
        let (from, to) = (Target::Obj(me.clone()), Target::Obj(target.clone()));
        let model_distance = distance_to(server, &from, &to, false);
        let model_distance_z = distance_to(server, &from, &to, true);
        let angle_world = clamp_deg_angle(calculate_angle_from(a.x, a.y, b.x, b.y) - 90.0);
        let angle = clamp_deg_angle(calculate_angle_from_objects(pa, a.rot_z, pb));

        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "\nRaw distance : {}\nRaw distance (Z) : {}\nModel adjusted distance: {}\nModel adjusted distance (Z): {}\nWorld Angle: {}°\nRelative Angle: {}°",
                f32s(raw_distance),
                f32s(raw_distance_z),
                f32s(model_distance),
                f32s(model_distance_z),
                fixed(angle_world, 1),
                fixed(angle, 1)
            ),
        );
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[objId]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Gets distance using various calculations with target".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn dist_reports_distances_and_angles() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "dist").await;
        assert_eq!(out, [harness::normal("distance", "No target selected")]);

        let npc = ts::spawn_npc(&server, ts::NPC, 103.0, 104.0);
        gm.p.data().unit.current_target = npc.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "distance").await;
        assert_eq!(
            out,
            [harness::normal(
                "distance",
                "\nRaw distance : 5\nRaw distance (Z) : 5\nModel adjusted distance: 5\nModel adjusted distance (Z): 5\nWorld Angle: -36.9°\nRelative Angle: -36.9°"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "dist abc").await;
        assert_eq!(out, [harness::error("distance", "[objId] parse error")]);
    }
}
