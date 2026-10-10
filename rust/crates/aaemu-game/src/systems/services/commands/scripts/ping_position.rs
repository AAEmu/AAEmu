//! `Scripts/Commands/PingPosition.cs`.

use super::prelude::*;
use crate::systems::skills::controllers::Vec3;

/// `PingPosition`.
pub struct PingPosition;

impl PingPosition {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `character.ParentWorld.Template.GeoData.GetHeight(new Vector3(x, y, 5000f))` (the
/// `ParentWorld == null` exception as `Err`).
pub(crate) fn ping_height(
    server: &GameServer,
    character: &PlayerRef,
    x: f32,
    y: f32,
) -> anyhow::Result<f32> {
    if parent_world(server, character).is_none() {
        anyhow::bail!(NULL_REFERENCE);
    }
    Ok(crate::systems::ai::geodata::geo_data_height(
        server,
        &WorldObject::from(character),
        Vec3::new(x, y, 5000.0),
    ))
}

impl Command for PingPosition {
    fn command_names(&self) -> Vec<String> {
        names(&["pingpos", "ping_pos", "pingposition"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let (px, py, _) = character.data().local_ping_position;
        if px == 0.0 && py == 0.0 {
            CommandManager::send_error_text(
                self,
                out,
                "Make sure you marked a location on the map WHILE IN A PARTY OR RAID, using this command.\n\
                 If required, you can use the /soloparty command to make a party of just yourself.|r",
            );
        } else {
            let height = ping_height(server, &character, px, py)?;
            let (x, y) = (fixed(f64::from(px), 1), fixed(f64::from(py), 1));
            if height == 0.0 {
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("|cFFFFFFFFX:{x} Y:{y} Z: ???|r"),
                );
            } else {
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("|cFFFFFFFFX:{x} Y:{y} Z:{}|r", fixed(f64::from(height), 1)),
                );
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Displays information about your pinged position. (map marker)".into()
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use std::collections::HashMap;
    use std::sync::Arc;

    use super::super::harness;
    use crate::data::client_files::ClientFileManager;
    use crate::server::GameServer;
    use crate::systems::ai::geodata::bai::UsedAreaNames;
    use crate::systems::ai::geodata::manager::AiGeoData;
    use crate::systems::ai::geodata::HeightmapTerrain;
    use crate::systems::npcs::heightmap::{
        HeightmapConfig, HeightmapWorlds, WorldHeightmap, CELL_HMAP_RESOLUTION,
    };
    use crate::test_support::TEST_ZONE;

    /// No `.bai` files.
    struct NoFiles;

    impl crate::systems::ai::geodata::bai::BaiFiles for NoFiles {
        fn files_in_directory(&self, _dir: &str, _pattern: &str) -> Vec<String> {
            Vec::new()
        }
        fn file_exists(&self, _name: &str) -> bool {
            false
        }
        fn read_file(&self, _name: &str) -> Option<Vec<u8>> {
            None
        }
    }

    /// An in-memory client file set (`.bai` files by path).
    pub(crate) struct MemFiles(pub HashMap<String, Vec<u8>>);

    impl crate::systems::ai::geodata::bai::BaiFiles for MemFiles {
        fn files_in_directory(&self, dir: &str, pattern: &str) -> Vec<String> {
            let dir = dir.to_lowercase();
            let pat = pattern.to_lowercase().replace('*', "");
            let mut v: Vec<String> = self
                .0
                .keys()
                .filter(|k| k.to_lowercase().starts_with(&dir))
                .filter(|k| {
                    let name = k.rsplit('/').next().unwrap_or("").to_lowercase();
                    let (pre, post) = pat.split_once('.').unwrap_or((&pat, ""));
                    name.starts_with(pre) && name.ends_with(&format!(".{post}"))
                })
                .cloned()
                .collect();
            v.sort();
            v
        }
        fn file_exists(&self, name: &str) -> bool {
            self.0.contains_key(name)
        }
        fn read_file(&self, name: &str) -> Option<Vec<u8>> {
            self.0.get(name).cloned()
        }
    }

    /// Installs a heightmap on the synthetic `main_world` whose cell (0, 0) has the raw height
    /// `x_index + 2 * y_index` metres (2 m grid), and the geodata on top of it (no bai files:
    /// `GetHeight` falls back to the raw heightmap).
    pub(crate) fn install_terrain(server: &GameServer) {
        let template = server
            .game_data
            .worlds
            .get_world_template_by_id(0)
            .expect("main_world");
        let hm = WorldHeightmap::new(template);
        let coef = hm.height_max_coefficient;
        let mut h = vec![0u16; (CELL_HMAP_RESOLUTION * CELL_HMAP_RESOLUTION) as usize];
        for x in 0..CELL_HMAP_RESOLUTION {
            for y in 0..CELL_HMAP_RESOLUTION {
                h[(x * CELL_HMAP_RESOLUTION + y) as usize] =
                    (f64::from(x + 2 * y) * coef).round() as u16;
            }
        }
        assert!(hm.insert_cell(0, 0, h));
        let mut zones = HashMap::new();
        zones.insert(TEST_ZONE, 0);
        let worlds = Arc::new(HeightmapWorlds::from_parts(
            Arc::new(ClientFileManager::new()),
            vec![hm],
            zones,
            HeightmapConfig {
                heightmaps_enable: true,
                ..Default::default()
            },
        ));
        server.systems.npcs.heightmaps.install(worlds.clone());
        server.systems.ai.geodata.install(Arc::new(AiGeoData::new(
            template,
            false,
            Arc::new(NoFiles),
            Arc::new(UsedAreaNames::default()),
            Some(Arc::new(HeightmapTerrain {
                worlds,
                template_id: 0,
            })),
        )));
    }

    #[tokio::test]
    async fn ping_position_reports_the_marked_height() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let (handled, out) = harness::run(&server, &gm.p, "pingpos").await;
        assert!(handled);
        assert_eq!(
            out,
            [harness::error(
                "pingpos",
                "Make sure you marked a location on the map WHILE IN A PARTY OR RAID, using this command.\nIf required, you can use the /soloparty command to make a party of just yourself.|r"
            )]
        );
        // No height data: Z unknown
        gm.p.data().local_ping_position = (10.25, 20.0, 0.0);
        let (_, out) = harness::run(&server, &gm.p, "ping_pos").await;
        assert_eq!(
            out,
            [harness::normal(
                "pingpos",
                "|cFFFFFFFFX:10.3 Y:20.0 Z: ???|r"
            )]
        );
        install_terrain(&server);
        // grid point (5, 10): 5 + 2 * 10 = 25 m
        let (_, out) = harness::run(&server, &gm.p, "pingposition").await;
        assert_eq!(
            out,
            [harness::normal(
                "pingpos",
                "|cFFFFFFFFX:10.3 Y:20.0 Z:25.0|r"
            )]
        );
    }
}
