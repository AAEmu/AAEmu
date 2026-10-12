//! Chat channel packets (`CSSendChatMessagePacket` itself is registered by `packets::c2g_world`
//! and routed through [`crate::world::session::send_chat_message`]).

use super::cs_packet;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;

cs_packet!(
    /// `CSJoinUserChatChannelPacket` (logged only, as in C#).
    CSJoinUserChatChannelPacket { name: String, pwd: String, create: bool }
    read |s| { Ok(Self { name: s.read_string()?, pwd: s.read_string()?, create: s.read_bool()? }) }
    handle |self, server, conn| {
        tracing::debug!("JoinUserChatChannel, Name: {}, Create: {}", self.name, self.create);
        Ok(())
    }
);

cs_packet!(
    /// `CSLeaveChatChannelPacket` (logged only, as in C#).
    CSLeaveChatChannelPacket { chat: i64 }
    read |s| { Ok(Self { chat: s.read_i64()? }) }
    handle |self, server, conn| {
        tracing::debug!("LeaveChatChannel, Chat: {}", self.chat);
        Ok(())
    }
);

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSJoinUserChatChannelPacket>(LEVEL_PLAIN, cs::CSJoinUserChatChannelPacket);
    reg.register::<CSLeaveChatChannelPacket>(LEVEL_PLAIN, cs::CSLeaveChatChannelPacket);
}
