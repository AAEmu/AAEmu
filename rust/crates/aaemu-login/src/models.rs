//! Port of `AAEmu.Login.Models` (plus `AAEmu.Commons.Models.LoginCharacterInfo`).

use std::fmt;

use aaemu_commons::{Marshal, PacketStream};

use crate::lenient::LenientRead;

/// `AccountId` (`users.id`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct AccountId(pub u32);

/// Identifier of a client connection to the login server. Doubles as the world cookie.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct ConnectionId(pub u32);

/// Identifier of a game server (world).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub struct GameServerId(pub u8);

impl fmt::Display for AccountId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.0)
    }
}

impl fmt::Display for ConnectionId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.0)
    }
}

impl fmt::Display for GameServerId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.0)
    }
}

/// `GSLoad`. Stored as the raw byte because the game server may send any value
/// (the C# code casts the byte straight into the enum).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub struct GsLoad(pub u8);

impl GsLoad {
    pub const LOW: Self = Self(0);
    pub const MEDIUM: Self = Self(1);
    pub const HIGH: Self = Self(2);
}

/// `GSRegisterResult`: the result of a request to register a game server.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
#[repr(u8)]
pub enum GsRegisterResult {
    Success = 0,
    Error = 1,
}

/// `LoginDeniedReason`. A newtype rather than an enum because ban reasons are read from the
/// database (`users.ban_reason`) and cast to a byte without validation.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub struct LoginDeniedReason(pub u8);

impl LoginDeniedReason {
    /// "Authentication Failed". Generic auth failure.
    pub const LOGIN_UNKNOWN: Self = Self(0);
    /// Username or password wrong.
    pub const BAD_ACCOUNT: Self = Self(1);
    /// Username or password wrong.
    pub const BAD_RESPONSE: Self = Self(2);
    /// Account already logged in.
    pub const DUPLICATE_LOGIN: Self = Self(3);
    /// The game is unavailable right now (e.g. under service/maintenance).
    pub const SERVICE_TIME: Self = Self(4);
    /// Account is suspended for $$ days due to suspicious transactions.
    pub const TRY_TRADE_CASH_TEMPORAL: Self = Self(5);
    // 6 = "try_trade_cash_forever";   7 = "traded_cash_temporal";   8 = "traded_cash_forever";
    // 9 = "try_trade_item_servers";   10 = "traded_item_servers";   11 = "traded_account";
    // 12 = "try_cheat_temporal";      13 = "try_cheat_forever";     14 = "cheated";
    // 15 = "gamble_temporal";         16 = "gamble_forever";        17 = "abuse_bug_forever";
    // 18 = "abuse_bug_temporal";      19 = "use_bot_forever";       20 = "use_bot_temporal";
    // 21 = "use_bad_sw_temporal";     22 = "use_bad_sw_forever";    23 = "bad_user_workplace";
    // 24 = "bad_user_proxy_ip";       25 = "steal_info";            26 = "foul_lang_temporal";
    // 27 = "foul_lang_forever";       28 = "bad_game_name";         29 = "disturb_play";
    // 30 = "abnormal_play";           31 = "disturb_gm";            32 = "fraudful_report";
    // 33 = "fake_gm";                 34 = "wait_cert";             35 = "steal_account_temporal";
    // 36 = "steal_account_forever";   37 = "fraudful_steal_report"; 38 = "steal_person";
    // 39 = "request_by_self";         40 = "request_by_parent";     41 = "ads";
    // 42 = "request_by_authority";    43 = "defraud_pay";           44 = "unpaid_account";
    // 45 = "bulk_blocked_account";    46 = "unpaid_pcbang";         47 = "congested_server";
    // 48 = "invalid_mac";
}

/// `AAEmu.Commons.Models.LoginCharacterInfo`, exchanged with the game server in
/// `GLRequestInfoPacket`. `gs_id` is not part of the wire format.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct LoginCharacterInfo {
    pub id: u32,
    pub account_id: u32,
    pub gs_id: u8,
    pub name: String,
    pub race: u8,
    pub gender: u8,
}

impl LoginCharacterInfo {
    /// Reads with the C# `PacketStream` semantics (out-of-bounds reads yield defaults).
    pub fn read_lenient(s: &mut PacketStream) -> Self {
        Self {
            id: s.l_u32(),
            account_id: s.l_u32(),
            gs_id: 0,
            name: s.l_string(),
            race: s.l_u8(),
            gender: s.l_u8(),
        }
    }
}

impl Marshal for LoginCharacterInfo {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id)
            .write_u32(self.account_id)
            .write_str(&self.name)
            .write_u8(self.race)
            .write_u8(self.gender);
    }
}

/// `TwoFactorMethod` flags.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
pub struct TwoFactorMethod(pub u8);

impl TwoFactorMethod {
    /// No two-factor authentication enabled.
    pub const NONE: Self = Self(0);
    /// Time-based one-time password (TOTP) via authenticator app.
    pub const OTP: Self = Self(1);
    /// PC certificate PIN verification.
    pub const PC_CERT: Self = Self(2);
    /// ARS phone callback verification.
    pub const ARS: Self = Self(4);

    pub fn contains(self, flag: Self) -> bool {
        self.0 & flag.0 == flag.0
    }
}

/// Complete two-factor authentication configuration for an account (`user_2fa` row).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct TwoFactorConfig {
    pub user_id: AccountId,
    pub enabled_methods: TwoFactorMethod,
    /// Base32-encoded TOTP secret.
    pub otp_secret: Option<String>,
    pub otp_verified: bool,
    /// Hashed PIN for the PC certificate.
    pub cert_pin_hash: Option<String>,
    pub ars_phone_number: Option<String>,
    pub ars_phone_verified: bool,
    pub created_at: i64,
    pub updated_at: i64,
}

/// Which 2FA steps are required for authentication.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct TwoFactorRequirements {
    pub requires_otp: bool,
    pub requires_pc_cert: bool,
    pub requires_ars: bool,
}

impl TwoFactorRequirements {
    pub const NONE: Self = Self {
        requires_otp: false,
        requires_pc_cert: false,
        requires_ars: false,
    };

    pub fn any_required(&self) -> bool {
        self.requires_otp || self.requires_pc_cert || self.requires_ars
    }
}

/// Korea challenge-response authentication material for a specific account.
#[derive(Clone)]
pub struct KoreaAuthInfo {
    pub account_id: AccountId,
    /// The 32-byte raw sha256_crypt hash used as the AES-256 key during V2 verification.
    /// Do not transmit or log this value.
    pub challenge_key_hash: [u8; 32],
    /// The salt extracted from the stored `$5$` hash string; sent in `ACChallenge2Packet`.
    pub challenge_salt: String,
    /// The iteration count extracted from the stored `$5$` hash string.
    pub challenge_rounds: i32,
}

impl fmt::Debug for KoreaAuthInfo {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("KoreaAuthInfo")
            .field("account_id", &self.account_id)
            .field("challenge_salt", &self.challenge_salt)
            .field("challenge_rounds", &self.challenge_rounds)
            .finish_non_exhaustive()
    }
}

/// `LoginResult`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct LoginResult {
    pub success: bool,
    pub account_id: AccountId,
    pub denial_reason: LoginDeniedReason,
}

impl LoginResult {
    pub fn ok(account_id: AccountId) -> Self {
        Self {
            success: true,
            account_id,
            denial_reason: LoginDeniedReason::default(),
        }
    }

    pub fn denied(reason: LoginDeniedReason) -> Self {
        Self {
            success: false,
            account_id: AccountId::default(),
            denial_reason: reason,
        }
    }
}

/// `ReconnectResult`.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ReconnectResult {
    pub success: bool,
    pub account_id: AccountId,
}
