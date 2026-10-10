//! The account/2FA operations the authentication flows depend on — the Rust counterpart of the
//! C# `ILoginController`, `ITwoFactorService`, `IOtpService`, `IPcCertService` and `IArsService`
//! seams that the flows receive through DI.
//!
//! [`DbAuthBackend`] is the production implementation (MySQL-backed); tests substitute their own
//! implementation via [`crate::server::LoginServer::with_auth_backend`].

use std::net::IpAddr;
use std::sync::Arc;

use futures::future::BoxFuture;
use futures::FutureExt;

use crate::controllers::login::LoginController;
use crate::models::{
    AccountId, KoreaAuthInfo, LoginDeniedReason, LoginResult, TwoFactorRequirements,
};
use crate::services::password::Password;
use crate::services::two_factor::{
    ArsService, ArsSession, OtpService, PcCertService, TwoFactorService,
};

/// Account and two-factor operations used by the authentication flows. Dyn-compatible (futures
/// are boxed) so the server can hold an `Arc<dyn AuthBackend>`.
pub trait AuthBackend: Send + Sync {
    /// `ILoginController.Login` (EU method auth).
    fn login<'a>(
        &'a self,
        username: &'a str,
        password: &'a Password,
        ip: IpAddr,
    ) -> BoxFuture<'a, anyhow::Result<LoginResult>>;

    /// `ILoginController.GetKoreaAuthInfoAsync`.
    fn get_korea_auth_info<'a>(
        &'a self,
        username: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<Option<KoreaAuthInfo>>>;

    /// `ILoginController.CheckBanStatusAsync`.
    fn check_ban_status(
        &self,
        account_id: AccountId,
    ) -> BoxFuture<'_, anyhow::Result<(bool, LoginDeniedReason)>>;

    /// `ITwoFactorService.GetRequirementsAsync`.
    fn get_two_factor_requirements(
        &self,
        account_id: AccountId,
    ) -> BoxFuture<'_, anyhow::Result<TwoFactorRequirements>>;

    /// `IOtpService.ValidateAsync`.
    fn validate_otp<'a>(
        &'a self,
        account_id: AccountId,
        code: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<bool>>;

    /// `IPcCertService.ValidateAsync`.
    fn validate_pc_cert<'a>(
        &'a self,
        account_id: AccountId,
        pin: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<bool>>;

    /// `IArsService.CreateSessionAsync`.
    fn create_ars_session(&self, account_id: AccountId, timeout_seconds: i32) -> ArsSession;
}

/// The production [`AuthBackend`]: the MySQL-backed controllers and services.
pub struct DbAuthBackend {
    pub logins: Arc<LoginController>,
    pub two_factor: TwoFactorService,
    pub otp: OtpService,
    pub pc_cert: PcCertService,
    pub ars: Arc<ArsService>,
}

impl AuthBackend for DbAuthBackend {
    fn login<'a>(
        &'a self,
        username: &'a str,
        password: &'a Password,
        ip: IpAddr,
    ) -> BoxFuture<'a, anyhow::Result<LoginResult>> {
        self.logins.login(username, password, ip).boxed()
    }

    fn get_korea_auth_info<'a>(
        &'a self,
        username: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<Option<KoreaAuthInfo>>> {
        self.logins.get_korea_auth_info(username).boxed()
    }

    fn check_ban_status(
        &self,
        account_id: AccountId,
    ) -> BoxFuture<'_, anyhow::Result<(bool, LoginDeniedReason)>> {
        self.logins.check_ban_status(account_id).boxed()
    }

    fn get_two_factor_requirements(
        &self,
        account_id: AccountId,
    ) -> BoxFuture<'_, anyhow::Result<TwoFactorRequirements>> {
        async move { Ok(self.two_factor.get_requirements(account_id).await?) }.boxed()
    }

    fn validate_otp<'a>(
        &'a self,
        account_id: AccountId,
        code: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<bool>> {
        async move { Ok(self.otp.validate(account_id, code).await?) }.boxed()
    }

    fn validate_pc_cert<'a>(
        &'a self,
        account_id: AccountId,
        pin: &'a str,
    ) -> BoxFuture<'a, anyhow::Result<bool>> {
        async move { Ok(self.pc_cert.validate(account_id, pin).await?) }.boxed()
    }

    fn create_ars_session(&self, account_id: AccountId, timeout_seconds: i32) -> ArsSession {
        self.ars.create_session(account_id, timeout_seconds)
    }
}
