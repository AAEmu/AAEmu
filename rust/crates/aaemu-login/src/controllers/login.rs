//! `LoginController`: account lookup/creation, password checks, reconnection tokens.

use std::net::IpAddr;
use std::sync::{Arc, LazyLock};

use anyhow::Context;
use dashmap::DashMap;
use regex::Regex;
use sqlx::MySqlPool;

use super::game::GameController;
use crate::config::KoreaAuthOptions;
use crate::models::{
    AccountId, GameServerId, KoreaAuthInfo, LoginDeniedReason, LoginResult, ReconnectResult,
};
use crate::network::internal::InternalConnection;
use crate::packets::l2g::LgPlayerReconnect;
use crate::services::password::{
    Password, PasswordKind, PasswordService, PasswordVerificationResult,
};
use crate::services::{korea, sha256_crypt};

/// Allows Unicode letters and digits (any script), plus `_ . - @`.
///
/// Faithful to the C# `^[\p{L}\p{Nd}_.\-@]{1,32}$`, including two .NET regex quirks:
/// `$` also matches before a final `\n` (so `"name\n"` is accepted), and .NET matches UTF-16
/// code units, so characters outside the BMP never match `\p{L}` (handled in [`is_valid_username`]).
static USERNAME_REGEX: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"^[\p{L}\p{Nd}_.\-@]{1,32}\n?\z").expect("valid regex"));

pub fn is_valid_username(username: &str) -> bool {
    username.chars().all(|c| (c as u32) <= 0xFFFF) && USERNAME_REGEX.is_match(username)
}

fn unix_now() -> i64 {
    chrono::Utc::now().timestamp()
}

/// `string.ReplaceLineEndings(" ")` for log output.
fn log_safe(s: &str) -> String {
    s.replace("\r\n", " ").replace(
        ['\r', '\n', '\u{0085}', '\u{2028}', '\u{2029}', '\u{000C}'],
        " ",
    )
}

/// The columns of `users` the login path uses.
type UserRow = (u64, Option<String>, Option<String>, i64, i64);

pub struct LoginController {
    pool: MySqlPool,
    passwords: PasswordService,
    auto_account: bool,
    korea: KoreaAuthOptions,
    /// gsId -> (token -> accountId)
    tokens: DashMap<GameServerId, Arc<DashMap<u32, AccountId>>>,
}

impl LoginController {
    pub fn new(
        pool: MySqlPool,
        passwords: PasswordService,
        auto_account: bool,
        korea: KoreaAuthOptions,
    ) -> Self {
        Self {
            pool,
            passwords,
            auto_account,
            korea,
            tokens: DashMap::new(),
        }
    }

    async fn verify_password(
        &self,
        stored: String,
        password: &Password,
    ) -> anyhow::Result<PasswordVerificationResult> {
        let svc = self.passwords.clone();
        let pw = password.clone();
        Ok(tokio::task::spawn_blocking(move || svc.verify_password(&stored, &pw)).await?)
    }

    async fn hash_for_storage(&self, password: &Password) -> anyhow::Result<String> {
        let svc = self.passwords.clone();
        let pw = password.clone();
        Ok(tokio::task::spawn_blocking(move || svc.hash_for_storage(&pw)).await??)
    }

    async fn korea_hash(&self, plaintext: &str) -> anyhow::Result<String> {
        let pw = plaintext.to_string();
        let rounds = self.korea.rounds;
        Ok(tokio::task::spawn_blocking(move || korea::compute(&pw, rounds)).await?)
    }

    /// EU method auth.
    pub async fn login(
        &self,
        username: &str,
        password: &Password,
        ip: IpAddr,
    ) -> anyhow::Result<LoginResult> {
        let row: Option<UserRow> = sqlx::query_as(
            "SELECT CAST(id AS UNSIGNED), password, korea_challenge_hash, CAST(banned AS SIGNED), CAST(ban_reason AS SIGNED) \
             FROM users WHERE username = ?",
        )
        .bind(username)
        .fetch_optional(&self.pool)
        .await?;

        let Some((id, stored_password, stored_korea_hash, banned, ban_reason)) = row else {
            if self.auto_account {
                return self.create_and_login_invalid(username, password, ip).await;
            }
            return Ok(LoginResult::denied(LoginDeniedReason::BAD_ACCOUNT));
        };

        // C#: reader.GetString("password") throws on NULL.
        let stored_password = stored_password.context("users.password is NULL")?;
        let verification = self.verify_password(stored_password, password).await?;
        if verification == PasswordVerificationResult::Failed {
            return Ok(LoginResult::denied(LoginDeniedReason::BAD_ACCOUNT));
        }

        if banned != 0 {
            return Ok(LoginResult::denied(LoginDeniedReason(ban_reason as u8)));
        }

        let account_id = AccountId(id as u32);
        let now = unix_now();
        tracing::info!("{} connected.", log_safe(username));

        // Determine what needs rehashing, which is only possible when we have a plaintext password
        let plaintext = password.kind == PasswordKind::Plaintext;
        let rehash_pbkdf2 =
            verification == PasswordVerificationResult::SuccessRehashNeeded && plaintext;
        let korea_rehash_needed = self.korea.enabled
            && plaintext
            && match &stored_korea_hash {
                None => true,
                Some(h) => sha256_crypt::parse_rounds(h)? != self.korea.rounds,
            };

        let ip_s = ip.to_string();
        let result = if rehash_pbkdf2 && korea_rehash_needed {
            let hash = self.hash_for_storage(password).await?;
            let korea_hash = self.korea_hash(&password.value).await?;
            sqlx::query(
                "UPDATE `users` SET password = ?, korea_challenge_hash = ?, last_ip = ?, last_login = ?, updated_at = ? WHERE id = ?",
            )
            .bind(hash)
            .bind(korea_hash)
            .bind(&ip_s)
            .bind(now)
            .bind(now)
            .bind(account_id.0)
            .execute(&self.pool)
            .await?
        } else if rehash_pbkdf2 {
            let hash = self.hash_for_storage(password).await?;
            sqlx::query("UPDATE `users` SET password = ?, last_ip = ?, last_login = ?, updated_at = ? WHERE id = ?")
                .bind(hash)
                .bind(&ip_s)
                .bind(now)
                .bind(now)
                .bind(account_id.0)
                .execute(&self.pool)
                .await?
        } else if korea_rehash_needed {
            let korea_hash = self.korea_hash(&password.value).await?;
            sqlx::query(
                "UPDATE `users` SET korea_challenge_hash = ?, last_ip = ?, last_login = ?, updated_at = ? WHERE id = ?",
            )
            .bind(korea_hash)
            .bind(&ip_s)
            .bind(now)
            .bind(now)
            .bind(account_id.0)
            .execute(&self.pool)
            .await?
        } else {
            sqlx::query(
                "UPDATE `users` SET last_ip = ?, last_login = ?, updated_at = ? WHERE id = ?",
            )
            .bind(&ip_s)
            .bind(now)
            .bind(now)
            .bind(account_id.0)
            .execute(&self.pool)
            .await?
        };
        if result.rows_affected() != 1 {
            tracing::warn!(
                "Database update failed, error occurred while updating account login IP and time"
            );
        }

        Ok(LoginResult::ok(account_id))
    }

    /// The Korea challenge-response material for `username`, or `None` if the account does not
    /// exist or has no `korea_challenge_hash` yet (the user must log in via EU auth first to seed
    /// it while `KoreaAuth:Enabled=true`).
    pub async fn get_korea_auth_info(
        &self,
        username: &str,
    ) -> anyhow::Result<Option<KoreaAuthInfo>> {
        let row: Option<(u64, Option<String>)> = sqlx::query_as(
            "SELECT CAST(id AS UNSIGNED), korea_challenge_hash FROM users WHERE username = ?",
        )
        .bind(username)
        .fetch_optional(&self.pool)
        .await?;
        let Some((id, Some(stored))) = row else {
            return Ok(None);
        };
        let (rounds, salt, raw) = sha256_crypt::parse(&stored)?;
        Ok(Some(KoreaAuthInfo {
            account_id: AccountId(id as u32),
            challenge_key_hash: raw,
            challenge_salt: salt,
            challenge_rounds: rounds,
        }))
    }

    /// Ban status of an account; called after successful challenge verification so account status
    /// is not leaked before authentication.
    pub async fn check_ban_status(
        &self,
        account_id: AccountId,
    ) -> anyhow::Result<(bool, LoginDeniedReason)> {
        let row: Option<(i64, i64)> = sqlx::query_as(
            "SELECT CAST(banned AS SIGNED), CAST(ban_reason AS SIGNED) FROM users WHERE id = ?",
        )
        .bind(account_id.0)
        .fetch_optional(&self.pool)
        .await?;
        Ok(match row {
            None => (false, LoginDeniedReason::default()),
            Some((banned, reason)) => {
                let banned = banned != 0;
                (
                    banned,
                    if banned {
                        LoginDeniedReason(reason as u8)
                    } else {
                        LoginDeniedReason::default()
                    },
                )
            }
        })
    }

    async fn create_and_login_invalid(
        &self,
        username: &str,
        password: &Password,
        ip: IpAddr,
    ) -> anyhow::Result<LoginResult> {
        if !is_valid_username(username) {
            return Ok(LoginResult::denied(LoginDeniedReason::BAD_ACCOUNT));
        }
        let password_hash = self.hash_for_storage(password).await?;
        let now = unix_now();
        let ip_s = ip.to_string();

        let inserted = if self.korea.enabled && password.kind == PasswordKind::Plaintext {
            let korea_hash = self.korea_hash(&password.value).await?;
            sqlx::query(
                "INSERT into users (username, password, korea_challenge_hash, email, last_ip, last_login, created_at, updated_at) \
                 VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            )
            .bind(username)
            .bind(&password_hash)
            .bind(korea_hash)
            .bind("")
            .bind(&ip_s)
            .bind(now)
            .bind(now)
            .bind(now)
            .execute(&self.pool)
            .await?
        } else {
            sqlx::query(
                "INSERT into users (username, password, email, last_ip, last_login, created_at, updated_at) \
                 VALUES (?, ?, ?, ?, ?, ?, ?)",
            )
            .bind(username)
            .bind(&password_hash)
            .bind("")
            .bind(&ip_s)
            .bind(now)
            .bind(now)
            .bind(now)
            .execute(&self.pool)
            .await?
        };
        if inserted.rows_affected() != 1 {
            return Ok(LoginResult::denied(LoginDeniedReason::LOGIN_UNKNOWN));
        }
        tracing::debug!(
            "Created account from invalid username login with value {}",
            username
        );
        Box::pin(self.login(username, password, ip)).await
    }

    /// Stores a reconnection token for `account_id` on `gs_id` and acknowledges it to the game
    /// server.
    pub fn add_reconnection_token(
        &self,
        connection: &InternalConnection,
        gs_id: GameServerId,
        account_id: AccountId,
        token: u32,
    ) {
        let tokens = self.tokens.entry(gs_id).or_default().clone();
        tokens.entry(token).or_insert(account_id);
        connection.send_packet(&LgPlayerReconnect { token });
    }

    /// Validates a reconnection token. An `Err` mirrors the C# `KeyNotFoundException` when the
    /// game server is a mirror whose parent has no tokens.
    pub fn reconnect(
        &self,
        games: &GameController,
        gs_id: GameServerId,
        account_id: AccountId,
        token: u32,
    ) -> anyhow::Result<ReconnectResult> {
        let fail = ReconnectResult {
            success: false,
            account_id: AccountId::default(),
        };
        let mut gs_id = gs_id;
        if !self.tokens.contains_key(&gs_id) {
            match games.try_get_parent_id(gs_id) {
                Some(parent) => gs_id = parent,
                None => return Ok(fail), // TODO ...
            }
        }
        let tokens = self
            .tokens
            .get(&gs_id)
            .map(|t| t.clone())
            .with_context(|| {
                format!("The given key '{gs_id}' was not present in the dictionary.")
            })?;
        Ok(match tokens.get(&token).map(|v| *v) {
            Some(v) if v == account_id => ReconnectResult {
                success: true,
                account_id,
            },
            _ => fail, // TODO ...
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn username_rules_match_dotnet_regex() {
        assert!(is_valid_username("test"));
        assert!(is_valid_username("José_99.x-y@z"));
        assert!(is_valid_username("테스트"));
        assert!(is_valid_username(&"a".repeat(32)));
        assert!(!is_valid_username(&"a".repeat(33)));
        assert!(!is_valid_username(""));
        assert!(!is_valid_username("bad name"));
        assert!(!is_valid_username("semi;colon"));
        // .NET `$` quirk: a single trailing newline is accepted.
        assert!(is_valid_username("name\n"));
        assert!(!is_valid_username("name\n\n"));
        // Non-BMP letters never match in .NET (UTF-16 surrogates).
        assert!(!is_valid_username("a\u{10400}"));
    }
}
