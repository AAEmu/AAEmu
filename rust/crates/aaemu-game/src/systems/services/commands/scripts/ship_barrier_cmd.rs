//! `Scripts/Commands/ShipBarrierCmd.cs`: debug / GM helpers for the BAI-derived ship static
//! barriers (`GeoDataMode`).

use super::prelude::*;
use crate::systems::ai::geodata::manager::to_cell_index;
use crate::systems::ai::physics;
use crate::systems::skills::controllers::Vec3;
use crate::world::WorldInstance;

/// `ShipBarrierCmd`.
pub struct ShipBarrierCmd;

impl ShipBarrierCmd {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `WorldInstance.ToString()`.
fn world_string(world: &WorldInstance) -> String {
    format!(
        "{}-{}({})",
        world.id, world.template.name, world.template.id
    )
}

impl ShipBarrierCmd {
    /// `HandleReset(character, world, args, messageOutput)`.
    fn handle_reset(
        &self,
        server: &GameServer,
        character: &PlayerRef,
        world: &Arc<WorldInstance>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) {
        let mode = if args.len() >= 2 {
            args[1].to_lowercase()
        } else {
            "world".to_string()
        };
        let do_ingest = args.iter().any(|a| a.eq_ignore_ascii_case("ingest"));

        if mode != "world" && mode != "cell" {
            CommandManager::send_error_text(self, out, "Usage: reset [world|cell] [ingest]");
            return;
        }

        let (before_barriers, before_cells) =
            physics::clear_ship_static_barriers_and_bai_ingest(server, world.id);

        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "Cleared ship barriers for {}. Barriers {before_barriers}→0, ingested cells {before_cells}→0.",
                world_string(world)
            ),
        );

        if !do_ingest {
            return;
        }

        let p = world_pos(server, &WorldObject::from(character));
        let (cx, cy) = to_cell_index(Vec3::new(p.x, p.y, p.z));

        // Ingest current + neighbors to make the effect immediate for nearby navigation.
        let mut ingested = 0;
        for dy in -1..=1 {
            for dx in -1..=1 {
                let tx = cx + dx;
                let ty = cy + dy;
                let pre_count = physics::ship_static_barrier_count(server, world.id);
                physics::ensure_ship_static_barrier_bai_cell(server, world, tx, ty);
                let post_count = physics::ship_static_barrier_count(server, world.id);
                if post_count > pre_count {
                    ingested += 1;
                }
            }
        }

        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "Triggered ingest around cell ({cx},{cy}); cells that added barriers: {ingested}."
            ),
        );
    }

    /// `SendStatus(world, messageOutput)`.
    fn send_status(&self, server: &GameServer, world: &WorldInstance, out: &mut dyn MessageOutput) {
        let (barriers, cells) = physics::ship_static_barrier_debug_counts(server, world.id);

        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "World={} GeoDataMode={} barriers={barriers} ingestedCells={cells}",
                world_string(world),
                bool_str(server.config.world.geo_data_mode)
            ),
        );
    }
}

impl Command for ShipBarrierCmd {
    fn command_names(&self) -> Vec<String> {
        names(&["shipbarrier", "sb"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let Some(world) = parent_world(server, &character) else {
            CommandManager::send_error_text(self, out, "No world instance.");
            return Ok(());
        };

        if !physics::ship_static_barriers_enabled(server) {
            CommandManager::send_error_text(
                self,
                out,
                "ShipStaticBarriers not initialized (GeoDataMode off for this instance?).",
            );
            return Ok(());
        }

        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        match args[0].to_lowercase().as_str() {
            "status" => self.send_status(server, &world, out),
            "reset" => self.handle_reset(server, &character, &world, args, out),
            _ => CommandManager::send_default_help_text(self, out),
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "reset [world|cell] [ingest] | status".into()
    }

    fn get_command_help_text(&self) -> String {
        let n = &self.command_names()[0];
        format!(
            "Ship barrier debug utilities.\n\
             reset world: clears all BAI-ingested ship barriers and ingested-cells cache for the current world.\n\
             reset cell: clears all barriers+cache (same as world for now) and optionally ingests the current cell.\n\
             Add \"ingest\" to immediately ingest current + neighbor cells.\n\
             status: prints current barrier/cache counters.\n\
             Examples:\n\
             {COMMAND_PREFIX}{n} status\n\
             {COMMAND_PREFIX}{n} reset world ingest\n\
             {COMMAND_PREFIX}{n} reset cell ingest"
        )
    }
}

#[cfg(test)]
mod tests {
    use std::collections::HashMap;
    use std::sync::Arc;

    use super::super::harness;
    use super::super::ping_position::tests::MemFiles;
    use crate::systems::ai::geodata::bai::test_files::areas_mission;
    use crate::systems::ai::geodata::bai::UsedAreaNames;
    use crate::systems::ai::geodata::manager::AiGeoData;

    #[tokio::test]
    async fn ship_barrier_status_and_reset() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let (handled, out) = harness::run(&server, &gm.p, "shipbarrier status").await;
        assert!(handled);
        assert_eq!(
            out,
            [harness::error(
                "shipbarrier",
                "ShipStaticBarriers not initialized (GeoDataMode off for this instance?)."
            )]
        );

        server.config.update(|c| c.world.geo_data_mode = true);
        // A coastal forbidden area at the ocean level in path block (0, 0) of cell (0, 0)
        let mut files = HashMap::new();
        files.insert(
            "game/worlds/main_world/paths/000_000/areasmission0.bai".to_string(),
            areas_mission(&[(
                "coast",
                vec![
                    [50.0, 50.0, 100.0],
                    [90.0, 50.0, 100.0],
                    [90.0, 90.0, 100.0],
                    [50.0, 90.0, 100.0],
                ],
            )]),
        );
        let t = server.game_data.worlds.get_world_template_by_id(0).unwrap();
        server.systems.ai.geodata.install(Arc::new(AiGeoData::new(
            t,
            true,
            Arc::new(MemFiles(files)),
            Arc::new(UsedAreaNames::default()),
            None,
        )));

        let (_, out) = harness::run(&server, &gm.p, "sb status").await;
        assert_eq!(
            out,
            [harness::normal(
                "shipbarrier",
                "World=0-main_world(0) GeoDataMode=True barriers=0 ingestedCells=0"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "sb reset cell ingest").await;
        assert_eq!(
            out[0],
            harness::normal(
                "shipbarrier",
                "Cleared ship barriers for 0-main_world(0). Barriers 0→0, ingested cells 0→0."
            )
        );
        assert!(out[1].starts_with(
            "[shipbarrier] Triggered ingest around cell (0,0); cells that added barriers: "
        ));
        let (barriers, cells) =
            crate::systems::ai::physics::ship_static_barrier_debug_counts(&server, 0);
        // the cells of the 2x2 world around (0, 0)
        assert_eq!(cells, 4);
        let (_, out) = harness::run(&server, &gm.p, "sb reset").await;
        assert_eq!(
            out,
            [harness::normal(
                "shipbarrier",
                &format!(
                    "Cleared ship barriers for 0-main_world(0). Barriers {barriers}→0, ingested cells 4→0."
                )
            )]
        );
        assert_eq!(
            crate::systems::ai::physics::ship_static_barrier_debug_counts(&server, 0),
            (0, 0)
        );
        // C#: "reset ingest" takes "ingest" as the mode
        let (_, out) = harness::run(&server, &gm.p, "sb reset ingest").await;
        assert_eq!(
            out,
            [harness::error(
                "shipbarrier",
                "Usage: reset [world|cell] [ingest]"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "sb").await;
        assert!(out[0].starts_with("Help for |cFFFFFFFF/shipbarrier|r"));
    }
}
