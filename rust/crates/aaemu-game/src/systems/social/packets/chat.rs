//! Chat channel packets (`CSSendChatMessagePacket` itself is registered by `packets::c2g_world`
//! and routed through [`crate::world::session::send_chat_message`]).

use super::{active_char, cs_packet};
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, sc};
use crate::packets::raw_body_packet;
use crate::packets::world::SCChatMessagePacket;
use crate::systems::social::{
    chat::{chat_type, ChatChannel},
    util::send_error,
};
use aaemu_commons::{Marshal, PacketStream};

cs_packet!(
    /// `CSJoinUserChatChannelPacket`: temporary user channel create/join.
    CSJoinUserChatChannelPacket { name: String, pwd: String, create: bool }
    read |s| { Ok(Self { name: s.read_string()?, pwd: s.read_string()?, create: s.read_bool()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        if let Err(error) = server.chat.join_user_channel(&p, &self.name, &self.pwd, self.create, &server.ids.chat) {
            send_error(&p, error);
        }
        Ok(())
    }
);

cs_packet!(
    /// `CSLeaveChatChannelPacket`: authenticated member leave of an opaque USER identity.
    CSLeaveChatChannelPacket { chat: i64 }
    read |s| { Ok(Self { chat: s.read_i64()? }) }
    handle |self, server, conn| {
        let p = active_char!(conn);
        if let Err(error) = server.chat.leave_user_channel(&p, self.chat, &server.ids.chat) {
            send_error(&p, error);
        }
        Ok(())
    }
);

raw_body_packet!(SCUserChatJoinedPacket, sc::SCJoinedChatChannelPacket);
raw_body_packet!(SCUserChatMessagePacket, sc::SCChatMessagePacket);

pub(crate) fn user_joined(channel: &ChatChannel) -> SCUserChatJoinedPacket {
    let mut body = PacketStream::new();
    body.write_i16(channel.chat_type)
        .write_i16(channel.sub_type)
        .write_u32(channel.faction)
        .write_str(&channel.internal_name());
    SCUserChatJoinedPacket {
        body: body.as_bytes().to_vec(),
    }
}

pub(crate) fn user_message(
    channel: &ChatChannel,
    message: &SCChatMessagePacket,
) -> SCUserChatMessagePacket {
    let mut body = PacketStream::new();
    message.write(&mut body);
    let mut body = body.as_bytes().to_vec();
    // Keep all existing sender/message fields; only select the actual channel in the first
    // eight bytes instead of the ordinary encoder's faction-based channel identity.
    body[..8].copy_from_slice(
        &crate::systems::social::chat::user_channel_identity(channel).to_le_bytes(),
    );
    SCUserChatMessagePacket { body }
}

/// The USER-only edge preserves both opaque identity fields that ordinary chat ignores.
/// Slash commands retain the same handling order as `world::session::send_chat_message`.
pub async fn send_user_chat(
    server: &crate::server::GameServer,
    conn: &mut crate::net::connection::GameConnection,
    packet: crate::packets::c2g_world::CSSendChatMessagePacket,
) {
    let Some(p) = conn.active_char.clone() else {
        return;
    };
    if let Some(command) = packet.message.strip_prefix('/') {
        let (handled, _) = server
            .systems
            .services
            .commands
            .handle(server, &p, command.trim())
            .await;
        if handled {
            return;
        }
    }
    let sender = crate::world::session::chat_sender(&p.data(), p.obj_id);
    let message = SCChatMessagePacket {
        chat_type: chat_type::USER,
        sender: Some(sender),
        message: packet.message,
        ability: packet.ability,
        language_type: packet.language_type,
    };
    if let Err(error) = server
        .chat
        .send_user_message(&p, packet.unk1, packet.unk2 as u32, &message)
    {
        send_error(&p, error);
    }
}

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSJoinUserChatChannelPacket>(LEVEL_PLAIN, cs::CSJoinUserChatChannelPacket);
    reg.register::<CSLeaveChatChannelPacket>(LEVEL_PLAIN, cs::CSLeaveChatChannelPacket);
}
