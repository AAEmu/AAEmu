//! `Scripts/Commands/TestNavMesh.cs`.

use std::sync::Mutex;

use super::prelude::*;
use crate::systems::ai::manager::run_now;
use crate::systems::npcs::doodad::manager as doodads;
use crate::systems::skills::controllers::Vec3;
use crate::world::{DoodadRef, WorldInstance};

/// Stone Post.
const STONE_POST_DOODAD: u32 = 5622;
/// Crescent Throne Flag.
const CRESCENT_THRONE_FLAG_DOODAD: u32 = 4763;
/// `NodeDescriptor.ToString()` (no override: the type name).
const NODE_DESCRIPTOR_TYPE: &str = "AAEmu.Game.Models.CryEngine.Entities.NodeDescriptor";

/// `TestNavMesh`.
pub struct TestNavMesh {
    /// `Markers` (C# `static`).
    markers: Mutex<Vec<DoodadRef>>,
}

impl TestNavMesh {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self {
            markers: Mutex::new(Vec::new()),
        }
    }

    /// `ClearMarkers()`.
    fn clear_markers(&self, server: &GameServer) {
        let markers = std::mem::take(&mut *self.markers.lock().expect("markers poisoned"));
        for marker in markers {
            server.ids.object.release_id(marker.obj_id);
            let obj = WorldObject::from(&marker);
            match server.world.world_of(&obj) {
                Some(world) => doodads::delete(server, &world, &marker),
                // not in a region (never shown): only the bookkeeping of Delete()
                None => {
                    if let Some(world) = server.world.world(marker.data().transform.instance_id) {
                        doodads::delete(server, &world, &marker);
                    }
                }
            }
        }
    }

    /// `AddDoodadMarker(world, pos, doodadTemplateId)`.
    fn add_doodad_marker(
        &self,
        server: &GameServer,
        world: &Arc<WorldInstance>,
        pos: Vec3,
        doodad_template_id: u32,
    ) -> anyhow::Result<()> {
        let marker = doodads::create(server, world, 0, doodad_template_id, None, false)
            .ok_or_else(|| anyhow::anyhow!(NULL_REFERENCE))?;
        marker.data().transform.set_xyz(pos.x, pos.y, pos.z);
        let changes = world.show(&marker, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
        self.markers.lock().expect("markers poisoned").push(marker);
        Ok(())
    }
}

/// `Vector3.ToString()`.
fn v3(v: Vec3) -> String {
    vector3_string([v.x, v.y, v.z])
}

impl Command for TestNavMesh {
    fn command_names(&self) -> Vec<String> {
        names(&["testnavmesh", "test_navmesh"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let nre = || anyhow::anyhow!(NULL_REFERENCE);

        self.clear_markers(server);
        let world = parent_world(server, &character).ok_or_else(nre)?;
        let geo = server.systems.ai.geodata.get(world.template.id);
        let char_obj = WorldObject::from(&character);
        let char_pos = {
            let p = world_pos(server, &char_obj);
            Vec3::new(p.x, p.y, p.z)
        };
        let Some(WorldObject::Npc(npc)) = current_target(server, &character) else {
            let zone_key = char_obj.transform().zone_id;
            let closest = geo
                .as_ref()
                .and_then(|g| g.find_closest_to_the_current(zone_key, char_pos));
            out.send_message(&format!(
                "Your closest node is: {}",
                if closest.is_some() {
                    NODE_DESCRIPTOR_TYPE
                } else {
                    ""
                }
            ));
            let (bai, node) = closest.ok_or_else(nre)?;
            self.add_doodad_marker(
                server,
                &world,
                bai.node(node).pos,
                CRESCENT_THRONE_FLAG_DOODAD,
            )?;
            return Ok(());
        };
        let npc_obj = WorldObject::from(&npc);
        let npc_pos = {
            let p = world_pos(server, &npc_obj);
            Vec3::new(p.x, p.y, p.z)
        };
        let pos = geo
            .as_ref()
            .and_then(|g| g.find_closest_to_the_current(npc_obj.transform().zone_id, npc_pos));
        out.send_message(&format!(
            "Closest to {} -> {}",
            v3(npc_pos),
            if pos.is_some() {
                NODE_DESCRIPTOR_TYPE
            } else {
                ""
            }
        ));
        // npc.Ai.PathNode.FindPath(npc.ParentWorld, from, to)
        let geo = geo.ok_or_else(nre)?;
        let found = {
            let geo = geo.clone();
            run_now(server, npc.obj_id, move |c| {
                c.ai.path_node.find_path(&geo, npc_pos, char_pos)
            })
            .ok_or_else(nre)?
        };
        let mut found_path = found.map_err(|e| anyhow::anyhow!(e.0))?;
        found_path.insert(0, npc_pos);
        found_path.push(char_pos);
        //npc.Ai.PathNode.FoundPath = foundPath;
        for p in &found_path {
            out.send_message(&format!("-> {}", v3(*p)));
            self.add_doodad_marker(server, &world, *p, STONE_POST_DOODAD)?;
        }
        out.send_message("Reduced:");
        let reduced_path = geo.reduce_path(&found_path, 10).map_err(|_| nre())?;
        //reducedPath.Insert(0, npc.Transform.World.Position);
        //reducedPath.Add(character.Transform.World.Position);
        for p in reduced_path {
            out.send_message(&format!("=> {}", v3(p)));
            self.add_doodad_marker(server, &world, p, CRESCENT_THRONE_FLAG_DOODAD)?;
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Shows route to target".into()
    }
}

#[cfg(test)]
mod tests {
    use std::collections::HashMap;
    use std::sync::Arc;

    use super::super::harness;
    use super::super::ping_position::tests::MemFiles;
    use crate::data::GameData;
    use crate::models::npc::Npc;
    use crate::systems::ai::framework::AiKind;
    use crate::systems::ai::geodata::bai::test_files::net_mission;
    use crate::systems::ai::geodata::bai::UsedAreaNames;
    use crate::systems::ai::geodata::manager::AiGeoData;
    use crate::systems::npcs::doodad::templates::DoodadTemplate;
    use crate::world::Entity;

    fn data() -> GameData {
        let mut data = GameData::empty();
        let d = &mut data.systems.npcs.doodad;
        for id in [super::STONE_POST_DOODAD, super::CRESCENT_THRONE_FLAG_DOODAD] {
            d.templates.insert(
                id,
                DoodadTemplate {
                    id,
                    ..DoodadTemplate::default()
                },
            );
            d.template_order.push(id);
        }
        data
    }

    /// A 3x3 grid of navmesh nodes 10 m apart (10..30, 10..30, z 5) in path block (0, 0).
    fn install_geo(server: &crate::server::GameServer) {
        let mut nodes = Vec::new();
        let mut id = 1;
        for x in 0..3 {
            for y in 0..3 {
                nodes.push((id, [10.0 + x as f32 * 10.0, 10.0 + y as f32 * 10.0, 5.0]));
                id += 1;
            }
        }
        let idx = |x: i32, y: i32| (x * 3 + y + 1) as u32;
        let mut links = Vec::new();
        for x in 0..3 {
            for y in 0..3 {
                for (dx, dy) in [(1, 0), (-1, 0), (0, 1), (0, -1)] {
                    let (nx, ny) = (x + dx, y + dy);
                    if (0..3).contains(&nx) && (0..3).contains(&ny) {
                        links.push((idx(x, y), idx(nx, ny)));
                    }
                }
            }
        }
        let mut files = HashMap::new();
        files.insert(
            "game/worlds/main_world/paths/000_000/netmission0.bai".to_string(),
            net_mission(&nodes, &links),
        );
        let t = server.game_data.worlds.get_world_template_by_id(0).unwrap();
        server.systems.ai.geodata.install(Arc::new(AiGeoData::new(
            t,
            true,
            Arc::new(MemFiles(files)),
            Arc::new(UsedAreaNames::default()),
            None,
        )));
    }

    #[tokio::test]
    async fn test_nav_mesh_marks_the_route() {
        let server = harness::server_with(data());
        install_geo(&server);
        let mut gm = harness::pc(&server, 0x600, "Gm", 31.0, 29.0);
        let world = server.world.world(0).unwrap();
        // the markers are shown (in the regions) without being added to the object table
        let me = crate::world::WorldObject::from(&gm.p);
        let markers = || {
            world
                .objects_around(&me)
                .into_iter()
                .filter_map(|o| o.as_doodad().cloned())
                .collect::<Vec<_>>()
        };

        // No NPC target: the closest node
        let (handled, out) = harness::run(&server, &gm.p, "testnavmesh").await;
        assert!(handled);
        assert_eq!(
            out,
            ["Your closest node is: AAEmu.Game.Models.CryEngine.Entities.NodeDescriptor"]
        );
        let doodads = markers();
        assert_eq!(doodads.len(), 1);
        let t = doodads[0].data().transform;
        assert_eq!((t.x, t.y, t.z), (30.0, 30.0, 5.0));
        gm.chat();

        // An NPC with an AI as the target
        let mut n = Npc::new(77);
        n.unit.transform.set_xyz(11.0, 9.0, 5.0);
        let npc = Entity::new(0x900, n);
        server.systems.ai.ais.add_ai(&npc, AiKind::HoldPosition);
        world.spawn(&npc, &server.view_ctx());
        gm.p.data().unit.current_target = npc.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "test_navmesh").await;
        assert_eq!(
            out[0],
            "Closest to <11, 9, 5> -> AAEmu.Game.Models.CryEngine.Entities.NodeDescriptor"
        );
        assert_eq!(out[1], "-> <11, 9, 5>");
        assert_eq!(out.last().unwrap(), "=> <31, 29, 100>");
        let reduced = out.iter().position(|l| l == "Reduced:").unwrap();
        // every line is a marker; the previous markers were removed
        assert_eq!(markers().len(), out.len() - 2);
        assert!(out[reduced + 1].starts_with("=> "));

        // An NPC without an AI: NullReferenceException after the first line
        let n2 = Entity::new(0x901, Npc::new(78));
        world.spawn(&n2, &server.view_ctx());
        gm.p.data().unit.current_target = n2.obj_id;
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "testnavmesh").await;
        assert_eq!(out.len(), 1);
        assert_eq!(
            gm.chat().last().unwrap(),
            "|cFFFF0000Object reference not set to an instance of an object.|r"
        );
        assert!(markers().is_empty());
    }
}
