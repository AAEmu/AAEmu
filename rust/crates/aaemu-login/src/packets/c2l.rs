//! Client -> login server packets (`AAEmu.Login.Core.Packets.C2L`).
//!
//! All reads use the lenient C# `PacketStream` semantics (see [`crate::lenient`]).

use aaemu_commons::PacketStream;

use super::PacketError;
use crate::lenient::LenientRead;
use crate::models::{AccountId, GameServerId};

/// `CLOffsets` — all opcodes here are updated for version client_12_r208022.
pub mod opcodes {
    pub const CA_REQUEST_AUTH: u16 = 0x001;
    pub const CA_REQUEST_AUTH_TENCENT: u16 = 0x002;
    pub const CA_REQUEST_AUTH_GAME_ON: u16 = 0x003;
    pub const CA_REQUEST_AUTH_TRION: u16 = 0x004;
    pub const CA_REQUEST_AUTH_MAIL_RU: u16 = 0x005;
    pub const CA_CHALLENGE_RESPONSE: u16 = 0x006;
    pub const CA_CHALLENGE_RESPONSE2: u16 = 0x007;
    pub const CA_OTP_NUMBER: u16 = 0x008;
    pub const CA_PC_CERT_NUMBER: u16 = 0x00a;
    pub const CA_LIST_WORLD: u16 = 0x00b;
    pub const CA_ENTER_WORLD: u16 = 0x00c;
    pub const CA_CANCEL_ENTER_WORLD: u16 = 0x00d;
    pub const CA_REQUEST_RECONNECT: u16 = 0x00e;
}

/// `CARequestAuthPacket`: request authentication (starts the Korea challenge flow).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaRequestAuth {
    pub p_from: u32,
    pub p_to: u32,
    pub svc: u8,
    pub dev: bool,
    pub account: String,
    pub mac: Vec<u8>,
    pub mac2: Vec<u8>,
    pub cpu: u64,
}

impl CaRequestAuth {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        Ok(Self {
            p_from: s.l_u32(),
            p_to: s.l_u32(),
            svc: s.l_u8(),
            dev: s.l_bool(),
            account: s.l_string(),
            mac: s.l_bytes_sized()?,
            mac2: s.l_bytes_sized()?,
            cpu: s.l_u64(),
        })
    }
}

/// `CARequestAuthTencentPacket` (handler is a no-op).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaRequestAuthTencent {
    pub p_from: u32,
    pub p_to: u32,
    pub dev: bool,
    pub qqno: u32,
    pub len: u16,
    pub sig: Vec<u8>,
    pub key: Vec<u8>,
    pub mac: Vec<u8>,
}

impl CaRequestAuthTencent {
    pub fn read(s: &mut PacketStream) -> Self {
        Self {
            p_from: s.l_u32(),
            p_to: s.l_u32(),
            dev: s.l_bool(),
            qqno: s.l_u32(),
            len: s.l_u16(),
            sig: s.l_bytes(128), // length 128 or len?
            key: s.l_bytes(16),
            mac: s.l_bytes(8),
        }
    }
}

/// `CARequestAuthGameOnPacket` (handler is a no-op).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaRequestAuthGameOn {
    pub p_from: u32,
    pub p_to: u32,
    pub dev: bool,
    pub mac: Vec<u8>,
    pub param: String,
    pub si: String,
}

impl CaRequestAuthGameOn {
    pub fn read(s: &mut PacketStream) -> Self {
        Self {
            p_from: s.l_u32(),
            p_to: s.l_u32(),
            dev: s.l_bool(),
            mac: s.l_bytes(8),
            param: s.l_string(), // or length 1023
            si: s.l_string(),    // or length 15
        }
    }
}

/// `CARequestAuthTrionPacket`: Trion ticket login (an XML ticket with username/password).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaRequestAuthTrion {
    pub p_from: u32,
    pub p_to: u32,
    pub dev: bool,
    pub mac: Vec<u8>,
    pub ticket: String,
    pub signature: String,
    pub is_last: bool,
    pub username: Option<String>,
    /// Hex-encoded SHA-256 of the password.
    pub password: Option<String>,
}

impl CaRequestAuthTrion {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        let mut p = Self {
            p_from: s.l_u32(),
            p_to: s.l_u32(),
            dev: s.l_bool(),
            mac: s.l_bytes_sized()?,
            ticket: s.l_string(),
            signature: s.l_string(),
            is_last: s.l_bool(),
            username: None,
            password: None,
        };
        // XDocument.Parse throws on invalid XML (uncaught -> packet fails to decode).
        let doc =
            roxmltree::Document::parse(&p.ticket).map_err(|e| PacketError::Xml(e.to_string()))?;
        let root = doc.root_element();
        // XElement.Element(name).Value: first direct child element with that (namespace-less)
        // name; Value is the concatenation of all descendant text.
        let element_value = |name: &str| {
            root.children()
                .find(|n| {
                    n.is_element()
                        && n.tag_name().name() == name
                        && n.tag_name().namespace().is_none()
                })
                .map(|n| {
                    n.descendants()
                        .filter(|d| d.is_text())
                        .filter_map(|d| d.text())
                        .collect::<String>()
                })
        };
        let username = element_value("username");
        let password = element_value("password");
        match (username, password) {
            (Some(u), Some(pw)) if !u.trim().is_empty() && !pw.trim().is_empty() => {
                p.username = Some(u);
                p.password = Some(pw);
            }
            _ => tracing::error!("RequestAuthTrion: username or password is empty or whitespace"),
        }
        Ok(p)
    }
}

/// `CARequestAuthMailRuPacket`: Mail.Ru login (the token is used as a SHA-256 password hash).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaRequestAuthMailRu {
    pub p_from: u32,
    pub p_to: u32,
    pub dev: bool,
    pub mac: Vec<u8>,
    /// The Mail.Ru user id.
    pub id: String,
    /// The authentication token provided by Mail.Ru.
    pub token: Vec<u8>,
}

impl CaRequestAuthMailRu {
    pub fn read(s: &mut PacketStream) -> Result<Self, PacketError> {
        Ok(Self {
            p_from: s.l_u32(),
            p_to: s.l_u32(),
            dev: s.l_bool(),
            mac: s.l_bytes_sized()?,
            id: s.l_string(),
            token: s.l_bytes_sized()?,
        })
    }
}

/// `CAChallengeResponsePacket`: response to the V1 Korea challenge (V1 is never issued).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaChallengeResponse {
    /// 4 AES-encrypted challenge uint32 values.
    pub ch: [u32; 4],
    /// 32 raw bytes containing the AES-encrypted plaintext password (V1 only).
    pub pw: Vec<u8>,
}

impl CaChallengeResponse {
    pub fn read(s: &mut PacketStream) -> Self {
        let mut ch = [0u32; 4];
        for v in &mut ch {
            *v = s.l_u32();
        }
        Self {
            ch,
            pw: s.l_bytes(32),
        }
    }
}

/// `CAChallengeResponse2Packet`: response to the V2 Korea challenge.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaChallengeResponse2 {
    /// 8 AES-encrypted challenge uint32 values.
    pub ch: [u32; 8],
}

impl CaChallengeResponse2 {
    pub fn read(s: &mut PacketStream) -> Self {
        let mut ch = [0u32; 8];
        for v in &mut ch {
            *v = s.l_u32();
        }
        Self { ch }
    }
}

/// `CAOtpNumberPacket`: OTP second factor for the Korea flow.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaOtpNumber {
    pub otp_number: String,
}

impl CaOtpNumber {
    pub fn read(s: &mut PacketStream) -> Self {
        Self {
            otp_number: s.l_string(),
        } // TODO but on old client length const 8
    }
}

/// `CAPcCertNumberPacket`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaPcCertNumber {
    pub cert_number: String,
}

impl CaPcCertNumber {
    pub fn read(s: &mut PacketStream) -> Self {
        // Nexon Simple Authentication Number? https://easyprotect.nexon.com/
        Self {
            cert_number: s.l_string(),
        } // TODO but on old client length const 8
    }
}

/// `CAListWorldPacket`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaListWorld {
    pub flag: u64,
}

impl CaListWorld {
    pub fn read(s: &mut PacketStream) -> Self {
        Self { flag: s.l_u64() }
    }
}

/// `CAEnterWorldPacket`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaEnterWorld {
    pub flag: u64,
    pub gs_id: GameServerId,
}

impl CaEnterWorld {
    pub fn read(s: &mut PacketStream) -> Self {
        Self {
            flag: s.l_u64(),
            gs_id: GameServerId(s.l_u8()),
        }
    }
}

/// `CACancelEnterWorldPacket`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaCancelEnterWorld {
    pub world_id: u8,
}

impl CaCancelEnterWorld {
    pub fn read(s: &mut PacketStream) -> Self {
        Self { world_id: s.l_u8() } // diw -> world id
    }
}

/// `CARequestReconnectPacket`.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct CaRequestReconnect {
    pub p_from: u32,
    pub p_to: u32,
    pub account_id: AccountId,
    pub gs_id: GameServerId,
    pub cookie: u32,
    pub mac: Vec<u8>,
}

impl CaRequestReconnect {
    pub fn read(s: &mut PacketStream) -> Self {
        let p_from = s.l_u32();
        let p_to = s.l_u32();
        let account_id = AccountId(s.l_u32());
        let gs_id = GameServerId(s.l_u8());
        let cookie = s.l_u32();
        let mac_len = s.l_u16();
        let mac = s.l_bytes(usize::from(mac_len));
        Self {
            p_from,
            p_to,
            account_id,
            gs_id,
            cookie,
            mac,
        }
    }
}

/// A decoded C2L packet (every packet registered in `PacketHandlers.ServiceCollectionExtensions`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum C2lPacket {
    RequestAuth(CaRequestAuth),
    RequestAuthTencent(CaRequestAuthTencent),
    RequestAuthGameOn(CaRequestAuthGameOn),
    RequestAuthTrion(CaRequestAuthTrion),
    RequestAuthMailRu(CaRequestAuthMailRu),
    ChallengeResponse(CaChallengeResponse),
    ChallengeResponse2(CaChallengeResponse2),
    OtpNumber(CaOtpNumber),
    ListWorld(CaListWorld),
    EnterWorld(CaEnterWorld),
    PcCertNumber(CaPcCertNumber),
    CancelEnterWorld(CaCancelEnterWorld),
    RequestReconnect(CaRequestReconnect),
}

impl C2lPacket {
    /// Decodes the body for `opcode`; `Ok(None)` for an unknown opcode.
    pub fn decode(opcode: u16, s: &mut PacketStream) -> Result<Option<Self>, PacketError> {
        use opcodes::*;
        Ok(Some(match opcode {
            CA_REQUEST_AUTH => Self::RequestAuth(CaRequestAuth::read(s)?),
            CA_REQUEST_AUTH_TENCENT => Self::RequestAuthTencent(CaRequestAuthTencent::read(s)),
            CA_REQUEST_AUTH_GAME_ON => Self::RequestAuthGameOn(CaRequestAuthGameOn::read(s)),
            CA_REQUEST_AUTH_TRION => Self::RequestAuthTrion(CaRequestAuthTrion::read(s)?),
            CA_REQUEST_AUTH_MAIL_RU => Self::RequestAuthMailRu(CaRequestAuthMailRu::read(s)?),
            CA_CHALLENGE_RESPONSE => Self::ChallengeResponse(CaChallengeResponse::read(s)),
            CA_CHALLENGE_RESPONSE2 => Self::ChallengeResponse2(CaChallengeResponse2::read(s)),
            CA_OTP_NUMBER => Self::OtpNumber(CaOtpNumber::read(s)),
            CA_LIST_WORLD => Self::ListWorld(CaListWorld::read(s)),
            CA_ENTER_WORLD => Self::EnterWorld(CaEnterWorld::read(s)),
            CA_PC_CERT_NUMBER => Self::PcCertNumber(CaPcCertNumber::read(s)),
            CA_CANCEL_ENTER_WORLD => Self::CancelEnterWorld(CaCancelEnterWorld::read(s)),
            CA_REQUEST_RECONNECT => Self::RequestReconnect(CaRequestReconnect::read(s)),
            _ => return Ok(None),
        }))
    }

    /// The C# class name, for logging.
    pub fn name(&self) -> &'static str {
        match self {
            Self::RequestAuth(_) => "CARequestAuthPacket",
            Self::RequestAuthTencent(_) => "CARequestAuthTencentPacket",
            Self::RequestAuthGameOn(_) => "CARequestAuthGameOnPacket",
            Self::RequestAuthTrion(_) => "CARequestAuthTrionPacket",
            Self::RequestAuthMailRu(_) => "CARequestAuthMailRuPacket",
            Self::ChallengeResponse(_) => "CAChallengeResponsePacket",
            Self::ChallengeResponse2(_) => "CAChallengeResponse2Packet",
            Self::OtpNumber(_) => "CAOtpNumberPacket",
            Self::ListWorld(_) => "CAListWorldPacket",
            Self::EnterWorld(_) => "CAEnterWorldPacket",
            Self::PcCertNumber(_) => "CAPcCertNumberPacket",
            Self::CancelEnterWorld(_) => "CACancelEnterWorldPacket",
            Self::RequestReconnect(_) => "CARequestReconnectPacket",
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn body(build: impl FnOnce(&mut PacketStream)) -> PacketStream {
        let mut s = PacketStream::new();
        build(&mut s);
        PacketStream::from_bytes(s.into_bytes())
    }

    #[test]
    fn request_auth() {
        let mut s = body(|s| {
            s.write_u32(1)
                .write_u32(2)
                .write_u8(3)
                .write_bool(true)
                .write_str("user");
            s.write_bytes_sized(&[1, 2])
                .write_bytes_sized(&[])
                .write_u64(99);
        });
        let p = CaRequestAuth::read(&mut s).unwrap();
        assert_eq!(p.account, "user");
        assert_eq!(p.mac, vec![1, 2]);
        assert_eq!(p.cpu, 99);
        assert!(p.dev);
    }

    #[test]
    fn trion_ticket() {
        let ticket = "<ticket><username>bob</username><password>ABCDEF</password></ticket>";
        let mut s = body(|s| {
            s.write_u32(0)
                .write_u32(0)
                .write_bool(false)
                .write_bytes_sized(&[0; 6]);
            s.write_str(ticket).write_str("sig").write_bool(true);
        });
        let p = CaRequestAuthTrion::read(&mut s).unwrap();
        assert_eq!(p.username.as_deref(), Some("bob"));
        assert_eq!(p.password.as_deref(), Some("ABCDEF"));

        let mut s = body(|s| {
            s.write_u32(0)
                .write_u32(0)
                .write_bool(false)
                .write_bytes_sized(&[]);
            s.write_str("<ticket><username> </username></ticket>")
                .write_str("")
                .write_bool(true);
        });
        let p = CaRequestAuthTrion::read(&mut s).unwrap();
        assert_eq!(p.username, None);

        let mut s = body(|s| {
            s.write_u32(0)
                .write_u32(0)
                .write_bool(false)
                .write_bytes_sized(&[]);
            s.write_str("<broken").write_str("").write_bool(true);
        });
        assert!(CaRequestAuthTrion::read(&mut s).is_err());
    }

    #[test]
    fn fixed_layouts() {
        let mut s = body(|s| {
            s.write_u64(0x1122).write_u8(3);
        });
        assert_eq!(
            CaEnterWorld::read(&mut s),
            CaEnterWorld {
                flag: 0x1122,
                gs_id: GameServerId(3)
            }
        );

        let mut s = body(|s| {
            s.write_u32(1)
                .write_u32(2)
                .write_u32(42)
                .write_u8(1)
                .write_u32(0xC0FFEE)
                .write_u16(2)
                .write_raw(&[9, 8]);
        });
        let p = CaRequestReconnect::read(&mut s);
        assert_eq!(
            (p.account_id, p.gs_id, p.cookie, p.mac),
            (AccountId(42), GameServerId(1), 0xC0FFEE, vec![9, 8])
        );

        let mut s = body(|s| {
            for i in 0..8 {
                s.write_u32(i);
            }
        });
        assert_eq!(
            CaChallengeResponse2::read(&mut s).ch,
            [0, 1, 2, 3, 4, 5, 6, 7]
        );

        // Truncated: lenient reads give zeros, like the C# client code path.
        let mut s = PacketStream::from_bytes(vec![1, 0, 0, 0]);
        assert_eq!(
            CaChallengeResponse2::read(&mut s).ch,
            [1, 0, 0, 0, 0, 0, 0, 0]
        );
    }

    #[test]
    fn opcodes_match_csharp_1_2() {
        // TypeId of every C2L packet class, printed by the C# 1.2 build.
        use opcodes::*;
        let table = [
            (CA_REQUEST_AUTH, 0x001),
            (CA_REQUEST_AUTH_TENCENT, 0x002),
            (CA_REQUEST_AUTH_GAME_ON, 0x003),
            (CA_REQUEST_AUTH_TRION, 0x004),
            (CA_REQUEST_AUTH_MAIL_RU, 0x005),
            (CA_CHALLENGE_RESPONSE, 0x006),
            (CA_CHALLENGE_RESPONSE2, 0x007),
            (CA_OTP_NUMBER, 0x008),
            (CA_PC_CERT_NUMBER, 0x00a),
            (CA_LIST_WORLD, 0x00b),
            (CA_ENTER_WORLD, 0x00c),
            (CA_CANCEL_ENTER_WORLD, 0x00d),
            (CA_REQUEST_RECONNECT, 0x00e),
        ];
        for (op, expected) in table {
            assert_eq!(op, expected);
        }
        // 0x009 is unassigned in 1.2.
        let mut s = PacketStream::new();
        assert_eq!(C2lPacket::decode(0x009, &mut s).unwrap(), None);
    }

    #[test]
    fn registered_packets_decode() {
        let mut s = body(|s| {
            s.write_str("123456");
        });
        assert_eq!(
            C2lPacket::decode(opcodes::CA_OTP_NUMBER, &mut s).unwrap(),
            Some(C2lPacket::OtpNumber(CaOtpNumber {
                otp_number: "123456".into()
            }))
        );
        let mut s = body(|s| {
            s.write_u64(7);
        });
        assert_eq!(
            C2lPacket::decode(opcodes::CA_LIST_WORLD, &mut s).unwrap(),
            Some(C2lPacket::ListWorld(CaListWorld { flag: 7 }))
        );
        let mut s = body(|s| {
            s.write_u64(0).write_u8(2);
        });
        assert_eq!(
            C2lPacket::decode(opcodes::CA_ENTER_WORLD, &mut s).unwrap(),
            Some(C2lPacket::EnterWorld(CaEnterWorld {
                flag: 0,
                gs_id: GameServerId(2)
            }))
        );
        let mut s = body(|s| {
            s.write_u32(1)
                .write_u32(2)
                .write_bool(false)
                .write_u32(12345)
                .write_u16(128);
            s.write_raw(&[0xAB; 128])
                .write_raw(&[0xCD; 16])
                .write_raw(&[0xEF; 8]);
        });
        match C2lPacket::decode(opcodes::CA_REQUEST_AUTH_TENCENT, &mut s).unwrap() {
            Some(C2lPacket::RequestAuthTencent(p)) => {
                assert_eq!((p.qqno, p.len), (12345, 128));
                assert_eq!(p.sig, vec![0xAB; 128]);
                assert_eq!(p.key, vec![0xCD; 16]);
                assert_eq!(p.mac, vec![0xEF; 8]);
            }
            other => panic!("unexpected {other:?}"),
        }
    }

    #[test]
    fn unknown_opcode() {
        let mut s = PacketStream::new();
        assert_eq!(C2lPacket::decode(0x0FF, &mut s).unwrap(), None);
    }
}
