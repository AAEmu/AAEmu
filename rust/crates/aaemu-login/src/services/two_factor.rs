//! Two-factor authentication services (`AAEmu.Login.Core.Services.TwoFactor`): repository,
//! requirements, TOTP (Otp.NET compatible), PC-cert PIN and ARS sessions.

use std::time::{Duration, SystemTime, UNIX_EPOCH};

use dashmap::DashMap;
use hmac::{Hmac, Mac};
use rand::{Rng, RngCore};
use sqlx::MySqlPool;
use subtle::ConstantTimeEq;

use super::password::{Password, PasswordService, PasswordVerificationResult};
use crate::models::{AccountId, TwoFactorConfig, TwoFactorMethod, TwoFactorRequirements};

/// Database access for two-factor authentication configuration (`user_2fa`).
#[derive(Clone, Debug)]
pub struct TwoFactorRepository {
    pool: MySqlPool,
}

impl TwoFactorRepository {
    pub fn new(pool: MySqlPool) -> Self {
        Self { pool }
    }

    /// The 2FA configuration for an account, or `None` if none exists.
    pub async fn get_config(&self, account_id: AccountId) -> sqlx::Result<Option<TwoFactorConfig>> {
        let row: Option<TwoFactorRow> = sqlx::query_as(
            "SELECT CAST(user_id AS SIGNED) AS user_id, CAST(enabled_methods AS SIGNED) AS enabled_methods, \
                    otp_secret, CAST(otp_verified AS SIGNED) AS otp_verified, cert_pin_hash, ars_phone_number, \
                    CAST(ars_phone_verified AS SIGNED) AS ars_phone_verified, \
                    CAST(created_at AS SIGNED) AS created_at, CAST(updated_at AS SIGNED) AS updated_at \
             FROM user_2fa WHERE user_id = ?",
        )
        .bind(account_id.0)
        .fetch_optional(&self.pool)
        .await?;
        Ok(row.map(|r| TwoFactorConfig {
            user_id: AccountId(r.user_id as u32),
            enabled_methods: TwoFactorMethod(r.enabled_methods as u8),
            otp_secret: r.otp_secret,
            otp_verified: r.otp_verified != 0,
            cert_pin_hash: r.cert_pin_hash,
            ars_phone_number: r.ars_phone_number,
            ars_phone_verified: r.ars_phone_verified != 0,
            created_at: r.created_at,
            updated_at: r.updated_at,
        }))
    }
}

/// A `user_2fa` row. Integer columns are CAST in the query so decoding does not depend on the
/// exact (signed/unsigned) column types of a given schema revision.
#[derive(sqlx::FromRow)]
struct TwoFactorRow {
    user_id: i64,
    enabled_methods: i64,
    otp_secret: Option<String>,
    otp_verified: i64,
    cert_pin_hash: Option<String>,
    ars_phone_number: Option<String>,
    ars_phone_verified: i64,
    created_at: i64,
    updated_at: i64,
}

/// Which 2FA methods a config requires: only methods that are both enabled AND properly
/// verified/configured.
pub fn requirements_for(config: Option<&TwoFactorConfig>) -> TwoFactorRequirements {
    let Some(config) = config else {
        return TwoFactorRequirements::NONE;
    };
    let methods = config.enabled_methods;
    let non_empty = |s: &Option<String>| s.as_deref().is_some_and(|v| !v.is_empty());
    TwoFactorRequirements {
        requires_otp: methods.contains(TwoFactorMethod::OTP)
            && config.otp_verified
            && non_empty(&config.otp_secret),
        requires_pc_cert: methods.contains(TwoFactorMethod::PC_CERT)
            && non_empty(&config.cert_pin_hash),
        requires_ars: methods.contains(TwoFactorMethod::ARS)
            && config.ars_phone_verified
            && non_empty(&config.ars_phone_number),
    }
}

/// `TwoFactorService`: entry point for querying 2FA requirements during authentication.
#[derive(Clone, Debug)]
pub struct TwoFactorService {
    repository: TwoFactorRepository,
}

impl TwoFactorService {
    pub fn new(repository: TwoFactorRepository) -> Self {
        Self { repository }
    }

    pub async fn get_requirements(
        &self,
        account_id: AccountId,
    ) -> sqlx::Result<TwoFactorRequirements> {
        Ok(requirements_for(
            self.repository.get_config(account_id).await?.as_ref(),
        ))
    }
}

/// Otp.NET-compatible Base32 (RFC 4648 alphabet, case-insensitive, `=` padding).
pub mod base32 {
    /// `Base32Encoding.ToBytes`. Trailing `=` are ignored; any other non-alphabet char is an error.
    pub fn decode(input: &str) -> Option<Vec<u8>> {
        let input = input.trim_end_matches('=');
        let mut out = Vec::with_capacity(input.len() * 5 / 8);
        let mut buffer = 0u32;
        let mut bits = 0u32;
        for c in input.chars() {
            let v = match c {
                'A'..='Z' => c as u32 - 'A' as u32,
                'a'..='z' => c as u32 - 'a' as u32,
                '2'..='7' => c as u32 - '2' as u32 + 26,
                _ => return None,
            };
            buffer = (buffer << 5) | v;
            bits += 5;
            if bits >= 8 {
                bits -= 8;
                out.push((buffer >> bits) as u8);
                buffer &= (1 << bits) - 1;
            }
        }
        Some(out)
    }

    /// `Base32Encoding.ToString`: uppercase, padded with `=` to a multiple of 8 chars.
    pub fn encode(input: &[u8]) -> String {
        const ALPHABET: &[u8; 32] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        let mut out = String::new();
        let mut buffer = 0u32;
        let mut bits = 0u32;
        for &b in input {
            buffer = (buffer << 8) | u32::from(b);
            bits += 8;
            while bits >= 5 {
                bits -= 5;
                out.push(ALPHABET[((buffer >> bits) & 31) as usize] as char);
            }
        }
        if bits > 0 {
            out.push(ALPHABET[((buffer << (5 - bits)) & 31) as usize] as char);
        }
        while !out.len().is_multiple_of(8) {
            out.push('=');
        }
        out
    }
}

/// TOTP as configured by the C# `OtpService`: `new Totp(secret, totpSize: 8)` — HMAC-SHA1,
/// 30-second step, 8 digits.
pub mod totp {
    use super::*;

    pub const STEP: u64 = 30;
    pub const DIGITS: u32 = 8;

    /// The code for a given time-step counter.
    pub fn code_for_counter(secret: &[u8], counter: u64) -> String {
        let mut mac =
            Hmac::<sha1::Sha1>::new_from_slice(secret).expect("HMAC accepts any key length");
        mac.update(&counter.to_be_bytes());
        let hash = mac.finalize().into_bytes();
        let offset = usize::from(hash[hash.len() - 1] & 0x0F);
        let binary = (u32::from(hash[offset] & 0x7F) << 24)
            | (u32::from(hash[offset + 1]) << 16)
            | (u32::from(hash[offset + 2]) << 8)
            | u32::from(hash[offset + 3]);
        let code = binary % 10u32.pow(DIGITS);
        format!("{code:0width$}", width = DIGITS as usize)
    }

    pub fn code_at(secret: &[u8], unix_seconds: u64) -> String {
        code_for_counter(secret, unix_seconds / STEP)
    }

    /// `Totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1))`.
    pub fn verify(secret: &[u8], code: &str, unix_seconds: u64) -> bool {
        let counter = unix_seconds / STEP;
        let mut ok = false;
        for c in [counter.wrapping_sub(1), counter, counter + 1] {
            let expected = code_for_counter(secret, c);
            // Otp.NET ValuesEqual: constant-time comparison of equal-length strings.
            ok |= expected.len() == code.len()
                && bool::from(expected.as_bytes().ct_eq(code.as_bytes()));
        }
        ok
    }
}

fn unix_now() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

/// `OtpService`: TOTP operations.
#[derive(Clone, Debug)]
pub struct OtpService {
    repository: TwoFactorRepository,
}

impl OtpService {
    /// 160 bits, standard for TOTP.
    const SECRET_LENGTH: usize = 20;

    pub fn new(repository: TwoFactorRepository) -> Self {
        Self { repository }
    }

    /// Validates a TOTP code for an account. Errors (missing config, bad secret) yield `false`.
    pub async fn validate(&self, account_id: AccountId, code: &str) -> sqlx::Result<bool> {
        let config = self.repository.get_config(account_id).await?;
        let Some(secret) = config.and_then(|c| c.otp_secret).filter(|s| !s.is_empty()) else {
            tracing::warn!(
                "OTP validation attempted for account {} with no OTP configured",
                account_id
            );
            return Ok(false);
        };
        let Some(secret_bytes) = base32::decode(&secret) else {
            tracing::error!(
                "Error validating OTP for account {}: invalid Base32 secret",
                account_id
            );
            return Ok(false);
        };
        // Allow for time drift by checking current and adjacent time steps
        let valid = totp::verify(&secret_bytes, code, unix_now());
        if !valid {
            tracing::debug!("OTP validation failed for account {}", account_id);
        }
        Ok(valid)
    }

    /// A new Base32-encoded TOTP secret for account setup.
    pub fn generate_secret(&self) -> String {
        let mut bytes = [0u8; Self::SECRET_LENGTH];
        rand::rngs::OsRng.fill_bytes(&mut bytes);
        base32::encode(&bytes)
    }
}

/// `PcCertService`: PC certificate PIN verification (PINs are hashed like passwords).
#[derive(Clone, Debug)]
pub struct PcCertService {
    repository: TwoFactorRepository,
    passwords: PasswordService,
}

impl PcCertService {
    pub fn new(repository: TwoFactorRepository, passwords: PasswordService) -> Self {
        Self {
            repository,
            passwords,
        }
    }

    pub async fn validate(&self, account_id: AccountId, pin: &str) -> sqlx::Result<bool> {
        let config = self.repository.get_config(account_id).await?;
        let Some(hash) = config
            .and_then(|c| c.cert_pin_hash)
            .filter(|s| !s.is_empty())
        else {
            tracing::warn!(
                "PcCert validation attempted for account {} with no PIN configured",
                account_id
            );
            return Ok(false);
        };
        // Reuse the password service for PIN verification - same hashing approach
        let passwords = self.passwords.clone();
        let pin = Password::from_plaintext(pin);
        let result = tokio::task::spawn_blocking(move || passwords.verify_password(&hash, &pin))
            .await
            .unwrap_or(PasswordVerificationResult::Failed);
        let valid = result != PasswordVerificationResult::Failed;
        if !valid {
            tracing::debug!("PcCert PIN validation failed for account {}", account_id);
        }
        Ok(valid)
    }
}

/// An active ARS phone callback session.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ArsSession {
    /// The 4-digit code displayed to the user and spoken during the call.
    pub session_code: String,
    pub expires_at: SystemTime,
}

/// `ArsService`: ARS (Automatic Response System) phone callback verification. The phone call
/// itself is a stub (no `IArsPhoneProvider` implementation exists).
#[derive(Debug, Default)]
pub struct ArsService {
    active_sessions: DashMap<AccountId, ArsSession>,
}

impl ArsService {
    /// Creates (or replaces) the ARS session for an account.
    pub fn create_session(&self, account_id: AccountId, timeout_seconds: i32) -> ArsSession {
        // Generate a random 4-digit code
        let code = format!("{:04}", rand::rngs::OsRng.gen_range(0..10_000));
        let expires_at = SystemTime::now() + Duration::from_secs(timeout_seconds.max(0) as u64);
        let session = ArsSession {
            session_code: code,
            expires_at,
        };
        self.active_sessions.insert(account_id, session.clone());
        tracing::debug!(
            "Created ARS session for account {}, expires at {:?}",
            account_id,
            expires_at
        );
        // Note: a real implementation would place the phone call here (IArsPhoneProvider).
        session
    }

    /// Completes an ARS session; called when the user confirms via phone.
    pub fn complete_session(&self, account_id: AccountId, session_code: &str) -> bool {
        let Some(session) = self.active_sessions.get(&account_id).map(|s| s.clone()) else {
            tracing::debug!(
                "ARS completion attempted for account {} with no active session",
                account_id
            );
            return false;
        };
        if SystemTime::now() > session.expires_at {
            self.active_sessions.remove(&account_id);
            tracing::debug!("ARS session for account {} has expired", account_id);
            return false;
        }
        if session.session_code != session_code {
            tracing::debug!("ARS code mismatch for account {}", account_id);
            return false;
        }
        self.active_sessions.remove(&account_id);
        tracing::debug!(
            "ARS session completed successfully for account {}",
            account_id
        );
        true
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn totp_matches_otp_net() {
        // Otp.NET: new Totp(Base32("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP"), totpSize: 8).ComputeTotp(1700000000)
        let secret = base32::decode("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP").unwrap();
        assert_eq!(totp::code_at(&secret, 1_700_000_000), "80406058");
        assert!(totp::verify(&secret, "80406058", 1_700_000_000));
        assert!(totp::verify(&secret, "80406058", 1_700_000_000 + 30));
        assert!(!totp::verify(&secret, "80406058", 1_700_000_000 + 90));
        assert!(!totp::verify(&secret, "8040605", 1_700_000_000));
    }

    #[test]
    fn base32_matches_otp_net() {
        assert_eq!(base32::encode(&[1, 2, 3, 4, 5, 6, 7]), "AEBAGBAFAYDQ====");
        assert_eq!(base32::decode("mzxw6ytboi======").unwrap(), b"foobar");
        assert_eq!(
            base32::decode("AEBAGBAFAYDQ====").unwrap(),
            vec![1, 2, 3, 4, 5, 6, 7]
        );
        assert_eq!(base32::decode("A1"), None);
    }

    fn cfg(methods: u8) -> TwoFactorConfig {
        TwoFactorConfig {
            user_id: AccountId(1),
            enabled_methods: TwoFactorMethod(methods),
            otp_secret: Some("ABC".into()),
            otp_verified: true,
            cert_pin_hash: Some("x".into()),
            ars_phone_number: Some("123".into()),
            ars_phone_verified: false,
            created_at: 0,
            updated_at: 0,
        }
    }

    #[test]
    fn requirements() {
        assert_eq!(requirements_for(None), TwoFactorRequirements::NONE);
        let r = requirements_for(Some(&cfg(7)));
        assert!(r.requires_otp && r.requires_pc_cert && !r.requires_ars);
        let mut c = cfg(1);
        c.otp_secret = Some(String::new());
        assert!(!requirements_for(Some(&c)).any_required());
    }

    #[test]
    fn ars_sessions() {
        let ars = ArsService::default();
        let s = ars.create_session(AccountId(1), 60);
        assert_eq!(s.session_code.len(), 4);
        assert!(!ars.complete_session(AccountId(1), "wrong"));
        assert!(ars.complete_session(AccountId(1), &s.session_code));
        assert!(!ars.complete_session(AccountId(1), &s.session_code));
        let s = ars.create_session(AccountId(2), 0);
        std::thread::sleep(Duration::from_millis(5));
        assert!(!ars.complete_session(AccountId(2), &s.session_code));
    }
}
