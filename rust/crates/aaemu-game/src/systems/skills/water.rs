//! The underwater state of units: the `IsUnderWater` setters (`Character`: breath refill +
//! `SCUnderWaterPacket`; `Unit`: a plain field) and the parts of `Character.SetPosition` /
//! `Unit.SetPosition` that update it.

use crate::packets::g2c_misc::SCUnderWaterPacket;
use crate::server::GameServer;
use crate::systems::skills::controllers::Vec3;
use crate::systems::skills::stats::{stat, StatKind};
use crate::world::{PlayerRef, WorldInstance, WorldObject};

/// `Character.SetPosition`: the probe is raised to a ship's deck level for this fraction of its
/// mass box height.
const DECK_PROBE_FRACTION: f32 = 0.35;
/// `surfaceBand` / `hysteresis` of `Character.SetPosition`.
const SURFACE_BAND: f32 = 2.0;
const HYSTERESIS: f32 = 0.35;

/// `Character.IsUnderWater = value`: on a change, leaving the water refills the breath
/// (`Breath = LungCapacity`) and the client gets `SCUnderWaterPacket(value)`.
pub fn set_player_under_water(server: &GameServer, p: &PlayerRef, value: bool) {
    {
        let mut c = p.data();
        if c.unit.is_under_water == value {
            return;
        }
        c.unit.is_under_water = value;
    }
    if !value {
        let lung = stat(server, &WorldObject::from(p), StatKind::LungCapacity) as u32;
        p.data().breath = lung;
    }
    p.send(SCUnderWaterPacket { start: value });
}

/// `IsUnderWater = value` of any unit (the character setter for players).
pub fn set_under_water(server: &GameServer, obj: &WorldObject, value: bool) {
    match obj.as_player() {
        Some(p) => set_player_under_water(server, p, value),
        None => {
            obj.with_unit(|u| u.unit_mut().is_under_water = value);
        }
    }
}

/// The closest slave in the parent chain of `obj` (direct parent first, then the sticky
/// parent's ancestry), as `Character.SetPosition` looks it up.
fn attached_slave(world: &WorldInstance, obj: &WorldObject) -> Option<WorldObject> {
    let walk = |start: Option<WorldObject>| {
        let mut cur = start;
        for _ in 0..16 {
            let o = cur?;
            if o.as_slave().is_some() {
                return Some(o);
            }
            cur = match o.transform().parent_obj_id {
                0 => None,
                id => world.object(id),
            };
        }
        None
    };
    let parent = match obj.transform().parent_obj_id {
        0 => None,
        id => world.object(id),
    };
    walk(parent).or_else(|| walk(world.sticky_parent(obj)))
}

/// The underwater part of `Character.SetPosition` (after the position is applied).
pub fn update_character_under_water(server: &GameServer, p: &PlayerRef) {
    let obj = WorldObject::from(p);
    let world = crate::systems::skills::geometry::world_of(server, &obj);
    let Some(world) = world else {
        set_player_under_water(server, p, false);
        return;
    };
    let w = world.world_pos_rot(&obj);
    let mut probe = Vec3::new(w.x, w.y, w.z);
    // Probe slightly above the character "feet" position to avoid false drowning when standing
    // on a ship deck (server-side Z for attached characters can be lower).
    if let Some(slave) = attached_slave(&world, &obj) {
        let model_id = slave.as_slave().map_or(0, |s| s.data().unit.model_id);
        let ship = server
            .game_data
            .systems
            .ai
            .ai
            .ship_model(model_id)
            .map(|m| m.mass_box_size_z);
        if let Some(size_z) = ship {
            let deck_probe_z =
                world.world_pos_rot(&slave).z + size_z * slave.scale() * DECK_PROBE_FRACTION;
            if deck_probe_z > probe.z {
                probe.z = deck_probe_z;
            }
        }
    }
    let instance = world.id;
    let under = p.data().unit.is_under_water;
    if crate::systems::ai::physics::is_water(server, instance, probe).is_none() {
        if under {
            set_player_under_water(server, p, false);
        }
        return;
    }
    let surface = crate::systems::ai::physics::water_surface(server, instance, probe)
        .map_or(world.template.ocean_level, |(s, _)| s);
    let enter = surface - SURFACE_BAND;
    let exit = surface - SURFACE_BAND + HYSTERESIS;
    if !under && probe.z < enter {
        set_player_under_water(server, p, true);
    } else if under && probe.z > exit {
        set_player_under_water(server, p, false);
    }
}

/// The underwater part of `Unit.SetPosition` for units that are not characters: below
/// `OceanLevel - 2` (98 without a world) the unit is under water.
pub fn update_unit_under_water(server: &GameServer, obj: &WorldObject) {
    if obj.as_player().is_some() || !obj.is_unit() {
        return;
    }
    let world = crate::systems::skills::geometry::world_of(server, obj);
    let threshold = world
        .as_ref()
        .map_or(98.0, |w| w.template.ocean_level - 2.0);
    let z = match &world {
        Some(w) => w.world_pos_rot(obj).z,
        None => obj.transform().z,
    };
    let under = obj.with_unit(|u| u.unit().is_under_water).unwrap_or(false);
    if !under && z < threshold {
        set_under_water(server, obj, true);
    } else if under && z > threshold {
        set_under_water(server, obj, false);
    }
}
