//! `Scripts/SubCommands/Doodads/**` and `Scripts/Commands/DoodadCmd.cs`.

mod remove;
pub(crate) mod save;

use super::super::prelude::*;
use crate::systems::npcs::doodad::manager as doodad_manager;
use crate::utils::numeric::{deg_to_rad_f32, rad_to_deg_f32};

pub use remove::{DoodadRemoveSubCommand, DoodadRemovesSubCommand};
pub use save::DoodadSaveSubCommand;

/// A `SubCommandBase` with `Title`, `Description`, `CallPrefix` and its parameters.
pub(crate) fn sub_base(
    title: &str,
    description: &str,
    call_prefix: &str,
    params: Vec<Box<dyn SubCommandParameter>>,
) -> SubCommandBase {
    let mut b = SubCommandBase::new(title, description, call_prefix);
    for p in params {
        b.add_parameter(p)
            .expect("sub command parameters are declared in a valid order");
    }
    b
}

/// `NumericSubCommandParameter<uint>(name, displayName, required)`.
pub(crate) fn uint_param(
    name: &str,
    display: &str,
    required: bool,
) -> Box<dyn SubCommandParameter> {
    Box::new(NumericSubCommandParameter::<u32>::new(
        name,
        Some(display),
        required,
    ))
}

/// `NumericSubCommandParameter<float>(name, displayName, false, prefix)`.
pub(crate) fn float_prefix_param(name: &str, display: &str) -> Box<dyn SubCommandParameter> {
    Box::new(NumericSubCommandParameter::<f32>::with_prefix(
        name,
        Some(display),
        false,
        Some(name),
    ))
}

/// `NumericSubCommandParameter<float>(name, displayName, false, prefix, min, max)`.
pub(crate) fn float_range_param(
    name: &str,
    display: &str,
    min: f32,
    max: f32,
) -> Box<dyn SubCommandParameter> {
    Box::new(
        NumericSubCommandParameter::<f32>::with_range(
            name,
            Some(display),
            false,
            Some(name),
            min,
            max,
        )
        .expect("valid range"),
    )
}

/// `StringSubCommandParameter(name, displayName, true, values...)`.
pub(crate) fn choice_param(name: &str, values: &[&str]) -> Box<dyn SubCommandParameter> {
    Box::new(StringSubCommandParameter::new(
        name,
        Some(name),
        true,
        values,
    ))
}

/// `((Character)character).ParentWorld.GetDoodad(objId)`.
fn doodad_by_obj_id(
    ctx: &CommandContext<'_>,
    obj_id: u32,
) -> anyhow::Result<(
    Arc<crate::world::WorldInstance>,
    Option<crate::world::DoodadRef>,
)> {
    let server = ctx.server()?;
    let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
    let doodad = world.doodad(obj_id);
    Ok((world, doodad))
}

/// `DoodadChainSubCommand`.
pub struct DoodadChainSubCommand {
    base: SubCommandBase,
}

impl Default for DoodadChainSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad Chain]",
                "Show all subrelated properties of a Doodad",
                &format!("{COMMAND_PREFIX}doodad chain"),
                vec![uint_param("templateId", "Template Id", true)],
            ),
        }
    }
}

impl SubCommand for DoodadChainSubCommand {
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
        let template_id = parameters.require("templateId")?.as_u32()?;
        let server = ctx.server()?;
        let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
        // C#: `DoodadManager.Create(world, 0, templateId)` (a new object id and the phase
        // initialization of a doodad that is never spawned).
        if doodad_manager::create(server, &world, 0, template_id, None, false).is_none() {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad with templateId {template_id} was not found"),
            );
            return Ok(());
        }

        self.base.send_message(out, "Phase chain, see the log");
        let title = &self.base.title;
        tracing::warn!("{title} Chain: TemplateId {template_id}");

        let data = &server.game_data.systems.npcs.doodad;
        for group in data.doodad_func_groups(template_id) {
            // Display all functions that are available
            let func_group_id = group.id;
            tracing::info!("{title} FuncGroupId: {func_group_id}");
            // Get all doodad_phase_funcs
            let phase_funcs = data.phase_funcs(func_group_id);
            if phase_funcs.is_empty() {
                tracing::info!("{title} PhaseFunc: GroupId 0, FuncId 0");
            } else {
                for phase_func in phase_funcs {
                    let _ = phase_func;
                    tracing::info!("{title} PhaseFunc: GroupId 0, FuncId 1, FuncType 2");
                }
            }

            // Get all doodad_funcs
            let funcs = data.doodad_funcs(func_group_id);
            if funcs.is_empty() {
                tracing::info!("{title} Func: GroupId 0, FuncId 0");
            } else {
                for func in funcs {
                    let _ = func;
                    tracing::info!(
                        "{title} Func: GroupId 0, FuncId 1, FuncType 2, NextPhase 3, Skill 4"
                    );
                }
            }
        }
        Ok(())
    }
}

/// `DoodadPhaseListSubCommand`.
pub struct DoodadPhaseListSubCommand {
    base: SubCommandBase,
}

impl Default for DoodadPhaseListSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad Phase List]",
                "List all the phases of a given doodad",
                &format!("{COMMAND_PREFIX}doodad phase list"),
                vec![uint_param("ObjId", "Object Id", true)],
            ),
        }
    }
}

impl SubCommand for DoodadPhaseListSubCommand {
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
        let doodad_obj_id = parameters.require("ObjId")?.as_u32()?;
        let (_, doodad) = doodad_by_obj_id(ctx, doodad_obj_id)?;
        let Some(doodad) = doodad else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad with objId {doodad_obj_id} Does not exist"),
            );
            return Ok(());
        };

        let template_id = doodad.data().template_id;
        let available_phases = ctx
            .server()?
            .game_data
            .systems
            .npcs
            .doodad
            .func_groups_ids(template_id)
            .iter()
            .map(u32::to_string)
            .collect::<Vec<_>>()
            .join(", ");

        self.base.send_message(
            out,
            &format!(
                "TemplateId {template_id}: ObjId{}, Available phase ids (func groups): {available_phases}",
                doodad.obj_id
            ),
        );
        tracing::warn!(
            "{} Chain: TemplateId {template_id}, doodadObjId {},  Available phase ids (func groups): {available_phases}",
            self.base.title,
            doodad.obj_id
        );
        Ok(())
    }
}

/// `DoodadPhaseChangeSubCommand`.
pub struct DoodadPhaseChangeSubCommand {
    base: SubCommandBase,
}

impl Default for DoodadPhaseChangeSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad Phase Change]",
                "Change the phase of a given doodad",
                &format!("{COMMAND_PREFIX}doodad phase change"),
                vec![
                    uint_param("ObjId", "Object Id", true),
                    Box::new(NumericSubCommandParameter::<i32>::new(
                        "PhaseId",
                        Some("Phase Id"),
                        true,
                    )),
                ],
            ),
        }
    }
}

impl SubCommand for DoodadPhaseChangeSubCommand {
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
        let doodad_obj_id = parameters.require("ObjId")?.as_u32()?;
        let phase_id = parameters.require("PhaseId")?.as_i32()?;
        let (world, doodad) = doodad_by_obj_id(ctx, doodad_obj_id)?;
        if doodad.is_none() {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad with objId {doodad_obj_id} Does not exist"),
            );
        }
        // C#: no return after the message, `doodad.TemplateId` throws.
        let doodad = doodad.ok_or_else(nre)?;

        let server = ctx.server()?;
        let template_id = doodad.data().template_id;
        let available_phases = server
            .game_data
            .systems
            .npcs
            .doodad
            .func_groups_ids(template_id)
            .iter()
            .map(u32::to_string)
            .collect::<Vec<_>>()
            .join(", ");

        self.base.send_message(out, &format!("SetPhase {phase_id}"));
        self.base.send_message(
            out,
            &format!(
                "TemplateId {template_id}: ObjId:{}, ChangedPhase:{phase_id}, Available phase ids (func groups): {available_phases}",
                doodad.obj_id
            ),
        );
        tracing::warn!(
            "{} Chain: TemplateId {template_id}, doodadObjId {}, SetPhase {phase_id}, Available phase ids (func groups): {available_phases}",
            self.base.title,
            doodad.obj_id
        );
        let caster = WorldObject::from(&ctx.player()?);
        doodad_manager::do_change_phase(server, &world, &doodad, Some(&caster), phase_id);
        Ok(())
    }
}

/// `DoodadPhaseSubCommand`.
pub struct DoodadPhaseSubCommand {
    base: SubCommandBase,
}

impl Default for DoodadPhaseSubCommand {
    fn default() -> Self {
        let mut base = SubCommandBase::new(
            "[Doodad Phase]",
            "Allow phase operations on a doodad",
            &format!("{COMMAND_PREFIX}doodad phase"),
        );
        base.register(Arc::new(DoodadPhaseListSubCommand::default()), &["list"]);
        base.register(
            Arc::new(DoodadPhaseChangeSubCommand::default()),
            &["change"],
        );
        Self { base }
    }
}

impl SubCommand for DoodadPhaseSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }
}

/// `DoodadPositionSubCommand`.
pub struct DoodadPositionSubCommand {
    base: SubCommandBase,
}

impl Default for DoodadPositionSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad Position]",
                "Manage Doodad positioning in the world. All positions are optional use all or only the ones you want to change",
                &format!("{COMMAND_PREFIX}doodad position||pos"),
                vec![
                    uint_param("ObjId", "Object Id", true),
                    float_prefix_param("x", "x=<new x>"),
                    float_prefix_param("y", "y=<new y>"),
                    float_prefix_param("z", "z=<new z>"),
                    float_range_param("roll", "roll=<new roll degrees>", -360.0, 360.0),
                    float_range_param("pitch", "pitch=<new pitch degrees>", -360.0, 360.0),
                    float_range_param("yaw", "yaw=<new yaw degrees>", -360.0, 360.0),
                ],
            ),
        }
    }
}

impl SubCommand for DoodadPositionSubCommand {
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
        let doodad_obj_id = parameters.require("ObjId")?.as_u32()?;
        let (world, doodad) = doodad_by_obj_id(ctx, doodad_obj_id)?;
        let Some(doodad) = doodad else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad with objId {doodad_obj_id} Does not exist"),
            );
            return Ok(());
        };

        let (t, template_id) = {
            let d = doodad.data();
            (d.transform, d.template_id)
        };
        let f = |v: &ParameterValue| v.as_f32();
        let x = optional_parameter(parameters, "x", t.x, f)?;
        let y = optional_parameter(parameters, "y", t.y, f)?;
        let z = optional_parameter(parameters, "z", t.z, f)?;
        let roll = deg_to_rad_f32(optional_parameter(
            parameters,
            "roll",
            rad_to_deg_f32(t.rot_x),
            f,
        )?);
        let pitch = deg_to_rad_f32(optional_parameter(
            parameters,
            "pitch",
            rad_to_deg_f32(t.rot_y),
            f,
        )?);
        let yaw = deg_to_rad_f32(optional_parameter(
            parameters,
            "yaw",
            rad_to_deg_f32(t.rot_z),
            f,
        )?);

        self.base.send_message(
            out,
            &format!(
                "Doodad ObjId: {} TemplateId:{template_id}, x:{}, y:{}, z:{}, roll:{}°, pitch:{}°, yaw:{}°",
                doodad.obj_id,
                f32s(x),
                f32s(y),
                f32s(z),
                fmt_0_1(rad_to_deg_f32(roll)),
                fmt_0_1(rad_to_deg_f32(pitch)),
                fmt_0_1(rad_to_deg_f32(yaw))
            ),
        );

        doodad
            .data()
            .transform
            .set_position(x, y, z, roll, pitch, yaw);

        hide_show(ctx.server()?, &world, &WorldObject::from(&doodad));
        Ok(())
    }
}

/// `DoodadSpawnSubCommand`.
pub struct DoodadSpawnSubCommand {
    base: SubCommandBase,
}

impl Default for DoodadSpawnSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad Spawn]",
                "Add a new doodad of a specific template 3 meters in front of the player. Default yaw will use characters facing angle.",
                &format!("{COMMAND_PREFIX}doodad spawn"),
                vec![
                    uint_param("templateId", "template id", true),
                    float_prefix_param("yaw", "yaw=<yaw facing degrees>"),
                ],
            ),
        }
    }
}

impl SubCommand for DoodadSpawnSubCommand {
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
        let unit_template_id = parameters.require("templateId")?.as_u32()?;
        let server = ctx.server()?;
        if !server.game_data.systems.npcs.doodad.exist(unit_template_id) {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad templateId:{unit_template_id} don't exist"),
            );
            return Ok(());
        }

        let character = ctx.player()?;
        let me = WorldObject::from(&character);
        let mut char_pos = clone_detached(server, &me);
        add_distance_to_front(&mut char_pos, 3.0);
        let default_yaw = angle_from(&char_pos, &clone_detached(server, &me)) as f32;
        let new_yaw = deg_to_rad_f32(optional_parameter(parameters, "yaw", default_yaw, |v| {
            v.as_f32()
        })?);
        let world = parent_world(server, &character).ok_or_else(nre)?;
        let mut position = spawn_position(&char_pos);
        position.yaw = new_yaw;
        position.pitch = 0.0;
        position.roll = 0.0;
        let spawner = new_doodad_spawner(world.id, unit_template_id, position);
        let created_doodad = spawner
            .spawn_for_character(server, 0, 0, character.obj_id)
            .ok_or_else(nre)?;

        if parameters.contains_key("yaw") {
            ctx.character.send_message(&format!(
                "Doodad ObjId:{}, Template:{unit_template_id} spawned using yaw {}° = {} rad",
                created_doodad.obj_id,
                fmt_0_1(rad_to_deg_f32(new_yaw)),
                f32s(new_yaw)
            ));
        } else {
            ctx.character.send_message(&format!(
                "Doodad ObjId:{}, Template {unit_template_id} spawned facing you, characters yaw {}°",
                created_doodad.obj_id,
                fmt_0_1(rad_to_deg_f32(new_yaw))
            ));
        }
        Ok(())
    }
}

/// `DoodadSetAgeCommand`.
pub struct DoodadSetAgeCommand {
    base: SubCommandBase,
}

impl Default for DoodadSetAgeCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad SetAge]",
                "Changes a doodads age in seconds (1h = 3600, 1d = 86400)",
                &format!("{COMMAND_PREFIX}doodad age||setage"),
                vec![
                    uint_param("ObjId", "Object Id", true),
                    uint_param("Age", "New Age Time", true),
                ],
            ),
        }
    }
}

impl SubCommand for DoodadSetAgeCommand {
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
        let doodad_obj_id = parameters.require("ObjId")?.as_u32()?;
        let new_age = parameters.require("Age")?.as_u32()?;
        let (world, doodad) = doodad_by_obj_id(ctx, doodad_obj_id)?;
        let Some(doodad) = doodad else {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Doodad with objId {doodad_obj_id} Does not exist"),
            );
            return Ok(());
        };

        let template_id = doodad.data().template_id;
        self.base.send_message(
            out,
            &format!(
                "Doodad ObjId: {} TemplateId:{template_id}, set new plat time to {new_age} seconds ago.",
                doodad.obj_id
            ),
        );

        doodad.data().plant_time = chrono::Utc::now().timestamp() - i64::from(new_age);

        hide_show(ctx.server()?, &world, &WorldObject::from(&doodad));
        Ok(())
    }
}

/// `DoodadCmd`.
pub fn doodad_cmd() -> RootCommand {
    RootCommand::new(
        &["doodad"],
        "[Doodad]",
        "Root command to manage Doodads",
        "",
    )
    .register(Arc::new(DoodadChainSubCommand::default()), &["chain"])
    .register(
        Arc::new(DoodadPhaseSubCommand::default()),
        &["phase", "setphase"],
    )
    .register(Arc::new(DoodadSaveSubCommand::default()), &["save"])
    .register(
        Arc::new(DoodadPositionSubCommand::default()),
        &["position", "pos"],
    )
    .register(Arc::new(DoodadSpawnSubCommand::default()), &["spawn"])
    .register(Arc::new(DoodadRemoveSubCommand::default()), &["remove"])
    .register(Arc::new(DoodadRemovesSubCommand::default()), &["removes"])
    .register(Arc::new(DoodadSetAgeCommand::default()), &["age", "setage"])
}

#[cfg(test)]
mod tests {
    use super::super::super::harness;
    use super::super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn doodad_root_help_and_chain() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "doodad help").await;
        assert_eq!(
            out,
            [
                "|cFF7CFC00[Doodad] Root command to manage Doodads|r",
                "|cFF7CFC00[Doodad] Supported subcommands: <chain||phase||setphase||save||position||pos||spawn||remove||removes||age||setage>|r",
                "|cFF7CFC00[Doodad] For more details use /<command> <subcommand> help.|r",
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, &format!("doodad chain {}", ts::DOODAD)).await;
        assert_eq!(out, ["[Doodad Chain] Phase chain, see the log"]);
        let (_, out) = harness::run(&server, &gm.p, "doodad chain 99999").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Doodad Chain] Doodad with templateId 99999 was not found|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "doodad chain x").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Doodad Chain] Invalid numeric value for parameter: Template Id|r"]
        );
    }

    #[tokio::test]
    async fn doodad_phase_position_spawn_and_age() {
        let server = ts::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let d = ts::spawn_doodad(&server, ts::DOODAD, 102.0, 100.0);

        let (_, out) =
            harness::run(&server, &gm.p, &format!("doodad phase list {}", d.obj_id)).await;
        assert_eq!(
            out,
            [format!(
                "[Doodad Phase List] TemplateId {}: ObjId{}, Available phase ids (func groups): ",
                ts::DOODAD,
                d.obj_id
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "doodad setphase change 9 1").await;
        assert_eq!(
            out,
            [
                "|cFFFF0000[Doodad Phase Change] Doodad with objId 9 Does not exist|r",
                "|cFFFF0000[Doodad Phase Change] Unexpected error: Object reference not set to an instance of an object.|r",
            ]
        );
        let (_, out) = harness::run(
            &server,
            &gm.p,
            &format!("doodad phase change {} 5", d.obj_id),
        )
        .await;
        assert_eq!(out[0], "[Doodad Phase Change] SetPhase 5");

        let (_, out) = harness::run(
            &server,
            &gm.p,
            &format!("doodad pos {} x=110.5 yaw=90", d.obj_id),
        )
        .await;
        assert_eq!(
            out,
            [format!(
                "[Doodad Position] Doodad ObjId: {} TemplateId:{}, x:110.5, y:100, z:100, roll:0°, pitch:0°, yaw:90°",
                d.obj_id,
                ts::DOODAD
            )]
        );
        assert_eq!(d.data().transform.x, 110.5);
        let (_, out) =
            harness::run(&server, &gm.p, &format!("doodad pos {} yaw=400", d.obj_id)).await;
        assert_eq!(
            out,
            ["|cFFFF0000[Doodad Position] The number 400 should be between -360 and 360 for parameter: yaw=<new yaw degrees>|r"]
        );

        let (_, out) = harness::run(&server, &gm.p, &format!("doodad age {} 3600", d.obj_id)).await;
        assert_eq!(
            out,
            [format!(
                "[Doodad SetAge] Doodad ObjId: {} TemplateId:{}, set new plat time to 3600 seconds ago.",
                d.obj_id,
                ts::DOODAD
            )]
        );
        let age = chrono::Utc::now().timestamp() - d.data().plant_time;
        assert!((3599..=3601).contains(&age));

        gm.chat();
        let (_, out) = harness::run(
            &server,
            &gm.p,
            &format!("doodad spawn {} yaw=45", ts::DOODAD),
        )
        .await;
        assert!(out.is_empty());
        let chat = gm.chat();
        assert!(chat[0].ends_with(&format!(
            ", Template:{} spawned using yaw 45° = 0.7853982 rad",
            ts::DOODAD
        )));
        assert_eq!(server.world.world(0).unwrap().all_doodads().len(), 2);
        let (_, out) = harness::run(&server, &gm.p, "doodad spawn 99999").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Doodad Spawn] Doodad templateId:99999 don't exist|r"]
        );
    }
}
