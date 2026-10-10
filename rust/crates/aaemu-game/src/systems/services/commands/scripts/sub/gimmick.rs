//! `Scripts/SubCommands/Gimmick/**` and `Scripts/Commands/GimmickCmd.cs`.

use super::super::prelude::*;
use super::doodads::{sub_base, uint_param};
use crate::systems::npcs::gimmick::manager::{spawn_from_effect, SpawnGimmickParams};

/// `GimmickSpawnSubCommand`.
pub struct GimmickSpawnSubCommand {
    base: SubCommandBase,
}

impl Default for GimmickSpawnSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Gimmick Spawn]",
                "Spawn one gimmick in front of the player facing player (default) or a optional direction in degrees",
                &format!("{COMMAND_PREFIX}gimmick spawn"),
                vec![uint_param("GimmickTemplateId", "Gimmick template Id", true)],
            ),
        }
    }
}

impl SubCommand for GimmickSpawnSubCommand {
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
        let gimmick_template_id = parameters.require("GimmickTemplateId")?.as_u32()?;
        let server = ctx.server()?;

        if !server
            .game_data
            .systems
            .npcs
            .gimmick
            .exist(gimmick_template_id)
        {
            self.base.send_color_message(
                out,
                Color::RED,
                &format!("Gimmick template {gimmick_template_id} doesn't exist"),
            );
            return Ok(());
        }

        let using_character = ctx.player()?;
        let creator_unit = current_target(server, &using_character)
            .unwrap_or_else(|| WorldObject::from(&using_character));

        // C#: `new GimmickSpawner(creatorUnit.ParentWorld, spawnEffect, creatorUnit)` with a null
        // effect throws (NullReferenceException).
        let e = server
            .game_data
            .systems
            .skills
            .spawn_gimmick_effect(gimmick_template_id)
            .cloned()
            .ok_or_else(nre)?;
        let world = world_of_obj(server, &creator_unit).ok_or_else(nre)?;
        let params = SpawnGimmickParams {
            gimmick_id: e.gimmick_id,
            offset_from_source: e.offset_from_source,
            offset_coordinate_id: e.offset_coordinate_id as i32,
            offset_x: e.offset[0],
            offset_y: e.offset[1],
            offset_z: e.offset[2],
            scale: e.scale,
            velocity_coordinate_id: e.velocity_coordinate_id as i32,
            velocity_x: e.velocity[0],
            velocity_y: e.velocity[1],
            velocity_z: e.velocity[2],
            ang_vel_coordinate_id: e.ang_vel_coordinate_id as i32,
            ang_vel_x: e.ang_vel[0],
            ang_vel_y: e.ang_vel[1],
            ang_vel_z: e.ang_vel[2],
        };
        if spawn_from_effect(server, &world, &params, &creator_unit).is_none() {
            // An invalid OffsetCoordinateId (C#: ArgumentOutOfRangeException).
            anyhow::bail!("Specified argument was out of the range of valid values.");
        }

        ctx.character.send_message(&format!(
            "Spawned Gimmick TemplateId: {} originating from {}.",
            e.gimmick_id,
            debug_name(&creator_unit)
        ));
        Ok(())
    }
}

/// `GimmickCmd`.
pub fn gimmick_cmd() -> RootCommand {
    RootCommand::new(
        &["gimmick"],
        "[Gimmick]",
        "Root command to manage Gimmicks",
        "",
    )
    .register(Arc::new(GimmickSpawnSubCommand::default()), &["spawn"])
}

#[cfg(test)]
mod tests {
    use super::super::super::harness;
    use super::super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn gimmick_spawn_from_its_spawn_effect() {
        let server = ts::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        gm.chat();
        let (_, out) =
            harness::run(&server, &gm.p, &format!("gimmick spawn {}", ts::GIMMICK)).await;
        assert!(out.is_empty());
        assert_eq!(
            gm.chat(),
            [format!(
                "Spawned Gimmick TemplateId: {} originating from ({}) - Gm.",
                ts::GIMMICK,
                gm.p.obj_id
            )]
        );
        let world = server.world.world(0).unwrap();
        assert_eq!(
            world.all_of_kind(crate::world::ObjectKind::Gimmick).len(),
            1
        );

        let (_, out) = harness::run(&server, &gm.p, "gimmick spawn 4242").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Gimmick Spawn] Gimmick template 4242 doesn't exist|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "gimmick").await;
        assert_eq!(
            out[0],
            "|cFF7CFC00[Gimmick] Root command to manage Gimmicks|r"
        );
    }
}
