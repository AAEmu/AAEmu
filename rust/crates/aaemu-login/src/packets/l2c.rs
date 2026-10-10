//! Login server -> client packets (`AAEmu.Login.Core.Packets.L2C`).

use std::net::IpAddr;
use std::time::Duration;

use aaemu_commons::PacketStream;

use super::{OutgoingPacket, PacketError};
use crate::models::{AccountId, GameServerId, GsLoad, LoginCharacterInfo, LoginDeniedReason};

/// `LCOffsets` — all opcodes here are updated for version client_12_r208022.
pub mod opcodes {
    pub const AC_JOIN_RESPONSE: u16 = 0x000;
    /// Listed in the C# `LCOffsets` (a C2L name in the L2C table); no packet uses it.
    pub const CA_REQUEST_AUTH: u16 = 0x001;
    pub const AC_CHALLENGE: u16 = 0x002;
    pub const AC_AUTH_RESPONSE: u16 = 0x003;
    pub const AC_CHALLENGE2: u16 = 0x004;
    pub const AC_ENTER_OTP: u16 = 0x005;
    pub const AC_SHOW_ARS: u16 = 0x006;
    pub const AC_ENTER_PC_CERT: u16 = 0x007;
    pub const AC_WORLD_LIST: u16 = 0x008;
    pub const AC_WORLD_QUEUE: u16 = 0x009;
    pub const AC_WORLD_COOKIE: u16 = 0x00a;
    pub const AC_ENTER_WORLD_DENIED: u16 = 0x00b;
    pub const AC_LOGIN_DENIED: u16 = 0x00c;
    pub const AC_ACCOUNT_WARNED: u16 = 0x00d;
}

/// `JoinResponseReason`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
#[repr(u16)]
pub enum JoinResponseReason {
    Success = 0,
    ProtocolMismatch = 1,
    ModeMismatch = 2,
}

/// An AFS (Account Feature Settings?) value.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct AfsValue {
    /// The maximum number of characters per account.
    pub max_characters_per_account: u8,
    /// The additional number of characters per server when using the slot increase item.
    pub additional_characters_per_server: u32,
    /// Whether the server is in character pre-creation mode.
    pub is_pre_select_character_period: bool,
}

impl AfsValue {
    pub fn from_u64(afs: u64) -> Self {
        Self {
            max_characters_per_account: (afs & 0xFF) as u8,
            is_pre_select_character_period: afs & 0x100 != 0,
            additional_characters_per_server: (afs >> 32) as u32,
        }
    }

    pub fn to_u64(self) -> u64 {
        (u64::from(self.additional_characters_per_server) << 32)
            | if self.is_pre_select_character_period {
                1 << 8
            } else {
                0
            }
            | u64::from(self.max_characters_per_account)
    }
}

/// `ACJoinResponsePacket`: sent in response to a successful authentication request (1.2 clients).
#[derive(Clone, Copy, Debug)]
pub struct AcJoinResponse {
    pub reason: u16,
    pub afs: u64,
}

impl AcJoinResponse {
    pub fn new(reason: JoinResponseReason, afs: AfsValue) -> Self {
        Self {
            reason: reason as u16,
            afs: afs.to_u64(),
        }
    }
}

impl OutgoingPacket for AcJoinResponse {
    const OPCODE: u16 = opcodes::AC_JOIN_RESPONSE;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u16(self.reason);
        // afs[0] -> max number of characters per account
        // afs[1] -> additional number of characters per server when using the slot increase item
        // afs[2] -> 1 - character pre-creation mode
        s.write_u64(self.afs);
        Ok(())
    }
}

/// `ACAuthResponsePacket`.
#[derive(Clone, Copy, Debug)]
pub struct AcAuthResponse {
    pub account_id: AccountId,
    pub slot_count: u8,
}

impl OutgoingPacket for AcAuthResponse {
    const OPCODE: u16 = opcodes::AC_AUTH_RESPONSE;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u32(self.account_id.0);
        s.write_bytes_sized(&[0u8; 32]); // wsk
        s.write_u8(self.slot_count);
        Ok(())
    }
}

/// `ACChallengePacket` — V1 Korea challenge.
///
/// **NOTE:** V1 challenge authentication is not enabled — SHA-1 without a per-account salt is
/// approximately 17 million times weaker than PBKDF2@600k. Preserved for protocol completeness
/// only; the server never issues this packet.
#[derive(Clone, Debug)]
pub struct AcChallenge {
    pub salt: u32,
    pub hc: Vec<u32>,
}

impl OutgoingPacket for AcChallenge {
    const OPCODE: u16 = opcodes::AC_CHALLENGE;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u32(self.salt);
        for i in 0..4 {
            s.write_u32(self.hc.get(i).copied().unwrap_or(0));
        }
        Ok(())
    }
}

/// `ACChallenge2Packet` — V2 Korea challenge. The client uses `round` and `salt` to derive the
/// AES-256 key via sha256_crypt, then computes the challenge response.
#[derive(Clone, Debug)]
pub struct AcChallenge2 {
    pub round: i32,
    pub salt: String,
    pub hc: Vec<u32>,
}

impl OutgoingPacket for AcChallenge2 {
    const OPCODE: u16 = opcodes::AC_CHALLENGE2;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_i32(self.round);
        // Encoding.ASCII: every char outside 0..=0x7F becomes '?'.
        let ascii: Vec<u8> = self
            .salt
            .chars()
            .map(|c| if c.is_ascii() { c as u8 } else { b'?' })
            .collect();
        s.write_bytes_sized(&ascii);
        for i in 0..8 {
            s.write_u32(self.hc.get(i).copied().unwrap_or(0));
        }
        Ok(())
    }
}

/// `ACEnterOtpPacket`: prompts for OTP entry.
///
/// If `current_try` is 0 or 1, no error message is displayed. If greater than one, the client
/// displays the number of remaining attempts (as `current_try-1/maximum_tries`).
#[derive(Clone, Copy, Debug, Default)]
pub struct AcEnterOtp {
    pub maximum_tries: i32,
    pub current_try: i32,
}

impl AcEnterOtp {
    /// An initial OTP entry packet with no error message.
    pub fn initial() -> Self {
        Self {
            maximum_tries: 0,
            current_try: 0,
        }
    }

    pub fn failure(maximum_tries: i32, current_try: i32) -> Self {
        Self {
            maximum_tries,
            current_try,
        }
    }
}

impl OutgoingPacket for AcEnterOtp {
    const OPCODE: u16 = opcodes::AC_ENTER_OTP;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_i32(self.maximum_tries); // mt
        s.write_i32(self.current_try); // ct
        Ok(())
    }
}

/// `ACEnterPcCertPacket`: initiates the certificate (PIN) process. Same semantics as
/// [`AcEnterOtp`].
#[derive(Clone, Copy, Debug, Default)]
pub struct AcEnterPcCert {
    pub maximum_tries: i32,
    pub current_try: i32,
}

impl AcEnterPcCert {
    pub fn initial() -> Self {
        Self {
            maximum_tries: 0,
            current_try: 0,
        }
    }

    pub fn failure(maximum_tries: i32, current_try: i32) -> Self {
        Self {
            maximum_tries,
            current_try,
        }
    }
}

impl OutgoingPacket for AcEnterPcCert {
    const OPCODE: u16 = opcodes::AC_ENTER_PC_CERT;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_i32(self.maximum_tries); // mt
        s.write_i32(self.current_try); // ct
        Ok(())
    }
}

/// `ACEnterWorldDeniedPacket`.
#[derive(Clone, Copy, Debug)]
pub struct AcEnterWorldDenied {
    pub reason: u8,
}

impl OutgoingPacket for AcEnterWorldDenied {
    const OPCODE: u16 = opcodes::AC_ENTER_WORLD_DENIED;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u8(self.reason);
        Ok(())
    }
}

/// `ACLoginDeniedPacket`.
///
/// `vp` are variable parameters that replace `$1`..`$9` placeholders in the client's localized
/// message for the given reason (e.g. `try_trade_cash_temporal` uses `$1` for the number of
/// suspension days).
#[derive(Clone, Debug)]
pub struct AcLoginDenied {
    pub reason: LoginDeniedReason,
    pub vp: Vec<i32>,
}

impl AcLoginDenied {
    pub fn new(reason: LoginDeniedReason) -> Self {
        Self {
            reason,
            vp: Vec::new(),
        }
    }

    /// Binary VP data: each entry is a 0x00 separator byte followed by a 4-byte LE int32.
    /// The client formats each int32 with `%d` and substitutes them into `$1`..`$9`.
    fn vp_data(&self) -> Vec<u8> {
        let mut data = Vec::with_capacity(self.vp.len() * 5);
        for v in &self.vp {
            data.push(0);
            data.extend_from_slice(&v.to_le_bytes());
        }
        data
    }
}

impl OutgoingPacket for AcLoginDenied {
    const OPCODE: u16 = opcodes::AC_LOGIN_DENIED;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u8(self.reason.0);
        s.write_bytes_sized(&self.vp_data()); // vp
        s.write_str(""); // msg - completely overrides the displayed message
        Ok(())
    }
}

/// `ACShowArsPacket`: shows ARS (Automatic Response System / 자동응답시스템) information.
///
/// ARS is automated phone verification: the system calls the registered phone number and the
/// user enters the code displayed on screen.
#[derive(Clone, Debug)]
pub struct AcShowArs {
    /// The text displayed to the user ("Enter the number below when you get a call").
    pub number: String,
    /// Displayed next to the number as remaining time.
    pub timeout: Duration,
}

impl OutgoingPacket for AcShowArs {
    const OPCODE: u16 = opcodes::AC_SHOW_ARS;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_str(&self.number); // num
        s.write_u32(self.timeout.as_millis() as u32); // timeout in milliseconds
        Ok(())
    }
}

/// `Helpers.ConvertIp(string)`: parses an IP and returns its bytes in reverse order.
pub fn convert_ip(ip: &str) -> Result<Vec<u8>, PacketError> {
    let addr: IpAddr = ip
        .parse()
        .map_err(|_| PacketError::InvalidIp(ip.to_string()))?;
    let mut bytes = match addr {
        IpAddr::V4(v4) => v4.octets().to_vec(),
        IpAddr::V6(v6) => v6.octets().to_vec(),
    };
    bytes.reverse();
    Ok(bytes)
}

/// `ACWorldCookiePacket`: the world cookie (the connection id) and the game server endpoint.
#[derive(Clone, Debug)]
pub struct AcWorldCookie {
    pub cookie: u32,
    pub server_ip: String,
    pub port: u16,
}

impl AcWorldCookie {
    /// If the client connected from the same machine as the login server, the client's own IP is
    /// sent instead of the configured game server host.
    pub fn new(
        connection_id: u32,
        client_ip: IpAddr,
        is_locally_connected: bool,
        gs_host: &str,
        gs_port: u16,
    ) -> Self {
        let server_ip = if is_locally_connected {
            client_ip.to_string()
        } else {
            gs_host.to_string()
        };
        Self {
            cookie: connection_id,
            server_ip,
            port: gs_port,
        }
    }
}

impl OutgoingPacket for AcWorldCookie {
    const OPCODE: u16 = opcodes::AC_WORLD_COOKIE;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        let ip = convert_ip(&self.server_ip)?;
        s.write_u32(self.cookie);
        for _ in 0..4 {
            s.write_raw(&ip);
            s.write_u16(self.port);
        }
        Ok(())
    }
}

/// One world entry of [`AcWorldList`].
#[derive(Clone, Debug)]
pub struct WorldInfo {
    pub id: GameServerId,
    pub name: String,
    pub active: bool,
    pub load: GsLoad,
}

/// `ACWorldListPacket`.
#[derive(Clone, Debug)]
pub struct AcWorldList {
    pub worlds: Vec<WorldInfo>,
    /// Characters of the account across all game servers.
    pub characters: Vec<LoginCharacterInfo>,
}

impl OutgoingPacket for AcWorldList {
    const OPCODE: u16 = opcodes::AC_WORLD_LIST;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u8(self.worlds.len() as u8);
        for w in &self.worlds {
            s.write_u8(w.id.0);
            s.write_str(&w.name);
            s.write_bool(w.active);
            if w.active {
                s.write_u8(w.load.0); // con
                for _ in 0..9 {
                    // race: NONE, NUIAN, FAIRY, DWARF, ELF, HARIHARAN, FERRE, RETURNED, WARBORN
                    // rcon (RACE_CONGESTION): LOW = 0, MIDDLE = 1, HIGH = 2, FULL = 3,
                    // PRE_SELECT_RACE_FULL = 9, CHECK = 10
                    s.write_u8(0);
                }
            }
        }

        s.write_u8(self.characters.len() as u8);
        for c in &self.characters {
            s.write_u32(c.account_id);
            s.write_u8(c.gs_id);
            s.write_u32(c.id);
            s.write_str(&c.name);
            s.write_u8(c.race);
            s.write_u8(c.gender);
            s.write_bytes_sized(&[0u8; 16]); // guid
            s.write_i64(0); // v
        }
        Ok(())
    }
}

/// `ACWorldQueuePacket`.
#[derive(Clone, Copy, Debug, Default)]
pub struct AcWorldQueue;

impl OutgoingPacket for AcWorldQueue {
    const OPCODE: u16 = opcodes::AC_WORLD_QUEUE;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u8(0); // diw -> world id
        s.write_bool(false); // isPremium
        s.write_u16(0); // myTurn
        s.write_u16(0); // normalLength
        s.write_u16(0); // premiumLength
        Ok(())
    }
}

/// `ACAccountWarnedPacket`.
#[derive(Clone, Copy, Debug, Default)]
pub struct AcAccountWarned;

impl OutgoingPacket for AcAccountWarned {
    const OPCODE: u16 = opcodes::AC_ACCOUNT_WARNED;
    fn write_body(&self, s: &mut PacketStream) -> Result<(), PacketError> {
        s.write_u8(0); // source
        s.write_str(""); // msg
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    //! Expected bytes were produced by running the C# `LoginPacket.Encode()` of each packet with
    //! the same inputs (full frame, including the u16 length).
    use super::*;
    use crate::packets::hex_bytes;

    fn frame<P: OutgoingPacket>(p: &P) -> String {
        hex::encode(p.encode_frame().unwrap())
    }

    #[test]
    fn auth_response() {
        let p = AcAuthResponse {
            account_id: AccountId(0x0102_0304),
            slot_count: 6,
        };
        assert_eq!(
            frame(&p),
            "29000300040302012000000000000000000000000000000000000000000000000000000000000000000006"
        );
    }

    #[test]
    fn challenge() {
        let p = AcChallenge {
            salt: 0xAABB_CCDD,
            hc: vec![1, 2, 3],
        };
        assert_eq!(
            frame(&p),
            "16000200ddccbbaa01000000020000000300000000000000"
        );
    }

    #[test]
    fn challenge2() {
        let p = AcChallenge2 {
            round: 100_000,
            salt: "abcdEFGH12345678".into(),
            hc: (1..=8).collect(),
        };
        assert_eq!(
            frame(&p),
            "38000400a08601001000616263644546474831323334353637380100000002000000030000000400000005000000060000000700000008000000"
        );
    }

    #[test]
    fn otp_cert_denied() {
        assert_eq!(
            frame(&AcEnterOtp::failure(3, 2)),
            "0a0005000300000002000000"
        );
        assert_eq!(
            frame(&AcEnterPcCert::failure(3, 2)),
            "0a0007000300000002000000"
        );
        assert_eq!(frame(&AcEnterWorldDenied { reason: 7 }), "03000b0007");
    }

    #[test]
    fn join_response() {
        let afs = AfsValue {
            max_characters_per_account: 6,
            additional_characters_per_server: 2,
            is_pre_select_character_period: true,
        };
        assert_eq!(
            frame(&AcJoinResponse::new(JoinResponseReason::Success, afs)),
            "0c00000000000601000002000000"
        );
        // What LoginClient.SendAuthSuccessAsync sends.
        let default_afs = AfsValue {
            max_characters_per_account: 6,
            additional_characters_per_server: 0,
            is_pre_select_character_period: false,
        };
        assert_eq!(
            frame(&AcJoinResponse::new(
                JoinResponseReason::Success,
                default_afs
            )),
            "0c00000000000600000000000000"
        );
        assert_eq!(AfsValue::from_u64(afs.to_u64()), afs);
    }

    #[test]
    fn login_denied() {
        assert_eq!(
            frame(&AcLoginDenied::new(LoginDeniedReason::BAD_ACCOUNT)),
            "07000c000100000000"
        );
        let p = AcLoginDenied {
            reason: LoginDeniedReason::TRY_TRADE_CASH_TEMPORAL,
            vp: vec![7, -1],
        };
        assert_eq!(frame(&p), "11000c00050a00000700000000ffffffff0000");
    }

    #[test]
    fn show_ars_queue_warned() {
        let p = AcShowArs {
            number: "1234".into(),
            timeout: Duration::from_secs(180),
        };
        assert_eq!(frame(&p), "0c00060004003132333420bf0200");
        assert_eq!(frame(&AcWorldQueue), "0a0009000000000000000000");
        assert_eq!(frame(&AcAccountWarned), "05000d00000000");
    }

    #[test]
    fn world_cookie() {
        let p = AcWorldCookie::new(77, "10.0.0.5".parse().unwrap(), false, "127.0.0.1", 1239);
        assert_eq!(
            frame(&p),
            "1e000a004d0000000100007fd7040100007fd7040100007fd7040100007fd704"
        );
        let p = AcWorldCookie::new(78, "192.168.1.9".parse().unwrap(), true, "127.0.0.1", 1239);
        assert_eq!(
            frame(&p),
            "1e000a004e0000000901a8c0d7040901a8c0d7040901a8c0d7040901a8c0d704"
        );
        let bad = AcWorldCookie::new(1, "10.0.0.5".parse().unwrap(), false, "not-an-ip", 1);
        assert!(bad.encode_frame().is_err());
    }

    #[test]
    fn world_list() {
        let p = AcWorldList {
            worlds: vec![
                WorldInfo {
                    id: GameServerId(1),
                    name: "AAEmu".into(),
                    active: false,
                    load: GsLoad::LOW,
                },
                WorldInfo {
                    id: GameServerId(2),
                    name: "Tést".into(),
                    active: true,
                    load: GsLoad::HIGH,
                },
            ],
            characters: vec![
                LoginCharacterInfo {
                    id: 10,
                    account_id: 20,
                    gs_id: 2,
                    name: "Bob".into(),
                    race: 1,
                    gender: 2,
                },
                LoginCharacterInfo {
                    id: 0xAABB_CCDD,
                    account_id: 7,
                    gs_id: 1,
                    name: "Ünï".into(),
                    race: 4,
                    gender: 1,
                },
            ],
        };
        assert_eq!(
            p.encode_frame().unwrap(),
            hex_bytes(concat!(
                "76000800020105004141456d750002050054c3a97374010200000000000000000002",
                "14000000020a0000000300426f6201021000000000000000000000000000000000000000000000000000",
                "0700000001ddccbbaa0500c39c6ec3af0401100000000000000000000000000000000000000000000000",
                "0000"
            ))
        );
        let empty = AcWorldList {
            worlds: vec![],
            characters: vec![],
        };
        assert_eq!(frame(&empty), "040008000000");
    }
}
