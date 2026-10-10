//! Port of `AAEmu.Login.IntegrationTests/LoginControllerIntegrationTests.cs` (and its
//! `Fixtures/MySqlFixture.cs`): `LoginController` against a real MySQL database.
//!
//! The C# fixture starts a `mysql:8.0` Testcontainer. Here the database is supplied by the
//! environment instead, so these tests are `#[ignore]`d by default. To run them:
//!
//! ```text
//! AAEMU_TEST_MYSQL=mysql://user:pass@127.0.0.1:3306/aaemu_login_test \
//!     cargo test -p aaemu-login --test login_controller_mysql -- --ignored
//! ```
//!
//! The database must exist and be disposable: `SQL/aaemu_login.sql` is applied to it (without
//! its `CREATE DATABASE` / `USE` lines) and `users` / `user_2fa` are truncated before each test.
//! Without `AAEMU_TEST_MYSQL` the tests return immediately.
//!
//! The C# tests mock `IPasswordService` with an identity "hash"; these use a real
//! `PasswordService` (with few PBKDF2 iterations) and store real hashes, which keeps the same
//! inputs and assertions.

use std::net::IpAddr;
use std::str::FromStr;
use std::sync::LazyLock;

use aaemu_login::config::KoreaAuthOptions;
use aaemu_login::controllers::login::LoginController;
use aaemu_login::models::{AccountId, LoginDeniedReason};
use aaemu_login::services::password::{Password, PasswordService};
use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions};
use sqlx::MySqlPool;

const ENV_VAR: &str = "AAEMU_TEST_MYSQL";

/// The tests share one database, so they run one at a time (xUnit `[Collection("MySql")]`).
static DB_LOCK: LazyLock<tokio::sync::Mutex<()>> = LazyLock::new(|| tokio::sync::Mutex::new(()));

fn ip_address() -> IpAddr {
    "127.0.0.1".parse().unwrap()
}

fn passwords() -> PasswordService {
    PasswordService { iterations: 1000 }
}

/// `MySqlFixture.InitializeAsync` + `LoginControllerIntegrationTests.InitializeAsync`: applies
/// the schema and empties the tables. `None` if `AAEMU_TEST_MYSQL` is not set.
async fn setup() -> Option<MySqlPool> {
    let Ok(url) = std::env::var(ENV_VAR) else {
        eprintln!("{ENV_VAR} not set; skipping MySQL integration test");
        return None;
    };
    let options = MySqlConnectOptions::from_str(&url).expect("valid AAEMU_TEST_MYSQL url");
    let pool = MySqlPoolOptions::new()
        .max_connections(2)
        .connect_with(options)
        .await
        .expect("connect to AAEMU_TEST_MYSQL");

    // Strip CREATE DATABASE / USE lines — the target database already exists.
    let schema_path = concat!(env!("CARGO_MANIFEST_DIR"), "/../../../SQL/aaemu_login.sql");
    let schema = std::fs::read_to_string(schema_path).expect("SQL/aaemu_login.sql");
    let cleaned: Vec<&str> = schema
        .split('\n')
        .filter(|line| {
            let t = line.trim_start().to_uppercase();
            !t.starts_with("CREATE DATABASE") && !t.starts_with("USE ")
        })
        .collect();
    sqlx::raw_sql(&cleaned.join("\n"))
        .execute(&pool)
        .await
        .expect("apply schema");

    sqlx::raw_sql(
        "SET FOREIGN_KEY_CHECKS = 0; TRUNCATE TABLE user_2fa; TRUNCATE TABLE users; SET FOREIGN_KEY_CHECKS = 1;",
    )
    .execute(&pool)
    .await
    .expect("truncate tables");
    Some(pool)
}

fn create_controller(pool: &MySqlPool, auto_account: bool) -> LoginController {
    LoginController::new(
        pool.clone(),
        passwords(),
        auto_account,
        KoreaAuthOptions::default(),
    )
}

async fn insert_user(
    pool: &MySqlPool,
    username: &str,
    password: &str,
    banned: bool,
    ban_reason: i32,
) {
    let hash = passwords()
        .hash_for_storage(&Password::from_plaintext(password))
        .unwrap();
    sqlx::query(
        "INSERT INTO users (username, password, email, last_ip, last_login, created_at, updated_at, banned, ban_reason) \
         VALUES (?, ?, '', '', 0, 0, 0, ?, ?)",
    )
    .bind(username)
    .bind(hash)
    .bind(i32::from(banned))
    .bind(ban_reason)
    .execute(pool)
    .await
    .expect("insert user");
}

#[tokio::test]
#[ignore = "requires MySQL: set AAEMU_TEST_MYSQL=mysql://user:pass@host:port/db"]
async fn login_non_existent_user_auto_account_off_returns_failed() {
    let _guard = DB_LOCK.lock().await;
    let Some(pool) = setup().await else { return };
    let controller = create_controller(&pool, false);

    let result = controller
        .login(
            "no_such_user",
            &Password::from_plaintext("pass"),
            ip_address(),
        )
        .await
        .unwrap();

    assert!(!result.success);
    assert_eq!(result.denial_reason, LoginDeniedReason::BAD_ACCOUNT);
}

#[tokio::test]
#[ignore = "requires MySQL: set AAEMU_TEST_MYSQL=mysql://user:pass@host:port/db"]
async fn login_non_existent_user_auto_account_on_creates_and_succeeds() {
    let _guard = DB_LOCK.lock().await;
    let Some(pool) = setup().await else { return };
    let controller = create_controller(&pool, true);

    let result = controller
        .login(
            "autouser",
            &Password::from_plaintext("newpass"),
            ip_address(),
        )
        .await
        .unwrap();

    assert!(result.success);
    assert_ne!(result.account_id, AccountId::default());
}

#[tokio::test]
#[ignore = "requires MySQL: set AAEMU_TEST_MYSQL=mysql://user:pass@host:port/db"]
async fn login_correct_password_returns_success() {
    let _guard = DB_LOCK.lock().await;
    let Some(pool) = setup().await else { return };
    insert_user(&pool, "alice", "mypassword", false, 0).await;
    let controller = create_controller(&pool, false);

    let result = controller
        .login(
            "alice",
            &Password::from_plaintext("mypassword"),
            ip_address(),
        )
        .await
        .unwrap();

    assert!(result.success);
}

#[tokio::test]
#[ignore = "requires MySQL: set AAEMU_TEST_MYSQL=mysql://user:pass@host:port/db"]
async fn login_wrong_password_returns_failed() {
    let _guard = DB_LOCK.lock().await;
    let Some(pool) = setup().await else { return };
    insert_user(&pool, "bob", "correctpass", false, 0).await;
    let controller = create_controller(&pool, false);

    let result = controller
        .login("bob", &Password::from_plaintext("wrongpass"), ip_address())
        .await
        .unwrap();

    assert!(!result.success);
    assert_eq!(result.denial_reason, LoginDeniedReason::BAD_ACCOUNT);
}

#[tokio::test]
#[ignore = "requires MySQL: set AAEMU_TEST_MYSQL=mysql://user:pass@host:port/db"]
async fn login_banned_user_returns_ban_reason() {
    let _guard = DB_LOCK.lock().await;
    let Some(pool) = setup().await else { return };
    insert_user(&pool, "banned_user", "pass", true, 5).await;
    let controller = create_controller(&pool, false);

    let result = controller
        .login(
            "banned_user",
            &Password::from_plaintext("pass"),
            ip_address(),
        )
        .await
        .unwrap();

    assert!(!result.success);
    assert_eq!(
        result.denial_reason,
        LoginDeniedReason::TRY_TRADE_CASH_TEMPORAL
    );
}

#[tokio::test]
#[ignore = "requires MySQL: set AAEMU_TEST_MYSQL=mysql://user:pass@host:port/db"]
async fn login_updates_last_login_and_ip() {
    let _guard = DB_LOCK.lock().await;
    let Some(pool) = setup().await else { return };
    insert_user(&pool, "tracker", "pass", false, 0).await;
    let controller = create_controller(&pool, false);

    let result = controller
        .login("tracker", &Password::from_plaintext("pass"), ip_address())
        .await
        .unwrap();
    assert!(result.success);

    // Verify the DB was updated
    let (last_ip, last_login, updated_at): (String, i64, i64) = sqlx::query_as(
        "SELECT last_ip, CAST(last_login AS SIGNED), CAST(updated_at AS SIGNED) FROM users WHERE username = ?",
    )
    .bind("tracker")
    .fetch_one(&pool)
    .await
    .unwrap();

    assert_eq!(last_ip.parse::<IpAddr>().unwrap(), ip_address());
    assert!(
        last_login > 0,
        "last_login should be updated to a non-zero timestamp"
    );
    assert!(
        updated_at > 0,
        "updated_at should be updated to a non-zero timestamp"
    );
}
