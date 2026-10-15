//! Instance / instant game / tower defense packets (`Core/Packets/C2G`, `Core/Packets/G2C`, 1.2
//! wire formats). `SCLoadInstancePacket` lives in `systems::npcs::portal::packets` (shared with
//! the portals).

use aaemu_commons::{Marshal, PacketStream};
use futures::future::BoxFuture;

use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::server::GameServer;

use super::data::GameRuleSet;

// ------------------------------------------------------------------------------------------
// Shared wire types
// ------------------------------------------------------------------------------------------

/// `ZoneInstanceId(zoneId, instanceId)`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ZoneInstanceId {
    pub zone_id: u32,
    pub instance_id: u32,
}

impl Marshal for ZoneInstanceId {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.zone_id).write_u32(self.instance_id);
    }
}

/// `InstantGameTeamMember` (wire part).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TeamMemberWire {
    pub character_id: u32,
    pub name: String,
    pub score: i32,
    pub bonus: i32,
    pub bonus_set: u32,
    pub killstreak: u16,
    pub kills: u16,
    pub assists: u16,
    pub deaths: u16,
}

impl Marshal for TeamMemberWire {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id)
            .write_str(&self.name)
            .write_i32(self.score)
            .write_i32(self.bonus)
            .write_u32(self.bonus_set)
            .write_u16(self.killstreak)
            .write_u16(self.kills)
            .write_u16(self.assists)
            .write_u16(self.deaths);
    }
}

/// `InstantGameTeamResult` (wire part; `Score` / `TotalKills` / `TotalDeaths` are sums over the
/// members).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct TeamResultWire {
    /// `VictoryState`.
    pub state: u8,
    pub faction_id: u32,
    pub members: Vec<TeamMemberWire>,
}

impl TeamResultWire {
    /// `Score` (`Members.Sum(m => m.Score)`).
    pub fn score(&self) -> i32 {
        self.members
            .iter()
            .fold(0i32, |a, m| a.wrapping_add(m.score))
    }

    /// `TotalKills` (`(short)Members.Sum(m => m.Kills)`).
    pub fn total_kills(&self) -> i16 {
        self.members.iter().map(|m| i32::from(m.kills)).sum::<i32>() as i16
    }

    /// `TotalDeaths`.
    pub fn total_deaths(&self) -> i16 {
        self.members
            .iter()
            .map(|m| i32::from(m.deaths))
            .sum::<i32>() as i16
    }
}

impl Marshal for TeamResultWire {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.state)
            .write_u32(self.faction_id)
            .write_i32(self.score())
            .write_i16(self.total_kills())
            .write_i16(self.total_deaths())
            .write_u8(self.members.len() as u8);
        for m in &self.members {
            m.write(s);
        }
    }
}

/// `TowerDefKey`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct TowerDefKey {
    pub tower_def_id: u32,
    pub zone_group_id: u16,
}

impl Marshal for TowerDefKey {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.tower_def_id).write_u16(self.zone_group_id);
    }
}

/// `TowerDefInfo`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct TowerDefInfo {
    pub key: TowerDefKey,
    pub zone_id: u32,
    pub spot_id: u32,
    pub target_obj_id: u32,
    /// `Position` (x, y, z).
    pub position: (f32, f32, f32),
    pub current_step: u32,
}

impl Marshal for TowerDefInfo {
    fn write(&self, s: &mut PacketStream) {
        self.key.write(s);
        s.write_u32(self.zone_id)
            .write_u32(self.spot_id)
            .write_bc(self.target_obj_id)
            .write_i64(crate::models::transform::convert_long(self.position.0))
            .write_i64(crate::models::transform::convert_long(self.position.1))
            .write_f32(self.position.2)
            .write_u32(self.current_step);
    }
}

// ------------------------------------------------------------------------------------------
// C -> S
// ------------------------------------------------------------------------------------------

/// `CSInstanceLoadedPacket` (empty).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CSInstanceLoadedPacket;

impl ClientPacket for CSInstanceLoadedPacket {
    const NAME: &'static str = "CSInstanceLoadedPacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            // `Connection.ActiveChar` is null outside the world: the C# throws (NRE).
            if let Some(me) = conn.active_char.clone() {
                super::worlds::instance_loaded(server, &me);
            }
            Ok(())
        })
    }
}

/// `CSUnknownInstancePacket` (read and ignored).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CSUnknownInstancePacket {
    pub kind: u16,
    pub zone_id: i32,
    pub inst_id: u32,
}

impl ClientPacket for CSUnknownInstancePacket {
    const NAME: &'static str = "CSUnknownInstancePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            kind: s.read_u16()?,
            zone_id: s.read_i32()?,
            inst_id: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move { Ok(()) })
    }
}

/// `CSApplyToInstantGamePacket`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CSApplyToInstantGamePacket {
    /// The battlefield id (`_instanceId` in C#).
    pub instance_id: u32,
    /// `(InstantCorps)ReadByte()`.
    pub corps: u8,
}

impl ClientPacket for CSApplyToInstantGamePacket {
    const NAME: &'static str = "CSApplyToInstantGamePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            instance_id: s.read_u32()?,
            corps: s.read_u8()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            if let Some(me) = conn.active_char.clone() {
                super::instant_game::apply_to_battlefield(
                    server,
                    self.instance_id,
                    self.corps,
                    &me,
                );
            }
            Ok(())
        })
    }
}

/// `CSCancelInstantGamePacket` (empty).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CSCancelInstantGamePacket;

impl ClientPacket for CSCancelInstantGamePacket {
    const NAME: &'static str = "CSCancelInstantGamePacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("CancelInstantGame");
            if let Some(me) = conn.active_char.clone() {
                super::instant_game::withdraw_from_battlefield(server, &me);
            }
            Ok(())
        })
    }
}

/// `CSJoinInstantGamePacket`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CSJoinInstantGamePacket {
    pub join: bool,
    pub qualified_id: u64,
}

impl ClientPacket for CSJoinInstantGamePacket {
    const NAME: &'static str = "CSJoinInstantGamePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            join: s.read_bool()?,
            qualified_id: s.read_u64()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "JoinInstantGame, Join: {}, QualifiedId: {}",
                if self.join { "True" } else { "False" },
                self.qualified_id
            );
            if let Some(me) = conn.active_char.clone() {
                super::instant_game::player_invite_response(
                    server,
                    &me,
                    self.join,
                    self.qualified_id,
                );
            }
            Ok(())
        })
    }
}

/// `CSEnteredInstantGameWorldPacket`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CSEnteredInstantGameWorldPacket {
    pub qualified_id: u64,
}

impl ClientPacket for CSEnteredInstantGameWorldPacket {
    const NAME: &'static str = "CSEnteredInstantGameWorldPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            qualified_id: s.read_u64()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "EnteredInstantGameWorld, QualifiedId: {}",
                self.qualified_id
            );
            if let Some(me) = conn.active_char.clone() {
                super::instant_game::on_enter_world(server, &me, self.qualified_id);
            }
            Ok(())
        })
    }
}

/// `CSLeaveInstantGamePacket` (empty).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct CSLeaveInstantGamePacket;

impl ClientPacket for CSLeaveInstantGamePacket {
    const NAME: &'static str = "CSLeaveInstantGamePacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("LeaveInstantGame");
            if let Some(me) = conn.active_char.clone() {
                super::instant_game::leave_current_game(server, &me);
            }
            Ok(())
        })
    }
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSInstanceLoadedPacket>(LEVEL_PLAIN, cs::CSInstanceLoadedPacket);
    reg.register::<CSApplyToInstantGamePacket>(LEVEL_PLAIN, cs::CSApplyToInstantGamePacket);
    reg.register::<CSCancelInstantGamePacket>(LEVEL_PLAIN, cs::CSCancelInstantGamePacket);
    reg.register::<CSJoinInstantGamePacket>(LEVEL_PLAIN, cs::CSJoinInstantGamePacket);
    reg.register::<CSEnteredInstantGameWorldPacket>(
        LEVEL_PLAIN,
        cs::CSEnteredInstantGameWorldPacket,
    );
    reg.register::<CSLeaveInstantGamePacket>(LEVEL_PLAIN, cs::CSLeaveInstantGamePacket);
    reg.register::<CSUnknownInstancePacket>(LEVEL_PLAIN, cs::CSUnknownInstancePacket);
}

// ------------------------------------------------------------------------------------------
// S -> C
// ------------------------------------------------------------------------------------------

/// `SCProcessingInstancePacket(zoneId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCProcessingInstancePacket {
    pub zone_id: i32,
}
packet_def!(
    SCProcessingInstancePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCProcessingInstancePacket
);
impl Marshal for SCProcessingInstancePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.zone_id);
    }
}

/// `SCAppliedToInstantGamePacket(battlefieldId, corps, errorMessageId = 0)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCAppliedToInstantGamePacket {
    pub battlefield_id: u32,
    pub corps: u8,
    pub error_message_id: u16,
}
packet_def!(
    SCAppliedToInstantGamePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAppliedToInstantGamePacket
);
impl Marshal for SCAppliedToInstantGamePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.battlefield_id)
            .write_u8(self.corps)
            .write_u16(self.error_message_id);
    }
}

/// `SCCancelInstantGamePacket(errorMessage, fromHomeland)`: the queue/invitation ended.
/// No 1.2-era type exists in any source; the layout follows the packet family as locked by
/// two later-client routes that agree field for field: the retail 10.x named-field
/// serializer (`x2game-dev_dedicate.c` sub_39C3B5D0 reads `ErrorMessage` as i16, then
/// `fromHomeland`) and the later C# `SCCancelInstantGamePacket` + `InstantGameWireContract`
/// (u16 error, u8 branch; branch 1 with error 0 = full queue clear, branch 0 with an error
/// = error only). Applying that layout to the 1.2 opcode is an inference from family
/// continuity; it has not been observed on a live 1.2 client yet.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCCancelInstantGamePacket {
    pub error_message_id: u16,
    pub from_homeland: u8,
}
packet_def!(
    SCCancelInstantGamePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCancelInstantGamePacket
);
impl SCCancelInstantGamePacket {
    /// Wire branch byte: clear the apply/queue UI (with error 0).
    pub const BRANCH_CLEAR_QUEUE: u8 = 1;
    /// Wire branch byte: show the error only, without clearing.
    pub const BRANCH_ERROR_ONLY: u8 = 0;

    /// The player left the queue or the server withdrew them (full UI clear).
    pub fn clear_queue() -> Self {
        Self {
            error_message_id: 0,
            from_homeland: Self::BRANCH_CLEAR_QUEUE,
        }
    }

    /// The queue ended with a client error message.
    pub fn with_error(error_message_id: u16) -> Self {
        Self {
            error_message_id,
            from_homeland: Self::BRANCH_ERROR_ONLY,
        }
    }
}
impl Marshal for SCCancelInstantGamePacket {
    fn write(&self, s: &mut PacketStream) {
        // The 1.2 native struct holds one dword at +0xc and its handler ignores the value
        // (FUN_3936d260 reads globals only), so the body is padded to the full dword: a
        // u16+u8 reader sees error+branch, a dword reader sees the same low bytes.
        s.write_u16(self.error_message_id)
            .write_u8(self.from_homeland)
            .write_u8(0);
    }
}

/// `SCInstantGameStatePacket`: queue state for one battlefield, consumed by the client's
/// `UPDATE_INSTANT_GAME_STATE` event. The unpacked 1.2 client's handler wrapper passes
/// `u32` at +0xc, `u8` at +0x10 and `u16` at +0x12/+0x14/+0x16 to its consumer, which only
/// applies them when the applied battlefield matches and stores the last four values for
/// `GetJoinedInfos`; that getter caps the three counts (corps 1, corps 2, any) by the stored
/// per-corps size. This writes the battlefield id, per-corps size, then the queued counts
/// in that order.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCInstantGameStatePacket {
    pub battlefield_id: u32,
    pub corps_size: u8,
    pub corps1_count: u16,
    pub corps2_count: u16,
    pub any_count: u16,
}
packet_def!(
    SCInstantGameStatePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameStatePacket
);
impl Marshal for SCInstantGameStatePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.battlefield_id)
            .write_u8(self.corps_size)
            .write_u16(self.corps1_count)
            .write_u16(self.corps2_count)
            .write_u16(self.any_count);
    }
}

/// `SCInstantGameInviteTimeoutPacket(zoneInstanceId, rulesetId, corps)`: an unanswered
/// invitation expired. The unpacked 1.2 client's consumer only accepts the packet when all
/// four identity values match the stored invitation (zone instance, ruleset, assigned
/// corps), then closes the dialog and returns the client to its applied/queued state.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCInstantGameInviteTimeoutPacket {
    pub zone_instance_id: ZoneInstanceId,
    pub ruleset_id: u32,
    pub corps: u8,
}
packet_def!(
    SCInstantGameInviteTimeoutPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameInviteTimeoutPacket
);
impl Marshal for SCInstantGameInviteTimeoutPacket {
    fn write(&self, s: &mut PacketStream) {
        self.zone_instance_id.write(s);
        s.write_u32(self.ruleset_id).write_u8(self.corps);
    }
}

/// `SCInviteToInstantGamePacket(zoneInstanceId, rulesetId, corps, qualifiedId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCInviteToInstantGamePacket {
    pub zone_instance_id: ZoneInstanceId,
    pub ruleset_id: u32,
    pub corps: u8,
    pub qualified_id: u64,
}
packet_def!(
    SCInviteToInstantGamePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInviteToInstantGamePacket
);
impl Marshal for SCInviteToInstantGamePacket {
    fn write(&self, s: &mut PacketStream) {
        self.zone_instance_id.write(s);
        s.write_u32(self.ruleset_id)
            .write_u8(self.corps)
            .write_u64(self.qualified_id);
    }
}

/// `SCInstantGameJoinedPacket(zoneInstanceId, corps, ruleSet)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCInstantGameJoinedPacket {
    pub zone_instance_id: ZoneInstanceId,
    pub corps: u8,
    pub rule_set: GameRuleSet,
}
packet_def!(
    SCInstantGameJoinedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameJoinedPacket
);
impl Marshal for SCInstantGameJoinedPacket {
    fn write(&self, s: &mut PacketStream) {
        let r = &self.rule_set;
        self.zone_instance_id.write(s);
        s.write_u8(self.corps)
            .write_i32(r.victory_score) // victoryScore
            .write_u16(r.victory_kill_count as u16) // victoryKillCount
            .write_u32(r.corps_head_target(self.corps)) // victoryKillCorpsHead (this corps' target)
            .write_i32(r.time_opening.wrapping_mul(60 * 1000)) // opening time
            .write_i32(r.time_playing.wrapping_mul(60 * 1000)) // playing time
            .write_i32(r.time_ending.wrapping_mul(60 * 1000)) // ending time
            .write_u32(r.corps1_faction_id) // corpsId[0]
            .write_u32(r.corps2_faction_id); // corpsId[1]
        for i in 0..24 {
            s.write_u32(r.killstreak_skills[i]); // killstreak skill id
        }
    }
}

/// `SCInstantGameStartPacket(zoneInstanceId, start, now)` (written as now, start).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCInstantGameStartPacket {
    pub zone_instance_id: ZoneInstanceId,
    pub start: u32,
    pub now: u32,
}
packet_def!(
    SCInstantGameStartPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameStartPacket
);
impl Marshal for SCInstantGameStartPacket {
    fn write(&self, s: &mut PacketStream) {
        self.zone_instance_id.write(s);
        s.write_u32(self.now).write_u32(self.start);
    }
}

/// `SCInstantGameEndPacket(zoneInstanceId, reason, corps1Result, corps2Result)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCInstantGameEndPacket {
    pub zone_instance_id: ZoneInstanceId,
    /// `BattlefieldEndingReason`.
    pub reason: u8,
    pub corps1: TeamResultWire,
    pub corps2: TeamResultWire,
}
packet_def!(
    SCInstantGameEndPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameEndPacket
);
impl Marshal for SCInstantGameEndPacket {
    fn write(&self, s: &mut PacketStream) {
        self.zone_instance_id.write(s);
        s.write_u8(self.reason);
        self.corps1.write(s);
        self.corps2.write(s);
    }
}

/// `SCInstantGameAddPointPacket(zoneInstanceId, corps, points, score1, score2, charName)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCInstantGameAddPointPacket {
    pub zone_instance_id: ZoneInstanceId,
    pub corps: u8,
    pub points: i32,
    pub score1: i32,
    pub score2: i32,
    pub char_name: String,
}
packet_def!(
    SCInstantGameAddPointPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameAddPointPacket
);
impl Marshal for SCInstantGameAddPointPacket {
    fn write(&self, s: &mut PacketStream) {
        self.zone_instance_id.write(s);
        s.write_u8(self.corps)
            .write_i32(self.points)
            .write_i32(self.score1)
            .write_i32(self.score2)
            .write_str(&self.char_name);
    }
}

/// `SCInstantGameKillPacket(zoneInstanceId, killer, victim, killerCorps, victimCorps,
/// killerKillstreak, killerCorpsKills, victimCorpsDeaths)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCInstantGameKillPacket {
    pub zone_instance_id: ZoneInstanceId,
    pub killer_id: u32,
    pub victim_id: u32,
    pub killer_corps: u8,
    pub victim_corps: u8,
    pub killer_killstreak: i8,
    pub killer_name: String,
    pub victim_name: String,
    pub killer_corps_kills: i32,
    pub victim_corps_deaths: i32,
}
packet_def!(
    SCInstantGameKillPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameKillPacket
);
impl Marshal for SCInstantGameKillPacket {
    fn write(&self, s: &mut PacketStream) {
        self.zone_instance_id.write(s);
        s.write_u32(self.killer_id)
            .write_u32(self.victim_id)
            .write_u8(self.killer_corps)
            .write_u8(self.victim_corps)
            .write_i8(self.killer_killstreak)
            .write_str(&self.killer_name)
            .write_str(&self.victim_name)
            .write_i32(self.killer_corps_kills)
            .write_i32(self.victim_corps_deaths);
    }
}

/// `SCInstantGameKillstreakPacket(zoneInstanceId, killstreak, skillId, enabled)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCInstantGameKillstreakPacket {
    pub zone_instance_id: ZoneInstanceId,
    pub killstreak: i8,
    pub skill_id: u32,
    pub enabled: bool,
}
packet_def!(
    SCInstantGameKillstreakPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameKillstreakPacket
);
impl Marshal for SCInstantGameKillstreakPacket {
    fn write(&self, s: &mut PacketStream) {
        self.zone_instance_id.write(s);
        s.write_i8(self.killstreak)
            .write_u32(self.skill_id)
            .write_bool(self.enabled);
    }
}

/// `SCInstantGameRemainTimePacket(remainTime)` (sent with the `SCInstantGameUnkPacket` opcode,
/// "TODO: Check offset!!" in C#; never sent by the server code).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCInstantGameRemainTimePacket {
    pub remain_time: u32,
}
packet_def!(
    SCInstantGameRemainTimePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInstantGameUnkPacket
);
impl Marshal for SCInstantGameRemainTimePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.remain_time);
    }
}

/// `SCTowerDefListPacket(towerDefInfos)`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct SCTowerDefListPacket {
    pub infos: Vec<TowerDefInfo>,
}
packet_def!(
    SCTowerDefListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTowerDefListPacket
);
impl Marshal for SCTowerDefListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.infos.len() as i32);
        for i in &self.infos {
            i.write(s);
        }
    }
}

/// `SCTowerDefStartPacket(key, eventZoneId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCTowerDefStartPacket {
    pub key: TowerDefKey,
    pub event_zone_id: u32,
}
packet_def!(
    SCTowerDefStartPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTowerDefStartPacket
);
impl Marshal for SCTowerDefStartPacket {
    fn write(&self, s: &mut PacketStream) {
        self.key.write(s);
        s.write_u32(self.event_zone_id);
    }
}

/// `SCTowerDefEndPacket(key, eventZoneId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCTowerDefEndPacket {
    pub key: TowerDefKey,
    pub event_zone_id: u32,
}
packet_def!(
    SCTowerDefEndPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTowerDefEndPacket
);
impl Marshal for SCTowerDefEndPacket {
    fn write(&self, s: &mut PacketStream) {
        self.key.write(s);
        s.write_u32(self.event_zone_id);
    }
}

/// `SCTowerDefWaveStartPacket(key, eventZoneId, step)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCTowerDefWaveStartPacket {
    pub key: TowerDefKey,
    pub event_zone_id: u32,
    pub step: u32,
}
packet_def!(
    SCTowerDefWaveStartPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTowerDefWaveStartPacket
);
impl Marshal for SCTowerDefWaveStartPacket {
    fn write(&self, s: &mut PacketStream) {
        self.key.write(s);
        s.write_u32(self.event_zone_id).write_u32(self.step);
    }
}

/// `SCSkillCooldownResetPacket`: one definition per opcode (`crate::systems::skills::packets::SCSkillCooldownResetPacket`).
pub use crate::systems::skills::packets::SCSkillCooldownResetPacket;
