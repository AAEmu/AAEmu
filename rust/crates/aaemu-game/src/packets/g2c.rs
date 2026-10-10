//! S->C game packets used by the lobby / enter-world flow (`AAEmu.Game.Core.Packets.G2C`).
//! Field order and widths follow the 1.2 C# `Write` methods exactly; every one of them is a
//! level-1 packet.

use aaemu_commons::{Marshal, PacketStream};

use super::packet_def;
use crate::data::FeatureSet;
use crate::models::character::Character;
use crate::models::item::UnixTime;
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;

/// `X2EnterWorldResponsePacket(short reason, bool gm, uint token, ushort port)`.
#[derive(Debug, Clone)]
pub struct X2EnterWorldResponsePacket {
    pub reason: i16,
    pub gm: bool,
    /// Stream token (`cs`): the game connection id.
    pub token: u32,
    /// Stream port (`cp`).
    pub port: u16,
    /// `wf`: `Helpers.UnixTimeNow()` at encode time.
    pub world_time: i64,
}
packet_def!(
    X2EnterWorldResponsePacket,
    level = LEVEL_PLAIN,
    opcode = sc::X2EnterWorldResponsePacket
);

impl Marshal for X2EnterWorldResponsePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i16(self.reason)
            .write_bool(self.gm)
            .write_u32(self.token) // Stream Token // cs
            .write_u16(self.port) // Stream Port // cp
            .write_i64(self.world_time) // wf
            .write_u16(0) // pubKeySize
            .write_str(""); // pubKey
    }
}

/// `SCReconnectAuthPacket(uint token)`.
#[derive(Debug, Clone)]
pub struct SCReconnectAuthPacket {
    pub token: u32,
}
packet_def!(
    SCReconnectAuthPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCReconnectAuthPacket
);

impl Marshal for SCReconnectAuthPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.token);
    }
}

/// `SCGetSlotCountPacket(byte sc)`.
#[derive(Debug, Clone)]
pub struct SCGetSlotCountPacket {
    pub sc: u8,
}
packet_def!(
    SCGetSlotCountPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCGetSlotCountPacket
);

impl Marshal for SCGetSlotCountPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.sc);
    }
}

/// `SCAccountInfoPacket(int payMethod, int payLocation, DateTime payStart, DateTime payEnd)`.
#[derive(Debug, Clone)]
pub struct SCAccountInfoPacket {
    pub pay_method: i32,
    pub pay_location: i32,
    pub pay_start: UnixTime,
    pub pay_end: UnixTime,
}
packet_def!(
    SCAccountInfoPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAccountInfoPacket
);

impl Marshal for SCAccountInfoPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.pay_method)
            .write_i32(self.pay_location)
            .write_i64(self.pay_start)
            .write_i64(self.pay_end)
            .write_i64(0); // realPayTime
    }
}

/// `SCRaceCongestionPacket()`: nine `RACE_CONGESTION.LOW` bytes.
#[derive(Debug, Clone, Default)]
pub struct SCRaceCongestionPacket;
packet_def!(
    SCRaceCongestionPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCRaceCongestionPacket
);

impl Marshal for SCRaceCongestionPacket {
    fn write(&self, s: &mut PacketStream) {
        for _ in 0..9 {
            s.write_u8(0);
        }
    }
}

/// `SCCharacterListPacket(bool last, Character[] characters)`.
#[derive(Debug, Clone)]
pub struct SCCharacterListPacket {
    pub last: bool,
    pub characters: Vec<Character>,
}
packet_def!(
    SCCharacterListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCCharacterListPacket
);

impl Marshal for SCCharacterListPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.last)
            .write_u8(self.characters.len() as u8);
        for c in &self.characters {
            c.write(s);
        }
    }
}

impl SCCharacterListPacket {
    /// The chunking done by `CSListCharacterPacket`: an empty list is sent as one `last = true`
    /// packet, otherwise in pairs with `last` set on the final chunk.
    pub fn chunked(characters: &[Character]) -> Vec<Self> {
        if characters.is_empty() {
            return vec![Self {
                last: true,
                characters: Vec::new(),
            }];
        }
        characters
            .chunks(2)
            .enumerate()
            .map(|(i, chunk)| Self {
                last: characters.len() - i * 2 <= 2,
                characters: chunk.to_vec(),
            })
            .collect()
    }
}

/// `SCInitialConfigPacket()`.
#[derive(Debug, Clone)]
pub struct SCInitialConfigPacket {
    /// `FeaturesManager.Fsets`.
    pub fsets: FeatureSet,
}
packet_def!(
    SCInitialConfigPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInitialConfigPacket
);

impl SCInitialConfigPacket {
    pub const HOST: &'static str = "aaemu.local";
}

impl Marshal for SCInitialConfigPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_str(Self::HOST); // host
        self.fsets.write(s); // fset
        s.write_i32(0) // count // candidatelist.lua
            .write_i32(50); // initLp
        for _ in 0..7 {
            // canPlaceHouse, canPayTax, canUseAuction, canTrade, canSendMail, canUseBank, canUseCopper
            s.write_bool(true);
        }
        s.write_u8(2) // secondPriceType (2 = na loyalty token)
            .write_u8(0) // secondPasswordMaxFailCount
            .write_u16(0); // idleKickTime
    }
}

/// `SCTrionConfigPacket(bool activate, string authUrl, string platformUrl, string commerceUrl)`.
#[derive(Debug, Clone)]
pub struct SCTrionConfigPacket {
    pub activate: bool,
    pub auth_url: String,
    pub platform_url: String,
    pub commerce_url: String,
}
packet_def!(
    SCTrionConfigPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTrionConfigPacket
);

impl Marshal for SCTrionConfigPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.activate)
            .write_str(&self.auth_url)
            .write_str(&self.platform_url)
            .write_str(&self.commerce_url);
    }
}

/// `SCChatSpamDelayPacket()`: an all-zero spam configuration.
#[derive(Debug, Clone, Default)]
pub struct SCChatSpamDelayPacket;
packet_def!(
    SCChatSpamDelayPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCChatSpamDelayPacket
);

impl Marshal for SCChatSpamDelayPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(0); // version
        for _ in 0..15 {
            s.write_u8(0); // chatTypeGroup
        }
        for _ in 0..15 {
            s.write_f32(0.0); // chatGroupDelay
        }
        s.write_str("") // applyConfig
            .write_str(""); // detectConfig
    }
}

/// `SCAccountAttributeConfigPacket(bool[] used)`: the first two entries are written.
#[derive(Debug, Clone)]
pub struct SCAccountAttributeConfigPacket {
    pub used: [bool; 2],
}
packet_def!(
    SCAccountAttributeConfigPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAccountAttributeConfigPacket
);

impl Marshal for SCAccountAttributeConfigPacket {
    fn write(&self, s: &mut PacketStream) {
        for u in self.used {
            s.write_bool(u);
        }
    }
}

/// `SCLevelRestrictionConfigPacket(searchLevel, bidLevel, postLevel, trade, mail, limitLevels)`:
/// the first 15 `limitLevels` are written.
#[derive(Debug, Clone)]
pub struct SCLevelRestrictionConfigPacket {
    pub search_level: u8,
    pub bid_level: u8,
    pub post_level: u8,
    pub trade: u8,
    pub mail: u8,
    pub limit_levels: [u8; 15],
}
packet_def!(
    SCLevelRestrictionConfigPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLevelRestrictionConfigPacket
);

impl Marshal for SCLevelRestrictionConfigPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.search_level)
            .write_u8(self.bid_level)
            .write_u8(self.post_level)
            .write_u8(self.trade)
            .write_u8(self.mail)
            .write_raw(&self.limit_levels);
    }
}

/// `SCUpdatePremiumPointPacket(int point, byte oldPg, byte pg)`.
#[derive(Debug, Clone)]
pub struct SCUpdatePremiumPointPacket {
    pub point: i32,
    pub old_pg: u8,
    pub pg: u8,
}
packet_def!(
    SCUpdatePremiumPointPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUpdatePremiumPointPacket
);

impl Marshal for SCUpdatePremiumPointPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.point)
            .write_u8(self.old_pg)
            .write_u8(self.pg);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::{encode, ServerPacket};

    fn body(p: &dyn ServerPacket) -> Vec<u8> {
        let mut s = PacketStream::new();
        p.write_body(&mut s);
        s.into_bytes()
    }

    pub(crate) fn hex(s: &str) -> Vec<u8> {
        s.split_whitespace()
            .map(|b| u8::from_str_radix(b, 16).unwrap())
            .collect()
    }

    #[test]
    fn enter_world_response_frame_is_byte_exact() {
        let p = X2EnterWorldResponsePacket {
            reason: 0,
            gm: false,
            token: 0x11223344,
            port: 1250,
            world_time: 0x0102030405060708,
        };
        assert_eq!(
            encode(&p),
            hex("DD 01 00 00  00 00  00 00  00  44 33 22 11  E2 04  08 07 06 05 04 03 02 01  00 00  00 00")
        );
        let gm = X2EnterWorldResponsePacket { gm: true, ..p };
        assert_eq!(body(&gm)[2], 1);
    }

    #[test]
    fn lobby_packet_frames_are_byte_exact() {
        assert_eq!(
            encode(&SCGetSlotCountPacket { sc: 0 }),
            hex("DD 01 00 00 E2 01 00")
        );
        assert_eq!(
            encode(&SCAccountInfoPacket {
                pay_method: 1,
                pay_location: 1,
                pay_start: 0,
                pay_end: 1_893_456_000
            }),
            hex("DD 01 00 00 C2 01  01 00 00 00  01 00 00 00  00 00 00 00 00 00 00 00  80 D8 DB 70 00 00 00 00  00 00 00 00 00 00 00 00")
        );
        assert_eq!(
            encode(&SCRaceCongestionPacket),
            hex("DD 01 00 00 3F 00  00 00 00 00 00 00 00 00 00")
        );
        assert_eq!(
            encode(&SCAccountAttributeConfigPacket {
                used: [false, true]
            }),
            hex("DD 01 00 00 15 02 00 01")
        );
        assert_eq!(
            encode(&SCLevelRestrictionConfigPacket {
                search_level: 10,
                bid_level: 10,
                post_level: 10,
                trade: 10,
                mail: 10,
                limit_levels: [0, 15, 15, 15, 0, 0, 15, 0, 0, 0, 0, 0, 0, 0, 15],
            }),
            hex("DD 01 00 00 17 02  0A 0A 0A 0A 0A  00 0F 0F 0F 00 00 0F 00 00 00 00 00 00 00 0F")
        );
        assert_eq!(
            encode(&SCUpdatePremiumPointPacket {
                point: 1,
                old_pg: 1,
                pg: 1
            }),
            hex("DD 01 00 00 01 02  01 00 00 00 01 01")
        );
        assert_eq!(
            encode(&SCReconnectAuthPacket { token: 7 }),
            hex("DD 01 00 00 01 00 07 00 00 00")
        );
        assert_eq!(
            encode(&SCCharacterListPacket::chunked(&[])[0]),
            hex("DD 01 00 00 3E 00 01 00")
        );
    }

    #[test]
    fn chat_spam_delay_layout() {
        let mut want = hex("DD 01 00 00 D3 00");
        want.extend_from_slice(&[0; 1 + 15 + 15 * 4 + 2 + 2]);
        assert_eq!(encode(&SCChatSpamDelayPacket), want);
    }

    #[test]
    fn trion_config_layout() {
        let p = SCTrionConfigPacket {
            activate: true,
            auth_url: "a/l".into(),
            platform_url: "p".into(),
            commerce_url: "".into(),
        };
        assert_eq!(
            encode(&p),
            hex("DD 01 00 00 07 00  01  03 00 61 2F 6C  01 00 70  00 00")
        );
    }

    #[test]
    fn initial_config_layout() {
        let f = FeatureSet::initialized(55, 50);
        let b = encode(&SCInitialConfigPacket { fsets: f.clone() });
        let mut want = hex("DD 01 00 00 06 00");
        want.extend_from_slice(&hex("0B 00"));
        want.extend_from_slice(b"aaemu.local");
        want.extend_from_slice(&[11, 0]);
        want.extend_from_slice(f.bytes());
        want.extend_from_slice(&hex(
            "00 00 00 00  32 00 00 00  01 01 01 01 01 01 01  02  00  00 00",
        ));
        assert_eq!(b, want);
    }

    #[test]
    fn character_list_chunking() {
        let chars: Vec<Character> = (1..=5)
            .map(|id| Character::new(id, 1, Default::default()))
            .collect();
        let shape = |v: Vec<SCCharacterListPacket>| -> Vec<(bool, usize)> {
            v.iter().map(|c| (c.last, c.characters.len())).collect()
        };
        assert_eq!(
            shape(SCCharacterListPacket::chunked(&chars)),
            vec![(false, 2), (false, 2), (true, 1)]
        );
        assert_eq!(
            shape(SCCharacterListPacket::chunked(&chars[..4])),
            vec![(false, 2), (true, 2)]
        );
        assert_eq!(
            shape(SCCharacterListPacket::chunked(&chars[..2])),
            vec![(true, 2)]
        );
    }
}

#[cfg(test)]
#[path = "sc_character_list_packet_tests.rs"]
mod sc_character_list_packet_tests;
