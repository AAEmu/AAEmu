//! `Scripts/Commands/WaterDebugCmd.cs`: GM debug for water zones loaded from client cell
//! `object.dat`.

use super::prelude::*;
use crate::systems::ai::physics::{self, water_debug};
use crate::systems::skills::controllers::Vec3;
use crate::world::WorldInstance;

/// `WaterDebugCmd`.
pub struct WaterDebugCmd;

impl WaterDebugCmd {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `TryParseXYZ(args, startIdx, out pos)`.
fn try_parse_xyz(args: &[String], start_idx: usize) -> Option<Vec3> {
    if args.len() < start_idx + 3 {
        return None;
    }
    let x = parse_f32(&args[start_idx])?;
    let y = parse_f32(&args[start_idx + 1])?;
    let z = parse_f32(&args[start_idx + 2])?;
    Some(Vec3::new(x, y, z))
}

impl WaterDebugCmd {
    /// `SendInfo(cmd, character, world, args, messageOutput)`.
    fn send_info(
        &self,
        server: &GameServer,
        character: &PlayerRef,
        world: &WorldInstance,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) {
        let p = world_pos(server, &WorldObject::from(character));
        let mut pos = Vec3::new(p.x, p.y, p.z);
        if let Some(parsed) = try_parse_xyz(args, 1) {
            pos = parsed;
        }

        for line in water_debug::capture(server, world, pos, true) {
            CommandManager::send_normal_text(self, out, &line);
        }
    }
}

impl Command for WaterDebugCmd {
    fn command_names(&self) -> Vec<String> {
        names(&["waterdebug", "water_debug"])
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

        if args.is_empty() || args[0].trim().is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        match args[0].to_lowercase().as_str() {
            "reload" => {
                physics::reload_water_from_loaded_cells(server, &world);
                let mut loaded_cells = 0;
                let mut client_volumes = 0;
                let (mut river, mut area, mut ocean, mut sector) = (0, 0, 0, 0);
                let mut client_with_flow = 0;

                let template = &world.template;
                for cy in 0..template.cell_y {
                    for cx in 0..template.cell_x {
                        let (loaded, objects) = physics::cell_object_dat(server, &world, cx, cy);
                        if !loaded {
                            continue;
                        }
                        loaded_cells += 1;
                        let ([n, r, a, o, s], with_flow) =
                            water_debug::count_client_water(objects.as_ref());
                        client_volumes += n;
                        river += r;
                        area += a;
                        ocean += o;
                        sector += s;
                        client_with_flow += with_flow;
                    }
                }
                let like_river = river;
                let like_area = area + ocean + sector;

                let water = physics::world_water(server, &world);
                let area_snapshot = water.areas_snapshot();
                let server_areas = area_snapshot.len();
                let server_zones_with_flow =
                    area_snapshot.iter().filter(|a| a.speed != 0.0).count();

                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "Water reload: OceanLevel={}, areas={server_areas}, loadedCells={loaded_cells}",
                        f32s(water.ocean_level)
                    ),
                );
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "Client water volumes={client_volumes} (River={river} Area={area} Ocean={ocean} Sector={sector}), likeRiver={like_river}, likeArea={like_area}, withFlow={client_with_flow}"
                    ),
                );
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "Server ingested zones with flow (Speed!=0): {server_zones_with_flow}"
                    ),
                );
            }
            "info" => {
                self.send_info(server, &character, &world, args, out);
            }
            "reloadinfo" => {
                physics::reload_water_from_loaded_cells(server, &world);
                self.send_info(server, &character, &world, args, out);
            }
            "setprobe" => {
                let Some(probe_pos) = try_parse_xyz(args, 1) else {
                    CommandManager::send_error_text(self, out, "Usage: setprobe x y z");
                    return Ok(());
                };
                water_debug::auto_water_probe_enable(server, probe_pos);
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "AutoWaterProbe enabled at ({},{},{})",
                        fixed(f64::from(probe_pos.x), 1),
                        fixed(f64::from(probe_pos.y), 1),
                        fixed(f64::from(probe_pos.z), 1)
                    ),
                );
            }
            "probeoff" => {
                water_debug::auto_water_probe_disable();
                CommandManager::send_normal_text(self, out, "AutoWaterProbe disabled.");
            }
            _ => {
                CommandManager::send_default_help_text(self, out);
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "reload | info [x y z] | reloadinfo [x y z] | setprobe x y z | probeoff".into()
    }

    fn get_command_help_text(&self) -> String {
        let n = &self.command_names()[0];
        format!(
            "Water zones from object.dat per cell.\n\
             {COMMAND_PREFIX}{n} reload — rebuild Water.Areas from Loaded cells.\n\
             {COMMAND_PREFIX}{n} info [x y z] — snapshot at your position or coordinates.\n\
             {COMMAND_PREFIX}{n} reloadinfo [x y z] — reload then info.\n\
             {COMMAND_PREFIX}{n} setprobe x y z — enable autoprobe logging.\n\
             {COMMAND_PREFIX}{n} probeoff — disable autoprobe logging."
        )
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::systems::ai::physics::water::{WaterBodyArea, WaterBodyAreaType};
    use crate::systems::ai::physics::{water_debug, world_water};
    use crate::systems::skills::controllers::Vec3;

    #[tokio::test]
    async fn water_debug_reload_info_and_probe() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let n = |t: &str| harness::normal("waterdebug", t);

        let (handled, out) = harness::run(&server, &gm.p, "waterdebug").await;
        assert!(handled);
        assert!(
            out[0].starts_with("Help for |cFFFFFFFF/waterdebug|r |cFFEEEEAAreload | info [x y z]")
        );

        // A lake around the player (the synthetic world's ocean level is 100)
        let world = server.world.world(0).unwrap();
        let mut lake = WaterBodyArea::new("Water_C0-0_3", WaterBodyAreaType::Polygon);
        lake.depth = 10.0;
        for (x, y) in [
            (0.0, 0.0),
            (300.0, 0.0),
            (300.0, 300.0),
            (0.0, 300.0),
            (0.0, 0.0),
        ] {
            lake.points.push(Vec3::new(x, y, 150.0));
        }
        lake.update_bounds();
        world_water(&server, &world).add(lake);

        let (_, out) = harness::run(&server, &gm.p, "water_debug info 150 150 145").await;
        assert_eq!(
            out[..8],
            [
                n("[waterprobe] Water.OceanLevel=100 (template 100), areas=1"),
                n("[waterprobe] Pos (150.00, 150.00, 145.00)"),
                n("[waterprobe] IsWater=True, flow=(0.000,0.000,0.000), len=0.000"),
                n("[waterprobe] GetWaterSurface Z=150.000, flowSurf=(0.000,0.000,0.000)"),
                n("[waterprobe] depthBelowSurface (surface - pos.Z)=5.000"),
                n("[waterprobe] Cell (0,0) Loaded=False, object.dat=no"),
                n("[waterprobe] Areas with XY inside rough bbox: 1"),
                n("[waterprobe] Nearest 4 water bodies (short id, full checks):"),
            ]
        );
        assert_eq!(out.len(), 9);
        assert!(out[8].starts_with("[waterdebug] [P] id=0 ~ C0-0_3 dist=0.00 bbox=True"));
        assert!(out[8].ends_with(
            " surfZ=150.000 depth=10.000 [zMin,zMax]=[140.000,150.000] vertOk=True flowLen=0.000"
        ));
        // The player's own position: below the lake, at the ocean level
        let (_, out) = harness::run(&server, &gm.p, "waterdebug INFO").await;
        assert_eq!(out[1], n("[waterprobe] Pos (100.00, 200.00, 100.00)"));
        assert_eq!(
            out[2],
            n("[waterprobe] IsWater=True, flow=(0.000,0.000,0.000), len=0.000")
        );

        // reload: rebuilt from the loaded cells (none) -> the lake is gone
        let (_, out) = harness::run(&server, &gm.p, "waterdebug reload").await;
        assert_eq!(
            out,
            [
                n("Water reload: OceanLevel=100, areas=0, loadedCells=0"),
                n("Client water volumes=0 (River=0 Area=0 Ocean=0 Sector=0), likeRiver=0, likeArea=0, withFlow=0"),
                n("Server ingested zones with flow (Speed!=0): 0"),
            ]
        );

        // probe
        let (_, out) = harness::run(&server, &gm.p, "waterdebug setprobe 1 2").await;
        assert_eq!(out, [harness::error("waterdebug", "Usage: setprobe x y z")]);
        let (_, out) = harness::run(&server, &gm.p, "waterdebug setprobe 1 2.25 -3").await;
        assert_eq!(out, [n("AutoWaterProbe enabled at (1.0,2.3,-3.0)")]);
        assert_eq!(
            water_debug::auto_water_probe_state(),
            (true, Vec3::new(1.0, 2.25, -3.0))
        );
        assert_eq!(
            water_debug::auto_water_probe_tick(&server).as_deref(),
            Some("[waterprobe] Pos (1.0,2.3,-3.0) IsWater=True flow=(0.000,0.000,0.000) len=0.000")
        );
        // unchanged within the minute: no log
        assert_eq!(water_debug::auto_water_probe_tick(&server), None);
        let (_, out) = harness::run(&server, &gm.p, "waterdebug probeoff").await;
        assert_eq!(out, [n("AutoWaterProbe disabled.")]);
        assert!(!water_debug::auto_water_probe_state().0);
        let (_, out) = harness::run(&server, &gm.p, "waterdebug dance").await;
        assert!(out[0].starts_with("Help for"));
    }
}
