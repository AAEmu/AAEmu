//! Sub commands of `Scripts/SubCommands/Doodads` (registered by the `Scripts/Commands`
//! `DoodadCmd` of the later scripts phase). Ported so far: `DoodadSetAgeCommand`.

use super::output::{Color, MessageOutput};
use super::params::{NumericSubCommandParameter, Parameters};
use super::subcommand::{SubCommand, SubCommandBase};
use super::{CommandContext, COMMAND_PREFIX};
use crate::world::WorldObject;

/// `DoodadSetAgeCommand` (`/doodad age||setage <ObjId> <Age>`): changes a doodad's age.
pub struct DoodadSetAgeCommand {
    base: SubCommandBase,
}

impl Default for DoodadSetAgeCommand {
    fn default() -> Self {
        let mut base = SubCommandBase::new(
            "[Doodad SetAge]",
            "Changes a doodads age in seconds (1h = 3600, 1d = 86400)",
            &format!("{COMMAND_PREFIX}doodad age||setage"),
        );
        base.add_parameter(Box::new(NumericSubCommandParameter::<u32>::new(
            "ObjId",
            Some("Object Id"),
            true,
        )))
        .expect("valid parameter order");
        base.add_parameter(Box::new(NumericSubCommandParameter::<u32>::new(
            "Age",
            Some("New Age Time"),
            true,
        )))
        .expect("valid parameter order");
        Self { base }
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
        let server = ctx.server()?;
        let player = ctx.player()?;
        // `((Character)character).ParentWorld.GetDoodad(doodadObjId)`
        let world = server
            .world
            .world_of(&WorldObject::from(&player))
            .ok_or_else(|| {
                anyhow::anyhow!("Object reference not set to an instance of an object.")
            })?;
        let Some(doodad) = world.doodad(doodad_obj_id) else {
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

        // `PlantTime = DateTime.UtcNow.AddSeconds(-newAge)` (the Rust PlantTime is unix seconds).
        doodad.data().plant_time = chrono::Utc::now().timestamp() - i64::from(new_age);

        world.hide(&doodad);
        let changes = world.show(&doodad, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
        Ok(())
    }
}
