//! `Scripts/Commands/TestHeight.cs`.

use std::sync::Arc;

use super::super::output::PlayerCharacter;
use super::prelude::*;
use crate::data::world_templates::WorldSpawnPosition;
use crate::systems::npcs::doodad::spawner::{DoodadSpawner, DoodadSpawnerDef, SpawnerBase};
use crate::systems::skills::controllers::Vec3;

const TARGET_X: f32 = 22500.0;
const TARGET_Y: f32 = 18500.0;
const TARGET_Z: f32 = 10.0;
/// Pillar.
const PILLAR: u32 = 5622;

/// `TestHeight`.
pub struct TestHeight;

impl TestHeight {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `(int)Math.Floor(v)` with the remainder rounding `r - r % 2` of the command.
fn even_floor(v: f32) -> i32 {
    let r = f64::from(v).floor() as i32;
    r - r % 2
}

/// `character.Transform.CloneAsSpawnPosition()`.
fn clone_as_spawn_position(server: &GameServer, p: &PlayerRef) -> WorldSpawnPosition {
    let obj = WorldObject::from(p);
    let t = obj.transform();
    let w = world_pos(server, &obj);
    WorldSpawnPosition {
        world_id: t.world_id,
        zone_id: t.zone_id,
        x: w.x,
        y: w.y,
        z: w.z,
        roll: w.rot_x,
        pitch: w.rot_y,
        yaw: w.rot_z,
    }
}

/// `new DoodadSpawner { ParentWorld, Id = 0, UnitId, Position }` with the given X / Y, the
/// geodata height of `(x, y, position.Z)` from `height_of`'s world, no rotation, then
/// `Spawn(0)`.
fn spawn_pillar(
    server: &GameServer,
    character: &PlayerRef,
    height_of: &PlayerRef,
    x: f32,
    y: f32,
) -> anyhow::Result<()> {
    let world = parent_world(server, character).ok_or_else(|| anyhow::anyhow!(NULL_REFERENCE))?;
    let mut position = clone_as_spawn_position(server, character);
    position.x = x;
    position.y = y;
    if parent_world(server, height_of).is_none() {
        anyhow::bail!(NULL_REFERENCE);
    }
    position.z = crate::systems::ai::geodata::geo_data_height(
        server,
        &WorldObject::from(height_of),
        Vec3::new(position.x, position.y, position.z),
    );
    position.yaw = 0.0;
    position.pitch = 0.0;
    position.roll = 0.0;
    let def = DoodadSpawnerDef {
        base: SpawnerBase {
            id: 0,
            unit_id: PILLAR,
            position,
            ..SpawnerBase::default()
        },
        scale: 1.0,
        count: 1,
        related_ids: None,
        respawn_doodad_template_id: 0,
    };
    let spawner = Arc::new(DoodadSpawner::new(def, Some(world.id)));
    spawner.spawn(server, 0);
    Ok(())
}

impl Command for TestHeight {
    fn command_names(&self) -> Vec<String> {
        names(&["testheightvisualizer", "test_height_visualizer"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut target_player = character.clone();
        let mut firstarg = 0;
        if !args.is_empty() {
            (target_player, firstarg) =
                get_target_or_self(server, &character, Some(args[0].as_str()));
        }
        let exists = |id: u32| server.game_data.systems.npcs.doodad.exist(id);
        let sub = args.get(firstarg).map(String::as_str);

        if sub == Some("testpos") {
            teleport_player(&target_player, 0, TARGET_X, TARGET_Y, TARGET_Z, 0.0);
            PlayerCharacter(target_player.clone()).send_message(&format!(
                "[Move] |cFFFFFFFF{}|r moved to X: {}, Y: {}, Z: {}",
                name_of(&target_player),
                f32s(TARGET_X),
                f32s(TARGET_Y),
                f32s(TARGET_Z)
            ));
        } else if sub == Some("mark") {
            // Place markers
            let tp = world_pos(server, &WorldObject::from(&target_player));
            let r_x = even_floor(tp.x);
            let r_y = even_floor(tp.y);
            let mut y = r_y - 10;
            while y <= r_y + 10 {
                let mut x = r_x - 10;
                while x <= r_x + 10 {
                    if !exists(PILLAR) {
                        return Ok(());
                    }
                    spawn_pillar(server, &character, &target_player, x as f32, y as f32)?;
                    x += 2;
                }
                y += 2;
            }
        } else if sub == Some("line") {
            // Place markers
            let tp = world_pos(server, &WorldObject::from(&target_player));
            let r_x = even_floor(tp.x) as f32;
            let r_y = even_floor(tp.y) as f32;
            let mut x = r_x - 10.0;
            while x <= r_x + 10.0 {
                if !exists(PILLAR) {
                    return Ok(());
                }
                spawn_pillar(server, &character, &character, x, r_y)?;
                x += 1.0;
            }

            let mut y = r_y - 10.0;
            while y <= r_y + 10.0 {
                if !exists(PILLAR) {
                    return Ok(());
                }
                spawn_pillar(server, &character, &character, r_x, y)?;
                y += 1.0;
            }
        } else {
            // Show info
            let zone_key = target_player.data().unit.transform.zone_id;
            let template = server
                .game_data
                .worlds
                .get_world_template_by_zone_key(zone_key)
                .ok_or_else(|| anyhow::anyhow!(NULL_REFERENCE))?;
            let heightmaps = server.systems.npcs.heightmaps.get();
            let hm = heightmaps
                .as_ref()
                .and_then(|h| h.template(template.id).map(|t| (t, h.files())));
            let tp = world_pos(server, &WorldObject::from(&target_player));
            let me = world_pos(server, &WorldObject::from(&character));

            let height = match &hm {
                Some((t, files)) => t.get_height(files, tp.x, tp.y)?,
                None => 0.0,
            };
            // C#: the delta uses the caller's Z, not the target's
            let h_delta = me.z - height;
            let name = name_of(&target_player);
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "{name} Z-Pos: {} - Floor: {} - HeightmapDelta: {}",
                    f32s(me.z),
                    f32s(height),
                    f32s(h_delta)
                ),
            );
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "|cFFFFFFFF{name}|r X: |cFFFFFFFF{}|r  Y: |cFFFFFFFF{}|r  Z: |cFFFFFFFF{}|r ",
                    fixed(f64::from(tp.x), 1),
                    fixed(f64::from(tp.y), 1),
                    fixed(f64::from(tp.z), 1)
                ),
            );

            let border_left = even_floor(tp.x);
            // we're using a divider of 2 of the heightmaps in memory, so we need to compensate
            // with that in mind (instead of 1)
            let border_right = border_left + 2;
            let border_bottom = even_floor(tp.y);
            let border_top = border_bottom + 2;

            // Get heights for these points
            let raw = |x: i32, y: i32| -> anyhow::Result<f32> {
                Ok(match &hm {
                    Some((t, files)) => t.get_raw_height_map_height(files, x, y)?,
                    None => 0.0,
                })
            };
            let height_tl = raw(border_left, border_top)?;
            let height_tr = raw(border_right, border_top)?;
            let height_bl = raw(border_left, border_bottom)?;
            let height_br = raw(border_right, border_bottom)?;
            for (corner, x, y, h) in [
                ("TL", border_left, border_top, height_tl),
                ("TR", border_right, border_top, height_tr),
                ("BL", border_left, border_bottom, height_bl),
                ("BR", border_right, border_bottom, height_br),
            ] {
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("{corner} @ {x}x{y} = {}", f32s(h)),
                );
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) [testpos||mark||line]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Gets your or target's current height and that of the supposed floor (using heightmap data)\n\
         testpos will move you near Freedich underwater\r\
         mark creates a grid of pillar doodads used for measuring the floor at 2m intervals (exact heightmap points)\r\
         line creates a cross of pillar doodads used for measuring the floor at 1m intervals (for in-between points)"
            .into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::move_cmd::tests::teleport_body;
    use super::super::ping_position::tests::install_terrain;
    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::systems::npcs::doodad::templates::DoodadTemplate;

    fn data() -> GameData {
        let mut data = GameData::empty();
        let d = &mut data.systems.npcs.doodad;
        d.templates.insert(
            super::PILLAR,
            DoodadTemplate {
                id: super::PILLAR,
                ..DoodadTemplate::default()
            },
        );
        d.template_order.push(super::PILLAR);
        data
    }

    #[tokio::test]
    async fn test_height_info_and_testpos() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 101.0, 201.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        let (handled, out) = harness::run(&server, &gm.p, "testheightvisualizer").await;
        assert!(handled);
        assert_eq!(
            out,
            [
                harness::normal(
                    "testheightvisualizer",
                    "Gm Z-Pos: 100 - Floor: 0 - HeightmapDelta: 100"
                ),
                harness::normal(
                    "testheightvisualizer",
                    "|cFFFFFFFFGm|r X: |cFFFFFFFF101.0|r  Y: |cFFFFFFFF201.0|r  Z: |cFFFFFFFF100.0|r "
                ),
                harness::normal("testheightvisualizer", "TL @ 100x202 = 0"),
                harness::normal("testheightvisualizer", "TR @ 102x202 = 0"),
                harness::normal("testheightvisualizer", "BL @ 100x200 = 0"),
                harness::normal("testheightvisualizer", "BR @ 102x200 = 0"),
            ]
        );
        install_terrain(&server);
        let h = server.systems.npcs.heightmaps.get().unwrap();
        let t = h.template(0).unwrap();
        let tl = t.get_raw_height_map_height(h.files(), 100, 202).unwrap();
        let floor = t.get_height(h.files(), 101.0, 201.0).unwrap();
        let (_, out) = harness::run(&server, &gm.p, "test_height_visualizer").await;
        assert_eq!(
            out[0],
            harness::normal(
                "testheightvisualizer",
                &format!(
                    "Gm Z-Pos: 100 - Floor: {} - HeightmapDelta: {}",
                    super::f32s(floor),
                    super::f32s(100.0 - floor)
                )
            )
        );
        assert_eq!(
            out[2],
            harness::normal(
                "testheightvisualizer",
                &format!("TL @ 100x202 = {}", super::f32s(tl))
            )
        );
        assert!((tl - 252.0).abs() < 0.01, "{tl}");

        // testpos on another player
        let (_, out) = harness::run(&server, &gm.p, "testheightvisualizer bob testpos").await;
        assert!(out.is_empty());
        let frames = bob.frames();
        assert_eq!(
            frames[0],
            (
                sc::SCTeleportUnitPacket,
                teleport_body(0, 22500.0, 18500.0, 10.0, 0.0)
            )
        );
        assert_eq!(
            harness::chat_text(&frames[1].1),
            "[Move] |cFFFFFFFFBob|r moved to X: 22500, Y: 18500, Z: 10"
        );
        assert!(gm.packets().is_empty());
    }

    #[tokio::test]
    async fn test_height_markers() {
        // Without the pillar template nothing is spawned
        let server = harness::server();
        let gm = harness::pc(&server, 0x600, "Gm", 101.0, 201.0);
        let (_, out) = harness::run(&server, &gm.p, "testheightvisualizer mark").await;
        assert!(out.is_empty());
        assert!(server.world.world(0).unwrap().all_doodads().is_empty());

        let server = harness::server_with(data());
        install_terrain(&server);
        let gm = harness::pc(&server, 0x600, "Gm", 101.0, 201.0);
        harness::run(&server, &gm.p, "testheightvisualizer mark").await;
        let world = server.world.world(0).unwrap();
        let doodads = world.all_doodads();
        assert_eq!(doodads.len(), 121);
        for d in &doodads {
            let t = d.data().transform;
            assert_eq!(t.x as i32 % 2, 0);
            // the geodata (raw heightmap) height at the pillar
            let expect = (t.x / 2.0) + 2.0 * (t.y / 2.0);
            assert!((t.z - expect).abs() < 0.05, "{t:?}");
        }
        harness::run(&server, &gm.p, "testheightvisualizer line").await;
        assert_eq!(world.all_doodads().len(), 121 + 42);
    }
}
