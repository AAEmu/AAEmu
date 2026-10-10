//! `Core/Managers/ChatManager` + `Models/Game/Chat/ChatChannel`: faction / nation / court room /
//! zone / party / raid / guild / family channels with join / leave / send and
//! `CleanUpChannels`. (Moved here from `managers::chat`, which re-exports this module.)

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

/// `CourtRoomRegion`.
pub mod court_room_region {
    pub const NUIAN: u8 = 0;
    pub const HARANYAN: u8 = 1;
    pub const INVALID: u8 = 0xFF;
}

/// `ChatChannel`.
#[derive(Debug)]
pub struct ChatChannel {
    pub chat_type: i16,
    pub sub_type: i16,
    pub faction: u32,
    pub internal_id: u32,
    /// `InternalName` (party / raid channels rename themselves on every lookup).
    internal_name: Mutex<String>,
    members: Mutex<Vec<PlayerRef>>,
}

impl ChatChannel {
    fn new(chat_type: i16, sub_type: i16, faction: u32, internal_id: u32, name: String) -> Self {
        Self {
            chat_type,
            sub_type,
            faction,
            internal_id,
            internal_name: Mutex::new(name),
            members: Mutex::new(Vec::new()),
        }
    }

    pub fn internal_name(&self) -> String {
        self.internal_name.lock().expect("channel poisoned").clone()
    }

    fn set_internal_name(&self, name: String) {
        *self.internal_name.lock().expect("channel poisoned") = name;
    }

    /// `GetMembersSnapshot`.
    pub fn members(&self) -> Vec<PlayerRef> {
        self.members.lock().expect("channel poisoned").clone()
    }

    pub fn contains(&self, p: &PlayerRef) -> bool {
        self.members
            .lock()
            .expect("channel poisoned")
            .iter()
            .any(|x| Arc::ptr_eq(x, p))
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

    fn is_empty(&self) -> bool {
        self.members.lock().expect("channel poisoned").is_empty()
    }
}

pub type ChannelRef = Arc<ChatChannel>;

/// `Race` values used by the nation channels.
pub mod race {
    pub const NUIAN: u8 = 1;
    pub const HARIHARAN: u8 = 5;
}

type Channels<K> = Mutex<BTreeMap<K, ChannelRef>>;

/// `ChatManager`.
#[derive(Debug)]
pub struct ChatManager {
    null_channel: ChannelRef,
    faction_channels: Channels<u32>,
    nation_channels: Channels<i64>,
    zone_channels: Channels<u32>,
    party_channels: Channels<u32>,
    raid_channels: Channels<u32>,
    guild_channels: Channels<u32>,
    family_channels: Channels<u32>,
    court_room_channels: Channels<u8>,
}

impl ChatManager {
    /// `new` + `Initialize()` (faction, nation and court room channels).
    pub fn new(chat_ids: &IdManager) -> Self {
        let next = || chat_ids.get_next_id().unwrap_or(0);
        let m = Self {
            null_channel: Arc::new(ChatChannel::new(chat_type::WHITE, 0, 0, 0, "Null".into())),
            faction_channels: Mutex::default(),
            nation_channels: Mutex::default(),
            zone_channels: Mutex::default(),
            party_channels: Mutex::default(),
            raid_channels: Mutex::default(),
            guild_channels: Mutex::default(),
            family_channels: Mutex::default(),
            court_room_channels: Mutex::default(),
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
            let mut c = m.court_room_channels.lock().expect("chat poisoned");
            for (region, fid, name) in [
                (
                    court_room_region::NUIAN,
                    factions::NUIA_ALLIANCE,
                    "Nuian Court",
                ),
                (
                    court_room_region::HARANYAN,
                    factions::HARANYA_ALLIANCE,
                    "Haranyan Court",
                ),
            ] {
                c.insert(
                    region,
                    Arc::new(ChatChannel::new(
                        chat_type::JUDGE,
                        0,
                        fid,
                        next(),
                        name.into(),
                    )),
                );
            }
        }
        m
    }

    /// `((byte)race - 1) & 0xFC` (birth continent).
    fn nation_key(race: u8) -> i64 {
        (i64::from(race) - 1) & 0xFC
    }

    /// `ListAllChannels` (null, faction, nation, court room, zone, party, raid, guild, family).
    pub fn list_all_channels(&self) -> Vec<ChannelRef> {
        let mut res = vec![self.null_channel.clone()];
        res.extend(values(&self.faction_channels));
        res.extend(values(&self.nation_channels));
        res.extend(values(&self.court_room_channels));
        res.extend(values(&self.zone_channels));
        res.extend(values(&self.party_channels));
        res.extend(values(&self.raid_channels));
        res.extend(values(&self.guild_channels));
        res.extend(values(&self.family_channels));
        res
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

    /// `GetPartyChat(party, myChar)`: `party_number` is the member's slot / 5 (0 when not a
    /// member), `owner_name` the online owner's name (`" ???"` otherwise).
    pub fn party_chat(
        &self,
        team_id: u32,
        party_number: u32,
        owner_name: Option<&str>,
        chat_ids: &IdManager,
    ) -> ChannelRef {
        let party_id = (team_id << 6).wrapping_add(party_number);
        let ch = self
            .party_channels
            .lock()
            .expect("chat poisoned")
            .entry(party_id)
            .or_insert_with(|| {
                Arc::new(ChatChannel::new(
                    chat_type::PARTY,
                    party_id as i16,
                    0,
                    chat_ids.get_next_id().unwrap_or(0),
                    format!("Party({party_id})"),
                ))
            })
            .clone();
        ch.set_internal_name(format!(
            "Party {} of {}",
            party_number + 1,
            owner_name.unwrap_or(" ???")
        ));
        ch
    }

    /// `GetRaidChat(party)`.
    pub fn raid_chat(
        &self,
        team_id: u32,
        owner_name: Option<&str>,
        chat_ids: &IdManager,
    ) -> ChannelRef {
        let ch = self
            .raid_channels
            .lock()
            .expect("chat poisoned")
            .entry(team_id)
            .or_insert_with(|| {
                Arc::new(ChatChannel::new(
                    chat_type::RAID,
                    team_id as i16,
                    0,
                    chat_ids.get_next_id().unwrap_or(0),
                    format!("Raid({team_id})"),
                ))
            })
            .clone();
        ch.set_internal_name(format!("Raid of {}", owner_name.unwrap_or(" ???")));
        ch
    }

    /// `GetTrialChat(courtRegion)` (`None` for regions without a court channel).
    pub fn court_chat(&self, region: u8) -> Option<ChannelRef> {
        self.court_room_channels
            .lock()
            .expect("chat poisoned")
            .get(&region)
            .cloned()
    }

    /// `LeaveAllChannels` (faction, court rooms, nation, zone, party, raid, guild, family; each
    /// in key order — the C# iterates `ConcurrentDictionary`s, whose order is unspecified).
    pub fn leave_all_channels(&self, p: &PlayerRef) {
        let lists: [Vec<ChannelRef>; 8] = [
            values(&self.faction_channels),
            values(&self.court_room_channels),
            values(&self.nation_channels),
            values(&self.zone_channels),
            values(&self.party_channels),
            values(&self.raid_channels),
            values(&self.guild_channels),
            values(&self.family_channels),
        ];
        for list in lists {
            for c in list {
                c.leave(p);
            }
        }
    }

    /// `CleanUpChannels`: removes the empty zone / party / raid / guild / family channels and
    /// releases their ids. Returns the number of removed channels.
    pub fn clean_up_channels(&self, chat_ids: &IdManager) -> usize {
        clean_up(&self.zone_channels, chat_ids)
            + clean_up(&self.party_channels, chat_ids)
            + clean_up(&self.raid_channels, chat_ids)
            + clean_up(&self.guild_channels, chat_ids)
            + clean_up(&self.family_channels, chat_ids)
    }
}

fn values<K>(m: &Channels<K>) -> Vec<ChannelRef> {
    m.lock().expect("chat poisoned").values().cloned().collect()
}

fn clean_up<K: Ord + Copy>(m: &Channels<K>, chat_ids: &IdManager) -> usize {
    let mut map = m.lock().expect("chat poisoned");
    let empty: Vec<K> = map
        .iter()
        .filter(|(_, c)| c.is_empty())
        .map(|(k, _)| *k)
        .collect();
    for k in &empty {
        if let Some(c) = map.remove(k) {
            chat_ids.release_id(c.internal_id);
        }
    }
    empty.len()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn initialize_allocates_fixed_channels_in_csharp_order() {
        let all_ids = crate::managers::id::IdManagers::default();
        let ids = &all_ids.chat;
        let m = ChatManager::new(ids);
        let all = m.list_all_channels();
        // null + 3 faction + 2 nation + 2 court
        assert_eq!(all.len(), 8);
        let got: Vec<(i16, u32, u32, String)> = all
            .iter()
            .map(|c| (c.chat_type, c.faction, c.internal_id, c.internal_name()))
            .collect();
        assert_eq!(got[0], (chat_type::WHITE, 0, 0, "Null".into()));
        assert_eq!(got[1], (chat_type::ALLY, 148, 1, "Nuia".into()));
        assert_eq!(
            got[4],
            (chat_type::REGION, 148, 4, "Nuian-Elf-Dwarf".into())
        );
        assert_eq!(got[6], (chat_type::JUDGE, 148, 6, "Nuian Court".into()));
        assert_eq!(got[7], (chat_type::JUDGE, 149, 7, "Haranyan Court".into()));
        assert!(m.court_chat(court_room_region::INVALID).is_none());
    }

    #[test]
    fn party_and_raid_channels_and_cleanup() {
        let all_ids = crate::managers::id::IdManagers::default();
        let ids = &all_ids.chat;
        let m = ChatManager::new(ids);
        let p = m.party_chat(3, 1, Some("Bob"), ids);
        assert_eq!((p.chat_type, p.sub_type), (chat_type::PARTY, (3 << 6) + 1));
        assert_eq!(p.internal_name(), "Party 2 of Bob");
        let r = m.raid_chat(3, None, ids);
        assert_eq!((r.chat_type, r.sub_type), (chat_type::RAID, 3));
        assert_eq!(r.internal_name(), "Raid of  ???");
        let id = r.internal_id;
        assert_eq!(m.clean_up_channels(ids), 2);
        // The released id is handed out again.
        let again = m.raid_chat(9, Some("x"), ids);
        assert!(again.internal_id == id || again.internal_id == p.internal_id);
    }
}
