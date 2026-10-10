//! `Scripts/SubCommands/Npcs/**` and `Scripts/Commands/NpcCmd.cs`.

use std::sync::atomic::{AtomicBool, Ordering};

use super::super::prelude::*;
use super::doodads::save::{
    file_stamp, load_spawns_from_world, merge_all, merge_by_id, saved_all_message, spawn_key,
};
use super::doodads::{choice_param, float_prefix_param, float_range_param, sub_base, uint_param};
use crate::models::movement::{
    GameStanceType, MoveKind, MoveType, MoveTypeAlertness, MoveTypeFlags, UnitMove,
};
use crate::packets::world::SCOneUnitMovementPacket;
use crate::utils::json_models::JsonPosition;
use crate::utils::numeric::{deg_to_rad_f32, rad_to_deg_f32};
use crate::world::NpcRef;

/// The `ObjId` parameter or the current target (`Npc`), with the C# replies.
fn select_npc(
    ctx: &CommandContext<'_>,
    base: &SubCommandBase,
    parameters: &Parameters,
    out: &mut dyn MessageOutput,
    log: bool,
) -> anyhow::Result<Option<NpcRef>> {
    let server = ctx.server()?;
    let character = ctx.player()?;
    if let Some(v) = parameters.get("ObjId") {
        let npc_obj_id = v.as_u32()?;
        let world = parent_world(server, &character).ok_or_else(nre)?;
        let npc = world.npc(npc_obj_id);
        if npc.is_none() {
            let msg = format!("Npc with objId {npc_obj_id} does not exist");
            base.send_color_message(out, Color::RED, &msg);
            if log {
                tracing::warn!("{msg}");
            }
        }
        return Ok(npc);
    }
    let npc = current_target(server, &character).and_then(|t| t.as_npc().cloned());
    if npc.is_none() {
        base.send_color_message(out, Color::RED, "You need to target a Npc first");
        if log {
            tracing::warn!("You need to target a Npc first");
        }
    }
    Ok(npc)
}

/// The `target` / `ObjId` parameters of the Npc and Slave sub commands.
pub(crate) fn target_params() -> Vec<Box<dyn SubCommandParameter>> {
    vec![
        choice_param("target", &["target", "id"]),
        uint_param("ObjId", "object id", false),
    ]
}

/// `NpcInformationSubCommand`.
pub struct NpcInformationSubCommand {
    base: SubCommandBase,
}

impl Default for NpcInformationSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Npc Information]",
                "Get all npc information from a NPC (Targeted or by Id)",
                &format!("{COMMAND_PREFIX}npc info"),
                target_params(),
            ),
        }
    }
}

impl SubCommand for NpcInformationSubCommand {
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
        let Some(npc) = select_npc(ctx, &self.base, parameters, out, false)? else {
            return Ok(());
        };
        let (t, template_id) = {
            let n = npc.data();
            (n.unit.transform, n.unit.template_id)
        };
        let yaw = rad_to_deg_f32(t.rot_z);
        let pitch = rad_to_deg_f32(t.rot_y);
        let roll = rad_to_deg_f32(t.rot_x);

        //TODO: There is much more potential information to show on this command.
        self.base.send_message(
            out,
            &format!(
                "Name:@NPC_NAME({template_id}) ObjId:{} TemplateId:{template_id}, x:{}, y:{}, z:{}, roll:{}°, pitch:{}°, yaw:{}°",
                npc.obj_id,
                f32s(t.x),
                f32s(t.y),
                f32s(t.z),
                fmt_0_1(roll),
                fmt_0_1(pitch),
                fmt_0_1(yaw)
            ),
        );
        Ok(())
    }
}

/// The `x=`, `y=`, `z=`, `roll=`, `pitch=`, `yaw=` parameters (rotations 0..360).
pub(crate) fn position_params() -> Vec<Box<dyn SubCommandParameter>> {
    vec![
        float_prefix_param("x", "x=<new x>"),
        float_prefix_param("y", "y=<new y>"),
        float_prefix_param("z", "z=<new z>"),
        float_range_param("roll", "roll=<new roll degrees>", 0.0, 360.0),
        float_range_param("pitch", "pitch=<new pitch degrees>", 0.0, 360.0),
        float_range_param("yaw", "yaw=<new yaw degrees>", 0.0, 360.0),
    ]
}

/// The optional position parameters over `t` (x, y, z, roll, pitch, yaw in radians).
pub(crate) fn new_position(
    parameters: &Parameters,
    t: &crate::models::transform::Transform,
) -> anyhow::Result<(f32, f32, f32, f32, f32, f32)> {
    let f = |v: &ParameterValue| v.as_f32();
    let x = optional_parameter(parameters, "x", t.x, f)?;
    let y = optional_parameter(parameters, "y", t.y, f)?;
    let z = optional_parameter(parameters, "z", t.z, f)?;
    let yaw = deg_to_rad_f32(optional_parameter(
        parameters,
        "yaw",
        rad_to_deg_f32(t.rot_z),
        f,
    )?);
    let pitch = deg_to_rad_f32(optional_parameter(
        parameters,
        "pitch",
        rad_to_deg_f32(t.rot_y),
        f,
    )?);
    let roll = deg_to_rad_f32(optional_parameter(
        parameters,
        "roll",
        rad_to_deg_f32(t.rot_x),
        f,
    )?);
    Ok((x, y, z, roll, pitch, yaw))
}

/// `NpcPositionSubCommand`.
pub struct NpcPositionSubCommand {
    base: SubCommandBase,
}

impl Default for NpcPositionSubCommand {
    fn default() -> Self {
        let mut params = target_params();
        params.extend(position_params());
        Self {
            base: sub_base(
                "[Npc Position]",
                "Change npc position and angle - All positions are optional use all or only the ones you want to change (Use yaw to rotate npc)",
                &format!("{COMMAND_PREFIX}npc position||pos"),
                params,
            ),
        }
    }
}

impl SubCommand for NpcPositionSubCommand {
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
        let Some(npc) = select_npc(ctx, &self.base, parameters, out, false)? else {
            return Ok(());
        };
        let (t, template_id) = {
            let n = npc.data();
            (n.unit.transform, n.unit.template_id)
        };
        let (x, y, z, roll, pitch, yaw) = new_position(parameters, &t)?;

        self.base.send_message(
            out,
            &format!(
                "Npc ObjId:{} TemplateId:{template_id}, x:{}, y:{}, z:{}, roll:{}°, pitch:{}°, yaw:{}°",
                npc.obj_id,
                f32s(x),
                f32s(y),
                f32s(z),
                fmt_0_1(rad_to_deg_f32(roll)),
                fmt_0_1(rad_to_deg_f32(pitch)),
                fmt_0_1(rad_to_deg_f32(yaw))
            ),
        );

        let npc_rot = {
            let mut n = npc.data();
            n.unit.transform.set_position(x, y, z, roll, pitch, yaw);
            n.unit.transform.roll_pitch_yaw_sbytes()
        };
        let move_type = MoveType {
            flags: MoveTypeFlags::MOVING,
            time: 50, // has to change all the time for normal motion.
            ..MoveType::new(MoveKind::Unit(UnitMove {
                x,
                y,
                z,
                rotation_x: npc_rot.0,
                rotation_y: npc_rot.1,
                rotation_z: npc_rot.2,
                delta_movement: [0, 0, 0],
                stance: GameStanceType::RELAXED, // 0; // combat=0, idle=1
                alertness: MoveTypeAlertness::IDLE, // idle=0, combat=2
                ..UnitMove::default()
            }))
        };
        let server = ctx.server()?;
        let character = ctx.player()?;
        if let Some(world) = parent_world(server, &character) {
            crate::world::broadcast(
                &world,
                &character,
                &SCOneUnitMovementPacket {
                    obj_id: npc.obj_id,
                    move_type,
                },
                true,
            );
        }
        Ok(())
    }
}

/// `NpcRemoveSubCommand`.
pub struct NpcRemoveSubCommand {
    base: SubCommandBase,
}

impl Default for NpcRemoveSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Npc Remove]",
                "Remove a targeted npc or using an npc <ObjId>",
                &format!("{COMMAND_PREFIX}remove"),
                target_params(),
            ),
        }
    }
}

impl SubCommand for NpcRemoveSubCommand {
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
        let Some(npc) = select_npc(ctx, &self.base, parameters, out, true)? else {
            return Ok(());
        };
        let server = ctx.server()?;

        // Remove Npc
        // C#: `npc.Spawner.Id = 0xffffffff` (NullReferenceException without a spawner); removed
        // from the game manually (not saved to npc_spawns_new.json by /save all).
        if npc_spawner_of(server, npc.obj_id).is_none() {
            return Err(nre());
        }
        let template_id = {
            let mut n = npc.data();
            n.spawner_id = 0xffff_ffff;
            n.unit.template_id
        };
        if let Some(world) = world_of_obj(server, &WorldObject::from(&npc)) {
            world.hide(&npc);
        }
        let msg = format!(
            "Npc @NPC_NAME({template_id}), ObjId: {}, TemplateId:{template_id} removed successfully",
            npc.obj_id
        );
        self.base.send_message(out, &msg);
        tracing::warn!("{msg}");
        Ok(())
    }
}

/// `NpcSpawnSubCommand`.
pub struct NpcSpawnSubCommand {
    base: SubCommandBase,
}

impl Default for NpcSpawnSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Npc Spawn]",
                "Spawn one npc in front of the player facing player (default) or a optional direction in degrees",
                &format!("{COMMAND_PREFIX}npc spawn"),
                vec![
                    uint_param("NpcTemplateId", "Npc template Id", true),
                    float_range_param("yaw", "yaw=<facing degrees>", 0.0, 360.0),
                ],
            ),
        }
    }
}

impl SubCommand for NpcSpawnSubCommand {
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
        let npc_template_id = parameters.require("NpcTemplateId")?.as_u32()?;
        let server = ctx.server()?;

        if !server.game_data.systems.npcs.npc.exist(npc_template_id) {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("NPC template {npc_template_id} doesn't exist"),
            );
            return Ok(());
        }

        let self_character = ctx.player()?;
        let me = WorldObject::from(&self_character);
        let world = parent_world(server, &self_character).ok_or_else(nre)?;
        let mut char_pos = clone_detached(server, &me);
        add_distance_to_front(&mut char_pos, 3.0);
        let mut position = spawn_position(&char_pos);

        let default_angle = angle_from(&char_pos, &clone_detached(server, &me)) as f32;
        let angle = deg_to_rad_f32(optional_parameter(parameters, "yaw", default_angle, |v| {
            v.as_f32()
        })?);

        if !parameters.contains_key("yaw") {
            self.base.send_message(
                out,
                &format!(
                    "NPC {npc_template_id} facing you using angle {}°",
                    fmt_0_1(rad_to_deg_f32(angle))
                ),
            );
        } else {
            self.base.send_message(
                out,
                &format!(
                    "NPC {npc_template_id} using angle {}°",
                    fmt_0_1(rad_to_deg_f32(angle))
                ),
            );
        }

        position.yaw = angle;
        position.pitch = 0.0;
        position.roll = 0.0;

        let npc_spawner = add_runtime_npc_spawner(server, &world, npc_template_id, position)?;
        npc_spawner.update(server);
        Ok(())
    }
}

/// `NpcSaveSubCommand`.
pub struct NpcSaveSubCommand {
    base: SubCommandBase,
    /// `_isSavingInProgress` (+ `_saveLock`).
    is_saving_in_progress: AtomicBool,
}

impl Default for NpcSaveSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Npc Save]",
                "Save one or all npc positions in the current character world to the world npc spawns file",
                &format!("{COMMAND_PREFIX}save"),
                vec![
                    choice_param("target", &["all", "id"]),
                    uint_param("ObjId", "object Id", false),
                ],
            ),
            is_saving_in_progress: AtomicBool::new(false),
        }
    }
}

impl NpcSaveSubCommand {
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
        let mut npcs_in_world = current_world.all_npcs();
        npcs_in_world.sort_by_key(|n| n.obj_id);
        let from_file = load_spawns_from_world(
            server,
            &current_world.template.name,
            "npc_spawns",
            SpawnFileKind::Npc,
        )?;
        let has_spawner = |obj_id: u32| npc_spawner_of(server, obj_id).is_some();
        let add_npcs: Vec<_> = npcs_in_world
            .iter()
            .filter(|n| has_spawner(n.obj_id) && n.data().spawner_id == 0)
            .cloned()
            .collect();
        let remove_npcs: Vec<_> = npcs_in_world
            .iter()
            .filter(|n| has_spawner(n.obj_id) && n.data().spawner_id == 0xffff_ffff)
            .cloned()
            .collect();

        // Получаем все npcs и последний идентификатор объекта
        // C#: `allNpcs.Last().ObjId++` (also increments that NPC's ObjId; not mirrored).
        let mut last_obj_id = npcs_in_world
            .last()
            .map(|n| n.obj_id)
            .ok_or_else(|| anyhow::anyhow!("Sequence contains no elements"))?;

        let names = spawn_names(server);
        let remove_keys: Vec<_> = remove_npcs
            .iter()
            .map(|n| {
                let p = world_pos(server, &WorldObject::from(n));
                spawn_key(n.data().unit.template_id, p.x, p.y, None)
            })
            .collect();
        let added: Vec<JsonSpawnEntry> = add_npcs
            .iter()
            .map(|n| {
                let pos = world_pos(server, &WorldObject::from(n));
                let (template_id, scale) = {
                    let n = n.data();
                    (n.unit.template_id, n.unit.scale)
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
                spawn_key(e.unit_id, p.x, p.y, None)
            },
            &remove_keys,
            added,
            |e| {
                if e.follow_path.is_none() {
                    e.follow_path = Some(String::new());
                }
            },
        );

        let json_path_out = world_data_dir(server, &current_world.template.name)
            .join(format!("npc_spawns_all_{}.json.add", file_stamp()));
        std::fs::write(
            &json_path_out,
            spawn_entries_json(&to_file, SpawnFileKind::Npc, true),
        )?;

        let msg = saved_all_message(
            "npcs",
            stopwatch.elapsed().as_millis(),
            initial_count,
            remove_npcs.len(),
            add_npcs.len(),
            to_file.len(),
        );
        self.base.send_message(out, &msg);
        tracing::warn!("{msg}");
        Ok(())
    }

    fn save_by_id(
        &self,
        ctx: &CommandContext<'_>,
        npc_obj_id: u32,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
        let Some(npc) = world.npc(npc_obj_id) else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Npc with objId {npc_obj_id} Does not exist"),
            );
            tracing::info!("Npc with objId {npc_obj_id} Does not exist");
            return Ok(());
        };

        let (t, template_id, scale) = {
            let n = npc.data();
            (n.unit.transform, n.unit.template_id, n.unit.scale)
        };
        let Some(world) = server.world.world(t.instance_id) else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Could not find the worldId {}", t.instance_id),
            );
            tracing::info!("Could not find the worldId {}", t.instance_id);
            return Ok(());
        };

        let names = spawn_names(server);
        let spawn = JsonSpawnEntry {
            id: npc.obj_id,
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
            ..JsonSpawnEntry::default()
        };

        let from_file = load_spawns_from_world(
            server,
            &world.template.name,
            "npc_spawns",
            SpawnFileKind::Npc,
        )?;
        let list = merge_by_id(from_file, spawn);

        let json_path_out = world_data_dir(server, &world.template.name)
            .join(format!("npc_spawns_add_{}.json.add", file_stamp()));
        std::fs::write(
            &json_path_out,
            spawn_entries_json(&list, SpawnFileKind::Npc, true),
        )?;
        let msg = format!(
            "All npcs have been saved with added npc ObjId:{}, TemplateId:{template_id}",
            npc.obj_id
        );
        self.base.send_message(out, &msg);
        tracing::info!("{msg}");
        Ok(())
    }
}

impl SubCommand for NpcSaveSubCommand {
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

/// `NpcCmd`.
pub fn npc_cmd() -> RootCommand {
    RootCommand::new(&["npc"], "[Npc]", "Root command to manage Npcs", "")
        .register(Arc::new(NpcInformationSubCommand::default()), &["info"])
        .register(
            Arc::new(NpcPositionSubCommand::default()),
            &["position", "pos"],
        )
        .register(Arc::new(NpcSaveSubCommand::default()), &["save"])
        .register(Arc::new(NpcSpawnSubCommand::default()), &["spawn"])
        .register(Arc::new(NpcRemoveSubCommand::default()), &["remove"])
}

#[cfg(test)]
mod tests {
    use super::super::super::harness;
    use super::super::super::util::objects_test_support as ts;
    use super::super::super::util::*;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn npc_info_position_remove() {
        let server = ts::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let npc = ts::spawn_npc(&server, ts::NPC, 110.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, "npc info target").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Npc Information] You need to target a Npc first|r"]
        );
        gm.p.data().unit.current_target = npc.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "npc info target").await;
        let info = format!(
            "[Npc Information] Name:@NPC_NAME({0}) ObjId:{1} TemplateId:{0}, x:110, y:100, z:100, roll:0°, pitch:0°, yaw:0°",
            ts::NPC,
            npc.obj_id
        );
        assert_eq!(out, std::slice::from_ref(&info));
        let (_, out) = harness::run(&server, &gm.p, &format!("npc info id {}", npc.obj_id)).await;
        assert_eq!(out, [info]);
        let (_, out) = harness::run(&server, &gm.p, "npc info id 5").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Npc Information] Npc with objId 5 does not exist|r"]
        );

        gm.packets();
        let (_, out) = harness::run(&server, &gm.p, "npc pos target x=111 yaw=90").await;
        assert_eq!(
            out,
            [format!(
                "[Npc Position] Npc ObjId:{} TemplateId:{}, x:111, y:100, z:100, roll:0°, pitch:0°, yaw:90°",
                npc.obj_id,
                ts::NPC
            )]
        );
        assert_eq!(npc.data().unit.transform.x, 111.0);
        assert!(gm
            .packets()
            .iter()
            .any(|p| p.0 == sc::SCOneUnitMovementPacket));

        // No spawner: `npc.Spawner.Id = ..` throws.
        let (_, out) = harness::run(&server, &gm.p, "npc remove target").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Npc Remove] Unexpected error: Object reference not set to an instance of an object.|r"]
        );
    }

    #[tokio::test]
    async fn npc_spawn_and_save() {
        let server = ts::server();
        let app = ts::temp_app_path(&server, "npc_save");
        let world = server.world.world(0).unwrap();
        let wdir = app.join("Data").join("Worlds").join(&world.template.name);
        std::fs::create_dir_all(&wdir).unwrap();
        std::fs::write(
            wdir.join("npc_spawns.json"),
            r#"[{"Id": 1, "UnitId": 9, "Position": {"X": 1, "Y": 2, "Z": 3}}]"#,
        )
        .unwrap();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, &format!("npc spawn {}", ts::NPC)).await;
        assert_eq!(
            out,
            [format!(
                "[Npc Spawn] NPC {} facing you using angle -90°",
                ts::NPC
            )]
        );
        crate::systems::npcs::spawn_manager::active_region_tick(&server);
        let npcs = world.all_npcs();
        assert_eq!(npcs.len(), 1);
        let npc = npcs[0].clone();
        assert!((npc.data().unit.transform.y - 103.0).abs() < 1e-4);

        let (_, out) = harness::run(&server, &gm.p, "npc spawn 1 yaw=500").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Npc Spawn] The number 500 should be between 0 and 360 for parameter: yaw=<facing degrees>|r"]
        );

        // C#: only spawners with `Id == 0` count as added (the spawn command's clones get
        // `_nextId`): simulate a manual spawn.
        npc.data().spawner_id = 0;
        let (_, out) = harness::run(&server, &gm.p, "npc save all").await;
        assert!(out[0]
            .ends_with(" ms\nInitial count: 1\nRemoved count: 0\nAdded count: 1\nFinal count: 2"));
        let f = std::fs::read_dir(&wdir)
            .unwrap()
            .filter_map(Result::ok)
            .find(|e| {
                e.file_name()
                    .to_string_lossy()
                    .starts_with("npc_spawns_all_")
            })
            .unwrap();
        let text = std::fs::read_to_string(f.path()).unwrap();
        let entries = parse_spawn_entries(&text, SpawnFileKind::Npc)
            .unwrap()
            .unwrap();
        assert_eq!(entries.len(), 2);
        assert_eq!(entries[0].follow_path.as_deref(), Some(""));
        assert_eq!(entries[1].id, npc.obj_id);
        assert!(text.contains("\"FollowPath\": null"));

        let (_, out) = harness::run(&server, &gm.p, &format!("npc save id {}", npc.obj_id)).await;
        assert_eq!(
            out,
            [format!(
                "[Npc Save] All npcs have been saved with added npc ObjId:{}, TemplateId:{}",
                npc.obj_id,
                ts::NPC
            )]
        );

        // With a spawner now: remove hides it and marks the spawner removed.
        let (_, out) = harness::run(&server, &gm.p, &format!("npc remove id {}", npc.obj_id)).await;
        assert_eq!(
            out,
            [format!(
                "[Npc Remove] Npc @NPC_NAME({0}), ObjId: {1}, TemplateId:{0} removed successfully",
                ts::NPC,
                npc.obj_id
            )]
        );
        assert!(!npc.is_visible());
        assert_eq!(npc.data().spawner_id, 0xffff_ffff);
    }
}
