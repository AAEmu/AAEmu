//! `Scripts/SubCommands/Doodads/DoodadSaveSubCommand.cs` (+ the spawn-file save helpers the
//! Npc save sub command shares).

use std::collections::HashMap;
use std::sync::atomic::{AtomicBool, Ordering};

use super::super::super::prelude::*;
use super::{choice_param, sub_base, uint_param};
use crate::utils::json_models::JsonPosition;
use crate::utils::numeric::rad_to_deg_f32;

/// `float` as a dictionary key component (`float.Equals`: 0 and -0 are equal).
fn key_bits(v: f32) -> u32 {
    if v == 0.0 {
        0
    } else {
        v.to_bits()
    }
}

/// The `(UnitId, X, Y[, Z])` dictionary key of the save-all commands.
pub(crate) fn spawn_key(unit_id: u32, x: f32, y: f32, z: Option<f32>) -> (u32, u32, u32, u32) {
    (unit_id, key_bits(x), key_bits(y), z.map_or(0, key_bits))
}

/// `Load*FromFileByWorld(worldInstance)`: every `<prefix>*.json` of the world directory.
/// `Err` = the C# exceptions (missing directory, bad JSON).
pub(crate) fn load_spawns_from_world(
    server: &GameServer,
    world_name: &str,
    prefix: &str,
    kind: SpawnFileKind,
) -> anyhow::Result<Vec<JsonSpawnEntry>> {
    let world_directory = world_data_dir(server, world_name);
    let json_files = json_files_with_prefix(&world_directory, prefix)?;
    if json_files.is_empty() {
        tracing::warn!(
            "No {prefix}*.json files found in directory {}.",
            world_directory.display()
        );
        return Ok(Vec::new());
    }

    let mut all = Vec::new();
    for json_file in json_files {
        let contents = file_contents(&json_file);
        if is_blank(&contents) {
            tracing::warn!(
                "File {} is empty or could not be read.",
                json_file.display()
            );
            continue;
        }
        tracing::info!("Loading spawns from file {} ...", json_file.display());
        match parse_spawn_entries(&contents, kind).map_err(anyhow::Error::msg)? {
            Some(list) => all.extend(list),
            None => tracing::warn!(
                "Failed to deserialize {} from file {}.",
                if kind == SpawnFileKind::Doodad {
                    "doodads"
                } else {
                    "npcs"
                },
                json_file.display()
            ),
        }
    }
    Ok(all)
}

/// `DateTime.Now:yyyyMMdd_HHmmss`.
pub(crate) fn file_stamp() -> String {
    chrono::Local::now().format("%Y%m%d_%H%M%S").to_string()
}

/// `Dictionary<uint, T>` built with `TryAdd` (first wins) then `dict[spawn.Id] = spawn`; the
/// values in dictionary order.
pub(crate) fn merge_by_id(
    from_file: Vec<JsonSpawnEntry>,
    spawn: JsonSpawnEntry,
) -> Vec<JsonSpawnEntry> {
    let mut index: HashMap<u32, usize> = HashMap::new();
    let mut list: Vec<JsonSpawnEntry> = Vec::new();
    for e in from_file {
        if let std::collections::hash_map::Entry::Vacant(v) = index.entry(e.id) {
            v.insert(list.len());
            list.push(e);
        }
    }
    match index.get(&spawn.id) {
        Some(&i) => list[i] = spawn,
        None => list.push(spawn),
    }
    list
}

/// The save-all merge: titles / scales of the file entries, duplicates (by key) dropped, the
/// removed objects' entries deleted and the added objects appended. Returns the entries to
/// write.
pub(crate) fn merge_all(
    mut to_file: Vec<JsonSpawnEntry>,
    names: &HashMap<u32, String>,
    key_of: impl Fn(&JsonSpawnEntry) -> (u32, u32, u32, u32),
    remove_keys: &[(u32, u32, u32, u32)],
    added: Vec<JsonSpawnEntry>,
    fix_entry: impl Fn(&mut JsonSpawnEntry),
) -> Vec<JsonSpawnEntry> {
    for e in &mut to_file {
        e.title = Some(names.get(&e.unit_id).cloned().unwrap_or_default());
        if e.scale == 0.0 {
            e.scale = 1.0;
        }
        fix_entry(e);
    }

    // Удаление дубликатов из начальных данных
    let mut dict: HashMap<(u32, u32, u32, u32), usize> = HashMap::new();
    let mut unique: Vec<Option<JsonSpawnEntry>> = Vec::new();
    for spawns in to_file {
        let key = key_of(&spawns);
        if dict.contains_key(&key) {
            tracing::warn!(
                "Дубликат объекта с UnitId {} на позиции ({}, {}, {})",
                spawns.unit_id,
                spawns.position.map_or(0.0, |p| p.x),
                spawns.position.map_or(0.0, |p| p.y),
                spawns.position.map_or(0.0, |p| p.z)
            );
            continue;
        }
        dict.insert(key, unique.len());
        unique.push(Some(spawns));
    }

    // Удаляем элементы, которые соответствуют удалённым объектам
    for key in remove_keys {
        if let Some(i) = dict.remove(key) {
            unique[i] = None;
        }
    }

    let mut out: Vec<JsonSpawnEntry> = unique.into_iter().flatten().collect();
    out.extend(added);
    out
}

/// The save-all report.
pub(crate) fn saved_all_message(
    what: &str,
    elapsed_ms: u128,
    initial: usize,
    removed: usize,
    added: usize,
    final_count: usize,
) -> String {
    format!(
        "All {what} have been saved! Time taken: {elapsed_ms} ms\nInitial count: {initial}\nRemoved count: {removed}\nAdded count: {added}\nFinal count: {final_count}"
    )
}

/// `DoodadSaveSubCommand`.
pub struct DoodadSaveSubCommand {
    base: SubCommandBase,
    /// `_isSavingInProgress` (+ `_saveLock`).
    is_saving_in_progress: AtomicBool,
}

impl Default for DoodadSaveSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad Save]",
                "Save current state of a doodad to the doodads world file.",
                &format!("{COMMAND_PREFIX}doodad save"),
                vec![
                    choice_param("target", &["all", "id"]),
                    uint_param("ObjId", "Object Id", false),
                ],
            ),
            is_saving_in_progress: AtomicBool::new(false),
        }
    }
}

impl DoodadSaveSubCommand {
    fn save_all(
        &self,
        ctx: &CommandContext<'_>,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // Проверка на выполнение записи
        if self
            .is_saving_in_progress
            .compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst)
            .is_err()
        {
            self.base
                .send_message(out, "Save operation is already in progress.");
            return Ok(());
        }
        let res = self.save_all_inner(ctx, out);
        self.is_saving_in_progress.store(false, Ordering::SeqCst);
        res
    }

    fn save_all_inner(
        &self,
        ctx: &CommandContext<'_>,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let stopwatch = std::time::Instant::now();
        let server = ctx.server()?;
        let character = ctx.player()?;
        let instance = character.data().unit.transform.instance_id;
        let current_world = server.world.world(instance).ok_or_else(nre)?;
        let mut doodads_in_world = current_world.all_doodads();
        doodads_in_world.sort_by_key(|d| d.obj_id);
        let from_file = load_spawns_from_world(
            server,
            &current_world.template.name,
            "doodad_spawns",
            SpawnFileKind::Doodad,
        )?;
        let spawns = &server.systems.npcs.spawns;
        let has_spawner = |obj_id: u32| spawns.doodad_spawner_of(obj_id).is_some();
        let add_doodads: Vec<_> = doodads_in_world
            .iter()
            .filter(|d| has_spawner(d.obj_id) && d.data().spawner_id == 0)
            .cloned()
            .collect();
        let remove_doodads: Vec<_> = doodads_in_world
            .iter()
            .filter(|d| has_spawner(d.obj_id) && d.data().spawner_id == 0xffff_ffff)
            .cloned()
            .collect();

        // Получаем все doodads и последний идентификатор объекта
        // C#: `allDoodads.Last().ObjId++` (also increments that doodad's ObjId; not mirrored).
        let mut last_obj_id = doodads_in_world
            .last()
            .map(|d| d.obj_id)
            .ok_or_else(|| anyhow::anyhow!("Sequence contains no elements"))?;

        let names = spawn_names(server);
        let remove_keys: Vec<_> = remove_doodads
            .iter()
            .map(|d| {
                let p = world_pos(server, &WorldObject::from(d));
                spawn_key(d.data().template_id, p.x, p.y, Some(p.z))
            })
            .collect();
        let added: Vec<JsonSpawnEntry> = add_doodads
            .iter()
            .map(|d| {
                let pos = world_pos(server, &WorldObject::from(d));
                let (template_id, scale, func_group_id) = {
                    let d = d.data();
                    (d.template_id, d.scale, d.func_group_id)
                };
                let id = last_obj_id;
                last_obj_id = last_obj_id.wrapping_add(1);
                JsonSpawnEntry {
                    id,
                    unit_id: template_id,
                    title: Some(names.get(&template_id).cloned().unwrap_or_default()),
                    position: Some(JsonPosition {
                        x: pos.x,
                        y: pos.y,
                        z: pos.z,
                        roll: rad_to_deg_f32(pos.rot_x),
                        pitch: rad_to_deg_f32(pos.rot_y),
                        yaw: rad_to_deg_f32(pos.rot_z),
                    }),
                    scale,
                    func_group_id,
                    ..JsonSpawnEntry::default()
                }
            })
            .collect();
        let initial_count = from_file.len();
        let to_file = merge_all(
            from_file,
            &names,
            |e| {
                let p = e.position.unwrap_or_default();
                spawn_key(e.unit_id, p.x, p.y, Some(p.z))
            },
            &remove_keys,
            added,
            |_| {},
        );

        let json_path_out = world_data_dir(server, &current_world.template.name)
            .join(format!("doodad_spawns_all_{}.json.add", file_stamp()));
        std::fs::write(
            &json_path_out,
            spawn_entries_json(&to_file, SpawnFileKind::Doodad, true),
        )?;

        let msg = saved_all_message(
            "doodads",
            stopwatch.elapsed().as_millis(),
            initial_count,
            remove_doodads.len(),
            add_doodads.len(),
            to_file.len(),
        );
        self.base.send_message(out, &msg);
        tracing::warn!("{msg}");
        Ok(())
    }

    fn save_by_id(
        &self,
        ctx: &CommandContext<'_>,
        doodad_obj_id: u32,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
        let Some(doodad) = world.doodad(doodad_obj_id) else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad with objId {doodad_obj_id} does not exist"),
            );
            tracing::warn!("Doodad with objId {doodad_obj_id} does not exist");
            return Ok(());
        };

        let (t, template_id, scale, func_group_id) = {
            let d = doodad.data();
            (d.transform, d.template_id, d.scale, d.func_group_id)
        };
        let Some(world) = server.world.world(t.instance_id) else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Could not find the worldId {}", t.world_id),
            );
            tracing::warn!("Could not find the worldId {}", t.world_id);
            return Ok(());
        };

        let names = spawn_names(server);
        let spawn = JsonSpawnEntry {
            // C#: `doodad.Id` (BaseUnit.Id, never set for doodads: always 0).
            id: 0,
            unit_id: template_id,
            title: Some(names.get(&template_id).cloned().unwrap_or_default()),
            position: Some(JsonPosition {
                x: t.x,
                y: t.y,
                z: t.z,
                roll: rad_to_deg_f32(t.rot_x),
                pitch: rad_to_deg_f32(t.rot_y),
                yaw: rad_to_deg_f32(t.rot_z),
            }),
            scale,
            func_group_id,
            ..JsonSpawnEntry::default()
        };

        let from_file = load_spawns_from_world(
            server,
            &world.template.name,
            "doodad_spawns",
            SpawnFileKind::Doodad,
        )?;
        let list = merge_by_id(from_file, spawn);

        let json_path_out = world_data_dir(server, &world.template.name)
            .join(format!("doodad_spawns_add_{}.json.add", file_stamp()));
        std::fs::write(
            &json_path_out,
            spawn_entries_json(&list, SpawnFileKind::Doodad, true),
        )?;
        let msg = format!(
            "All doodads have been saved with added doodad ObjId:{}, TemplateId:{template_id}",
            doodad.obj_id
        );
        self.base.send_message(out, &msg);
        tracing::warn!("{msg}");
        Ok(())
    }
}

impl SubCommand for DoodadSaveSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // C#: runs on a background task (`Task.Run`); exceptions are only logged.
        let res = match parameters.get("ObjId") {
            Some(v) => v.as_u32().and_then(|id| self.save_by_id(ctx, id, out)),
            None => self.save_all(ctx, out),
        };
        if let Err(ex) = res {
            tracing::error!("Ошибка при выполнении метода: {ex}");
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::super::super::super::harness;
    use super::super::super::super::util::objects_test_support as ts;
    use super::super::super::super::util::*;

    fn written(dir: &std::path::Path, prefix: &str) -> String {
        let f = std::fs::read_dir(dir)
            .unwrap()
            .filter_map(Result::ok)
            .find(|e| e.file_name().to_string_lossy().starts_with(prefix))
            .expect("file written");
        std::fs::read_to_string(f.path()).unwrap()
    }

    #[tokio::test]
    async fn doodad_save_all_and_by_id() {
        let server = ts::server();
        let app = ts::temp_app_path(&server, "doodad_save");
        let world = server.world.world(0).unwrap();
        let wdir = app.join("Data").join("Worlds").join(&world.template.name);
        std::fs::create_dir_all(&wdir).unwrap();
        std::fs::write(
            wdir.join("doodad_spawns.json"),
            r#"[
              {"Id": 1, "UnitId": 5, "Position": {"X": 1, "Y": 2, "Z": 3}},
              {"Id": 2, "UnitId": 5, "Position": {"X": 1, "Y": 2, "Z": 3}},
              {"Id": 3, "UnitId": 6, "Position": {"X": 7, "Y": 8, "Z": 9}, "Scale": 2}
            ]"#,
        )
        .unwrap();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let added = ts::spawn_doodad_with_spawner(&server, ts::DOODAD, 102.0, 100.0, world.id);
        let removed = ts::spawn_doodad_with_spawner(&server, 6, 7.0, 8.0, world.id);
        removed.data().transform.z = 9.0;
        removed.data().spawner_id = 0xffff_ffff;

        let (_, out) = harness::run(&server, &gm.p, "doodad save all").await;
        assert_eq!(out.len(), 1);
        assert!(out[0].starts_with("[Doodad Save] All doodads have been saved! Time taken: "));
        assert!(out[0]
            .ends_with(" ms\nInitial count: 3\nRemoved count: 1\nAdded count: 1\nFinal count: 2"));
        let entries =
            parse_spawn_entries(&written(&wdir, "doodad_spawns_all_"), SpawnFileKind::Doodad)
                .unwrap()
                .unwrap();
        assert_eq!(entries.len(), 2);
        assert_eq!((entries[0].id, entries[0].scale), (1, 1.0));
        assert_eq!(entries[0].title.as_deref(), Some(""));
        assert_eq!(entries[1].unit_id, ts::DOODAD);
        assert_eq!(entries[1].position.unwrap().x, 102.0);

        let (_, out) =
            harness::run(&server, &gm.p, &format!("doodad save id {}", added.obj_id)).await;
        assert_eq!(
            out,
            [format!(
                "[Doodad Save] All doodads have been saved with added doodad ObjId:{}, TemplateId:{}",
                added.obj_id,
                ts::DOODAD
            )]
        );
        let text = written(&wdir, "doodad_spawns_add_");
        let entries = parse_spawn_entries(&text, SpawnFileKind::Doodad)
            .unwrap()
            .unwrap();
        // The file ids (1, 2, 3) + the doodad as id 0 (C#: `doodad.Id`).
        assert_eq!(
            entries.iter().map(|e| e.id).collect::<Vec<_>>(),
            [1, 2, 3, 0]
        );
        assert!(text.contains("\"FuncGroupId\": 0"));

        let (_, out) = harness::run(&server, &gm.p, "doodad save id 99").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Doodad Save] Doodad with objId 99 does not exist|r"]
        );
    }
}
