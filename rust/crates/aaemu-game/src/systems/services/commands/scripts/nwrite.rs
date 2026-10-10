//! `Scripts/Commands/Nwrite.cs`.

use super::prelude::*;
use crate::utils::json_models::JsonPosition;
use crate::utils::math::clamp_deg_angle;
use crate::utils::numeric::rad_to_deg_f32;
use crate::world::{DoodadRef, NpcRef};

/// `Nwrite`.
pub struct Nwrite;

impl Nwrite {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// The .NET message of `list[^1]` on an empty list.
const INDEX_OUT_OF_RANGE: &str =
    "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')";

/// `(float)MathUtil.ClampDegAngle(radians.RadToDeg())`.
fn clamp_deg(rad: f32) -> f32 {
    clamp_deg_angle(f64::from(rad_to_deg_f32(rad))) as f32
}

/// `spawners[^1].Id + 1`.
fn next_id(spawners: &[JsonSpawnEntry]) -> anyhow::Result<u32> {
    spawners
        .last()
        .map(|s| s.id.wrapping_add(1))
        .ok_or_else(|| anyhow::anyhow!(INDEX_OUT_OF_RANGE))
}

/// `JsonHelper.TryDeserializeObject(contents, out List<T> spawners, out _)`.
fn try_deserialize(contents: &str, kind: SpawnFileKind) -> Option<Vec<JsonSpawnEntry>> {
    parse_spawn_entries(contents, kind).ok().flatten()
}

/// `File.WriteAllText(path, JsonConvert.SerializeObject(spawners.ToArray(), Formatting.Indented))`.
fn write_spawners(
    path: &std::path::Path,
    spawners: &[JsonSpawnEntry],
    kind: SpawnFileKind,
) -> anyhow::Result<()> {
    std::fs::write(path, spawn_entries_json(spawners, kind, false))
        .map_err(|e| anyhow::anyhow!("{e}"))
}

impl Nwrite {
    /// The doodad part (one try around the whole world loop).
    fn save_doodad(
        &self,
        server: &GameServer,
        worlds: &[Arc<crate::world::WorldInstance>],
        doodad: &DoodadRef,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // TODO (C#): replace with templates instead of instances
        for world in worlds {
            let json_path = world_data_dir(server, &world.template.name).join("doodad_spawns.json");
            // `doodad.Spawner.Position.WorldId` (NullReferenceException without a spawner).
            let spawner = doodad_spawner_of(server, doodad.obj_id).ok_or_else(nre)?;
            if spawner.position.world_id != world.id {
                continue;
            }
            let contents = file_contents(&json_path);
            if is_blank(&contents) {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!("File {} doesn't exists or is empty.", json_path.display()),
                );
                continue;
            }
            let Some(mut spawners) = try_deserialize(&contents, SpawnFileKind::Doodad) else {
                continue;
            };
            let (t, template_id, spawner_id) = {
                let d = doodad.data();
                (d.transform, d.template_id, d.spawner_id)
            };
            let w = world_pos(server, &WorldObject::from(doodad));
            if spawner_id == 0 {
                // spawned into the game manually
                let new_id = next_id(&spawners)?;
                let pos = JsonPosition {
                    x: w.x,
                    y: w.y,
                    z: w.z,
                    roll: clamp_deg(t.rot_x),
                    pitch: clamp_deg(t.rot_y),
                    yaw: clamp_deg(t.rot_z),
                };
                spawners.push(JsonSpawnEntry {
                    id: new_id,
                    unit_id: template_id,
                    position: Some(pos),
                    ..JsonSpawnEntry::default()
                });
                // Set ID in case you edit it after adding! (`doodad.Spawner.Id = newId`)
                doodad.data().spawner_id = new_id;
            } else if let Some(entry) = spawners.iter_mut().find(|s| s.id == spawner_id) {
                let p = entry.position.as_mut().ok_or_else(nre)?;
                p.x = w.x;
                p.y = w.y;
                p.z = w.z;
                p.roll = clamp_deg(t.rot_x);
                p.pitch = clamp_deg(t.rot_y);
                p.yaw = clamp_deg(t.rot_z);
            }

            write_spawners(&json_path, &spawners, SpawnFileKind::Doodad)?;
            CommandManager::send_normal_text(
                self,
                out,
                &format!("Doodad ObjId: {} has been saved!", doodad.obj_id),
            );
        }
        Ok(())
    }

    /// One world of the "all" part.
    fn save_all_in_world(
        &self,
        server: &GameServer,
        world: &crate::world::WorldInstance,
        character_world_id: u32,
        npcs: &[NpcRef],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // C#: compares the character's world template id with the instance id.
        if character_world_id != world.id {
            return Ok(());
        }
        let json_path = world_data_dir(server, &world.template.name).join("npc_spawns.json");
        let contents = file_contents(&json_path);
        if is_blank(&contents) {
            CommandManager::send_error_text(
                self,
                out,
                &format!("File {} doesn't exists or is empty.", json_path.display()),
            );
            return Ok(());
        }
        let Some(mut spawners) = try_deserialize(&contents, SpawnFileKind::Npc) else {
            return Ok(());
        };
        for npc in npcs {
            let Some(spawner) = npc_spawner_of(server, npc.obj_id) else {
                continue; // No spawner attached
            };
            if spawner.position().world_id != character_world_id {
                continue; // wrong world
            }
            let (spawner_id, template_id) = {
                let n = npc.data();
                (n.spawner_id, n.unit.template_id)
            };
            if spawner_id == 0 {
                // spawned into the game manually
                let new_id = next_id(&spawners)?;
                let pos = world_pos(server, &WorldObject::from(npc));
                spawners.push(JsonSpawnEntry {
                    id: new_id,
                    unit_id: template_id,
                    position: Some(JsonPosition {
                        x: pos.x,
                        y: pos.y,
                        z: pos.z,
                        yaw: clamp_deg(pos.rot_z),
                        ..JsonPosition::default()
                    }),
                    ..JsonSpawnEntry::default()
                });
                // Set ID in-case you edit it after adding! (`npcs[i].Spawner.Id = newId`)
                npc.data().spawner_id = new_id;
            } else if !spawners.is_empty() {
                // C#: compares with `npc.Spawner.Id` where `npc` (the single-NPC variable) is
                // null in this branch: NullReferenceException.
                return Err(nre());
            }
        }

        write_spawners(&json_path, &spawners, SpawnFileKind::Npc)?;
        CommandManager::send_normal_text(self, out, "All npcs have been saved!");
        Ok(())
    }

    /// One world of the single-NPC part.
    fn save_npc_in_world(
        &self,
        server: &GameServer,
        world: &crate::world::WorldInstance,
        npc: &NpcRef,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // `npc.Spawner.Position.WorldId` (NullReferenceException without a spawner).
        let spawner = npc_spawner_of(server, npc.obj_id).ok_or_else(nre)?;
        if spawner.position().world_id != world.id {
            return Ok(());
        }
        let mut app = server.config.app_path.to_string_lossy().to_string();
        if !app.ends_with(['/', '\\']) {
            app.push(std::path::MAIN_SEPARATOR);
        }
        let json_path = format!(
            "{app}Data/Worlds/{}/npc_spawns_new.json",
            world.template.name
        );
        let contents = file_contents(std::path::Path::new(&json_path));
        if is_blank(&contents) {
            CommandManager::send_error_text(
                self,
                out,
                &format!("File {json_path} doesn't exists or is empty."),
            );
            return Ok(());
        }
        // C#: a failed deserialization leaves `spawners` null (`spawners.ToArray()` throws).
        let mut spawners = try_deserialize(&contents, SpawnFileKind::Npc).ok_or_else(nre)?;
        let spawner_id = npc.data().spawner_id;
        if spawner_id == 0 {
            // C#: `new JsonNpcSpawns { ..., Position = { X = .. } }` initializes the members of
            // the null `Position`: NullReferenceException.
            return Err(nre());
        }
        let pos = world_pos(server, &WorldObject::from(npc));
        if let Some(entry) = spawners.iter_mut().find(|s| s.id == spawner_id) {
            let p = entry.position.as_mut().ok_or_else(nre)?;
            p.x = pos.x;
            p.y = pos.y;
            p.z = pos.z;
        }

        write_spawners(
            std::path::Path::new(&json_path),
            &spawners,
            SpawnFileKind::Npc,
        )?;
        CommandManager::send_normal_text(self, out, "Npc data has been saved!");
        Ok(())
    }
}

impl Command for Nwrite {
    fn command_names(&self) -> Vec<String> {
        names(&["nwrite", "nw"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut worlds = server.world.worlds();
        worlds.sort_by_key(|w| w.id);
        let parent = parent_world(server, &character).ok_or_else(nre)?;
        let mut npcs = parent.all_npcs();
        npcs.sort_by_key(|n| n.obj_id);

        let mut doodad = None;
        let mut npc = None;

        // Select what we're editing
        // Doodad by Id ?
        if args.len() >= 2 && args[0].eq_ignore_ascii_case("doodad") {
            if let Some(id) = try_parse::<u32>(&args[1]) {
                doodad = parent.doodad(id);
            }
        }

        // NPC by targeting
        if args.is_empty() {
            if let Some(n) = current_target(server, &character).and_then(|t| t.as_npc().cloned()) {
                npc = Some(n);
            }
        }

        // NPC by Id ?
        if args.len() >= 2 && args[0].eq_ignore_ascii_case("npc") {
            if let Some(id) = try_parse::<u32>(&args[1]) {
                npc = parent.npc(id);
            }
        }

        // All Npcs ?
        let save_all = !args.is_empty() && args[0].eq_ignore_ascii_case("all");

        if doodad.is_none() && npc.is_none() && !save_all {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        if let Some(doodad) = &doodad {
            // Save target Doodad
            if let Err(e) = self.save_doodad(server, &worlds, doodad, out) {
                CommandManager::send_error_text(self, out, &format!("Exception: {e}"));
            }
        } else if save_all {
            // Save all NPCs in the current world
            let character_world_id = character.data().unit.transform.world_id;
            for world in &worlds {
                if let Err(e) =
                    self.save_all_in_world(server, world, character_world_id, &npcs, out)
                {
                    CommandManager::send_error_text(self, out, &format!("Exception: {e}"));
                }
            }
        } else if let Some(npc) = &npc {
            for world in &worlds {
                if let Err(e) = self.save_npc_in_world(server, world, npc, out) {
                    CommandManager::send_error_text(self, out, &format!("Exception: {e}"));
                }
            }
        } else {
            ctx.character
                .send_message("[Nwrite] I don't know what to do here");
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) [all||[<doodad||npc> <objectId>]] ".into()
    }

    fn get_command_help_text(&self) -> String {
        "Writes All or given Npc or given Doodad's current position and rotation to json".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;
    use super::super::util::*;

    #[tokio::test]
    async fn nwrite_doodad_appends_a_manual_spawn() {
        let server = ts::server();
        let dir = ts::temp_app_path(&server, "nwrite");
        let world = server.world.world(0).unwrap();
        let wdir = dir.join("Data").join("Worlds").join(&world.template.name);
        std::fs::create_dir_all(&wdir).unwrap();
        std::fs::write(
            wdir.join("doodad_spawns.json"),
            r#"[{"Id": 4, "UnitId": 1, "Position": {"X": 1, "Y": 2, "Z": 3}}]"#,
        )
        .unwrap();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let d = ts::spawn_doodad_with_spawner(&server, ts::DOODAD, 102.0, 100.0, world.id);

        let (_, out) = harness::run(&server, &gm.p, &format!("nw doodad {}", d.obj_id)).await;
        assert_eq!(
            out,
            [harness::normal(
                "nwrite",
                &format!("Doodad ObjId: {} has been saved!", d.obj_id)
            )]
        );
        let text = std::fs::read_to_string(wdir.join("doodad_spawns.json")).unwrap();
        let entries = parse_spawn_entries(&text, SpawnFileKind::Doodad)
            .unwrap()
            .unwrap();
        assert_eq!(entries.len(), 2);
        assert_eq!(entries[1].id, 5);
        assert_eq!(entries[1].unit_id, ts::DOODAD);
        assert_eq!(entries[1].position.unwrap().x, 102.0);
        assert!(text.contains("\"RelatedIds\": null"));
        assert!(text.contains("\"Roll\": 0.0"));
        assert_eq!(d.data().spawner_id, 5);

        // A doodad without a spawner: the NullReferenceException is reported.
        let plain = ts::spawn_doodad(&server, ts::DOODAD, 103.0, 100.0);
        let (_, out) =
            harness::run(&server, &gm.p, &format!("nwrite doodad {}", plain.obj_id)).await;
        assert_eq!(
            out,
            [harness::error(
                "nwrite",
                "Exception: Object reference not set to an instance of an object."
            )]
        );

        // Missing npc_spawns.json for "all".
        gm.p.data().unit.transform.world_id = world.id;
        let (_, out) = harness::run(&server, &gm.p, "nwrite all").await;
        assert_eq!(
            out,
            [harness::error(
                "nwrite",
                &format!(
                    "File {} doesn't exists or is empty.",
                    wdir.join("npc_spawns.json").display()
                )
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "nwrite").await;
        assert!(out[0].starts_with("Help for |cFFFFFFFF/nwrite|r"));
    }
}
