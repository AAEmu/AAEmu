//! `CSResurrectCharacterPacket`: the temple / in-place resurrection of a dead character
//! (respawn point, HP / MP, `SCCharacterResurrectedPacket`, the revival debuffs of
//! `ApplyRevivalDebuffs`, underwater reset and breath refill).

use aaemu_commons::PacketStream;
use futures::future::BoxFuture;
use rand::Rng;

use crate::data::world_data::{conflict_type, Portal};
use crate::error::Result;
use crate::models::character::DEFAULT_INSTANCE_ID;
use crate::net::connection::GameConnection;
use crate::net::registry::ClientPacket;
use crate::packets::world::SCUnitPointsPacket;
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

use super::packets_special::SCCharacterResurrectedPacket;
use super::stats::{stat, StatKind};
use super::types::{KillReason, SkillCaster};

/// `RespawnCooldownDurationMs` (5 min).
pub const RESPAWN_COOLDOWN_DURATION_MS: i32 = 300_000;
/// `BuffConstants.WeakenedBody` (PvE death penalty).
pub const WEAKENED_BODY: u32 = 1128;
/// `BuffConstants.RespawnCooldown` (5 min cooldown after temple-revive).
pub const RESPAWN_COOLDOWN: u32 = 2385;
/// `BuffConstants.WarZoneLeech` (PvP death penalty in War zones).
pub const WAR_ZONE_LEECH: u32 = 4424;
/// The "Temple Priestess" NPC template used as respawn point inside instances.
const TEMPLE_PRIESTESS_TEMPLATE_ID: u32 = 502;
/// `FactionsEnum.NuiaAlliance` / `HaranyaAlliance`.
const NUIA_ALLIANCE: u32 = 148;
const HARANYA_ALLIANCE: u32 = 149;

/// `CSResurrectCharacterPacket` (level 1).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSResurrectCharacterPacket {
    pub in_place: bool,
}

impl ClientPacket for CSResurrectCharacterPacket {
    const NAME: &'static str = "CSResurrectCharacterPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            in_place: s.read_bool()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!("ResurrectCharacter, InPlace: {}", self.in_place);
            if let Some(p) = conn.active_char.clone() {
                resurrect_character(server, &p, self.in_place);
            }
            Ok(())
        })
    }
}

/// The respawn point of `CSResurrectCharacterPacket` (`new Portal()` = X 0 when none).
fn respawn_portal(server: &GameServer, p: &PlayerRef) -> Portal {
    if let Some(portal) = crate::systems::housing::dominion::headquarters::respawn_portal(server, p)
    {
        return portal;
    }
    let mut portal = Portal::default();
    let (instance_id, world_id, zone_key, faction_id) = {
        let c = p.data();
        let t = c.unit.transform;
        (t.instance_id, t.world_id, t.zone_id, c.unit.faction_id)
    };
    if instance_id != DEFAULT_INSTANCE_ID {
        // Inside dungeons or other instances, just respawn at the nearest Priestess
        let world = server.world.world(instance_id);
        let priestess = world.as_ref().and_then(|w| {
            w.all_npcs()
                .into_iter()
                .find(|n| n.data().unit.template_id == TEMPLE_PRIESTESS_TEMPLATE_ID)
                .map(|n| (WorldObject::from(&n), w.clone()))
        });
        if let Some((npc, w)) = priestess {
            let pos = w.world_pos_rot(&npc);
            let zone_id = npc.transform().zone_id;
            let mut rng = rand::thread_rng();
            portal.world_id = world_id;
            portal.zone_id = zone_id;
            portal.x = pos.x + rng.gen_range(1..3) as f32;
            portal.y = pos.y + rng.gen_range(1..3) as f32;
            portal.z = pos.z;
            portal.z_rot = pos.rot_z;
            portal.yaw = pos.rot_z;
        }
        return portal;
    }
    // Check if the current zone is at War and if it has special respawn areas for factions
    let mut use_portal_id = 0;
    if let Some(zone) = server.game_data.zones.zone_by_key(zone_key) {
        let conflict = server.systems.social.conflicts.get(zone.group_id);
        if let Some(c) = conflict.filter(|c| c.current_state == conflict_type::WAR) {
            let mother_id = server
                .game_data
                .factions
                .get(faction_id)
                .map_or(0, |f| f.mother_id);
            match mother_id {
                NUIA_ALLIANCE => use_portal_id = c.nuia_return_point_id,
                HARANYA_ALLIANCE => use_portal_id = c.harihara_return_point_id,
                _ => {}
            }
        }
    }
    // Try to get a faction specific respawn
    let faction_portal = if use_portal_id > 0 {
        crate::systems::npcs::portal::respawn_by_id(server, use_portal_id)
    } else {
        None
    };
    // Find the closest return portal (in the world) for the player if none has been found yet
    faction_portal.unwrap_or_else(|| crate::systems::npcs::portal::closest_return_portal(server, p))
}

/// `CSResurrectCharacterPacket.Read` for the active character.
pub fn resurrect_character(server: &GameServer, p: &PlayerRef, in_place: bool) {
    if p.data().unit.hp > 0 {
        return;
    }
    let portal = if in_place {
        Portal::default()
    } else {
        respawn_portal(server, p)
    };
    let me = WorldObject::from(p);
    let hp = {
        let mut c = p.data();
        let (max_hp, max_mp) = (c.unit.max_hp, c.unit.max_mp);
        if in_place {
            let hp_pct = c.skill_char.resurrect_hp_percent as f32 / 100.0;
            let mp_pct = c.skill_char.resurrect_mp_percent as f32 / 100.0;
            c.unit.hp = (max_hp as f32 * hp_pct) as i32;
            c.unit.mp = (max_mp as f32 * mp_pct) as i32;
            c.skill_char.resurrect_hp_percent = 1;
            c.skill_char.resurrect_mp_percent = 1;
        } else {
            c.unit.hp = (f64::from(max_hp) * 0.1) as i32;
            c.unit.mp = (f64::from(max_mp) * 0.1) as i32;
        }
        c.unit.hp
    };
    super::combat::post_update_current_hp(server, &me, Some(&me), 0, hp, KillReason::Unknown);

    let packet = if portal.x != 0.0 {
        SCCharacterResurrectedPacket {
            unit_id: p.obj_id,
            x: portal.x,
            y: portal.y,
            z: portal.z,
            z_rot: portal.z_rot,
        }
    } else {
        let pos = match super::geometry::world_of(server, &me) {
            Some(w) => w.world_pos_rot(&me),
            None => me.transform().local(),
        };
        // GetResurrectablePosition (x2game sub_3925D3B0, rays 0x107 | 0x8F): the ground under
        // the body (collision service; the body's z otherwise)
        let z = crate::systems::ai::collision::ground_height_or(
            server,
            me.transform().instance_id,
            pos.x,
            pos.y,
            pos.z,
            || pos.z,
        );
        SCCharacterResurrectedPacket {
            unit_id: p.obj_id,
            x: pos.x,
            y: pos.y,
            z,
            z_rot: 0.0,
        }
    };
    super::broadcast(server, &me, &packet, true);
    let (hp, mp) = {
        let c = p.data();
        (c.unit.hp, c.unit.mp)
    };
    super::broadcast(
        server,
        &me,
        &SCUnitPointsPacket {
            obj_id: p.obj_id,
            hp,
            mp,
        },
        true,
    );

    // Route death-debuffs based on death context (set by Character.DoDie).
    apply_revival_debuffs(server, p, in_place);

    super::water::set_player_under_water(server, p, false);
    let lung = stat(server, &me, StatKind::LungCapacity) as u32;
    p.data().breath = lung;
}

/// `ApplyRevivalDebuffs(character, inPlace)`: in place (player-res) → no debuffs; PvP death in
/// a War zone → Leech + Respawn-CD; other PvP death → Respawn-CD only; PvE death → Weakened
/// Body + Respawn-CD.
fn apply_revival_debuffs(server: &GameServer, p: &PlayerRef, in_place: bool) {
    let (war_zone, pvp) = {
        let mut c = p.data();
        let flags = (c.died_in_pvp_war_zone, c.died_in_pvp);
        if in_place || flags.0 {
            c.died_in_pvp_war_zone = false;
            c.died_in_pvp = false;
        } else if flags.1 {
            c.died_in_pvp = false;
        }
        flags
    };
    if in_place {
        return;
    }
    let buffs: &[(u32, i32)] = if war_zone {
        &[
            (WAR_ZONE_LEECH, 0),
            (RESPAWN_COOLDOWN, RESPAWN_COOLDOWN_DURATION_MS),
        ]
    } else if pvp {
        &[(RESPAWN_COOLDOWN, RESPAWN_COOLDOWN_DURATION_MS)]
    } else {
        &[
            (WEAKENED_BODY, 0),
            (RESPAWN_COOLDOWN, RESPAWN_COOLDOWN_DURATION_MS),
        ]
    };
    let me = WorldObject::from(p);
    for &(buff_id, forced) in buffs {
        // ApplyBuff: an unknown template is skipped silently.
        let Some(t) = server.game_data.systems.skills.buff(buff_id).cloned() else {
            continue;
        };
        let b = super::buffs::new_buff(
            server,
            &t,
            Some(&me),
            &SkillCaster::Unit(p.obj_id),
            None,
            crate::world::now_ms(),
        );
        super::buffs::add_buff(server, &me, b, 0, forced.max(0));
    }
}
