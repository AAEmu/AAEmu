//! `FishSchoolManager` (`Core/Managers/FishSchoolManager.cs`): the fish school doodads of
//! every world instance (used by the radar / fish finder), `FishDetailsGameData`
//! (`fish_details`: trophy fish sizes) and the fish finder packets
//! (`SCSchoolOfFishDoodadsPacket`, `SCSchoolOfFishFinderToggledPacket`).
//!
//! `FishDetailsGameData.Create` (building a `BigFish` item) is item code and belongs to the
//! economy subsystem; the size helpers it uses are here.

use std::collections::HashMap;
use std::sync::{Arc, Mutex};

use aaemu_commons::{Marshal, PacketStream};
use rand::Rng;

use crate::data::sqlite::for_each_row;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::sc;
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;
use crate::world::{DoodadRef, WorldInstance, WorldObject};

/// `DoodadConstants.FreshwaterFishSchool`.
pub const FRESHWATER_FISH_SCHOOL: u32 = 6447;
/// `DoodadConstants.SaltwaterFishSchool`.
pub const SALTWATER_FISH_SCHOOL: u32 = 6448;

/// `FishDetails`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct FishDetails {
    pub id: i32,
    pub name: String,
    pub item_id: u32,
    pub min_weight: i32,
    pub max_weight: i32,
    pub min_length: i32,
    pub max_length: i32,
}

/// `FishDetailsGameData` (`_fishDetails`, keyed by the fish item id).
#[derive(Debug, Default)]
pub struct FishSchoolData {
    pub fish_details: HashMap<u32, FishDetails>,
}

impl FishSchoolData {
    /// `FishDetailsGameData.Load` (names through `LocalizationManager.Get("fish_details",
    /// "name", id, name)`).
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        let mut names: HashMap<i64, String> = HashMap::new();
        let sql = format!(
            "SELECT idx, \"{}\" AS txt FROM localized_texts \
             WHERE tbl_name = 'fish_details' AND tbl_column_name = 'name'",
            ctx.language.replace('"', "")
        );
        // Missing localization (table / language column) keeps the DB names.
        let _ = for_each_row(ctx.conn, &sql, [], |r| {
            let text = r.string_or("txt", "")?;
            if !text.is_empty() {
                names.insert(r.i64("idx")?, text);
            }
            Ok(())
        });
        let mut d = FishSchoolData::default();
        for_each_row(ctx.conn, "SELECT * FROM fish_details", [], |r| {
            let id = r.i32("id")?;
            let db_name = r.string("name")?;
            let t = FishDetails {
                id,
                name: names.get(&i64::from(id)).cloned().unwrap_or(db_name),
                item_id: r.u32("item_id")?,
                min_weight: r.i32("min_weight")?,
                max_weight: r.i32("max_weight")?,
                min_length: r.i32("min_length")?,
                max_length: r.i32("max_length")?,
            };
            d.fish_details.entry(t.item_id).or_insert(t);
            Ok(())
        })?;
        tracing::info!("Loaded {} fish details", d.fish_details.len());
        Ok(d)
    }

    /// `GetFishLength(templateId)`: `Random.Next(MinLength, MaxLength)` (max exclusive).
    /// `None` for an unknown fish (the C# throws `KeyNotFoundException`).
    pub fn fish_length(&self, template_id: u32, rng: &mut impl Rng) -> Option<f32> {
        let d = self.fish_details.get(&template_id)?;
        Some(random_next(rng, d.min_length, d.max_length) as f32)
    }

    /// `GetFishWeight(templateId)`: `Random.Next(MinWeight, MaxWeight)`.
    pub fn fish_weight_random(&self, template_id: u32, rng: &mut impl Rng) -> Option<f32> {
        let d = self.fish_details.get(&template_id)?;
        Some(random_next(rng, d.min_weight, d.max_weight) as f32)
    }

    /// `GetFishWeight(templateId, amount)`: `Lerp(MinWeight, MaxWeight, amount)`.
    pub fn fish_weight(&self, template_id: u32, amount: f32) -> Option<f32> {
        let d = self.fish_details.get(&template_id)?;
        Some(lerp(d.min_weight as f32, d.max_weight as f32, amount))
    }

    /// `GetFishSize(templateId)`: `(length, weight)` with the weight interpolated by
    /// `length / MaxLength`. (`Create(Item)` passes the item's `MadeUnitId` as the template id
    /// — a C# bug the economy port has to mirror.)
    pub fn fish_size(&self, template_id: u32, rng: &mut impl Rng) -> Option<(f32, f32)> {
        let length = self.fish_length(template_id, rng)?;
        let max_length = self.fish_details.get(&template_id)?.max_length;
        let amount = length / max_length as f32;
        Some((length, self.fish_weight(template_id, amount)?))
    }
}

/// `Random.Next(min, max)`: `min` when `max <= min` (the C# throws for `max < min`).
fn random_next(rng: &mut impl Rng, min: i32, max: i32) -> i32 {
    if max <= min {
        return min;
    }
    rng.gen_range(min..max)
}

fn lerp(v1: f32, v2: f32, t: f32) -> f32 {
    v1 + (v2 - v1) * t
}

/// Runtime state of `FishSchoolManager`: `FishSchools` (world instance id -> fish school
/// doodads).
#[derive(Debug, Default)]
pub struct FishSchoolManager {
    fish_schools: Mutex<HashMap<u32, Vec<DoodadRef>>>,
}

impl FishSchoolManager {
    /// `Initialize()`.
    pub fn initialize(&self) {
        self.fish_schools
            .lock()
            .expect("fish schools poisoned")
            .clear();
        tracing::info!("Initialising FishSchool Manager...");
    }

    /// `GetAllFishSchools()`.
    pub fn all_fish_schools(&self) -> Vec<DoodadRef> {
        let fs = self.fish_schools.lock().expect("fish schools poisoned");
        let mut ids: Vec<&u32> = fs.keys().collect();
        ids.sort();
        ids.into_iter()
            .flat_map(|id| fs[id].iter().cloned())
            .collect()
    }

    /// The fish schools of one world instance.
    pub fn fish_schools_of(&self, world_id: u32) -> Vec<DoodadRef> {
        self.fish_schools
            .lock()
            .expect("fish schools poisoned")
            .get(&world_id)
            .cloned()
            .unwrap_or_default()
    }
}

/// `FishSchoolManager.Load(world)` (called by `SpawnManager` after the world's doodads are
/// spawned): collects the fresh / salt water fish school doodads of `world`.
pub fn load_world(server: &GameServer, world: &Arc<WorldInstance>) {
    tracing::info!("Loading FishSchool...");
    let fish: Vec<DoodadRef> = world
        .all_doodads()
        .into_iter()
        .filter(|d| {
            let t = d.data().template_id;
            t == FRESHWATER_FISH_SCHOOL || t == SALTWATER_FISH_SCHOOL
        })
        .collect();
    let count = fish.len();
    if count > 0 {
        server
            .systems
            .npcs
            .fish_schools
            .fish_schools
            .lock()
            .expect("fish schools poisoned")
            .entry(world.id)
            .or_default()
            .extend(fish);
    }
    tracing::info!("Loaded {count} FishSchool for world {} ...", world.id);
}

/// Startup, after the spawners ran: `Initialize()` + `Load(world)` for every world instance.
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    server.systems.npcs.fish_schools.initialize();
    for world in server.world.worlds() {
        load_world(server, &world);
    }
    Ok(())
}

/// No C->S packets.
pub fn register(_reg: &mut PacketRegistry) {}

/// One `Doodad.WriteFishFinderUnit` entry.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct FishFinderUnit {
    pub obj_id: u32,
    pub template_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
}

impl FishFinderUnit {
    /// The entry of `doodad` (world position).
    pub fn of(world: &WorldInstance, doodad: &DoodadRef) -> Self {
        let pos = world.world_pos_rot(&WorldObject::Doodad(doodad.clone()));
        Self {
            obj_id: doodad.obj_id,
            template_id: doodad.data().template_id,
            x: pos.x,
            y: pos.y,
            z: pos.z,
        }
    }
}

impl Marshal for FishFinderUnit {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u32(self.template_id)
            .write_position(self.x, self.y, self.z);
    }
}

/// `SCSchoolOfFishDoodadsPacket(last, transfers)` (at most 10 entries per packet).
#[derive(Debug, Clone, PartialEq)]
pub struct SCSchoolOfFishDoodadsPacket {
    pub last: bool,
    pub transfers: Vec<FishFinderUnit>,
}
packet_def!(
    SCSchoolOfFishDoodadsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSchoolOfFishDoodadsPacket
);
impl Marshal for SCSchoolOfFishDoodadsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.last).write_u8(self.transfers.len() as u8);
        for t in &self.transfers {
            t.write(s);
        }
    }
}

/// `SCSchoolOfFishFinderToggledPacket(on, range)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCSchoolOfFishFinderToggledPacket {
    pub on: bool,
    pub range: f32,
}
packet_def!(
    SCSchoolOfFishFinderToggledPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCSchoolOfFishFinderToggledPacket
);
impl Marshal for SCSchoolOfFishFinderToggledPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.on).write_f32(self.range);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::doodad::Doodad;
    use crate::net::packet::encode;
    use crate::test_support::test_server;
    use crate::world::object::Entity;

    fn body(p: &dyn crate::net::packet::ServerPacket) -> Vec<u8> {
        encode(p)[6..].to_vec()
    }

    #[test]
    fn packets_are_byte_exact() {
        let mut e = vec![1u8, 2];
        for (id, t, x, y, z) in [
            (0x010203u32, 6447u32, 100.0f32, 200.0f32, 50.0f32),
            (5, 6448, 1.0, 2.0, 3.0),
        ] {
            e.extend_from_slice(&id.to_le_bytes()[..3]);
            e.extend_from_slice(&t.to_le_bytes());
            e.extend_from_slice(&aaemu_commons::helpers::encode_position(x, y, z));
        }
        assert_eq!(
            body(&SCSchoolOfFishDoodadsPacket {
                last: true,
                transfers: vec![
                    FishFinderUnit {
                        obj_id: 0x010203,
                        template_id: 6447,
                        x: 100.0,
                        y: 200.0,
                        z: 50.0
                    },
                    FishFinderUnit {
                        obj_id: 5,
                        template_id: 6448,
                        x: 1.0,
                        y: 2.0,
                        z: 3.0
                    },
                ],
            }),
            e
        );
        let mut e = vec![1u8];
        e.extend_from_slice(&250.5f32.to_le_bytes());
        assert_eq!(
            body(&SCSchoolOfFishFinderToggledPacket {
                on: true,
                range: 250.5
            }),
            e
        );
    }

    #[test]
    fn fish_size_follows_lerp() {
        let mut d = FishSchoolData::default();
        d.fish_details.insert(
            27602,
            FishDetails {
                id: 10,
                name: "marlin".into(),
                item_id: 27602,
                min_weight: 515,
                max_weight: 644,
                min_length: 286,
                max_length: 358,
            },
        );
        let mut rng = rand::thread_rng();
        for _ in 0..100 {
            let (len, w) = d.fish_size(27602, &mut rng).unwrap();
            assert!((286.0..358.0).contains(&len));
            assert!((w - (515.0 + 129.0 * (len / 358.0))).abs() < 1e-3);
        }
        assert_eq!(d.fish_weight(27602, 0.5), Some(579.5));
        assert!(d.fish_size(1, &mut rng).is_none());
    }

    #[tokio::test]
    async fn load_world_collects_fish_schools() {
        let server = test_server();
        let world = server.world.world(0).unwrap();
        let ctx = server.view_ctx();
        let mut ids = Vec::new();
        for (i, t) in [6447u32, 1000, 6448].into_iter().enumerate() {
            let mut d = Doodad::new(t);
            d.transform.set_xyz(100.0 + i as f32, 100.0, 50.0);
            let d = Entity::new(server.ids.object.get_next_id().unwrap(), d);
            world.spawn(&d, &ctx);
            ids.push(d.obj_id);
        }
        server.systems.npcs.fish_schools.initialize();
        load_world(&server, &world);
        let mut got: Vec<u32> = server
            .systems
            .npcs
            .fish_schools
            .all_fish_schools()
            .iter()
            .map(|d| d.obj_id)
            .collect();
        got.sort();
        assert_eq!(got, vec![ids[0], ids[2]]);
        let unit = FishFinderUnit::of(
            &world,
            &server.systems.npcs.fish_schools.fish_schools_of(0)[0],
        );
        assert_eq!(unit.z, 50.0);
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db_fish_details() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = rusqlite::Connection::open(&path).unwrap();
        let files = crate::data::client_files::ClientFileManager::from_sources(
            &[],
            std::path::Path::new("."),
        );
        let ctx = DataLoadCtx {
            conn: &conn,
            app_path: std::path::Path::new("."),
            files: &files,
            language: "en_us",
        };
        let d = FishSchoolData::load(&ctx).unwrap();
        let n: i64 = conn
            .query_row(
                "SELECT COUNT(DISTINCT item_id) FROM fish_details",
                [],
                |r| r.get(0),
            )
            .unwrap();
        assert_eq!(d.fish_details.len() as i64, n);
        eprintln!("fish details: {}", d.fish_details.len());
    }
}
