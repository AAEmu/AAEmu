//! `Scripts/Commands/Despawn.cs`.

use super::prelude::*;
use crate::systems::npcs::spawn_manager;

/// `Despawn`.
pub struct Despawn;

impl Despawn {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `ObjectIdManager.Instance.ReleaseId(obj.ObjId); obj.Delete();`
fn release_and_delete(server: &GameServer, obj: &WorldObject) {
    server.ids.object.release_id(obj.obj_id());
    spawn_manager::delete_object(server, obj, None);
}

impl Command for Despawn {
    fn command_names(&self) -> Vec<String> {
        names(&["despawn"])
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

        let action = args[0].as_str();
        let mut radius = 30f32;
        let use_radius = args.len() >= 3;
        let Some(unit_id) = try_parse::<u32>(&args[1]) else {
            if use_radius {
                CommandManager::send_error_text(self, out, "<templateId> parse error");
            } else {
                CommandManager::send_error_text(self, out, "<objId> parse error");
            }
            return Ok(());
        };

        if args.len() >= 3 {
            match parse_f32(&args[2]) {
                Some(r) => radius = r,
                None => {
                    CommandManager::send_error_text(self, out, "<radius> parse error");
                    return Ok(());
                }
            }
        }

        let server = ctx.server()?;
        let character = ctx.player()?;
        if !use_radius {
            let world = parent_world(server, &character).ok_or_else(nre)?;
            match action {
                "doodad" => match world.doodad(unit_id) {
                    Some(my_doodad) => {
                        let template_id = my_doodad.data().template_id;
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Removing Doodad with ID {} - @DOODAD_NAME({template_id})",
                                my_doodad.obj_id
                            ),
                        );
                        release_and_delete(server, &WorldObject::from(&my_doodad));
                    }
                    None => CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Doodad with Id {unit_id} does not exist"),
                    ),
                },
                "npc" => match world.npc(unit_id) {
                    Some(my_npc) => {
                        let template_id = my_npc.data().unit.template_id;
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Removing NPC with ID {} - @NPC_NAME({template_id})",
                                my_npc.obj_id
                            ),
                        );
                        release_and_delete(server, &WorldObject::from(&my_npc));
                    }
                    None => CommandManager::send_error_text(
                        self,
                        out,
                        &format!("NPC with objectId {unit_id} does not exist"),
                    ),
                },
                "unit" => match world.object(unit_id) {
                    Some(my_unit) => {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!("Removing Transfer with ID {}", my_unit.obj_id()),
                        );
                        release_and_delete(server, &my_unit);
                    }
                    None => CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Unit with objectId {unit_id} does not exist"),
                    ),
                },
                _ => {
                    CommandManager::send_error_text(self, out, &format!("Unknown action {action}"))
                }
            }
        } else {
            let mut removed_count = 0;
            // Use radius
            match action {
                "doodad" => {
                    let my_doodads =
                        around_of(server, &character, radius, |o| o.as_doodad().cloned());
                    for doodad in &my_doodads {
                        if doodad.data().template_id == unit_id {
                            release_and_delete(server, &WorldObject::from(doodad));
                            removed_count += 1;
                        }
                    }
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "Removed {removed_count} Doodad(s) with TemplateID {unit_id} - @DOODAD_NAME({unit_id})"
                        ),
                    );
                }
                "npc" => {
                    let my_npcs = around_of(server, &character, radius, |o| o.as_npc().cloned());
                    for npc in &my_npcs {
                        if npc.data().unit.template_id == unit_id {
                            release_and_delete(server, &WorldObject::from(npc));
                            removed_count += 1;
                        }
                    }
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "Removed {removed_count} NPC(s) with TemplateID {unit_id} - @NPC_NAME({unit_id})"
                        ),
                    );
                }
                _ => {
                    CommandManager::send_error_text(self, out, &format!("Unknown action {action}"))
                }
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<npc || doodad> < <objId> || <templateId> <radius> >".into()
    }

    fn get_command_help_text(&self) -> String {
        "Despawns a npc or doodad using by <objId> or <templateId> inside a <radius> range.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn despawn_by_id_and_radius() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let world = server.world.world(0).unwrap();
        let npc = ts::spawn_npc(&server, ts::NPC, 110.0, 100.0);
        let d1 = ts::spawn_doodad(&server, ts::DOODAD, 102.0, 100.0);
        let d2 = ts::spawn_doodad(&server, ts::DOODAD, 103.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, &format!("despawn npc {}", npc.obj_id)).await;
        assert_eq!(
            out,
            [harness::normal(
                "despawn",
                &format!(
                    "Removing NPC with ID {} - @NPC_NAME({})",
                    npc.obj_id,
                    ts::NPC
                )
            )]
        );
        assert!(world.npc(npc.obj_id).is_none());

        let (_, out) =
            harness::run(&server, &gm.p, &format!("despawn doodad {} 10", ts::DOODAD)).await;
        assert_eq!(
            out,
            [harness::normal(
                "despawn",
                &format!(
                    "Removed 2 Doodad(s) with TemplateID {} - @DOODAD_NAME({})",
                    ts::DOODAD,
                    ts::DOODAD
                )
            )]
        );
        assert!(world.doodad(d1.obj_id).is_none() && world.doodad(d2.obj_id).is_none());

        let (_, out) = harness::run(&server, &gm.p, "despawn npc 5").await;
        assert_eq!(
            out,
            [harness::error(
                "despawn",
                "NPC with objectId 5 does not exist"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "despawn npc x 3").await;
        assert_eq!(out, [harness::error("despawn", "<templateId> parse error")]);
        let (_, out) = harness::run(&server, &gm.p, "despawn npc 1 r").await;
        assert_eq!(out, [harness::error("despawn", "<radius> parse error")]);
        let (_, out) = harness::run(&server, &gm.p, "despawn foo 1").await;
        assert_eq!(out, [harness::error("despawn", "Unknown action foo")]);
    }
}
