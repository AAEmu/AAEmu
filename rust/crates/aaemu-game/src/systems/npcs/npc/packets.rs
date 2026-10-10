//! NPC interaction / state packets (`AAEmu.Game.Core.Packets`).
//!
//! C->S: `CSStartInteractionPacket`, `CSInteractNPCPacket`, `CSInteractNPCEndPacket`,
//! `CSSelectInteractionExPacket`.
//! S->C: `SCNpcInteractionSkillListPacket`, `SCAiAggroPacket`, `SCUnitDeathPacket`,
//! `SCNpcChatMessagePacket`, `SCUnitModelPostureChangedPacket`, `SCAggroTargetChangedPacket`.
//! All level 1; field order and widths follow the 1.2 C# `Read` / `Write`.

use aaemu_commons::{Marshal, PacketStream};
use futures::future::BoxFuture;

use crate::error::Result;
use crate::models::unit::ModelPostureType;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::world::WorldObject;

/// `SkillsEnum` values used by the interaction code.
pub mod skills_enum {
    pub const USE_WAREHOUSE: u32 = 13238;
    pub const CHANGE_SKILLSETS: u32 = 12082;
    pub const USE_AUCTIONEER: u32 = 12083;
    pub const BLESSING: u32 = 12084;
    pub const REPAIR: u32 = 12086;
    pub const USE_STORE: u32 = 12087;
    pub const SLAVE_MOUNTING: u32 = 12077;
    pub const HEAL_PET_S_WOUNDS: u32 = 12195;
    pub const FORM_GUILD: u32 = 13736;
    pub const WAR_SUPPORT: u32 = 12660;
    pub const ITEM_FUSION: u32 = 23478;
}

/// `KillReason`.
pub mod kill_reason {
    pub const UNKNOWN: u8 = 0;
    pub const DAMAGE: u8 = 1;
}

// ---------------------------------------------------------------------------------------------
// S->C
// ---------------------------------------------------------------------------------------------

/// `SCNpcInteractionSkillListPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCNpcInteractionSkillListPacket {
    pub npc_obj_id: u32,
    pub obj_id: u32,
    pub extra_info: i32,
    pub pick_id: i32,
    pub mouse_button: u8,
    pub modifier_keys: i32,
    pub skill_list: Vec<u32>,
}
packet_def!(
    SCNpcInteractionSkillListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCNpcInteractionSkillListPacket
);
impl Marshal for SCNpcInteractionSkillListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.npc_obj_id)
            .write_bc(self.obj_id)
            .write_i32(self.extra_info)
            .write_i32(self.pick_id)
            .write_u8(self.mouse_button)
            .write_i32(self.skill_list.len() as i32);
        for &id in &self.skill_list {
            s.write_u32(id);
        }
        s.write_u8(1) // interactable
            .write_i32(self.modifier_keys);
    }
}

/// `SCAiAggroPacket(npcId, count, hostileUnitId, summarizeDamage, val2, val3)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub struct SCAiAggroPacket {
    pub npc_id: u32,
    pub count: i32,
    pub hostile_unit_id: u32,
    pub summarize_damage: i32,
    pub val2: i32,
    pub val3: i32,
}
impl SCAiAggroPacket {
    /// `new SCAiAggroPacket(npcId, count)`.
    pub fn new(npc_id: u32, count: i32) -> Self {
        Self {
            npc_id,
            count,
            ..Self::default()
        }
    }
}
packet_def!(
    SCAiAggroPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAiAggroPacket
);
impl Marshal for SCAiAggroPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.npc_id).write_i32(self.count);
        if self.count > 0 {
            s.write_bc(self.hostile_unit_id)
                .write_i32(self.summarize_damage)
                .write_i32(self.val2)
                .write_i32(self.val3)
                .write_u8(135); // topFlags
        }
    }
}

/// `SCUnitDeathPacket(objId, killReason, resurrectWaitTime, lostExp, durabilityLossRate,
/// killer)`; `killer` = (`ObjId`, `Name`) of the killer unit.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCUnitDeathPacket {
    pub obj_id: u32,
    pub kill_reason: u8,
    pub resurrect_wait_time: i32,
    pub lost_exp: i32,
    pub durability_loss_rate: u8,
    pub killer: Option<(u32, String)>,
}
packet_def!(
    SCUnitDeathPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitDeathPacket
);
impl Marshal for SCUnitDeathPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id)
            .write_u8(self.kill_reason)
            .write_i32(self.resurrect_wait_time)
            .write_i32(self.lost_exp)
            .write_u8(self.durability_loss_rate)
            .write_bc(self.killer.as_ref().map_or(0, |k| k.0));
        if let Some((_, name)) = &self.killer {
            s.write_u8(0) // GameType
                .write_u16(0) // killStreak
                .write_u8(0) // param1
                .write_u8(0) // param2
                .write_u8(0) // param3
                .write_str(name);
        }
    }
}

/// `SCNpcChatMessagePacket(chatType, npc, character, kind, type, message)`. `npc_name` is the
/// resolved name (see [`npc_chat_name`]); `faction_id` = `npc?.Faction.Id ?? 0`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCNpcChatMessagePacket {
    pub chat_type: i16,
    pub faction_id: u32,
    pub npc_obj_id: u32,
    pub npc_name: String,
    pub character_obj_id: u32,
    pub kind: u8,
    pub type_: u32,
    pub message: String,
}
packet_def!(
    SCNpcChatMessagePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCNpcChatMessagePacket
);
impl Marshal for SCNpcChatMessagePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i16(self.chat_type)
            .write_i16(self.faction_id as i16) // subType
            .write_u32(self.faction_id)
            .write_bc(self.npc_obj_id)
            .write_str(&self.npc_name)
            .write_bc(self.character_obj_id)
            .write_u8(self.kind);
        if self.kind == 1 {
            s.write_u32(self.type_);
        } else {
            s.write_str(&self.message);
        }
    }
}

/// The name `SCNpcChatMessagePacket` writes: `npc.Name`, or when empty the localized
/// `npcs.name` of the template (`""` without an NPC).
pub fn npc_chat_name(server: &GameServer, npc_name: &str, template_id: u32) -> String {
    if !npc_name.is_empty() {
        return npc_name.to_string();
    }
    server
        .game_data
        .systems
        .npcs
        .npc
        .localized_name(template_id, "")
}

/// `SCUnitModelPostureChangedPacket(unit, animActionId, activateAnimation)`; the posture type
/// and `HasLootLeft` are read from the unit.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCUnitModelPostureChangedPacket {
    pub obj_id: u32,
    pub posture: ModelPostureType,
    pub has_loot_left: bool,
    pub anim_action_id: u32,
    pub activate_animation: bool,
}
packet_def!(
    SCUnitModelPostureChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitModelPostureChangedPacket
);
impl Marshal for SCUnitModelPostureChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id);
        // Unit.ModelPosture(stream, unit, animActionId, activateAnimation)
        s.write_u8(self.posture as u8)
            .write_bool(self.has_loot_left);
        match self.posture {
            ModelPostureType::HouseState => {
                for _ in 0..8 {
                    s.write_bool(true); // 2 doors, 6 windows
                }
            }
            ModelPostureType::ActorModelState => {
                s.write_u32(self.anim_action_id)
                    .write_bool(self.activate_animation);
            }
            ModelPostureType::FarmfieldState => {
                s.write_u32(0)
                    .write_f32(0.0)
                    .write_i32(0)
                    .write_bool(false)
                    .write_bool(false);
            }
            ModelPostureType::TurretState => {
                s.write_f32(0.0).write_f32(0.0);
            }
            ModelPostureType::None => {}
        }
    }
}

/// `SCAggroTargetChangedPacket(npcId, targetId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCAggroTargetChangedPacket {
    pub npc_id: u32,
    pub target_id: u32,
}
packet_def!(
    SCAggroTargetChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAggroTargetChanged
);
impl Marshal for SCAggroTargetChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.npc_id).write_bc(self.target_id);
    }
}

// ---------------------------------------------------------------------------------------------
// C->S
// ---------------------------------------------------------------------------------------------

/// `CSStartInteractionPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSStartInteractionPacket {
    pub npc_obj_id: u32,
    pub obj_id: u32,
    pub extra_info: i32,
    pub pick_id: i32,
    pub mouse_button: u8,
    pub modifier_keys: i32,
}

/// The interaction option for an NPC template (the `if / else if` chain of
/// `CSStartInteractionPacket`): 0 when the NPC has no special function.
pub fn interaction_option(t: &super::data::NpcTemplate) -> u32 {
    use skills_enum::*;
    if t.banker {
        USE_WAREHOUSE
    } else if t.ability_changer {
        CHANGE_SKILLSETS
    } else if t.auctioneer {
        USE_AUCTIONEER
    } else if t.priest {
        BLESSING
    } else if t.repairman {
        REPAIR
    } else if t.merchant {
        USE_STORE
    } else if t.stabler {
        HEAL_PET_S_WOUNDS
    } else if t.expedition {
        FORM_GUILD
    } else if t.recruting_battlefield_id > 0 {
        WAR_SUPPORT
    } else if t.blacksmith {
        ITEM_FUSION
    } else {
        0
    }
}

impl ClientPacket for CSStartInteractionPacket {
    const NAME: &'static str = "CSStartInteractionPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            npc_obj_id: s.read_bc()?,
            obj_id: s.read_bc()?,
            extra_info: s.read_i32()?,
            pick_id: s.read_i32()?,
            mouse_button: s.read_u8()?,
            modifier_keys: s.read_i32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "StartInteraction, NpcObjId: {}, objId: {}, extraInfo: {}, pickId: {}, mouse: {}, mods: {}",
                self.npc_obj_id,
                self.obj_id,
                self.extra_info,
                self.pick_id,
                self.mouse_button,
                self.modifier_keys
            );
            let Some(me) = conn.active_char.clone() else {
                return Ok(());
            };
            let instance = me.data().unit.transform.instance_id;
            let Some(world) = server.world.world(instance) else {
                return Ok(());
            };
            let packet = |skills: Vec<u32>| SCNpcInteractionSkillListPacket {
                npc_obj_id: self.npc_obj_id,
                obj_id: self.obj_id,
                extra_info: self.extra_info,
                pick_id: self.pick_id,
                mouse_button: self.mouse_button,
                modifier_keys: self.modifier_keys,
                skill_list: skills,
            };
            // TODO (C#): Distance-check
            if let Some(npc) = world.npc(self.npc_obj_id) {
                let template_id = npc.data().unit.template_id;
                // `npc.Template` is never null for a created NPC.
                let option = server
                    .game_data
                    .systems
                    .npcs
                    .npc
                    .template(template_id)
                    .map_or(0, interaction_option);
                me.send(packet(vec![option]));
            }
            if let Some(WorldObject::Mate(_)) = world.unit(self.npc_obj_id) {
                me.send(packet(vec![skills_enum::SLAVE_MOUNTING]));
            }
            Ok(())
        })
    }
}

/// `CSInteractNPCPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSInteractNPCPacket {
    pub obj_id: u32,
    pub is_target_changed: bool,
}

impl ClientPacket for CSInteractNPCPacket {
    const NAME: &'static str = "CSInteractNPCPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            is_target_changed: s.read_bool()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!(
                "InteractNPC, BcId: {}, TargetChanged: {}",
                self.obj_id,
                self.is_target_changed
            );
            let Some(me) = conn.active_char.clone() else {
                return Ok(()); // C#: NullReferenceException
            };
            let instance = me.data().unit.transform.instance_id;
            let unit_id = if self.obj_id > 0 {
                server
                    .world
                    .world(instance)
                    .and_then(|w| w.unit(self.obj_id))
                    .map_or(0, |u| u.obj_id())
            } else {
                0
            };
            {
                let mut c = me.data();
                c.unit.current_interaction_object = unit_id;
                if self.is_target_changed {
                    c.unit.current_target = unit_id;
                }
            }
            conn.send(&SCAiAggroPacket::new(self.obj_id, 0));
            Ok(())
        })
    }
}

/// `CSInteractNPCEndPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSInteractNPCEndPacket {
    pub obj_id: u32,
}

impl ClientPacket for CSInteractNPCEndPacket {
    const NAME: &'static str = "CSInteractNPCEndPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!("InteractNPCEnd, BcId: {}", self.obj_id);
            if let Some(me) = &conn.active_char {
                me.data().unit.current_interaction_object = 0;
            }
            Ok(())
        })
    }
}

/// `CSSelectInteractionExPacket` (logged only, as in C#).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSSelectInteractionExPacket {
    pub target_id: u32,
    pub interaction_ex: i32,
    pub var1: i32,
}

impl ClientPacket for CSSelectInteractionExPacket {
    const NAME: &'static str = "CSSelectInteractionExPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            target_id: s.read_bc()?,
            interaction_ex: s.read_i32()?,
            var1: s.read_i32()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "SelectInteractionEx, TargetId: {}, InteractionEx: {}, Var1: {}",
                self.target_id,
                self.interaction_ex,
                self.var1
            );
            Ok(())
        })
    }
}

/// Registers the NPC C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSStartInteractionPacket>(LEVEL_PLAIN, cs::CSStartInteractionPacket)
        .register::<CSInteractNPCPacket>(LEVEL_PLAIN, cs::CSInteractNPCPacket)
        .register::<CSInteractNPCEndPacket>(LEVEL_PLAIN, cs::CSInteractNPCEndPacket)
        .register::<CSSelectInteractionExPacket>(LEVEL_PLAIN, cs::CSSelectInteractionExPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    fn frame(op: u16, body: &[u8]) -> Vec<u8> {
        let mut v = vec![0xDD, 0x01, 0, 0];
        v.extend_from_slice(&op.to_le_bytes());
        v.extend_from_slice(body);
        v
    }

    #[test]
    fn interaction_skill_list_bytes() {
        let p = SCNpcInteractionSkillListPacket {
            npc_obj_id: 0x0A0B0C,
            obj_id: 0x000102,
            extra_info: -1,
            pick_id: 7,
            mouse_button: 2,
            modifier_keys: 0x11223344,
            skill_list: vec![12087, 5],
        };
        #[rustfmt::skip]
        let body = [
            0x0C, 0x0B, 0x0A, // bc npc
            0x02, 0x01, 0x00, // bc obj
            0xFF, 0xFF, 0xFF, 0xFF, // extraInfo
            7, 0, 0, 0, // pickId
            2, // mouseButton
            2, 0, 0, 0, // count
            0x37, 0x2F, 0, 0, // 12087
            5, 0, 0, 0,
            1, // interactable
            0x44, 0x33, 0x22, 0x11, // modifierKeys
        ];
        assert_eq!(encode(&p), frame(0x07b, &body));
    }

    #[test]
    fn ai_aggro_bytes() {
        assert_eq!(
            encode(&SCAiAggroPacket::new(0x123456, 0)),
            frame(0x1c4, &[0x56, 0x34, 0x12, 0, 0, 0, 0])
        );
        let p = SCAiAggroPacket {
            npc_id: 1,
            count: 1,
            hostile_unit_id: 2,
            summarize_damage: 300,
            val2: -1,
            val3: 4,
        };
        #[rustfmt::skip]
        let body = [
            1, 0, 0, 1, 0, 0, 0, // bc npc, count
            2, 0, 0, // bc hostile
            0x2C, 0x01, 0, 0, // 300
            0xFF, 0xFF, 0xFF, 0xFF,
            4, 0, 0, 0,
            135, // topFlags
        ];
        assert_eq!(encode(&p), frame(0x1c4, &body));
    }

    #[test]
    fn unit_death_bytes() {
        let mut p = SCUnitDeathPacket {
            obj_id: 0x200,
            kill_reason: kill_reason::DAMAGE,
            resurrect_wait_time: 60,
            lost_exp: 0,
            durability_loss_rate: 0,
            killer: None,
        };
        #[rustfmt::skip]
        let body = [
            0x00, 0x02, 0x00, 1, // bc, reason
            60, 0, 0, 0, 0, 0, 0, 0, 0, // wait, lostExp, durability
            0, 0, 0, // killer bc
        ];
        assert_eq!(encode(&p), frame(0x071, &body));
        p.killer = Some((0x100, "Ab".into()));
        #[rustfmt::skip]
        let body = [
            0x00, 0x02, 0x00, 1,
            60, 0, 0, 0, 0, 0, 0, 0, 0,
            0x00, 0x01, 0x00, // killer bc
            0, // GameType
            0, 0, // killStreak
            0, 0, 0, // params
            2, 0, b'A', b'b', // name
        ];
        assert_eq!(encode(&p), frame(0x071, &body));
    }

    #[test]
    fn npc_chat_bytes() {
        let mut p = SCNpcChatMessagePacket {
            chat_type: -3,
            faction_id: 0x00010002,
            npc_obj_id: 5,
            npc_name: "N".into(),
            character_obj_id: 6,
            kind: 1,
            type_: 77,
            message: "x".into(),
        };
        #[rustfmt::skip]
        let body = [
            0xFD, 0xFF, // chatType
            0x02, 0x00, // (short)faction
            0x02, 0x00, 0x01, 0x00, // faction
            5, 0, 0, // bc npc
            1, 0, b'N', // name
            6, 0, 0, // bc char
            1, // kind
            77, 0, 0, 0, // type
        ];
        assert_eq!(encode(&p), frame(0x0cf, &body));
        p.kind = 0;
        #[rustfmt::skip]
        let body = [
            0xFD, 0xFF, 0x02, 0x00, 0x02, 0x00, 0x01, 0x00,
            5, 0, 0, 1, 0, b'N', 6, 0, 0,
            0, // kind
            1, 0, b'x', // message
        ];
        assert_eq!(encode(&p), frame(0x0cf, &body));
    }

    #[test]
    fn posture_changed_bytes() {
        let p = SCUnitModelPostureChangedPacket {
            obj_id: 0x200,
            posture: ModelPostureType::ActorModelState,
            has_loot_left: false,
            anim_action_id: 0x01020304,
            activate_animation: false,
        };
        assert_eq!(
            encode(&p),
            frame(0x103, &[0x00, 0x02, 0x00, 4, 0, 4, 3, 2, 1, 0])
        );
        let p = SCUnitModelPostureChangedPacket {
            posture: ModelPostureType::None,
            has_loot_left: true,
            ..p
        };
        assert_eq!(encode(&p), frame(0x103, &[0x00, 0x02, 0x00, 0, 1]));
        let p = SCUnitModelPostureChangedPacket {
            posture: ModelPostureType::TurretState,
            ..p
        };
        assert_eq!(
            encode(&p),
            frame(0x103, &[0x00, 0x02, 0x00, 8, 1, 0, 0, 0, 0, 0, 0, 0, 0])
        );
    }

    #[test]
    fn aggro_target_changed_bytes() {
        assert_eq!(
            encode(&SCAggroTargetChangedPacket {
                npc_id: 0x200,
                target_id: 0x100
            }),
            frame(0x1bd, &[0x00, 0x02, 0x00, 0x00, 0x01, 0x00])
        );
    }

    #[test]
    fn decodes_client_packets() {
        let mut s = PacketStream::new();
        s.write_bc(0x200)
            .write_bc(0x100)
            .write_i32(-2)
            .write_i32(9)
            .write_u8(1)
            .write_i32(4);
        let p = CSStartInteractionPacket::read(&mut s).unwrap();
        assert_eq!(
            p,
            CSStartInteractionPacket {
                npc_obj_id: 0x200,
                obj_id: 0x100,
                extra_info: -2,
                pick_id: 9,
                mouse_button: 1,
                modifier_keys: 4
            }
        );
        let mut s = PacketStream::from_bytes(vec![0x00, 0x02, 0x00, 1]);
        assert_eq!(
            CSInteractNPCPacket::read(&mut s).unwrap(),
            CSInteractNPCPacket {
                obj_id: 0x200,
                is_target_changed: true
            }
        );
        let mut s = PacketStream::from_bytes(vec![0x03, 0x02, 0x01]);
        assert_eq!(
            CSInteractNPCEndPacket::read(&mut s).unwrap().obj_id,
            0x010203
        );
        let mut s = PacketStream::from_bytes(vec![1, 0, 0, 5, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF]);
        assert_eq!(
            CSSelectInteractionExPacket::read(&mut s).unwrap(),
            CSSelectInteractionExPacket {
                target_id: 1,
                interaction_ex: 5,
                var1: -1
            }
        );
    }

    #[tokio::test]
    async fn start_interaction_flow() {
        use crate::test_support::*;
        let server = test_server_with(super::super::manager::tests::npc_game_data());
        let mut a = TestClient::new(&server, 1, 1001);
        let pa = spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
        a.conn.active_char = Some(pa.clone());
        let npc = super::super::manager::create(&server, 0x200, 100).unwrap();
        npc.data().unit.transform.set_xyz(105.0, 100.0, 100.0);
        let world = server.world.world(0).unwrap();
        world.spawn(&npc, &server.view_ctx());
        assert_eq!(
            a.drain_ops(),
            vec![sc::SCUnitStatePacket, sc::SCUnitPointsPacket]
        );

        let req = CSStartInteractionPacket {
            npc_obj_id: 0x200,
            obj_id: 0x100,
            extra_info: 0,
            pick_id: 3,
            mouse_button: 1,
            modifier_keys: 0,
        };
        req.handle(&server, &mut a.conn).await.unwrap();
        let expected = SCNpcInteractionSkillListPacket {
            npc_obj_id: 0x200,
            obj_id: 0x100,
            extra_info: 0,
            pick_id: 3,
            mouse_button: 1,
            modifier_keys: 0,
            skill_list: vec![skills_enum::USE_STORE], // the guard is a merchant
        };
        let mut body = PacketStream::new();
        expected.write(&mut body);
        assert_eq!(
            a.drain(),
            vec![(sc::SCNpcInteractionSkillListPacket, body.into_bytes())]
        );

        // An unknown object: no answer.
        CSStartInteractionPacket {
            npc_obj_id: 0x999,
            ..req
        }
        .handle(&server, &mut a.conn)
        .await
        .unwrap();
        assert!(a.drain().is_empty());

        // CSInteractNPC: interaction object + target, SCAiAggroPacket(objId, 0).
        CSInteractNPCPacket {
            obj_id: 0x200,
            is_target_changed: true,
        }
        .handle(&server, &mut a.conn)
        .await
        .unwrap();
        assert_eq!(
            a.drain(),
            vec![(sc::SCAiAggroPacket, vec![0x00, 0x02, 0x00, 0, 0, 0, 0])]
        );
        assert_eq!(pa.data().unit.current_interaction_object, 0x200);
        assert_eq!(pa.data().unit.current_target, 0x200);
        CSInteractNPCEndPacket { obj_id: 0x200 }
            .handle(&server, &mut a.conn)
            .await
            .unwrap();
        assert_eq!(pa.data().unit.current_interaction_object, 0);
        assert_eq!(pa.data().unit.current_target, 0x200);
    }

    #[test]
    fn interaction_option_priority() {
        let mut t = super::super::data::NpcTemplate::default();
        assert_eq!(interaction_option(&t), 0);
        t.blacksmith = true;
        assert_eq!(interaction_option(&t), skills_enum::ITEM_FUSION);
        t.merchant = true;
        assert_eq!(interaction_option(&t), skills_enum::USE_STORE);
        t.banker = true;
        assert_eq!(interaction_option(&t), skills_enum::USE_WAREHOUSE);
    }
}
