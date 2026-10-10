//! `Core/Managers/RadarManager` + `Models/Game/TelescopeRegistrationEntry`: the public
//! transport telescope, the fish finder and the ship telescope (buffs with
//! `TransferTelescopeRange` / `FindSchoolOfFishRange` / `TelescopeRange` register the
//! character; every second the objects in range are sent, 10 per packet).

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex, Weak};
use std::time::Duration;

use aaemu_commons::{Marshal, PacketStream};

use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::systems::npcs::fish_school::{
    FishFinderUnit, SCSchoolOfFishDoodadsPacket, SCSchoolOfFishFinderToggledPacket,
};
use crate::systems::HookSlot;
use crate::world::{ObjectKind, PlayerRef, SlaveRef, WorldInstance, WorldObject};

/// `RadarUpdateDelay` (ms).
pub const RADAR_UPDATE_DELAY_MS: u64 = 1000;
/// `TransfersPerPacket` / `FishPerPacket` / `ShipsPerPacket`.
pub const UNITS_PER_PACKET: usize = 10;
/// `TemplateId == 46`: carriage boardings are ignored.
const CARRIAGE_BOARDING_TEMPLATE: u32 = 46;

/// `SCTransferTelescopeToggledPacket(on, range)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCTransferTelescopeToggledPacket {
    pub on: bool,
    pub range: f32,
}
packet_def!(
    SCTransferTelescopeToggledPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTransferTelescopeToggledPacket
);
impl Marshal for SCTransferTelescopeToggledPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.on).write_f32(self.range);
    }
}

/// `SCTelescopeToggledPacket(on, range)`.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SCTelescopeToggledPacket {
    pub on: bool,
    pub range: f32,
}
packet_def!(
    SCTelescopeToggledPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTelescopeToggledPacket
);
impl Marshal for SCTelescopeToggledPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.on).write_f32(self.range);
    }
}

/// `Transfer.WriteTelescopeUnit`.
#[derive(Debug, Clone, PartialEq)]
pub struct TransferTelescopeUnit {
    pub obj_id: u32,
    pub template_id: u32,
    pub pos: (f32, f32, f32),
    pub name: String,
}

impl Marshal for TransferTelescopeUnit {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u32(self.template_id)
            .write_position(self.pos.0, self.pos.1, self.pos.2)
            .write_str(&self.name);
    }
}

/// `SCTransferTelescopeUnitsPacket(last, transfers)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCTransferTelescopeUnitsPacket {
    pub last: bool,
    pub transfers: Vec<TransferTelescopeUnit>,
}
packet_def!(
    SCTransferTelescopeUnitsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTransferTelescopeUnitsPacket
);
impl Marshal for SCTransferTelescopeUnitsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.last).write_u8(self.transfers.len() as u8);
        for t in &self.transfers {
            t.write(s);
        }
    }
}

/// One ship of `SCTelescopeUnitsPacket`.
#[derive(Debug, Clone, PartialEq)]
pub struct ShipTelescopeUnit {
    pub obj_id: u32,
    /// `Summoner?.Id ?? 0`.
    pub summoner_id: u32,
    pub template_id: u32,
    pub pos: (f32, f32, f32),
    pub faction_id: u32,
    pub expedition_id: u32,
    pub name: String,
    pub summoner_name: String,
    pub hp: i32,
    pub max_hp: i32,
}

impl Marshal for ShipTelescopeUnit {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u32(self.summoner_id)
            .write_i32(2) // UnitType Slave
            .write_u32(self.template_id)
            .write_position(self.pos.0, self.pos.1, self.pos.2)
            .write_u32(self.faction_id)
            .write_u32(self.expedition_id)
            .write_str(&self.name)
            .write_str(&self.summoner_name)
            .write_i32(self.hp)
            .write_i32(self.max_hp)
            .write_u32(0);
    }
}

/// `SCTelescopeUnitsPacket(last, slaves)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCTelescopeUnitsPacket {
    pub last: bool,
    pub slaves: Vec<ShipTelescopeUnit>,
}
packet_def!(
    SCTelescopeUnitsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTelescopeUnitsPacket
);
impl Marshal for SCTelescopeUnitsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.last).write_u8(self.slaves.len() as u8);
        for t in &self.slaves {
            t.write(s);
        }
    }
}

/// `SlaveManager.GetActiveSlavesByKinds(Boat, Fishboat, Speedboat, MerchantShip,
/// BigSailingShip, SmallSailingShip)` of one world instance (vehicles subsystem).
pub trait RadarShipHooks: Send + Sync {
    fn active_ships(&self, server: &GameServer, world: &Arc<WorldInstance>) -> Vec<SlaveRef>;
}

/// `TelescopeRegistrationEntry`.
#[derive(Debug, Clone)]
pub struct TelescopeRegistrationEntry {
    pub player: PlayerRef,
    show_public_transport_range: f32,
    show_fish_school_range: f32,
    show_ship_telescope_range: f32,
}

impl TelescopeRegistrationEntry {
    fn new(player: PlayerRef) -> Self {
        Self {
            player,
            show_public_transport_range: 0.0,
            show_fish_school_range: 0.0,
            show_ship_telescope_range: 0.0,
        }
    }

    /// `ShowPublicTransportRange` setter (changes smaller than 1 m are ignored).
    pub fn set_public_transport_range(&mut self, value: f32) {
        if (self.show_public_transport_range - value).abs() < 1.0 {
            return;
        }
        self.show_public_transport_range = value;
        self.player.send(SCTransferTelescopeToggledPacket {
            on: value > 0.0,
            range: value,
        });
    }

    /// `ShowFishSchoolRange` setter.
    pub fn set_fish_school_range(&mut self, value: f32) {
        if (self.show_fish_school_range - value).abs() < 1.0 {
            return;
        }
        self.show_fish_school_range = value;
        self.player.send(SCSchoolOfFishFinderToggledPacket {
            on: value > 0.0,
            range: value,
        });
    }

    /// `ShowShipTelescopeRange` setter.
    pub fn set_ship_telescope_range(&mut self, value: f32) {
        if (self.show_ship_telescope_range - value).abs() < 1.0 {
            return;
        }
        self.show_ship_telescope_range = value;
        self.player.send(SCTelescopeToggledPacket {
            on: value > 0.0,
            range: value,
        });
    }

    pub fn public_transport_range(&self) -> f32 {
        self.show_public_transport_range
    }

    pub fn fish_school_range(&self) -> f32 {
        self.show_fish_school_range
    }

    pub fn ship_telescope_range(&self) -> f32 {
        self.show_ship_telescope_range
    }

    /// `IsActive`.
    pub fn is_active(&self) -> bool {
        self.show_public_transport_range > 0.0
            || self.show_fish_school_range > 0.0
            || self.show_ship_telescope_range > 0.0
    }
}

/// `RadarManager`.
#[derive(Default)]
pub struct RadarManager {
    /// `Registrations` (character id -> entry), under the static `Lock`.
    registrations: Mutex<BTreeMap<u32, TelescopeRegistrationEntry>>,
    /// The ship lists of the vehicles subsystem.
    pub ships: HookSlot<dyn RadarShipHooks>,
}

impl std::fmt::Debug for RadarManager {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("RadarManager")
            .field(
                "registrations",
                &self.registrations.lock().map(|r| r.len()).unwrap_or(0),
            )
            .finish()
    }
}

impl RadarManager {
    /// `Initialize()`: `RadarTick` every second (async subscriber).
    pub fn initialize(server: &Arc<GameServer>) {
        let weak: Weak<GameServer> = Arc::downgrade(server);
        let rt = tokio::runtime::Handle::try_current().ok();
        server.systems.services.tick.on_tick.subscribe(
            move |_| {
                let (Some(server), Some(rt)) = (weak.upgrade(), rt.as_ref()) else {
                    return;
                };
                rt.block_on(server.systems.services.radar.radar_tick(&server));
            },
            Duration::from_millis(RADAR_UPDATE_DELAY_MS),
            true,
        );
    }

    fn register(&self, player: &PlayerRef, set: impl FnOnce(&mut TelescopeRegistrationEntry)) {
        let mut regs = self.registrations.lock().expect("radar poisoned");
        let id = player.character_id;
        let entry = regs
            .entry(id)
            .or_insert_with(|| TelescopeRegistrationEntry::new(player.clone()));
        set(entry);
        if !entry.is_active() {
            regs.remove(&id);
        }
    }

    /// `RegisterForPublicTransport(player, checkRange)`.
    pub fn register_for_public_transport(&self, player: &PlayerRef, range: f32) {
        self.register(player, |e| e.set_public_transport_range(range));
    }

    /// `RegisterForFishSchool(player, checkRange)`.
    pub fn register_for_fish_school(&self, player: &PlayerRef, range: f32) {
        self.register(player, |e| e.set_fish_school_range(range));
    }

    /// `RegisterForShips(player, checkRange)`.
    pub fn register_for_ships(&self, player: &PlayerRef, range: f32) {
        self.register(player, |e| e.set_ship_telescope_range(range));
    }

    /// `UnRegister(player)`.
    pub fn unregister(&self, character_id: u32) {
        self.registrations
            .lock()
            .expect("radar poisoned")
            .remove(&character_id);
    }

    /// The current registration of a character.
    pub fn registration(&self, character_id: u32) -> Option<TelescopeRegistrationEntry> {
        self.registrations
            .lock()
            .expect("radar poisoned")
            .get(&character_id)
            .cloned()
    }

    /// `RadarTick(delta)`.
    pub async fn radar_tick(&self, server: &GameServer) {
        let entries: Vec<TelescopeRegistrationEntry> = {
            let regs = self.registrations.lock().expect("radar poisoned");
            if regs.is_empty() {
                return;
            }
            regs.values().cloned().collect()
        };

        let all_fish = server.systems.npcs.fish_schools.all_fish_schools();
        // TODO (C#): Add Shipyards
        let mut all_transfers = Vec::new();
        let mut all_ships: BTreeMap<u32, Vec<SlaveRef>> = BTreeMap::new();
        let ship_hooks = self.ships.get();
        for world in server.world.worlds() {
            all_transfers.extend(world.all_of_kind(ObjectKind::Transfer));
            let ships = ship_hooks
                .as_ref()
                .map(|h| h.active_ships(server, &world))
                .unwrap_or_default();
            all_ships.entry(world.id).or_insert(ships);
        }

        for entry in entries {
            // TODO (C#): Make a proper GM flag
            let gm_range =
                if super::character::effective_access_level(server, &entry.player).await >= 100 {
                    100_000.0f32
                } else {
                    0.0
                };
            let player_obj = WorldObject::from(&entry.player);
            let Some(world) = server.world.world_of(&player_obj) else {
                continue;
            };
            let me = world.world_pos_rot(&player_obj);
            let (my_world, my_instance) = {
                let c = entry.player.data();
                (c.unit.transform.world_id, c.unit.transform.instance_id)
            };
            let distance = |o: &WorldObject, w: &WorldInstance| {
                let p = w.world_pos_rot(o);
                crate::utils::math::calculate_distance([me.x, me.y, me.z], [p.x, p.y, p.z], true)
            };
            let same_place = |o: &WorldObject| {
                let t = o.transform();
                t.world_id == my_world && t.instance_id == my_instance
            };

            // Public transportation
            if entry.public_transport_range() > 0.0 {
                let range = entry.public_transport_range().max(gm_range);
                let mut in_range = Vec::new();
                for t in &all_transfers {
                    let Some(tr) = t.as_transfer() else { continue };
                    let (template_id, name) = {
                        let d = tr.data();
                        (d.unit.template_id, d.unit.name.clone())
                    };
                    if template_id == CARRIAGE_BOARDING_TEMPLATE || !same_place(t) {
                        continue;
                    }
                    let Some(w) = server.world.world_of(t) else {
                        continue;
                    };
                    if distance(t, &w) <= range {
                        let p = w.world_pos_rot(t);
                        in_range.push(TransferTelescopeUnit {
                            obj_id: t.obj_id(),
                            template_id,
                            pos: (p.x, p.y, p.z),
                            name,
                        });
                    }
                }
                for (last, chunk) in chunks(in_range) {
                    entry.player.send(SCTransferTelescopeUnitsPacket {
                        last,
                        transfers: chunk,
                    });
                }
            }

            // Fish schools
            if entry.fish_school_range() > 0.0 {
                let range = entry.fish_school_range().max(gm_range);
                let mut in_range = Vec::new();
                for d in &all_fish {
                    let o = WorldObject::from(d);
                    if !same_place(&o) {
                        continue;
                    }
                    let Some(w) = server.world.world_of(&o) else {
                        continue;
                    };
                    if distance(&o, &w) <= range {
                        in_range.push(FishFinderUnit::of(&w, d));
                    }
                }
                for (last, chunk) in chunks(in_range) {
                    entry.player.send(SCSchoolOfFishDoodadsPacket {
                        last,
                        transfers: chunk,
                    });
                }
            }

            // All ships
            if entry.ship_telescope_range() > 0.0 {
                let range = entry.ship_telescope_range().max(gm_range);
                let mut in_range = Vec::new();
                for ship in all_ships
                    .get(&my_instance)
                    .map(Vec::as_slice)
                    .unwrap_or(&[])
                {
                    let o = WorldObject::from(ship);
                    if !same_place(&o) {
                        continue;
                    }
                    let Some(w) = server.world.world_of(&o) else {
                        continue;
                    };
                    if distance(&o, &w) <= range {
                        let p = w.world_pos_rot(&o);
                        let d = ship.data();
                        in_range.push(ShipTelescopeUnit {
                            obj_id: ship.obj_id,
                            summoner_id: d.summoner_id,
                            template_id: d.unit.template_id,
                            pos: (p.x, p.y, p.z),
                            faction_id: d.unit.faction_id,
                            expedition_id: d.unit.expedition_id,
                            name: d.unit.name.clone(),
                            summoner_name: d.summoner_name.clone(),
                            hp: d.unit.hp,
                            max_hp: d.unit.max_hp,
                        });
                    }
                }
                for (last, chunk) in chunks(in_range) {
                    entry.player.send(SCTelescopeUnitsPacket {
                        last,
                        slaves: chunk,
                    });
                }
            }
        }
    }
}

/// Splits into packets of [`UNITS_PER_PACKET`] with the `last` flag.
fn chunks<T: Clone>(items: Vec<T>) -> Vec<(bool, Vec<T>)> {
    let n = items.len();
    let mut out = Vec::new();
    let mut i = 0;
    while i < n {
        let last = n - i <= UNITS_PER_PACKET;
        let end = if last { n } else { i + UNITS_PER_PACKET };
        out.push((last, items[i..end].to_vec()));
        i += UNITS_PER_PACKET;
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;
    use crate::test_support::{spawn_player, test_character, test_server, TestClient};

    fn body(p: &dyn crate::net::packet::ServerPacket) -> Vec<u8> {
        encode(p)[6..].to_vec()
    }

    #[test]
    fn packets_are_byte_exact() {
        let mut e = PacketStream::new();
        e.write_bool(true).write_u8(1);
        e.write_bc(0x0a0b0c)
            .write_u32(77)
            .write_position(1.0, 2.0, 3.0)
            .write_str("Carriage");
        assert_eq!(
            body(&SCTransferTelescopeUnitsPacket {
                last: true,
                transfers: vec![TransferTelescopeUnit {
                    obj_id: 0x0a0b0c,
                    template_id: 77,
                    pos: (1.0, 2.0, 3.0),
                    name: "Carriage".into()
                }]
            }),
            e.into_bytes()
        );

        let mut e = PacketStream::new();
        e.write_bool(false).write_u8(1);
        e.write_bc(5)
            .write_u32(9)
            .write_i32(2)
            .write_u32(100)
            .write_position(4.0, 5.0, 6.0)
            .write_u32(101)
            .write_u32(102)
            .write_str("Boat")
            .write_str("Bob")
            .write_i32(10)
            .write_i32(20)
            .write_u32(0);
        assert_eq!(
            body(&SCTelescopeUnitsPacket {
                last: false,
                slaves: vec![ShipTelescopeUnit {
                    obj_id: 5,
                    summoner_id: 9,
                    template_id: 100,
                    pos: (4.0, 5.0, 6.0),
                    faction_id: 101,
                    expedition_id: 102,
                    name: "Boat".into(),
                    summoner_name: "Bob".into(),
                    hp: 10,
                    max_hp: 20
                }]
            }),
            e.into_bytes()
        );

        let mut e = PacketStream::new();
        e.write_bool(true).write_f32(1000.0);
        assert_eq!(
            body(&SCTelescopeToggledPacket {
                on: true,
                range: 1000.0
            }),
            e.clone().into_bytes()
        );
        assert_eq!(
            body(&SCTransferTelescopeToggledPacket {
                on: true,
                range: 1000.0
            }),
            e.into_bytes()
        );
    }

    #[test]
    fn chunking() {
        let c = chunks((0..25).collect::<Vec<_>>());
        assert_eq!(c.len(), 3);
        assert_eq!((c[0].0, c[0].1.len()), (false, 10));
        assert_eq!((c[2].0, c[2].1.len()), (true, 5));
        let c = chunks((0..10).collect::<Vec<_>>());
        assert_eq!(c.len(), 1);
        assert!(c[0].0);
        assert!(chunks(Vec::<u8>::new()).is_empty());
    }

    #[tokio::test]
    async fn registrations_toggle_and_expire() {
        let server = test_server();
        let mut client = TestClient::new(&server, 1, 1);
        let p = spawn_player(
            &server,
            &client,
            test_character(1, "Bob", 10.0, 10.0),
            0x100,
        );
        client.drain();
        let radar = &server.systems.services.radar;
        radar.register_for_fish_school(&p, 1000.0);
        assert!(radar.registration(1).is_some());
        assert_eq!(
            client.drain_ops(),
            vec![sc::SCSchoolOfFishFinderToggledPacket]
        );
        // Changes below 1 m are ignored.
        radar.register_for_fish_school(&p, 1000.5);
        assert!(client.drain_ops().is_empty());
        radar.register_for_public_transport(&p, 500.0);
        radar.register_for_fish_school(&p, 0.0);
        assert!(radar.registration(1).is_some());
        radar.register_for_public_transport(&p, 0.0);
        assert!(radar.registration(1).is_none());
        assert_eq!(
            client.drain_ops(),
            vec![
                sc::SCTransferTelescopeToggledPacket,
                sc::SCSchoolOfFishFinderToggledPacket,
                sc::SCTransferTelescopeToggledPacket
            ]
        );
    }
}
