//! Shared test doubles: the counterparts of the C# unit tests' mocked `ILoginController`,
//! `ITwoFactorService`, `IOtpService`, `IPcCertService`, `IArsService` ([`MockBackend`]) and
//! `ILoginClient` ([`RecordingClient`]), plus helpers to build a [`LoginServer`] without a
//! database and to inspect the packets a connection sent.

use std::net::IpAddr;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use futures::future::BoxFuture;
use futures::FutureExt;
use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
use tokio::sync::mpsc;

use crate::auth::LoginClient;
use crate::backend::AuthBackend;
use crate::config::{lowercase_keys, AppConfig};
use crate::id_manager::{ConnectionIdLease, SimpleIdManager};
use crate::models::{
    AccountId, KoreaAuthInfo, LoginCharacterInfo, LoginDeniedReason, LoginResult,
    TwoFactorRequirements,
};
use crate::network::login::LoginConnection;
use crate::packets::l2c::WorldInfo;
use crate::packets::PacketError;
use crate::server::LoginServer;
use crate::services::password::Password;
use crate::services::two_factor::{ArsService, ArsSession};

/// What [`MockBackend::login`] returns.
pub(crate) enum LoginBehavior {
    Result(LoginResult),
    /// An exception escaping `ILoginController.Login`.
    Error,
}

/// A scripted [`AuthBackend`] that records its calls. Defaults mirror the C# test setups:
/// login denied with `BadAccount`, no Korea material, not banned, no 2FA requirements.
pub(crate) struct MockBackend {
    pub login: Mutex<LoginBehavior>,
    pub login_calls: Mutex<Vec<(String, Password, IpAddr)>>,
    pub korea_info: Mutex<Option<KoreaAuthInfo>>,
    pub korea_calls: Mutex<Vec<String>>,
    pub ban: Mutex<(bool, LoginDeniedReason)>,
    pub requirements: Mutex<TwoFactorRequirements>,
    pub otp_valid: AtomicBool,
    pub cert_valid: AtomicBool,
    pub ars: ArsService,
}

impl Default for MockBackend {
    fn default() -> Self {
        Self {
            login: Mutex::new(LoginBehavior::Result(LoginResult::denied(
                LoginDeniedReason::BAD_ACCOUNT,
            ))),
            login_calls: Mutex::default(),
            korea_info: Mutex::default(),
            korea_calls: Mutex::default(),
            ban: Mutex::default(),
            requirements: Mutex::default(),
            otp_valid: AtomicBool::new(false),
            cert_valid: AtomicBool::new(false),
            ars: ArsService::default(),
        }
    }
}

impl MockBackend {
    pub fn set_login(&self, behavior: LoginBehavior) {
        *self.login.lock().unwrap() = behavior;
    }

    pub fn set_korea_info(&self, info: Option<KoreaAuthInfo>) {
        *self.korea_info.lock().unwrap() = info;
    }

    pub fn set_ban(&self, banned: bool, reason: LoginDeniedReason) {
        *self.ban.lock().unwrap() = (banned, reason);
    }
}

impl AuthBackend for MockBackend {
    fn login<'a>(
        &'a self,
        username: &'a str,
        password: &'a Password,
        ip: IpAddr,
    ) -> BoxFuture<'a, anyhow::Result<LoginResult>> {
        self.login_calls
            .lock()
            .unwrap()
            .push((username.to_string(), password.clone(), ip));
        let r = match &*self.login.lock().unwrap() {
            LoginBehavior::Result(r) => Ok(*r),
            LoginBehavior::Error => Err(anyhow::anyhow!("simulated database failure")),
        };
        async move { r }.boxed()
    }

    fn get_korea_auth_info<'a>(
        &'a self,
        username: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<Option<KoreaAuthInfo>>> {
        self.korea_calls.lock().unwrap().push(username.to_string());
        let info = self.korea_info.lock().unwrap().clone();
        async move { Ok(info) }.boxed()
    }

    fn check_ban_status(
        &self,
        _account_id: AccountId,
    ) -> BoxFuture<'_, anyhow::Result<(bool, LoginDeniedReason)>> {
        let ban = *self.ban.lock().unwrap();
        async move { Ok(ban) }.boxed()
    }

    fn get_two_factor_requirements(
        &self,
        _account_id: AccountId,
    ) -> BoxFuture<'_, anyhow::Result<TwoFactorRequirements>> {
        let r = *self.requirements.lock().unwrap();
        async move { Ok(r) }.boxed()
    }

    fn validate_otp<'a>(
        &'a self,
        _account_id: AccountId,
        _code: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<bool>> {
        let v = self.otp_valid.load(Ordering::SeqCst);
        async move { Ok(v) }.boxed()
    }

    fn validate_pc_cert<'a>(
        &'a self,
        _account_id: AccountId,
        _pin: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<bool>> {
        let v = self.cert_valid.load(Ordering::SeqCst);
        async move { Ok(v) }.boxed()
    }

    fn create_ars_session(&self, account_id: AccountId, timeout_seconds: i32) -> ArsSession {
        self.ars.create_session(account_id, timeout_seconds)
    }
}

/// A call made on [`RecordingClient`].
#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) enum ClientCall {
    AuthSuccess(AccountId),
    AuthDenied(LoginDeniedReason),
    WorldList,
    Challenge,
    Challenge2 {
        round: i32,
        salt: String,
        hc: Vec<u32>,
    },
    OtpPrompt(i32, i32),
    CertPrompt(i32, i32),
    ArsPrompt(String, Duration),
    WorldCookie(String, u16),
    EnterWorldDenied(u8),
}

/// A [`LoginClient`] that records every call (the C# `Mock<ILoginClient>`).
#[derive(Default)]
pub(crate) struct RecordingClient {
    pub calls: Mutex<Vec<ClientCall>>,
}

impl RecordingClient {
    pub fn calls(&self) -> Vec<ClientCall> {
        self.calls.lock().unwrap().clone()
    }

    fn push(&self, c: ClientCall) -> Result<(), PacketError> {
        self.calls.lock().unwrap().push(c);
        Ok(())
    }

    /// The `hc` values of the last `SendChallenge2Async` call.
    pub fn captured_hc(&self) -> Vec<u32> {
        self.calls()
            .into_iter()
            .rev()
            .find_map(|c| match c {
                ClientCall::Challenge2 { hc, .. } => Some(hc),
                _ => None,
            })
            .expect("SendChallenge2Async was called")
    }
}

impl LoginClient for RecordingClient {
    fn send_auth_success(&self, account_id: AccountId) -> Result<(), PacketError> {
        self.push(ClientCall::AuthSuccess(account_id))
    }

    fn send_auth_denied(&self, reason: LoginDeniedReason) -> Result<(), PacketError> {
        self.push(ClientCall::AuthDenied(reason))
    }

    fn send_world_list(
        &self,
        _worlds: Vec<WorldInfo>,
        _characters: Vec<LoginCharacterInfo>,
    ) -> Result<(), PacketError> {
        self.push(ClientCall::WorldList)
    }

    fn send_challenge(&self, _salt: u32, _hc: &[u32]) -> Result<(), PacketError> {
        self.push(ClientCall::Challenge)
    }

    fn send_challenge2(&self, round: i32, salt: &str, hc: &[u32]) -> Result<(), PacketError> {
        self.push(ClientCall::Challenge2 {
            round,
            salt: salt.to_string(),
            hc: hc.to_vec(),
        })
    }

    fn send_otp_prompt(&self, maximum_tries: i32, current_try: i32) -> Result<(), PacketError> {
        self.push(ClientCall::OtpPrompt(maximum_tries, current_try))
    }

    fn send_cert_prompt(&self, maximum_tries: i32, current_try: i32) -> Result<(), PacketError> {
        self.push(ClientCall::CertPrompt(maximum_tries, current_try))
    }

    fn send_ars_prompt(&self, session_code: &str, timeout: Duration) -> Result<(), PacketError> {
        self.push(ClientCall::ArsPrompt(session_code.to_string(), timeout))
    }

    fn send_world_cookie(&self, host: &str, port: u16) -> Result<(), PacketError> {
        self.push(ClientCall::WorldCookie(host.to_string(), port))
    }

    fn send_enter_world_denied(&self, reason: u8) -> Result<(), PacketError> {
        self.push(ClientCall::EnterWorldDenied(reason))
    }
}

/// An [`AppConfig`] like the C# tests' `new AppConfiguration { SecretKey = "test", ... }`.
/// `overrides` (C#-style keys) are merged over the defaults.
pub(crate) fn test_config(overrides: serde_json::Value) -> AppConfig {
    let mut root = serde_json::json!({
        "SecretKey": "test",
        "SkipHostResolve": true,
        "InternalNetwork": { "Host": "127.0.0.1", "Port": 0 },
        "Network": { "Host": "127.0.0.1", "Port": 0 },
        "Connections": { "MySQLProvider": { "Host": "127.0.0.1", "Port": 1, "User": "x", "Password": "x", "Database": "x" } },
        "GameServers": []
    });
    crate::config::merge(&mut root, overrides);
    AppConfig::from_value(lowercase_keys(root)).expect("valid test config")
}

/// A [`LoginServer`] whose flows use `backend`. The MySQL pool is lazy and never connects.
/// Must be called inside a Tokio runtime (the pool spawns its maintenance task).
pub(crate) fn test_server(config: AppConfig, backend: Arc<MockBackend>) -> Arc<LoginServer> {
    let pool = MySqlPoolOptions::new().connect_lazy_with(MySqlConnectOptions::new());
    let server = LoginServer::with_auth_backend(config, pool, backend);
    server.requests.initialize();
    server
}

/// A [`LoginConnection`] whose outgoing packets can be read from the returned receiver.
pub(crate) fn test_connection(ip: IpAddr) -> (Arc<LoginConnection>, PacketLog) {
    let ids = Arc::new(SimpleIdManager::new());
    let (tx, rx) = mpsc::unbounded_channel();
    let lease = ConnectionIdLease::rent(&ids).unwrap();
    (
        Arc::new(LoginConnection::new(lease, ip, false, tx)),
        PacketLog { rx },
    )
}

/// The packets a test connection sent (`_sentPackets` in the C# tests).
pub(crate) struct PacketLog {
    pub rx: mpsc::UnboundedReceiver<Vec<u8>>,
}

impl PacketLog {
    /// Drains the queued packets and returns their `[opcode][body]` payloads.
    pub fn drain(&mut self) -> Vec<Vec<u8>> {
        let mut out = Vec::new();
        while let Ok(p) = self.rx.try_recv() {
            out.push(p);
        }
        out
    }

    /// Drains the queued packets and returns their opcodes.
    pub fn opcodes(&mut self) -> Vec<u16> {
        self.drain().iter().map(|p| opcode_of(p)).collect()
    }
}

pub(crate) fn opcode_of(payload: &[u8]) -> u16 {
    u16::from_le_bytes([payload[0], payload[1]])
}
