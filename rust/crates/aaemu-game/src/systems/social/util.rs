//! Small helpers shared by the social managers: `ErrorMessageType` values, `SendErrorMessage`,
//! `SendMessage`, positions and the time base.

use crate::packets::world::{SCChatMessagePacket, SCErrorMsgPacket};
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

/// `ErrorMessageType` values used by the social subsystem.
pub mod error {
    pub const TEAM_FULL: i16 = 73;
    pub const TEAM_INVITE_REFUSED: i16 = 80;
    pub const EXPEDITION_CREATE_LEVEL: i16 = 293;
    pub const EXPEDITION_CREATE_MEMBER: i16 = 294;
    pub const EXPEDITION_CREATE_MONEY: i16 = 295;
    pub const EXPEDITION_NAME_CHARACTER: i16 = 296;
    pub const EXPEDITION_NAME_LENGTH: i16 = 297;
    pub const EXPEDITION_NAME_EXIST: i16 = 299;
    pub const CHAT_NOT_IN_EXPEDITION: i16 = 311;
    pub const EXPEDITION_CREATE_FACTION: i16 = 321;
    pub const EXPEDITION_CREATE_MEMBER_EXPEDITION: i16 = 330;
    pub const CHAT_NOT_IN_FAMILY: i16 = 412;
    pub const TRIALS_ALREADY_CLOSED: i16 = 478;
    pub const TRIALS_JURY_FULL: i16 = 479;
    pub const CHAT_NOT_RAID_OWNER: i16 = 496;
    pub const TARGET_REJECTED_DUEL: i16 = 507;
    pub const CANNOT_JOIN_TRIAL_FROM_INSTANT_ZONE: i16 = 544;
    pub const TRIALS_CANNOT_JOIN_AFTER_START: i16 = 549;
    pub const EXPEDITION_ALREADY_MEMBER: i16 = 761;
    pub const ONLY_EXPEDITION_OWNER: i16 = 807;
    pub const ONLY_EXPEDITION_MEMBER: i16 = 808;
    pub const CHAT_NOT_IN_PARTY: i16 = 267;
    pub const CHAT_NOT_IN_RAID: i16 = 268;
}

/// `Character.SendErrorMessage(type)` (`SCErrorMsgPacket(type, 0, true)`).
pub fn send_error(p: &PlayerRef, error: i16) {
    p.send(SCErrorMsgPacket::new(error, 0, true));
}

/// `Character.SendMessage(type, message)` without a sender.
pub fn send_message(p: &PlayerRef, chat_type: i16, message: &str) {
    p.send(SCChatMessagePacket {
        chat_type,
        sender: None,
        message: message.to_string(),
        ability: 0,
        language_type: 0,
    });
}

/// `Transform.World.Position` of a player (local position unless it is attached to a parent).
pub fn world_position(server: &GameServer, p: &PlayerRef) -> (f32, f32, f32) {
    let (t, inst) = {
        let c = p.data();
        (c.unit.transform, c.unit.transform.instance_id)
    };
    if t.parent_obj_id == 0 {
        return (t.x, t.y, t.z);
    }
    match server.world.world(inst) {
        Some(w) => {
            let pr = w.world_pos_rot(&WorldObject::from(p));
            (pr.x, pr.y, pr.z)
        }
        None => (t.x, t.y, t.z),
    }
}

/// `MathUtil.CalculateDistance(a, b, includeZ)`.
pub fn distance(a: (f32, f32, f32), b: (f32, f32, f32), include_z: bool) -> f32 {
    let dx = a.0 - b.0;
    let dy = a.1 - b.1;
    if include_z {
        let dz = a.2 - b.2;
        (dx * dx + dy * dy + dz * dz).sqrt()
    } else {
        (dx * dx + dy * dy).sqrt()
    }
}

/// `DateTime.UtcNow` in unix seconds.
pub fn now_secs() -> i64 {
    chrono::Utc::now().timestamp()
}

/// `DateTime.UtcNow` in unix milliseconds.
pub fn now_ms() -> i64 {
    chrono::Utc::now().timestamp_millis()
}

/// `Faction.MotherId` of a faction id (0 when the faction is unknown).
pub fn mother_id(server: &GameServer, faction_id: u32) -> u32 {
    server
        .game_data
        .factions
        .get(faction_id)
        .map_or(0, |f| f.mother_id)
}

/// Runs `f` with the player's character data locked (one object lock, released afterwards).
pub fn with_char<R>(
    p: &PlayerRef,
    f: impl FnOnce(&mut crate::models::character::Character) -> R,
) -> R {
    let mut c = p.data();
    f(&mut c)
}
