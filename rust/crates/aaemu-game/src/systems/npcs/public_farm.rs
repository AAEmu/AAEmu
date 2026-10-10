//! `PublicFarmManager` (`Core/Managers/PublicFarmManager.cs`) + `CommonFarmGameData`
//! (`common_farms`, `farm_groups`, `farm_group_doodads`): public farm sub zones, placement
//! checks for `CSCreateDoodad`, the guard-time tick that releases expired public farm doodads,
//! `CSRequestCommonFarmList` and the common farm packets.

use std::collections::{BTreeMap, HashMap, HashSet};
use std::sync::Arc;
use std::time::Duration;

use aaemu_commons::{helpers, Marshal, PacketStream};
use futures::future::BoxFuture;

use crate::data::sqlite::for_each_row;
use crate::data::world_templates::WorldTemplate;
use crate::error::Result;
use crate::models::doodad::doodad_owner_type;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::systems::DataLoadCtx;
use crate::world::{now_ms, DoodadRef, PlayerRef, WorldInstance, WorldObject};

/// `FarmGroupKind`.
pub mod farm_group_kind {
    pub const INVALID: u8 = 0;
    pub const FARM: u8 = 1;
    pub const NURSERY: u8 = 2;
    pub const RANCH: u8 = 3;
    pub const STABLE: u8 = 4;
}

/// `ErrorMessageType` values used here.
pub mod error_message {
    pub const COMMON_FARM_COUNT_OVER: i16 = 493;
    pub const COMMON_FARM_NOT_ALLOWED_TYPE: i16 = 494;
}

/// `CommonFarm`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct CommonFarm {
    pub id: u32,
    /// `FarmId` (`FarmGroupKind`).
    pub farm_id: u32,
    /// ms.
    pub guard_time: u32,
    pub comments: String,
}

/// `FarmGroup`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct FarmGroup {
    pub id: u32,
    pub count: u32,
}

/// `FarmGroupDoodads`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct FarmGroupDoodads {
    pub id: u32,
    pub farm_id: u32,
    pub doodad_id: u32,
    pub item_id: u32,
}

/// `CommonFarmGameData` + `PublicFarmManager._farmZones`.
#[derive(Debug, Clone, Default)]
pub struct PublicFarmData {
    /// `_commonFarm.Values` (insertion order, `TryAdd` by id).
    pub common_farms: Vec<CommonFarm>,
    /// `_farmGroup`.
    pub farm_groups: HashMap<u32, FarmGroup>,
    /// `_farmGroupDoodads.Values` (insertion order).
    pub farm_group_doodads: Vec<FarmGroupDoodads>,
    /// `_farmZones` (sub zone id -> `FarmGroupKind`; hard-coded in `PublicFarmManager.Load`).
    pub farm_zones: HashMap<u32, u8>,
}

/// `PublicFarmManager.Load`: "We have no idea where the client is actually pulling this data
/// from".
fn default_farm_zones() -> HashMap<u32, u8> {
    HashMap::from([
        (966, farm_group_kind::FARM),
        (967, farm_group_kind::RANCH),
        (968, farm_group_kind::NURSERY),
        (974, farm_group_kind::STABLE),
        (998, farm_group_kind::FARM),
    ])
}

impl PublicFarmData {
    /// `CommonFarmGameData.Load` + `PublicFarmManager.Load`.
    pub fn load(ctx: &DataLoadCtx<'_>) -> std::result::Result<Self, crate::data::GameDataError> {
        let conn = ctx.conn;
        let mut d = PublicFarmData {
            farm_zones: default_farm_zones(),
            ..Default::default()
        };
        let mut seen = HashSet::new();
        for_each_row(conn, "SELECT * FROM common_farms", [], |r| {
            let t = CommonFarm {
                id: r.u32("id")?,
                farm_id: r.u32("farm_group_id")?,
                guard_time: r.u32("guard_time")?,
                comments: r.string("comments")?,
            };
            if seen.insert(t.id) {
                d.common_farms.push(t);
            }
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM farm_groups", [], |r| {
            let t = FarmGroup {
                id: r.u32("id")?,
                count: r.u32("count")?,
            };
            d.farm_groups.entry(t.id).or_insert(t);
            Ok(())
        })?;
        let mut seen = HashSet::new();
        for_each_row(conn, "SELECT * FROM farm_group_doodads", [], |r| {
            let t = FarmGroupDoodads {
                id: r.u32("id")?,
                farm_id: r.u32("farm_group_id")?,
                doodad_id: r.u32("doodad_id")?,
                item_id: r.u32("item_id")?,
            };
            if seen.insert(t.id) {
                d.farm_group_doodads.push(t);
            }
            Ok(())
        })?;
        Ok(d)
    }

    /// `GetFarmGroupMaxCount(kind)` (0 when unknown).
    pub fn farm_group_max_count(&self, kind: u8) -> u32 {
        self.farm_groups
            .get(&u32::from(kind))
            .map_or(0, |f| f.count)
    }

    /// `GetAllowedDoodads(kind)`.
    pub fn allowed_doodads(&self, kind: u8) -> Vec<u32> {
        self.farm_group_doodads
            .iter()
            .filter(|f| f.farm_id == u32::from(kind))
            .map(|f| f.doodad_id)
            .collect()
    }

    /// `GetFarmGuardTime(kind, zoneKey)`: the guard time (ms) of the first common farm of that
    /// kind whose comment mentions the zone key, else of the first of that kind, else 0.
    pub fn farm_guard_time(&self, kind: u8, zone_key: u32) -> u32 {
        let key = zone_key.to_string();
        self.common_farms
            .iter()
            .find(|f| f.farm_id == u32::from(kind) && f.comments.contains(&key))
            .or_else(|| {
                self.common_farms
                    .iter()
                    .find(|f| f.farm_id == u32::from(kind))
            })
            .map_or(0, |f| f.guard_time)
    }

    /// `InPublicFarm` on an already computed sub zone list.
    pub fn sub_zones_in_public_farm(&self, sub_zones: &[u32]) -> bool {
        !sub_zones.is_empty() && sub_zones.iter().any(|s| self.farm_zones.contains_key(s))
    }

    /// `GetFarmType` on an already computed sub zone list (`GetFarmSubZoneId`: the first farm
    /// sub zone, 0 when none).
    pub fn farm_type_of_sub_zones(&self, sub_zones: &[u32]) -> u8 {
        let sub_zone = sub_zones
            .iter()
            .copied()
            .find(|s| self.farm_zones.contains_key(s))
            .unwrap_or(0);
        self.farm_zones
            .get(&sub_zone)
            .copied()
            .unwrap_or(farm_group_kind::INVALID)
    }
}

/// Runtime state of `PublicFarmManager` (none besides the timer).
#[derive(Debug, Default)]
pub struct PublicFarmManager {}

fn data(server: &GameServer) -> &PublicFarmData {
    &server.game_data.systems.npcs.public_farm
}

/// `SubZoneManager.GetSubZoneByPosition(worldTemplate, x, y)`.
pub fn sub_zones_by_position(
    server: &GameServer,
    world_template: &WorldTemplate,
    x: f32,
    y: f32,
) -> Vec<u32> {
    super::subzone::get_sub_zone_by_position(&server.game_data, world_template, x, y)
}

/// `InPublicFarm(worldTemplate, pos)`.
pub fn in_public_farm(
    server: &GameServer,
    world_template: &WorldTemplate,
    pos: (f32, f32, f32),
) -> bool {
    data(server).sub_zones_in_public_farm(&sub_zones_by_position(
        server,
        world_template,
        pos.0,
        pos.1,
    ))
}

/// `GetFarmType(world, pos)` (`FarmGroupKind`).
pub fn get_farm_type(server: &GameServer, world: &WorldInstance, pos: (f32, f32, f32)) -> u8 {
    data(server).farm_type_of_sub_zones(&sub_zones_by_position(
        server,
        &world.template,
        pos.0,
        pos.1,
    ))
}

/// The common farm doodads of a character, grouped by farm kind in `FarmGroupKind` order.
pub type CommonFarmDoodads = BTreeMap<u8, Vec<DoodadRef>>;

fn doodad_world_pos(world: &WorldInstance, d: &DoodadRef) -> (f32, f32, f32) {
    let p = world.world_pos_rot(&WorldObject::Doodad(d.clone()));
    (p.x, p.y, p.z)
}

/// `GetCommonFarmDoodads(character)`: the character's player doodads standing in a public
/// farm of their own farm type.
pub fn get_common_farm_doodads(server: &GameServer, player: &PlayerRef) -> CommonFarmDoodads {
    let mut list = CommonFarmDoodads::new();
    let (instance_id, character_id) = {
        let c = player.data();
        (c.unit.transform.instance_id, c.id)
    };
    let Some(world) = server.world.world(instance_id) else {
        return list;
    };
    let Some(spawns) = server.systems.npcs.spawns.get(world.id) else {
        return list;
    };
    for doodad in spawns.get_player_doodads(character_id) {
        let pos = doodad_world_pos(&world, &doodad);
        if in_public_farm(server, &world.template, pos) {
            let farm_type = get_farm_type(server, &world, pos);
            let doodad_farm_type = doodad.data().farm_type;
            if doodad_farm_type == farm_type {
                list.entry(farm_type).or_default().push(doodad);
            }
        }
    }
    list
}

/// `CanPlace(character, farmGroupKind, doodadId)`: the farm kind's count limit and allowed
/// doodads (error messages as in C#).
pub fn can_place(server: &GameServer, player: &PlayerRef, farm_kind: u8, doodad_id: u32) -> bool {
    let d = data(server);
    let all_planted = get_common_farm_doodads(server, player);
    if let Some(list) = all_planted.get(&farm_kind) {
        if list.len() as u32 >= d.farm_group_max_count(farm_kind) {
            player.send(SCErrorMsgPacket::new(
                error_message::COMMON_FARM_COUNT_OVER,
                0,
                true,
            ));
            return false;
        }
    }
    if d.allowed_doodads(farm_kind).contains(&doodad_id) {
        return true;
    }
    player.send(SCErrorMsgPacket::new(
        error_message::COMMON_FARM_NOT_ALLOWED_TYPE,
        0,
        true,
    ));
    false
}

/// `IsProtectedByPublicFarm(doodad, now)`: still inside its farm's guard time.
pub fn is_protected_by_public_farm(server: &GameServer, doodad: &DoodadRef, now_ms: i64) -> bool {
    let (farm_type, zone_id, plant_time) = {
        let d = doodad.data();
        (d.farm_type, d.transform.zone_id, d.plant_time)
    };
    is_protected(data(server), farm_type, zone_id, plant_time, now_ms)
}

/// The pure part of `IsProtectedByPublicFarm`: `now < PlantTime + guardTime` (ms).
pub fn is_protected(
    d: &PublicFarmData,
    farm_type: u8,
    zone_key: u32,
    plant_time_secs: i64,
    now_ms: i64,
) -> bool {
    let guard_time = d.farm_guard_time(farm_type, zone_key);
    if guard_time == 0 {
        return false;
    }
    now_ms < plant_time_secs * 1000 + i64::from(guard_time)
}

/// `PublicFarmTick()` (`PublicFarmTickStartTask`, every minute): player doodads of
/// `main_world` whose guard time is over become system doodads and leave the player doodad
/// list.
pub fn public_farm_tick(server: &GameServer, now_ms: i64) -> Vec<DoodadRef> {
    let mut released = Vec::new();
    let Some(world) = server.world.world(0) else {
        return released;
    };
    let Some(spawns) = server.systems.npcs.spawns.get(world.id) else {
        return released;
    };
    for doodad in spawns.get_all_player_doodads() {
        if doodad.data().farm_type == farm_group_kind::INVALID {
            continue;
        }
        if is_protected_by_public_farm(server, &doodad, now_ms) {
            continue;
        }
        {
            let mut d = doodad.data();
            d.owner_id = 0;
            d.owner_type = doodad_owner_type::SYSTEM;
            d.farm_type = farm_group_kind::INVALID;
        }
        crate::systems::npcs::doodad::db::save(server, &doodad);
        released.push(doodad);
    }
    for doodad in &released {
        spawns.remove_player_doodad(doodad);
    }
    released
}

/// `PublicFarmTickStartTask` start delay (`TimeSpan.FromMinutes(1)`).
pub const TICK_START_DELAY: Duration = Duration::from_secs(60);
/// `PublicFarmTickStartTask` repeat interval (`TimeSpan.FromMinutes(1)`).
pub const TICK_INTERVAL: Duration = Duration::from_secs(60);

/// `Initialize()`: the one-minute `PublicFarmTickStartTask`.
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    tracing::info!("Initialising Public Farm Manager...");
    tracing::info!("PublicFarmTickTask: Started");
    schedule_tick(server, TICK_START_DELAY, TICK_INTERVAL);
    Ok(())
}

/// `taskManager.Schedule(new PublicFarmTickStartTask(), startDelay, repeatInterval)`: one
/// Tokio task running [`public_farm_tick`] after `start_delay`, then every `interval`.
fn schedule_tick(server: &Arc<GameServer>, start_delay: Duration, interval: Duration) {
    let weak = Arc::downgrade(server);
    tokio::spawn(async move {
        let mut iv = tokio::time::interval_at(tokio::time::Instant::now() + start_delay, interval);
        loop {
            iv.tick().await;
            let Some(server) = weak.upgrade() else {
                break;
            };
            public_farm_tick(&server, now_ms());
        }
    });
}

// ------------------------------------------------------------------------------------------
// Packets
// ------------------------------------------------------------------------------------------

/// `CSRequestCommonFarmList`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRequestCommonFarmList;

impl ClientPacket for CSRequestCommonFarmList {
    const NAME: &'static str = "CSRequestCommonFarmList";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!("CSRequestCommonFarmList");
            let Some(me) = conn.active_char.clone() else {
                return Ok(());
            };
            let packet = response_common_farm_list(server, &me);
            me.send(packet);
            Ok(())
        })
    }
}

/// Builds `SCResponseCommonFarmListPacket(GetCommonFarmDoodads(character))`.
pub fn response_common_farm_list(
    server: &GameServer,
    player: &PlayerRef,
) -> SCResponseCommonFarmListPacket {
    let instance = player.data().unit.transform.instance_id;
    let world = server.world.world(instance);
    let now = now_ms();
    let mut entries = Vec::new();
    for (kind, doodads) in get_common_farm_doodads(server, player) {
        for d in doodads {
            let (x, y, z) = world
                .as_ref()
                .map_or((0.0, 0.0, 0.0), |w| doodad_world_pos(w, &d));
            let g = d.data();
            entries.push(CommonFarmEntry {
                farm_type: u32::from(kind),
                template_id: g.template_id,
                time_left: g.time_left(now),
                func_group_id: g.func_group_id,
                x,
                y,
                z,
                plant_time: g.plant_time,
            });
        }
    }
    SCResponseCommonFarmListPacket { entries }
}

/// C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSRequestCommonFarmList>(LEVEL_PLAIN, cs::CSRequestCommonFarmList);
}

/// One planted doodad of `SCResponseCommonFarmListPacket`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CommonFarmEntry {
    pub farm_type: u32,
    pub template_id: u32,
    pub time_left: u32,
    pub func_group_id: u32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
    /// `PlantTime` (unix seconds).
    pub plant_time: i64,
}

/// `SCResponseCommonFarmListPacket(allPlanted)` (entries in `FarmGroupKind` order).
#[derive(Debug, Clone, PartialEq)]
pub struct SCResponseCommonFarmListPacket {
    pub entries: Vec<CommonFarmEntry>,
}
packet_def!(
    SCResponseCommonFarmListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCResponseCommonFarmListPacket
);
impl Marshal for SCResponseCommonFarmListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.entries.len() as i32)
            .write_i32(self.entries.len() as i32);
        for e in &self.entries {
            s.write_u32(e.farm_type)
                .write_u32(e.template_id)
                .write_u32(e.time_left)
                .write_u32(e.func_group_id)
                .write_position(e.x, e.y, e.z)
                .write_i64(e.plant_time);
        }
    }
}

/// `SCShowCommonFarmPacket(farmType, count, pos)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCShowCommonFarmPacket {
    pub farm_type: i32,
    pub count: i32,
    pub x: f32,
    pub y: f32,
    pub z: f32,
}
packet_def!(
    SCShowCommonFarmPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCShowCommonFarmPacket
);
impl Marshal for SCShowCommonFarmPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.farm_type)
            .write_i32(self.count)
            .write_i64(helpers::convert_coord_to_long(self.x))
            .write_i64(helpers::convert_coord_to_long(self.y))
            .write_f32(self.z);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    fn body(p: &dyn crate::net::packet::ServerPacket) -> Vec<u8> {
        encode(p)[6..].to_vec()
    }

    fn sample() -> PublicFarmData {
        PublicFarmData {
            common_farms: vec![
                CommonFarm {
                    id: 10,
                    farm_id: 1,
                    guard_time: 86_400_000,
                    comments: "zone (195) - 1".into(),
                },
                CommonFarm {
                    id: 11,
                    farm_id: 1,
                    guard_time: 3_600_000,
                    comments: "zone (33) - 1".into(),
                },
                CommonFarm {
                    id: 12,
                    farm_id: 3,
                    guard_time: 0,
                    comments: "".into(),
                },
            ],
            farm_groups: HashMap::from([
                (1, FarmGroup { id: 1, count: 10 }),
                (2, FarmGroup { id: 2, count: 5 }),
            ]),
            farm_group_doodads: vec![
                FarmGroupDoodads {
                    id: 13,
                    farm_id: 2,
                    doodad_id: 413,
                    item_id: 4865,
                },
                FarmGroupDoodads {
                    id: 14,
                    farm_id: 1,
                    doodad_id: 380,
                    item_id: 13725,
                },
                FarmGroupDoodads {
                    id: 15,
                    farm_id: 1,
                    doodad_id: 381,
                    item_id: 13726,
                },
            ],
            farm_zones: default_farm_zones(),
        }
    }

    #[test]
    fn game_data_queries() {
        let d = sample();
        assert_eq!(d.farm_group_max_count(farm_group_kind::FARM), 10);
        assert_eq!(d.farm_group_max_count(farm_group_kind::STABLE), 0);
        assert_eq!(d.allowed_doodads(farm_group_kind::FARM), vec![380, 381]);
        // Comment match first, then the first of the kind.
        assert_eq!(d.farm_guard_time(farm_group_kind::FARM, 33), 3_600_000);
        assert_eq!(d.farm_guard_time(farm_group_kind::FARM, 195), 86_400_000);
        assert_eq!(d.farm_guard_time(farm_group_kind::FARM, 7), 86_400_000);
        assert_eq!(d.farm_guard_time(farm_group_kind::NURSERY, 7), 0);
        // Sub zones.
        assert!(d.sub_zones_in_public_farm(&[1, 967]));
        assert!(!d.sub_zones_in_public_farm(&[]));
        assert!(!d.sub_zones_in_public_farm(&[1, 2]));
        assert_eq!(
            d.farm_type_of_sub_zones(&[5, 974, 966]),
            farm_group_kind::STABLE
        );
        assert_eq!(d.farm_type_of_sub_zones(&[5]), farm_group_kind::INVALID);
        // Guard time (plant time in seconds, now in ms).
        let plant = 1_700_000_000;
        assert!(is_protected(
            &d,
            farm_group_kind::FARM,
            33,
            plant,
            plant * 1000 + 3_599_999
        ));
        assert!(!is_protected(
            &d,
            farm_group_kind::FARM,
            33,
            plant,
            plant * 1000 + 3_600_000
        ));
        assert!(!is_protected(&d, farm_group_kind::RANCH, 33, plant, 0));
    }

    #[test]
    fn packets_are_byte_exact() {
        let p = SCResponseCommonFarmListPacket {
            entries: vec![CommonFarmEntry {
                farm_type: 1,
                template_id: 380,
                time_left: 5000,
                func_group_id: 77,
                x: 100.0,
                y: 200.0,
                z: 30.0,
                plant_time: 1_700_000_000,
            }],
        };
        let mut e = Vec::new();
        e.extend_from_slice(&1i32.to_le_bytes());
        e.extend_from_slice(&1i32.to_le_bytes());
        for v in [1u32, 380, 5000, 77] {
            e.extend_from_slice(&v.to_le_bytes());
        }
        e.extend_from_slice(&helpers::encode_position(100.0, 200.0, 30.0));
        e.extend_from_slice(&1_700_000_000i64.to_le_bytes());
        assert_eq!(body(&p), e);

        let mut e = Vec::new();
        e.extend_from_slice(&3i32.to_le_bytes());
        e.extend_from_slice(&2i32.to_le_bytes());
        e.extend_from_slice(&(((1024.5f32 * 4096.0) as i64) << 32).to_le_bytes());
        e.extend_from_slice(&(((2048.0f32 * 4096.0) as i64) << 32).to_le_bytes());
        e.extend_from_slice(&12.5f32.to_le_bytes());
        assert_eq!(
            body(&SCShowCommonFarmPacket {
                farm_type: 3,
                count: 2,
                x: 1024.5,
                y: 2048.0,
                z: 12.5
            }),
            e
        );
        let mut s = PacketStream::from_bytes(Vec::<u8>::new());
        assert_eq!(
            CSRequestCommonFarmList::read(&mut s).unwrap(),
            CSRequestCommonFarmList
        );
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db_common_farms() {
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
        let d = PublicFarmData::load(&ctx).unwrap();
        let count = |t: &str| -> usize {
            conn.query_row(&format!("SELECT COUNT(*) FROM {t}"), [], |r| {
                r.get::<_, i64>(0)
            })
            .unwrap() as usize
        };
        assert_eq!(d.common_farms.len(), count("common_farms"));
        assert_eq!(d.farm_groups.len(), count("farm_groups"));
        assert_eq!(d.farm_group_doodads.len(), count("farm_group_doodads"));
        eprintln!(
            "common farms {} groups {} group doodads {}; farm guard {} ms, max farm {}",
            d.common_farms.len(),
            d.farm_groups.len(),
            d.farm_group_doodads.len(),
            d.farm_guard_time(farm_group_kind::FARM, 195),
            d.farm_group_max_count(farm_group_kind::FARM)
        );
    }
}

#[cfg(test)]
#[path = "public_farm_tests.rs"]
mod port_tests;
