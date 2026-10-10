//! `Scripts/Commands/Around.cs`.

use super::prelude::*;
use crate::utils::math::get_distance;

/// `Around`.
pub struct Around;

impl Around {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `ShowObjectData(go, index, indexPrefix, verbose, messageOutput)` (shared with `FindObject`,
/// which always shows the transform): the object line, the transform when `verbose`, then the
/// children. Returns `1 + Children.Count`.
pub fn show_object_data(
    server: &GameServer,
    go: &WorldObject,
    index: usize,
    index_prefix: &str,
    verbose: bool,
    out: &mut dyn MessageOutput,
) -> usize {
    let mut index_str = index_prefix.to_string();
    if !index_str.is_empty() {
        index_str += " . ";
    }
    index_str += &(index + 1).to_string();

    match go {
        WorldObject::Gimmick(g) => {
            let (gimmick_id, template_id, model) = {
                let d = g.data();
                (d.gimmick_id, d.unit.template_id, d.model_path.clone())
            };
            out.send_message(&format!(
                "#{index_str} -> BcId: {}, GimmickId: {gimmick_id}, TemplateId: {template_id} - Model: {model}",
                g.obj_id
            ));
        }
        WorldObject::Doodad(d) => {
            let (template_id, func_group_id) = {
                let d = d.data();
                (d.template_id, d.func_group_id)
            };
            out.send_message(&format!(
                "#{index_str} -> BcId: {} DoodadTemplateId: {template_id} - @DOODAD_NAME({template_id}) FuncGroupId {func_group_id}",
                d.obj_id
            ));
        }
        WorldObject::Player(p) => {
            out.send_message(&format!(
                "#{index_str} -> BcId: {} CharacterId: {} - {}",
                p.obj_id,
                p.character_id,
                name_of(p)
            ));
        }
        other => {
            let name = other.with_base_unit(|b| b.name().to_string());
            out.send_message(&format!(
                "#{index_str} -> BcId: {} - {name}",
                other.obj_id()
            ));
        }
    }

    if verbose {
        out.send_message(&format!(
            "#{index_str} -> {}",
            transform_full_string(server, go, true, true)
        ));
    }

    // Cycle Children
    let children = go.core().children();
    for (i, child) in children.iter().enumerate() {
        show_object_data(server, child, i, &index_str, verbose, out);
    }

    1 + children.len()
}

impl Command for Around {
    fn command_names(&self) -> Vec<String> {
        names(&["around", "near"])
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

        let mut radius = 30f32;
        if args.len() > 1 {
            match parse_f32(&args[1]) {
                Some(r) => radius = r,
                None => {
                    CommandManager::send_error_text(self, out, "Error parsing Radius !");
                    return Ok(());
                }
            }
        }

        let verbose = args.len() > 2 && !args[2].trim().is_empty();
        let server = ctx.server()?;
        let character = ctx.player()?;
        let cmd = &self.command_names()[0];

        let sb = String::new();
        match args[0].as_str() {
            "doodad" => {
                let doodads = around_of(server, &character, radius, |o| o.as_doodad().cloned());

                out.send_message(&format!("[{cmd}] Doodads:"));
                for (i, doodad) in doodads.iter().enumerate() {
                    let (template_id, func_group_id, spawner_id) = {
                        let d = doodad.data();
                        (d.template_id, d.func_group_id, d.spawner_id)
                    };
                    out.send_message(&format!(
                        "#{} -> BcId: {} DoodadTemplateId: {template_id} - @DOODAD_NAME({template_id}), FuncGroupId: {func_group_id}",
                        i + 1,
                        doodad.obj_id
                    ));

                    let spawner = server.systems.npcs.spawns.doodad_spawner_of(doodad.obj_id);
                    out.send_message(&format!(
                        "#{} -> SpawnerID = {}, Respawns Template: {}\n",
                        i + 1,
                        spawner
                            .as_ref()
                            .map_or_else(|| "none".to_string(), |_| spawner_id.to_string()),
                        spawner.as_ref().map_or_else(
                            || "default".to_string(),
                            |s| s.respawn_doodad_template_id().to_string()
                        )
                    ));

                    if verbose {
                        let obj = WorldObject::from(doodad);
                        let shorts = world_pos(server, &obj).roll_pitch_yaw_shorts();
                        let short_string =
                            format!("(short[3])[r:{} p:{} y:{}]", shorts.0, shorts.1, shorts.2);
                        out.send_message(&format!(
                            "#{} -> {} = {short_string}\n",
                            i + 1,
                            transform_full_string(server, &obj, true, false)
                        ));
                    }
                }

                out.send_message(&sb);
                out.send_message(&format!("[{cmd}] Doodad count: {}", doodads.len()));
            }
            "mob" | "npc" => {
                let npcs = around_of(server, &character, radius, |o| o.as_npc().cloned());

                out.send_message(&format!("[{cmd}] NPCs"));
                for (i, npc) in npcs.iter().enumerate() {
                    let template_id = npc.data().unit.template_id;
                    out.send_message(&format!(
                        "#{} -> BcId: {} NpcTemplateId: {template_id} - @NPC_NAME({template_id})",
                        i + 1,
                        npc.obj_id
                    ));
                    if verbose {
                        out.send_message(&format!(
                            "#{} -> {}",
                            i + 1,
                            transform_full_string(server, &WorldObject::from(npc), true, false)
                        ));
                    }
                }

                out.send_message(&format!("[{cmd}] NPC count: {}", npcs.len()));
            }
            "character" | "pc" | "player" => {
                let characters = around_of(server, &character, radius, |o| o.as_player().cloned());

                out.send_message(&format!("[{cmd}] Characters"));
                for (i, c) in characters.iter().enumerate() {
                    out.send_message(&format!(
                        "#{} -> BcId: {} CharacterId: {} - {}",
                        i + 1,
                        c.obj_id,
                        c.character_id,
                        name_of(c)
                    ));
                    if verbose {
                        out.send_message(&format!(
                            "#{} -> {}",
                            i + 1,
                            transform_full_string(server, &WorldObject::from(c), true, false)
                        ));
                    }
                }

                out.send_message(&format!("[{cmd}] Character count: {}", characters.len()));
            }
            "npcspawner" => {
                // Grab all spawner info and put them into one list
                let world = parent_world(server, &character).ok_or_else(nre)?;
                let manager = server.systems.npcs.spawns.get_or_create(&world);
                let npc_spawners: Vec<_> =
                    manager.get_all_spawners().into_values().flatten().collect();
                let me = world_pos(server, &WorldObject::from(&character));

                out.send_message(&format!("[{cmd}] NPC Spawners"));
                let mut si = 0;
                for npc_spawner in &npc_spawners {
                    let p = npc_spawner.position();
                    if get_distance([p.x, p.y, p.z], [me.x, me.y, me.z], false) > radius {
                        continue;
                    }
                    si += 1;
                    // C#: `npcSpawner.Template.Id` (NullReferenceException without a template).
                    let template = npc_spawner.template().ok_or_else(nre)?;
                    // C#: the line number is `si + 1` after the increment (starts at #2).
                    out.send_message(&format!("#{} -> TemplateId: {}", si + 1, template.id));
                    for n in npc_spawner.spawnable_npcs() {
                        out.send_message(&format!(
                            "#{} -> Spawnable NPC: {} - {} - @NPC_NAME({})",
                            si + 1,
                            n.member_id,
                            n.member_type,
                            n.member_id
                        ));
                    }
                    if verbose {
                        out.send_message(&format!(
                            "#{} -> {}",
                            si + 1,
                            vector3_string([p.x, p.y, p.z])
                        ));
                    }
                }

                out.send_message(&format!("[{cmd}] NPC Spawner count: {si}"));
            }
            // case "all":
            _ => {
                let world = parent_world(server, &character);
                let go = match &world {
                    Some(w) => around_radius(w, &WorldObject::from(&character), radius),
                    None => Vec::new(),
                };

                let mut c = 0;
                out.send_message(&format!("[{cmd}] GameObjects:"));
                for (i, o) in go.iter().enumerate() {
                    if o.transform().parent_obj_id == 0 {
                        c += show_object_data(server, o, i, "", verbose, out);
                    }
                }

                out.send_message(&format!("[{cmd}] Object Count: {c}"));
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<doodad || npc || player || all> [radius] [verbose || v]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Creates a list of specified <objectType> in a [radius] radius around you. Default radius is 30.\nNote: Only lists objects in viewing range of you (recommended maximum radius of 100).".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn around_lists_objects_by_kind() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let _bob = harness::pc(&server, 2, "Bob", 105.0, 100.0);
        let npc = ts::spawn_npc(&server, ts::NPC, 110.0, 100.0);
        let doodad = ts::spawn_doodad(&server, ts::DOODAD, 102.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, "around doodad").await;
        assert_eq!(
            out,
            [
                "[around] Doodads:".to_string(),
                format!(
                    "#1 -> BcId: {} DoodadTemplateId: {} - @DOODAD_NAME({}), FuncGroupId: 0",
                    doodad.obj_id,
                    ts::DOODAD,
                    ts::DOODAD
                ),
                "#1 -> SpawnerID = none, Respawns Template: default\n".to_string(),
                String::new(),
                "[around] Doodad count: 1".to_string(),
            ]
        );

        let (_, out) = harness::run(&server, &gm.p, "near npc 30 v").await;
        assert_eq!(out[0], "[around] NPCs");
        assert_eq!(
            out[1],
            format!(
                "#1 -> BcId: {} NpcTemplateId: {} - @NPC_NAME({})",
                npc.obj_id,
                ts::NPC,
                ts::NPC
            )
        );
        assert_eq!(out[2], "#1 -> [i0] X:110 Y:100 Z:100  r:0° p:0° y:0°");
        assert_eq!(out[3], "[around] NPC count: 1");

        let (_, out) = harness::run(&server, &gm.p, "around player").await;
        assert_eq!(
            out,
            [
                "[around] Characters",
                "#1 -> BcId: 258 CharacterId: 2 - Bob",
                "[around] Character count: 1"
            ]
        );

        let (_, out) = harness::run(&server, &gm.p, "around all 5").await;
        assert_eq!(out.first().unwrap(), "[around] GameObjects:");
        assert_eq!(out.last().unwrap(), "[around] Object Count: 1");
        assert_eq!(out.len(), 3);

        let (_, out) = harness::run(&server, &gm.p, "around npcspawner").await;
        assert_eq!(
            out,
            ["[around] NPC Spawners", "[around] NPC Spawner count: 0"]
        );

        let (_, out) = harness::run(&server, &gm.p, "around npc abc").await;
        assert_eq!(out, [harness::error("around", "Error parsing Radius !")]);
        let (_, out) = harness::run(&server, &gm.p, "around").await;
        assert!(out[0].starts_with("Help for |cFFFFFFFF/around|r |cFFEEEEAA<doodad || npc || player || all> [radius] [verbose || v]|r"));
    }
}
