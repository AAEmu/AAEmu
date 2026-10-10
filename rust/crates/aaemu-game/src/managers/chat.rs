//! `Core/Managers/ChatManager` + `Models/Game/Chat/ChatChannel`: faction / nation / zone / guild /
//! family channels with join / leave / send. Party, raid and trial channels need the team and
//! trial systems (later phases).

use std::collections::BTreeMap;
use std::sync::{Arc, Mutex};

use crate::data::world_data::factions;
use crate::managers::id::IdManager;
use crate::net::packet::ServerPacket;
use crate::packets::world::{
    ChatSender, SCChatMessagePacket, SCJoinedChatChannelPacket, SCLeavedChatChannelPacket,
};
use crate::world::PlayerRef;

/// `ChatType`.
pub mod chat_type {
    pub const WHISPERED: i16 = -4;
    pub const WHISPER: i16 = -3;
    pub const SYSTEM: i16 = -2;
    pub const NOTICE: i16 = -1;
    pub const WHITE: i16 = 0;
    pub const SHOUT: i16 = 1;
    pub const TRADE: i16 = 2;
    pub const GROUP_FIND: i16 = 3;
    pub const PARTY: i16 = 4;
    pub const RAID: i16 = 5;
    pub const REGION: i16 = 6;
    pub const CLAN: i16 = 7;
    pub const SYSTEM2: i16 = 8;
    pub const FAMILY: i16 = 9;
    pub const RAID_LEADER: i16 = 10;
    pub const JUDGE: i16 = 11;
    pub const ALLY: i16 = 14;
    pub const USER: i16 = 15;
}

/// `ChatChannel`.
#[derive(Debug)]
pub struct ChatChannel {
    pub chat_type: i16,
    pub sub_type: i16,
    pub faction: u32,
    pub internal_id: u32,
    pub internal_name: String,
    members: Mutex<Vec<PlayerRef>>,
}

impl ChatChannel {
    fn new(chat_type: i16, sub_type: i16, faction: u32, internal_id: u32, name: String) -> Self {
        Self {
            chat_type,
            sub_type,
            faction,
            internal_id,
            internal_name: name,
            members: Mutex::new(Vec::new()),
        }
    }

    pub fn members(&self) -> Vec<PlayerRef> {
        self.members.lock().expect("channel poisoned").clone()
    }

    /// `JoinChannel`.
    pub fn join(&self, p: &PlayerRef) -> bool {
        {
            let mut m = self.members.lock().expect("channel poisoned");
            if m.iter().any(|x| Arc::ptr_eq(x, p)) {
                return false;
            }
            m.push(p.clone());
        }
        p.send(SCJoinedChatChannelPacket {
            chat_type: self.chat_type,
            sub_type: self.sub_type,
            faction_id: self.faction,
        });
        true
    }

    /// `LeaveChannel`.
    pub fn leave(&self, p: &PlayerRef) -> bool {
        let removed = {
            let mut m = self.members.lock().expect("channel poisoned");
            match m.iter().position(|x| Arc::ptr_eq(x, p)) {
                Some(i) => {
                    m.remove(i);
                    true
                }
                None => false,
            }
        };
        if removed {
            p.send(SCLeavedChatChannelPacket {
                chat_type: self.chat_type,
                sub_type: self.sub_type,
                faction_id: self.faction,
            });
        }
        removed
    }

    /// `SendMessage(origin, msg, ability, languageType)`: `origin ?? member` as sender.
    pub fn send_message(
        &self,
        origin: Option<&ChatSender>,
        member_sender: impl Fn(&PlayerRef) -> ChatSender,
        msg: &str,
        ability: i32,
        language_type: u8,
    ) -> usize {
        let members = self.members();
        for m in &members {
            m.send(SCChatMessagePacket {
                chat_type: self.chat_type,
                sender: Some(origin.cloned().unwrap_or_else(|| member_sender(m))),
                message: msg.to_string(),
                ability,
                language_type,
            });
        }
        members.len()
    }

    /// `SendPacket(packet)`.
    pub fn send_packet(&self, packet: &dyn ServerPacket) -> usize {
        let members = self.members();
        for m in &members {
            m.send_dyn(packet);
        }
        members.len()
    }
}

pub type ChannelRef = Arc<ChatChannel>;

/// `Race` values used by the nation channels.
pub mod race {
    pub const NUIAN: u8 = 1;
    pub const HARIHARAN: u8 = 5;
}

/// `ChatManager`.
#[derive(Debug)]
pub struct ChatManager {
    null_channel: ChannelRef,
    faction_channels: Mutex<BTreeMap<u32, ChannelRef>>,
    nation_channels: Mutex<BTreeMap<i64, ChannelRef>>,
    zone_channels: Mutex<BTreeMap<u32, ChannelRef>>,
    guild_channels: Mutex<BTreeMap<u32, ChannelRef>>,
    family_channels: Mutex<BTreeMap<u32, ChannelRef>>,
}

impl ChatManager {
    /// `new` + `Initialize()` (faction and nation channels; trial channels are a later phase).
    pub fn new(chat_ids: &IdManager) -> Self {
        let next = || chat_ids.get_next_id().unwrap_or(0);
        let m = Self {
            null_channel: Arc::new(ChatChannel::new(chat_type::WHITE, 0, 0, 0, "Null".into())),
            faction_channels: Mutex::new(BTreeMap::new()),
            nation_channels: Mutex::new(BTreeMap::new()),
            zone_channels: Mutex::new(BTreeMap::new()),
            guild_channels: Mutex::new(BTreeMap::new()),
            family_channels: Mutex::new(BTreeMap::new()),
        };
        {
            let mut f = m.faction_channels.lock().expect("chat poisoned");
            for (id, name) in [
                (factions::NUIA_ALLIANCE, "Nuia"),
                (factions::HARANYA_ALLIANCE, "Haranya"),
                (factions::PIRATE, "Pirate"),
            ] {
                f.insert(
                    id,
                    Arc::new(ChatChannel::new(
                        chat_type::ALLY,
                        0,
                        id,
                        next(),
                        name.into(),
                    )),
                );
            }
            let mut n = m.nation_channels.lock().expect("chat poisoned");
            for (r, fid, name) in [
                (race::NUIAN, factions::NUIA_ALLIANCE, "Nuian-Elf-Dwarf"),
                (
                    race::HARIHARAN,
                    factions::HARANYA_ALLIANCE,
                    "Harani-Firran-Warborn",
                ),
            ] {
                n.insert(
                    Self::nation_key(r),
                    Arc::new(ChatChannel::new(
                        chat_type::REGION,
                        0,
                        fid,
                        next(),
                        name.into(),
                    )),
                );
            }
            // Trial (court room) channels: later phase (TrialManager).
        }
        m
    }

    /// `((byte)race - 1) & 0xFC` (birth continent).
    fn nation_key(race: u8) -> i64 {
        (i64::from(race) - 1) & 0xFC
    }

    /// `GetFactionChat(motherId)`.
    pub fn faction_chat(&self, mother_id: u32) -> ChannelRef {
        self.faction_channels
            .lock()
            .expect("chat poisoned")
            .get(&mother_id)
            .cloned()
            .unwrap_or_else(|| self.null_channel.clone())
    }

    /// `GetNationChat(race)`.
    pub fn nation_chat(&self, race: u8) -> ChannelRef {
        self.nation_channels
            .lock()
            .expect("chat poisoned")
            .get(&Self::nation_key(race))
            .cloned()
            .unwrap_or_else(|| self.null_channel.clone())
    }

    /// `GetZoneChat(zoneKey)` (channels are keyed by zone group; created on demand).
    pub fn zone_chat(
        &self,
        zone_group_id: u32,
        group_name: Option<&str>,
        chat_ids: &IdManager,
    ) -> ChannelRef {
        self.zone_channels
            .lock()
            .expect("chat poisoned")
            .entry(zone_group_id)
            .or_insert_with(|| {
                let name = group_name
                    .map(str::to_string)
                    .unwrap_or_else(|| format!("ZoneGroup({zone_group_id})"));
                Arc::new(ChatChannel::new(
                    chat_type::SHOUT,
                    zone_group_id as i16,
                    0,
                    chat_ids.get_next_id().unwrap_or(0),
                    name,
                ))
            })
            .clone()
    }

    /// `GetGuildChat(expedition)`.
    pub fn guild_chat(&self, guild_id: u32, name: &str, chat_ids: &IdManager) -> ChannelRef {
        self.guild_channels
            .lock()
            .expect("chat poisoned")
            .entry(guild_id)
            .or_insert_with(|| {
                Arc::new(ChatChannel::new(
                    chat_type::CLAN,
                    guild_id as i16,
                    0,
                    chat_ids.get_next_id().unwrap_or(0),
                    name.to_string(),
                ))
            })
            .clone()
    }

    /// `GetFamilyChat(familyId)`.
    pub fn family_chat(&self, family_id: u32, chat_ids: &IdManager) -> ChannelRef {
        self.family_channels
            .lock()
            .expect("chat poisoned")
            .entry(family_id)
            .or_insert_with(|| {
                Arc::new(ChatChannel::new(
                    chat_type::FAMILY,
                    family_id as i16,
                    0,
                    chat_ids.get_next_id().unwrap_or(0),
                    format!("Family {family_id}"),
                ))
            })
            .clone()
    }

    /// `LeaveAllChannels` (faction, nation, zone, guild, family; in key order — the C#
    /// iterates `ConcurrentDictionary`s, whose order is unspecified).
    pub fn leave_all_channels(&self, p: &PlayerRef) {
        let lists = [
            &self.faction_channels,
            // court rooms: later phase
        ];
        for l in lists {
            let chans: Vec<ChannelRef> =
                l.lock().expect("chat poisoned").values().cloned().collect();
            for c in chans {
                c.leave(p);
            }
        }
        let n: Vec<ChannelRef> = self
            .nation_channels
            .lock()
            .expect("chat poisoned")
            .values()
            .cloned()
            .collect();
        for c in n {
            c.leave(p);
        }
        for l in [
            &self.zone_channels,
            &self.guild_channels,
            &self.family_channels,
        ] {
            let chans: Vec<ChannelRef> =
                l.lock().expect("chat poisoned").values().cloned().collect();
            for c in chans {
                c.leave(p);
            }
        }
    }
}
