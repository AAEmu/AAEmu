//! Packet definitions. One sub-module per C# packet namespace:
//! `c2g` (`Core/Packets/C2G`), `g2c` (`Core/Packets/G2C`), `proxy` (`Core/Packets/Proxy`).
//! Internal login-server packets live in [`crate::login`].

pub mod c2g;
pub mod c2g_world;
pub mod g2c;
pub mod proxy;
pub mod world;
pub mod world_objects;

/// Implements [`crate::net::packet::PacketDef`] for a packet struct.
macro_rules! packet_def {
    ($ty:ident, level = $level:expr, opcode = $op:expr) => {
        impl $crate::net::packet::PacketDef for $ty {
            const OPCODE: u16 = $op;
            const LEVEL: u8 = $level;
            const NAME: &'static str = stringify!($ty);
        }
    };
}
pub(crate) use packet_def;

/// A level-1 packet whose body was encoded up front (packets whose C# `Write` reads live game
/// objects are built by the object's owner while it holds the object's lock).
macro_rules! raw_body_packet {
    ($ty:ident, $op:expr) => {
        #[derive(Debug, Clone, PartialEq, Eq)]
        pub struct $ty {
            pub body: Vec<u8>,
        }
        $crate::packets::packet_def!($ty, level = $crate::net::packet::LEVEL_PLAIN, opcode = $op);
        impl aaemu_commons::Marshal for $ty {
            fn write(&self, s: &mut aaemu_commons::PacketStream) {
                s.write_raw(&self.body);
            }
        }
    };
}
pub(crate) use raw_body_packet;

use crate::net::packet::{LEVEL_PLAIN, LEVEL_PROXY};
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, pp};

/// `GameNetwork()` registrations for the packets ported so far. Every other C# registration is
/// logged as "not ported yet" by the dispatcher (see `opcodes::cs::NAMES`).
pub fn build_registry() -> PacketRegistry {
    use c2g::*;
    use c2g_world::*;
    use proxy::*;

    let mut r = PacketRegistry::new();
    // World / lobby
    r.register::<X2EnterWorldPacket>(LEVEL_PLAIN, cs::X2EnterWorldPacket)
        .register::<CSLeaveWorldPacket>(LEVEL_PLAIN, cs::CSLeaveWorldPacket)
        .register::<CSListCharacterPacket>(LEVEL_PLAIN, cs::CSListCharacterPacket)
        .register::<CSCancelLeaveWorldPacket>(LEVEL_PLAIN, cs::CSCancelLeaveWorldPacket)
        .register::<CSCreateCharacterPacket>(LEVEL_PLAIN, cs::CSCreateCharacterPacket)
        .register::<CSDeleteCharacterPacket>(LEVEL_PLAIN, cs::CSDeleteCharacterPacket)
        .register::<CSCancelCharacterDeletePacket>(LEVEL_PLAIN, cs::CSCancelCharacterDeletePacket)
        .register::<CSSelectCharacterPacket>(LEVEL_PLAIN, cs::CSSelectCharacterPacket)
        .register::<CSSpawnCharacterPacket>(LEVEL_PLAIN, cs::CSSpawnCharacterPacket)
        .register::<CSNotifyInGamePacket>(LEVEL_PLAIN, cs::CSNotifyInGamePacket)
        .register::<CSNotifyInGameCompletedPacket>(LEVEL_PLAIN, cs::CSNotifyInGameCompletedPacket)
        .register::<CSMoveUnitPacket>(LEVEL_PLAIN, cs::CSMoveUnitPacket)
        .register::<CSSendChatMessagePacket>(LEVEL_PLAIN, cs::CSSendChatMessagePacket);

    // Proxy
    r.register::<ChangeStateClientPacket>(LEVEL_PROXY, pp::ChangeStatePacket)
        .register::<FinishStatePacket>(LEVEL_PROXY, pp::FinishStatePacket)
        .register::<FlushMsgsPacket>(LEVEL_PROXY, pp::FlushMsgsPacket)
        .register::<UpdatePhysicsTimePacket>(LEVEL_PROXY, pp::UpdatePhysicsTimePacket)
        .register::<BeginUpdateObjPacket>(LEVEL_PROXY, pp::BeginUpdateObjPacket)
        .register::<EndUpdateObjPacket>(LEVEL_PROXY, pp::EndUpdateObjPacket)
        .register::<BeginBindObjPacket>(LEVEL_PROXY, pp::BeginBindObjPacket)
        .register::<EndBindObjPacket>(LEVEL_PROXY, pp::EndBindObjPacket)
        .register::<UnbindPredictedObjPacket>(LEVEL_PROXY, pp::UnbindPredictedObjPacket)
        .register::<RemoveStaticObjPacket>(LEVEL_PROXY, pp::RemoveStaticObjPacket)
        .register::<VoiceDataPacket>(LEVEL_PROXY, pp::VoiceDataPacket)
        .register::<UpdateAspectPacket>(LEVEL_PROXY, pp::UpdateAspectPacket)
        .register::<SetAspectProfilePacket>(LEVEL_PROXY, pp::SetAspectProfilePacket)
        .register::<PartialAspectPacket>(LEVEL_PROXY, pp::PartialAspectPacket)
        .register::<SetGameTypeClientPacket>(LEVEL_PROXY, pp::SetGameTypePacket)
        .register::<ChangeCVarPacket>(LEVEL_PROXY, pp::ChangeCVarPacket)
        .register::<EntityClassRegistrationPacket>(LEVEL_PROXY, pp::EntityClassRegistrationPacket)
        .register::<PingPacket>(LEVEL_PROXY, pp::PingPacket)
        .register::<PongClientPacket>(LEVEL_PROXY, pp::PongPacket)
        .register::<PacketSeqChange>(LEVEL_PROXY, pp::PacketSeqChange)
        .register::<FastPingPacket>(LEVEL_PROXY, pp::FastPingPacket)
        .register::<FastPongClientPacket>(LEVEL_PROXY, pp::FastPongPacket);
    r
}
