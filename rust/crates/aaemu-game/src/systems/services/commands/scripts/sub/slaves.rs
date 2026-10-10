//! `Scripts/SubCommands/Slaves/**` and `Scripts/Commands/SlaveCmd.cs`.

use futures::future::BoxFuture;

use super::super::prelude::*;
use super::doodads::save::merge_by_id;
use super::doodads::{choice_param, float_range_param, sub_base, uint_param};
use super::npcs::{new_position, position_params, target_params};
use crate::utils::json_models::JsonPosition;
use crate::utils::numeric::rad_to_deg_f32;
use crate::world::SlaveRef;

/// `Models.Game.Units.Slave` (cast exception target).
const SLAVE_TYPE: &str = "AAEmu.Game.Models.Game.Units.Slave";

/// `(Slave)ParentWorld.GetGameObject(objId)`: `Ok(None)` = null, `Err` = invalid cast.
fn slave_by_obj_id(ctx: &CommandContext<'_>, obj_id: u32) -> anyhow::Result<Option<SlaveRef>> {
    let server = ctx.server()?;
    let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
    match world.object(obj_id) {
        None => Ok(None),
        Some(WorldObject::Slave(s)) => Ok(Some(s)),
        Some(other) => Err(invalid_cast(&other, SLAVE_TYPE)),
    }
}

/// The `ObjId` parameter or the current target (`Slave`), with the C# replies.
fn select_slave(
    ctx: &CommandContext<'_>,
    base: &SubCommandBase,
    parameters: &Parameters,
    out: &mut dyn MessageOutput,
) -> anyhow::Result<Option<SlaveRef>> {
    if let Some(v) = parameters.get("ObjId") {
        let obj_id = v.as_u32()?;
        let slave = slave_by_obj_id(ctx, obj_id)?;
        if slave.is_none() {
            base.send_color_message(
                out,
                Color::RED,
                &format!("Slave with objId {obj_id} does not exist"),
            );
        }
        return Ok(slave);
    }
    let server = ctx.server()?;
    let slave = current_target(server, &ctx.player()?).and_then(|t| t.as_slave().cloned());
    if slave.is_none() {
        base.send_color_message(out, Color::RED, "You need to target a Slave first");
    }
    Ok(slave)
}

/// `SlaveInformationSubCommand`.
pub struct SlaveInformationSubCommand {
    base: SubCommandBase,
}

impl Default for SlaveInformationSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Slave Information]",
                "Get all slave information from a Slave (Targeted or by Id)",
                &format!("{COMMAND_PREFIX}slave info"),
                target_params(),
            ),
        }
    }
}

impl SubCommand for SlaveInformationSubCommand {
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
        let Some(slave) = select_slave(ctx, &self.base, parameters, out)? else {
            return Ok(());
        };
        let (t, template_id) = {
            let s = slave.data();
            (s.unit.transform, s.unit.template_id)
        };
        let yaw = rad_to_deg_f32(t.rot_z);
        let pitch = rad_to_deg_f32(t.rot_y);
        let roll = rad_to_deg_f32(t.rot_x);

        //TODO: There is much more potential information to show on this command.
        self.base.send_message(
            out,
            &format!(
                "Name:@NPC_NAME({template_id}) ObjId:{} TemplateId:{template_id}, x:{}, y:{}, z:{}, roll:{}°, pitch:{}°, yaw:{}°",
                slave.obj_id,
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

/// `SlavePositionSubCommand`.
pub struct SlavePositionSubCommand {
    base: SubCommandBase,
}

impl Default for SlavePositionSubCommand {
    fn default() -> Self {
        let mut params = target_params();
        params.extend(position_params());
        Self {
            base: sub_base(
                "[Slave Position]",
                "Change slave position and angle - All positions are optional use all or only the ones you want to change (Use yaw to rotate Slave)",
                &format!("{COMMAND_PREFIX}slave position||pos"),
                params,
            ),
        }
    }
}

impl SubCommand for SlavePositionSubCommand {
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
        let Some(slave) = select_slave(ctx, &self.base, parameters, out)? else {
            return Ok(());
        };
        let (t, template_id) = {
            let s = slave.data();
            (s.unit.transform, s.unit.template_id)
        };
        let (x, y, z, roll, pitch, yaw) = new_position(parameters, &t)?;

        self.base.send_message(
            out,
            &format!(
                "Slave ObjId:{} TemplateId:{template_id}, x:{}, y:{}, z:{}, roll:{}°, pitch:{}°, yaw:{}°",
                slave.obj_id,
                f32s(x),
                f32s(y),
                f32s(z),
                fmt_0_1(rad_to_deg_f32(roll)),
                fmt_0_1(rad_to_deg_f32(pitch)),
                fmt_0_1(rad_to_deg_f32(yaw))
            ),
        );

        slave
            .data()
            .unit
            .transform
            .set_position(x, y, z, roll, pitch, yaw);
        let server = ctx.server()?;
        let obj = WorldObject::from(&slave);
        if let Some(world) = world_of_obj(server, &obj) {
            hide_show(server, &world, &obj);
        }
        Ok(())
    }
}

/// `SlaveRemoveSubCommand`.
pub struct SlaveRemoveSubCommand {
    base: SubCommandBase,
}

impl Default for SlaveRemoveSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Slave Remove]",
                "Remove a targeted slave or using an slave <ObjId>",
                &format!("{COMMAND_PREFIX}slave remove"),
                target_params(),
            ),
        }
    }
}

impl SubCommand for SlaveRemoveSubCommand {
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
        let Some(slave) = select_slave(ctx, &self.base, parameters, out)? else {
            return Ok(());
        };

        // Remove Slave
        let (name, template_id) = {
            let mut s = slave.data();
            if s.spawner_id != 0 {
                // removed from the game manually (not saved by /save all)
                s.spawner_id = 0xffff_ffff;
            }
            (s.unit.name.clone(), s.unit.template_id)
        };

        let server = ctx.server()?;
        if let Some(world) = world_of_obj(server, &WorldObject::from(&slave)) {
            world.hide(&slave);
        }
        self.base.send_message(
            out,
            &format!(
                "Slave ({name}), ObjId: {}, TemplateId:{template_id} removed successfully",
                slave.obj_id
            ),
        );
        Ok(())
    }
}

/// `SlaveSpawnSubCommand`.
pub struct SlaveSpawnSubCommand {
    base: SubCommandBase,
}

impl Default for SlaveSpawnSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Slave Spawn]",
                "Spawn one slave in front of the player facing player (default) or a optional direction in degrees",
                &format!("{COMMAND_PREFIX}slave spawn"),
                vec![
                    uint_param("TemplateId", "Slave template Id", true),
                    float_range_param("yaw", "yaw=<facing degrees>", 0.0, 360.0),
                ],
            ),
        }
    }
}

impl SubCommand for SlaveSpawnSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        _trigger: &'a str,
        parameters: &'a Parameters,
        out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(async move {
            let template_id = parameters.require("TemplateId")?.as_u32()?;
            let server = ctx.server()?;
            let owner = ctx.player()?;

            if server
                .game_data
                .systems
                .vehicles
                .slave_template(template_id)
                .is_none()
            {
                self.base.send_color_message(
                    out,
                    Color::RED,
                    &format!("Slave template {template_id} doesn't exist"),
                );
                return Ok(());
            }

            let world = parent_world(server, &owner).ok_or_else(nre)?;
            crate::systems::vehicles::slaves::create(
                server,
                &world,
                Some(&owner),
                template_id,
                None,
                false,
                None,
            )
            .await;
            Ok(())
        })
    }
}

/// `SlaveSaveSubCommand`.
pub struct SlaveSaveSubCommand {
    base: SubCommandBase,
}

impl Default for SlaveSaveSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Slave Save]",
                "Save one or all slave positions in the current character world to the world slave spawns file",
                &format!("{COMMAND_PREFIX}slave save"),
                vec![
                    choice_param("target", &["all", "id"]),
                    uint_param("ObjId", "object Id", false),
                ],
            ),
        }
    }
}

/// `LoadSlavesFromFileByWorld(worldInstance)`.
fn load_slaves_from_file_by_world(
    server: &GameServer,
    world_name: &str,
) -> anyhow::Result<Vec<JsonSpawnEntry>> {
    let json_path_in = world_data_dir(server, world_name).join("slave_spawns.json");
    if !json_path_in.is_file() {
        anyhow::bail!("File {} doesn't exists.", json_path_in.display());
    }
    let contents = file_contents(&json_path_in);
    tracing::info!("Loading spawns from file {} ...", json_path_in.display());
    if is_blank(&contents) {
        return Ok(Vec::new());
    }
    // C#: `JsonHelper.DeserializeObject` returning null for a `null` document.
    parse_spawn_entries(&contents, SpawnFileKind::Slave)
        .map_err(anyhow::Error::msg)?
        .ok_or_else(nre)
}

impl SlaveSaveSubCommand {
    fn save_all(
        &self,
        ctx: &CommandContext<'_>,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let current_world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
        let mut slaves_in_world = crate::systems::vehicles::slaves::all_slaves(&current_world);
        slaves_in_world.sort_by_key(|s| s.obj_id);

        let mut slave_spawners_to_file =
            load_slaves_from_file_by_world(server, &current_world.template.name)?;

        // Rust: `Slave.spawner_id` 0 means "no spawner", so the C# `case 0` (a spawner with Id 0,
        // spawned into the game manually) cannot occur.
        for npc in &slaves_in_world {
            let (spawner_id, template_id) = {
                let s = npc.data();
                (s.spawner_id, s.unit.template_id)
            };
            if spawner_id != 0xffff_ffff {
                continue;
            }
            // removed from the game manually: do not add it to the output
            let p = world_pos(server, &WorldObject::from(npc));
            let mut to_remove = None;
            for (i, s) in slave_spawners_to_file.iter().enumerate() {
                if s.unit_id != template_id {
                    continue;
                }
                // `slaveSpawnerToFile.Position.AsVector3()` (null position: exception).
                let sp = s.position.ok_or_else(nre)?;
                // If the position changed don't mark to be removed
                if !(sp.x == p.x && sp.y == p.y && sp.z == p.z) {
                    continue;
                }
                to_remove = Some(i);
                break;
            }
            if let Some(i) = to_remove {
                slave_spawners_to_file.remove(i);
            }
        }

        let json_path_out =
            world_data_dir(server, &current_world.template.name).join("slave_spawns_new.json");
        std::fs::write(
            &json_path_out,
            spawn_entries_json(&slave_spawners_to_file, SpawnFileKind::Slave, true),
        )?;
        self.base.send_message(out, "All slaves have been saved!");
        Ok(())
    }

    fn save_by_id(
        &self,
        ctx: &CommandContext<'_>,
        obj_id: u32,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let Some(slave) = slave_by_obj_id(ctx, obj_id)? else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Slave with objId {obj_id} Does not exist"),
            );
            return Ok(());
        };

        let server = ctx.server()?;
        let (t, template_id) = {
            let s = slave.data();
            (s.unit.transform, s.unit.template_id)
        };
        let Some(world) = server.world.world(t.instance_id) else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Could not find the worldId {}", t.instance_id),
            );
            return Ok(());
        };

        let spawn = JsonSpawnEntry {
            id: slave.obj_id,
            unit_id: template_id,
            position: Some(JsonPosition {
                x: t.x,
                y: t.y,
                z: t.z,
                roll: rad_to_deg_f32(t.rot_x),
                pitch: rad_to_deg_f32(t.rot_y),
                yaw: rad_to_deg_f32(t.rot_z),
            }),
            ..JsonSpawnEntry::default()
        };

        let list = merge_by_id(
            load_slaves_from_file_by_world(server, &world.template.name)?,
            spawn,
        );

        let json_path_out =
            world_data_dir(server, &world.template.name).join("slave_spawns_new.json");
        std::fs::write(
            &json_path_out,
            spawn_entries_json(&list, SpawnFileKind::Slave, true),
        )?;
        self.base.send_message(
            out,
            &format!(
                "All slaves have been saved with added slave ObjId:{}, TemplateId:{template_id}",
                slave.obj_id
            ),
        );
        Ok(())
    }
}

impl SubCommand for SlaveSaveSubCommand {
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
        match parameters.get("ObjId") {
            Some(v) => self.save_by_id(ctx, v.as_u32()?, out),
            None => self.save_all(ctx, out),
        }
    }
}

/// `SlaveCmd`.
pub fn slave_cmd() -> RootCommand {
    RootCommand::new(&["slave"], "[slave]", "Root command to manage Slaves", "")
        .register(Arc::new(SlaveInformationSubCommand::default()), &["info"])
        .register(
            Arc::new(SlavePositionSubCommand::default()),
            &["position", "pos"],
        )
        .register(Arc::new(SlaveSaveSubCommand::default()), &["save"])
        .register(Arc::new(SlaveSpawnSubCommand::default()), &["spawn"])
        .register(Arc::new(SlaveRemoveSubCommand::default()), &["remove"])
}

#[cfg(test)]
mod tests {
    use super::super::super::harness;
    use super::super::super::util::objects_test_support as ts;
    use super::super::super::util::*;

    #[tokio::test]
    async fn slave_spawn_info_position_save_remove() {
        let server = ts::server();
        let app = ts::temp_app_path(&server, "slave_save");
        let world = server.world.world(0).unwrap();
        let wdir = app.join("Data").join("Worlds").join(&world.template.name);
        std::fs::create_dir_all(&wdir).unwrap();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, "slave spawn 4242").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Slave Spawn] Slave template 4242 doesn't exist|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, &format!("slave spawn {}", ts::SLAVE)).await;
        assert!(out.is_empty());
        let slaves = crate::systems::vehicles::slaves::all_slaves(&world);
        assert_eq!(slaves.len(), 1);
        let slave = slaves[0].clone();
        let id = slave.obj_id;

        let (_, out) = harness::run(&server, &gm.p, &format!("slave info id {id}")).await;
        assert!(out[0].starts_with(&format!(
            "[Slave Information] Name:@NPC_NAME({0}) ObjId:{id} TemplateId:{0}, x:",
            ts::SLAVE
        )));
        let (_, out) = harness::run(&server, &gm.p, "slave info target").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Slave Information] You need to target a Slave first|r"]
        );
        // A non-slave object: InvalidCastException.
        let (_, out) =
            harness::run(&server, &gm.p, &format!("slave info id {}", gm.p.obj_id)).await;
        assert_eq!(
            out,
            ["|cFFFF0000[Slave Information] Unexpected error: Unable to cast object of type 'AAEmu.Game.Models.Game.Char.Character' to type 'AAEmu.Game.Models.Game.Units.Slave'.|r"]
        );

        let (_, out) = harness::run(
            &server,
            &gm.p,
            &format!("slave pos id {id} x=50 y=60 z=70 yaw=0"),
        )
        .await;
        assert_eq!(
            out,
            [format!(
                "[Slave Position] Slave ObjId:{id} TemplateId:{}, x:50, y:60, z:70, roll:0°, pitch:0°, yaw:0°",
                ts::SLAVE
            )]
        );

        // Save needs slave_spawns.json.
        let (_, out) = harness::run(&server, &gm.p, "slave save all").await;
        assert_eq!(
            out,
            [format!(
                "|cFFFF0000[Slave Save] Unexpected error: File {} doesn't exists.|r",
                wdir.join("slave_spawns.json").display()
            )]
        );
        std::fs::write(
            wdir.join("slave_spawns.json"),
            r#"[{"Id": 3, "UnitId": 1, "Position": {"X": 1, "Y": 2, "Z": 3}}]"#,
        )
        .unwrap();
        let (_, out) = harness::run(&server, &gm.p, &format!("slave save id {id}")).await;
        assert_eq!(
            out,
            [format!(
                "[Slave Save] All slaves have been saved with added slave ObjId:{id}, TemplateId:{}",
                ts::SLAVE
            )]
        );
        let text = std::fs::read_to_string(wdir.join("slave_spawns_new.json")).unwrap();
        let entries = parse_spawn_entries(&text, SpawnFileKind::Slave)
            .unwrap()
            .unwrap();
        assert_eq!(entries.iter().map(|e| e.id).collect::<Vec<_>>(), [3, id]);
        assert_eq!(entries[1].position.unwrap().x, 50.0);
        let (_, out) = harness::run(&server, &gm.p, "slave save all").await;
        assert_eq!(out, ["[Slave Save] All slaves have been saved!"]);

        let (_, out) = harness::run(&server, &gm.p, &format!("slave remove id {id}")).await;
        assert_eq!(
            out,
            [format!(
                "[Slave Remove] Slave ({}), ObjId: {id}, TemplateId:{} removed successfully",
                slave.data().unit.name,
                ts::SLAVE
            )]
        );
        assert!(!slave.is_visible());
    }
}
