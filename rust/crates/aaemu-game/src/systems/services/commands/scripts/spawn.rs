//! `Scripts/Commands/Spawn.cs`.

use super::prelude::*;
use crate::utils::numeric::deg_to_rad_f32;

/// `Spawn`.
pub struct Spawn;

/// `DUMMY_NPC_TEMPLATE_ID`.
const DUMMY_NPC_TEMPLATE_ID: u32 = 7512;

impl Spawn {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)` (no group
/// separators).
pub(super) fn parse_f32_float_style(text: &str) -> Option<f32> {
    if text.contains(',') {
        return None;
    }
    parse_f32(text)
}

impl Command for Spawn {
    fn command_names(&self) -> Vec<String> {
        names(&["spawn"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.len() < 2 {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        let unit_id = if args[1].to_lowercase() == "dummy" && args[0] == "npc" {
            DUMMY_NPC_TEMPLATE_ID
        } else {
            match try_parse::<u32>(&args[1]) {
                Some(v) => v,
                None => {
                    CommandManager::send_error_text(self, out, "<unitId> parse error");
                    return Ok(());
                }
            }
        };

        if unit_id == 0 {
            CommandManager::send_normal_text(self, out, "unitId can not be 0");
            return Ok(());
        }

        let server = ctx.server()?;
        let character = ctx.player()?;
        let me = WorldObject::from(&character);
        let world = parent_world(server, &character).ok_or_else(nre)?;
        let mut char_pos = clone_detached(server, &me);
        let char_world = clone_detached(server, &me);
        match args[0].as_str() {
            "remove" => match world.doodad(unit_id) {
                Some(my_doodad) => {
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!("Removing Doodad with ID {}", my_doodad.obj_id),
                    );
                    server.ids.object.release_id(my_doodad.obj_id);
                    crate::systems::npcs::spawn_manager::delete_object(
                        server,
                        &WorldObject::from(&my_doodad),
                        None,
                    );
                }
                None => CommandManager::send_error_text(
                    self,
                    out,
                    &format!("Doodad with Id {unit_id} Does not exist"),
                ),
            },
            "npc" => {
                if !server.game_data.systems.npcs.npc.exist(unit_id) {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("NPC {unit_id} don't exist|r"),
                    );
                    return Ok(());
                }

                add_distance_to_front(&mut char_pos, 3.0);
                let mut angle = angle_from(&char_pos, &char_world) as f32;
                let mut position = spawn_position(&char_pos);

                match args.get(2).and_then(|a| parse_f32_float_style(a)) {
                    Some(new_rot_z) => {
                        angle = deg_to_rad_f32(new_rot_z);
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Spawn NPC {unit_id} using angle {}° = {} rad",
                                f32s(new_rot_z),
                                f32s(angle)
                            ),
                        );
                    }
                    None => {
                        angle = deg_to_rad_f32(angle);
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Spawn NPC {unit_id} facing you using angle {} rad",
                                f32s(angle)
                            ),
                        );
                    }
                }

                position.yaw = angle;
                position.pitch = 0.0;
                position.roll = 0.0;

                let npc_spawner = add_runtime_npc_spawner(server, &world, unit_id, position)?;
                npc_spawner.spawn_all(server, false);
            }
            "doodad" => {
                if !server.game_data.systems.npcs.doodad.exist(unit_id) {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Doodad {unit_id} don't exist"),
                    );
                    return Ok(());
                }

                add_distance_to_front(&mut char_pos, 3.0);
                let mut angle = angle_from(&char_pos, &char_world) as f32;
                let mut position = spawn_position(&char_pos);
                match args.get(2).and_then(|a| parse_f32_float_style(a)) {
                    Some(degrees) => {
                        angle = deg_to_rad_f32(degrees);
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Spawn Doodad {unit_id} using user provided angle {}° = {} rad",
                                f32s(degrees),
                                f32s(angle)
                            ),
                        );
                    }
                    None => {
                        angle = deg_to_rad_f32(angle);
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Spawn Doodad {unit_id} facing you, using characters angle {}",
                                f32s(angle)
                            ),
                        );
                    }
                }

                position.yaw = angle;
                position.pitch = 0.0;
                position.roll = 0.0;
                let spawner = new_doodad_spawner(world.id, unit_id, position);
                spawner.spawn_for_character(server, 0, 0, character.obj_id);
            }
            _ => {}
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<npc||doodad||remove> <unitId> [rotationZ]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Spawns a npc or doodad using <unitId> as a template. Or remove a doodad".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn spawn_doodad_npc_and_remove() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let world = server.world.world(0).unwrap();

        let (_, out) =
            harness::run(&server, &gm.p, &format!("spawn doodad {} 90", ts::DOODAD)).await;
        assert_eq!(
            out,
            [harness::normal(
                "spawn",
                &format!(
                    "Spawn Doodad {} using user provided angle 90° = 1.5707964 rad",
                    ts::DOODAD
                )
            )]
        );
        let doodads = world.all_doodads();
        assert_eq!(doodads.len(), 1);
        let d = doodads[0].clone();
        {
            let t = d.data().transform;
            // 3 m in front of a yaw-0 character (+Y).
            assert!((t.x - 100.0).abs() < 1e-4 && (t.y - 103.0).abs() < 1e-4);
            assert!((t.rot_z - std::f32::consts::FRAC_PI_2).abs() < 1e-6);
        }

        // NPC: the spawner is registered; the NPC appears on the active-region tick.
        let (_, out) = harness::run(&server, &gm.p, &format!("spawn npc {}", ts::NPC)).await;
        assert_eq!(
            out,
            [harness::normal(
                "spawn",
                &format!(
                    "Spawn NPC {} facing you using angle -1.5707964 rad",
                    ts::NPC
                )
            )]
        );
        crate::systems::npcs::spawn_manager::active_region_tick(&server);
        let npcs = world.all_npcs();
        assert_eq!(npcs.len(), 1);
        assert_eq!(npcs[0].data().unit.template_id, ts::NPC);

        let (_, out) = harness::run(&server, &gm.p, &format!("spawn remove {}", d.obj_id)).await;
        assert_eq!(
            out,
            [harness::normal(
                "spawn",
                &format!("Removing Doodad with ID {}", d.obj_id)
            )]
        );
        assert!(world.doodad(d.obj_id).is_none());

        let (_, out) = harness::run(&server, &gm.p, "spawn npc 99999").await;
        assert_eq!(out, [harness::error("spawn", "NPC 99999 don't exist|r")]);
        let (_, out) = harness::run(&server, &gm.p, "spawn doodad 99999").await;
        assert_eq!(out, [harness::error("spawn", "Doodad 99999 don't exist")]);
        let (_, out) = harness::run(&server, &gm.p, "spawn npc 0").await;
        assert_eq!(out, [harness::normal("spawn", "unitId can not be 0")]);
        let (_, out) = harness::run(&server, &gm.p, "spawn npc x").await;
        assert_eq!(out, [harness::error("spawn", "<unitId> parse error")]);
    }
}
