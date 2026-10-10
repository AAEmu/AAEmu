//! Authentication flows (`AAEmu.Login.Core.Authentication`).
//!
//! Flows are pure decision-makers that return [`AuthFlowResult`] values; the login session
//! manages the lifecycle (keeping the flow on `Pending`, clearing it on `Success`/`Denied`).
//! Only [`KoreaAuthFlow`] is multi-step; the C# interfaces `IChallengeAuthFlow`,
//! `IChallenge2AuthFlow`, `IOtpAuthFlow`, `ICertAuthFlow` and `IArsAuthFlow` are its
//! [`KoreaStep`]s.

use std::net::IpAddr;
use std::time::Duration;

use rand::RngCore;

use crate::models::{AccountId, GameServerId, LoginCharacterInfo, LoginDeniedReason};
use crate::packets::l2c::WorldInfo;
use crate::packets::PacketError;
use crate::server::LoginServer;
use crate::services::korea;
use crate::services::password::Password;

/// The result of an authentication flow step.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum AuthFlowResult {
    /// Authentication completed successfully.
    Success {
        account_id: AccountId,
        username: Option<String>,
    },
    /// Authentication was denied; the reason is sent to the client.
    Denied(LoginDeniedReason),
    /// The flow is in progress and managing itself (it already sent its own prompt).
    Pending,
}

/// `ILoginClient`: sends response packets to the client, decoupling flows from packet building.
pub trait LoginClient: Send + Sync {
    fn send_auth_success(&self, account_id: AccountId) -> Result<(), PacketError>;
    fn send_auth_denied(&self, reason: LoginDeniedReason) -> Result<(), PacketError>;
    fn send_world_list(
        &self,
        worlds: Vec<WorldInfo>,
        characters: Vec<LoginCharacterInfo>,
    ) -> Result<(), PacketError>;
    /// V1 Korea challenge (`ACChallengePacket`). V1 is not enabled — SHA-1 without a per-account
    /// salt is ~17Mx weaker than PBKDF2@600k. Preserved for protocol completeness only.
    fn send_challenge(&self, salt: u32, hc: &[u32]) -> Result<(), PacketError>;
    /// V2 Korea challenge (`ACChallenge2Packet`). The client derives the AES-256 key from `round`
    /// and `salt` via sha256_crypt, then computes the 8-uint challenge response.
    fn send_challenge2(&self, round: i32, salt: &str, hc: &[u32]) -> Result<(), PacketError>;
    fn send_otp_prompt(&self, maximum_tries: i32, current_try: i32) -> Result<(), PacketError>;
    fn send_cert_prompt(&self, maximum_tries: i32, current_try: i32) -> Result<(), PacketError>;
    fn send_ars_prompt(&self, session_code: &str, timeout: Duration) -> Result<(), PacketError>;
    fn send_world_cookie(&self, host: &str, port: u16) -> Result<(), PacketError>;
    fn send_enter_world_denied(&self, reason: u8) -> Result<(), PacketError>;
}

/// Username/password authentication (EU method); single step.
#[derive(Clone, Debug)]
pub struct PasswordAuthFlow {
    /// `None` reproduces the C# behaviour of a Trion ticket without username/password, where
    /// a `null` username is passed down to the login controller.
    pub username: Option<String>,
    pub password: Option<Password>,
    pub client_ip: IpAddr,
}

/// Reconnection validation, wrapped as a flow so it goes through the normal authenticate path.
#[derive(Clone, Debug)]
pub struct ReconnectAuthFlow {
    pub gs_id: GameServerId,
    pub account_id: AccountId,
    pub token: u32,
}

/// Any authentication flow.
#[derive(Debug)]
pub enum AuthFlow {
    Password(PasswordAuthFlow),
    Reconnect(ReconnectAuthFlow),
    Korea(Box<KoreaAuthFlow>),
}

impl AuthFlow {
    /// The C# class name, for logging.
    pub fn name(&self) -> &'static str {
        match self {
            Self::Password(_) => "PasswordAuthFlow",
            Self::Reconnect(_) => "ReconnectAuthFlow",
            Self::Korea(_) => "KoreaAuthFlow",
        }
    }

    /// `IAuthenticationFlow.StartAsync`. An `Err` corresponds to an exception escaping the C#
    /// flow (e.g. a database error).
    pub async fn start(
        &mut self,
        server: &LoginServer,
        client: &dyn LoginClient,
    ) -> anyhow::Result<AuthFlowResult> {
        match self {
            Self::Password(f) => {
                let (Some(username), Some(password)) = (&f.username, &f.password) else {
                    // C#: `SELECT ... username=NULL` finds nothing; then either BadAccount or,
                    // with AutoAccount, `UsernameRegex().IsMatch(null)` throws.
                    if server.config.auto_account {
                        anyhow::bail!("Value cannot be null. (Parameter 'input'): Trion ticket without username");
                    }
                    return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_ACCOUNT));
                };
                let r = server.auth.login(username, password, f.client_ip).await?;
                Ok(if r.success {
                    AuthFlowResult::Success {
                        account_id: r.account_id,
                        username: Some(username.clone()),
                    }
                } else {
                    AuthFlowResult::Denied(r.denial_reason)
                })
            }
            Self::Reconnect(f) => {
                let r = server
                    .logins
                    .reconnect(&server.games, f.gs_id, f.account_id, f.token)?;
                Ok(if r.success {
                    AuthFlowResult::Success {
                        account_id: r.account_id,
                        username: None,
                    }
                } else {
                    AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE)
                })
            }
            Self::Korea(f) => f.start(server, client).await,
        }
    }
}

/// A continuation of the Korea flow, triggered by a client packet (or the ARS callback).
#[derive(Clone, Debug)]
pub enum KoreaStep {
    /// `IChallengeAuthFlow.ContinueAsync` (`CAChallengeResponsePacket`).
    ChallengeV1 { ch: [u32; 4], pw: Vec<u8> },
    /// `IChallenge2AuthFlow.ContinueV2Async` (`CAChallengeResponse2Packet`).
    ChallengeV2 { ch: [u32; 8] },
    /// `IOtpAuthFlow.SubmitOtpAsync` (`CAOtpNumberPacket`).
    Otp(String),
    /// `ICertAuthFlow.SubmitCertAsync` (`CAPcCertNumberPacket`).
    Cert(String),
    /// `IArsAuthFlow.CompleteArsAsync` (external phone system callback).
    Ars(bool),
}

impl KoreaStep {
    /// The C# flow interface this step belongs to (for the type-mismatch log).
    pub fn interface_name(&self) -> &'static str {
        match self {
            Self::ChallengeV1 { .. } => "IChallengeAuthFlow",
            Self::ChallengeV2 { .. } => "IChallenge2AuthFlow",
            Self::Otp(_) => "IOtpAuthFlow",
            Self::Cert(_) => "ICertAuthFlow",
            Self::Ars(_) => "IArsAuthFlow",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
enum KoreaState {
    AwaitingChallengeResponse,
    AwaitingOtp,
    AwaitingCert,
    AwaitingArs,
    Completed,
}

/// Korea authentication: V2 challenge-response, then OTP, certificate and ARS verification as
/// required by the account (`user_2fa`). Each step is optional; the flow advances through each
/// required step in sequence until all verifications complete.
pub struct KoreaAuthFlow {
    username: String,
    /// Unused, as in the C# flow.
    #[allow(dead_code)]
    client_ip: IpAddr,
    state: KoreaState,
    account_id: AccountId,
    // V2 challenge state (cleared after successful verification)
    v2_hc: Vec<u32>,
    challenge_key: Option<[u8; 32]>,
    requires_otp: bool,
    requires_cert: bool,
    requires_ars: bool,
    otp_attempts: i32,
    cert_attempts: i32,
}

impl std::fmt::Debug for KoreaAuthFlow {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("KoreaAuthFlow")
            .field("username", &self.username)
            .field("state", &self.state)
            .field("account_id", &self.account_id)
            .finish_non_exhaustive()
    }
}

impl KoreaAuthFlow {
    pub fn new(username: impl Into<String>, client_ip: IpAddr) -> Self {
        Self {
            username: username.into(),
            client_ip,
            state: KoreaState::AwaitingChallengeResponse,
            account_id: AccountId::default(),
            v2_hc: Vec::new(),
            challenge_key: None,
            requires_otp: false,
            requires_cert: false,
            requires_ars: false,
            otp_attempts: 0,
            cert_attempts: 0,
        }
    }

    async fn start(
        &mut self,
        server: &LoginServer,
        client: &dyn LoginClient,
    ) -> anyhow::Result<AuthFlowResult> {
        if !server.config.korea_auth.enabled {
            return Ok(AuthFlowResult::Denied(LoginDeniedReason::LOGIN_UNKNOWN));
        }
        // Look up the account's Korea challenge material.
        // No ban check here — doing so before authentication would leak whether an account exists.
        let Some(info) = server.auth.get_korea_auth_info(&self.username).await? else {
            // Account doesn't exist, or has no korea_challenge_hash yet. The user must log in via
            // EU auth first (with KoreaAuth:Enabled=true) to seed the hash.
            return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_ACCOUNT));
        };
        self.account_id = info.account_id;
        self.challenge_key = Some(info.challenge_key_hash);

        // Generate 8 cryptographically secure random uint32 challenge values
        let mut rng_bytes = [0u8; 32];
        rand::rngs::OsRng.fill_bytes(&mut rng_bytes);
        self.v2_hc = rng_bytes
            .as_chunks::<4>()
            .0
            .iter()
            .map(|c| u32::from_le_bytes(*c))
            .collect();

        client.send_challenge2(info.challenge_rounds, &info.challenge_salt, &self.v2_hc)?;
        Ok(AuthFlowResult::Pending)
    }

    /// Executes one continuation step.
    pub async fn step(
        &mut self,
        server: &LoginServer,
        client: &dyn LoginClient,
        step: KoreaStep,
    ) -> anyhow::Result<AuthFlowResult> {
        match step {
            // V1 Korea challenge is disabled (SHA-1 without per-account salt is ~17Mx weaker than
            // PBKDF2@600k). The server never issues ACChallengePacket, so always deny.
            KoreaStep::ChallengeV1 { .. } => {
                Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE))
            }
            KoreaStep::ChallengeV2 { ch } => self.continue_v2(server, client, &ch).await,
            KoreaStep::Otp(code) => self.submit_otp(server, client, &code).await,
            KoreaStep::Cert(pin) => self.submit_cert(server, client, &pin).await,
            KoreaStep::Ars(success) => self.complete_ars(server, client, success).await,
        }
    }

    async fn continue_v2(
        &mut self,
        server: &LoginServer,
        client: &dyn LoginClient,
        ch: &[u32],
    ) -> anyhow::Result<AuthFlowResult> {
        if self.state != KoreaState::AwaitingChallengeResponse {
            return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE));
        }
        let key = self.challenge_key.unwrap_or_default();
        let key_slice: &[u8] = if self.challenge_key.is_some() {
            &key
        } else {
            &[]
        };
        if !korea::verify_v2(key_slice, &self.v2_hc, ch) {
            return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_ACCOUNT));
        }
        // Clear key material now that verification is done
        self.challenge_key = None;
        self.v2_hc.clear();

        // Query 2FA requirements from the database
        let requirements = server
            .auth
            .get_two_factor_requirements(self.account_id)
            .await?;
        self.requires_otp = requirements.requires_otp;
        self.requires_cert = requirements.requires_pc_cert;
        self.requires_ars = requirements.requires_ars;
        self.advance_to_next_step(server, client).await
    }

    async fn submit_otp(
        &mut self,
        server: &LoginServer,
        client: &dyn LoginClient,
        code: &str,
    ) -> anyhow::Result<AuthFlowResult> {
        if self.state != KoreaState::AwaitingOtp {
            return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE));
        }
        self.otp_attempts += 1;
        if !server.auth.validate_otp(self.account_id, code).await? {
            let max = server.config.korea_auth.max_otp_attempts;
            if self.otp_attempts >= max {
                return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE));
            }
            // Send retry packet
            client.send_otp_prompt(max, self.otp_attempts + 1)?;
            return Ok(AuthFlowResult::Pending);
        }
        self.advance_to_next_step(server, client).await
    }

    async fn submit_cert(
        &mut self,
        server: &LoginServer,
        client: &dyn LoginClient,
        pin: &str,
    ) -> anyhow::Result<AuthFlowResult> {
        if self.state != KoreaState::AwaitingCert {
            return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE));
        }
        self.cert_attempts += 1;
        if !server.auth.validate_pc_cert(self.account_id, pin).await? {
            let max = server.config.korea_auth.max_cert_attempts;
            if self.cert_attempts >= max {
                return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE));
            }
            // Send retry packet
            client.send_cert_prompt(max, self.cert_attempts + 1)?;
            return Ok(AuthFlowResult::Pending);
        }
        self.advance_to_next_step(server, client).await
    }

    async fn complete_ars(
        &mut self,
        server: &LoginServer,
        client: &dyn LoginClient,
        success: bool,
    ) -> anyhow::Result<AuthFlowResult> {
        if self.state != KoreaState::AwaitingArs || !success {
            return Ok(AuthFlowResult::Denied(LoginDeniedReason::BAD_RESPONSE));
        }
        self.advance_to_next_step(server, client).await
    }

    /// Advances to the next required verification step, or completes authentication. The ban
    /// check happens immediately before the final Success to avoid leaking account status
    /// prior to successful authentication.
    async fn advance_to_next_step(
        &mut self,
        server: &LoginServer,
        client: &dyn LoginClient,
    ) -> anyhow::Result<AuthFlowResult> {
        if self.state <= KoreaState::AwaitingChallengeResponse && self.requires_otp {
            self.state = KoreaState::AwaitingOtp;
            client.send_otp_prompt(0, 0)?;
            return Ok(AuthFlowResult::Pending);
        }
        if self.state <= KoreaState::AwaitingOtp && self.requires_cert {
            self.state = KoreaState::AwaitingCert;
            client.send_cert_prompt(0, 0)?;
            return Ok(AuthFlowResult::Pending);
        }
        if self.state <= KoreaState::AwaitingCert && self.requires_ars {
            self.state = KoreaState::AwaitingArs;
            let timeout = server.config.korea_auth.ars_timeout;
            let session = server
                .auth
                .create_ars_session(self.account_id, timeout.as_secs() as i32);
            client.send_ars_prompt(&session.session_code, timeout)?;
            return Ok(AuthFlowResult::Pending);
        }
        // All steps complete. Check ban status before issuing success
        self.state = KoreaState::Completed;
        let (banned, reason) = server.auth.check_ban_status(self.account_id).await?;
        if banned {
            return Ok(AuthFlowResult::Denied(reason));
        }
        Ok(AuthFlowResult::Success {
            account_id: self.account_id,
            username: Some(self.username.clone()),
        })
    }
}

#[cfg(test)]
mod tests;
