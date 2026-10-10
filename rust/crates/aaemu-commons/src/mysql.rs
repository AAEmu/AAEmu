//! Ports of `AAEmu.Commons.Utils.DB.MySQL` (connection settings / factory) and
//! `AAEmu.Commons.Utils.Updater.MySqlDatabaseUpdater` (applies `SQL/updates/*<prefix>*.sql`).
//!
//! The C# `MySQL` class kept a global connection string set by `SetConfiguration`; here the
//! options are a value ([`connect_options`]) the caller keeps. `DefaultCommandTimeout = 180`,
//! `AllowZeroDateTime` / `ConvertZeroDateTime` have no sqlx equivalent.

use std::io::Write as _;
use std::path::{Path, PathBuf};
use std::time::Duration;

use chrono::{NaiveDate, NaiveDateTime, Utc};
use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions, MySqlSslMode};
use sqlx::{Connection, MySqlConnection, MySqlPool};

use crate::file_manager::{self, wildcard_match, FS_IGNORE_CASE};
use crate::models::MySqlConnectionSettings;

/// `MySQL.SetConfiguration`: connection options from settings (`None` → `root@localhost:3306`,
/// empty password and database), SSL preferred, utf8.
pub fn connect_options(settings: Option<&MySqlConnectionSettings>) -> MySqlConnectOptions {
    let opts = match settings {
        Some(s) => MySqlConnectOptions::new()
            .host(&s.host)
            .port(s.port)
            .username(&s.user)
            .password(&s.password)
            .database(&s.database),
        None => MySqlConnectOptions::new()
            .host("localhost")
            .port(3306)
            .username("root")
            .password(""),
    };
    opts.ssl_mode(MySqlSslMode::Preferred).charset("utf8")
}

/// Connection pool with the C# pooling settings (min 0, max 10, lifetime 600 s). Lazy: no
/// connection is made until first use.
pub fn create_pool(options: MySqlConnectOptions) -> MySqlPool {
    MySqlPoolOptions::new()
        .min_connections(0)
        .max_connections(10)
        .max_lifetime(Duration::from_secs(600))
        .connect_lazy_with(options)
}

/// `MySQL.CreateConnection`: opens a dedicated connection, logging failures.
pub async fn create_connection(
    options: &MySqlConnectOptions,
) -> Result<MySqlConnection, sqlx::Error> {
    MySqlConnection::connect_with(options)
        .await
        .inspect_err(|e| {
            tracing::error!("Error on DB connect: {e}");
        })
}

#[derive(Debug, thiserror::Error)]
pub enum UpdaterError {
    #[error(transparent)]
    Db(#[from] sqlx::Error),
    #[error(transparent)]
    Io(#[from] std::io::Error),
    #[error("console input failed: {0}")]
    Input(String),
}

async fn updates_table_exists(
    conn: &mut MySqlConnection,
    schema: &str,
) -> Result<bool, UpdaterError> {
    let (exists,): (i64,) = sqlx::query_as(
        "SELECT EXISTS( SELECT `TABLE_NAME` FROM `INFORMATION_SCHEMA`.`TABLES` WHERE (`TABLE_NAME` = 'updates') AND (`TABLE_SCHEMA` = ?) ) as `is-exists`",
    )
    .bind(schema)
    .fetch_one(&mut *conn)
    .await?;
    Ok(exists > 0)
}

/// SQL used to create the `updates` table.
pub const CREATE_UPDATES_TABLE_SQL: &str = "CREATE TABLE `updates` ( \
    `script_name` varchar(255) NOT NULL, \
    `installed` tinyint NOT NULL DEFAULT '0', \
    `install_date` datetime NOT NULL, \
    `last_error` text NOT NULL \
    ) COMMENT='Table containing SQL update script information' COLLATE 'utf8mb4_general_ci';\
    ALTER TABLE `updates` ADD PRIMARY KEY `script_name` (`script_name`);";

async fn create_updates_table(conn: &mut MySqlConnection) -> bool {
    match sqlx::raw_sql(CREATE_UPDATES_TABLE_SQL)
        .execute(&mut *conn)
        .await
    {
        Ok(_) => {
            tracing::info!("Created updates table");
            true
        }
        Err(e) => {
            tracing::error!("Failed to create updates table! {e}");
            false
        }
    }
}

async fn record_update(
    conn: &mut MySqlConnection,
    name: &str,
    installed: bool,
    date: NaiveDateTime,
    error: &str,
) -> Result<(), UpdaterError> {
    sqlx::query(
        "REPLACE INTO `updates` (`script_name`,`installed`,`install_date`,`last_error`) VALUES (?, ?, ?, ?)",
    )
    .bind(name)
    .bind(i32::from(installed))
    .bind(date)
    .bind(error)
    .execute(&mut *conn)
    .await?;
    Ok(())
}

fn file_name(path: &Path) -> String {
    path.file_name()
        .map(|f| f.to_string_lossy().into_owned())
        .unwrap_or_default()
}

/// `DateTime.MinValue`.
fn min_date() -> NaiveDateTime {
    NaiveDate::from_ymd_opt(1, 1, 1)
        .and_then(|d| d.and_hms_opt(0, 0, 0))
        .unwrap_or_default()
}

/// Marks all current updates as installed (first run of the updater on an existing DB).
/// Names are stored lower-cased, like the C#.
async fn initialize_updates_table(
    conn: &mut MySqlConnection,
    files: &[PathBuf],
    prefix: &str,
) -> Result<(), UpdaterError> {
    for f in files {
        let name = file_name(f).to_lowercase();
        if !name.contains(prefix) {
            continue;
        }
        record_update(conn, &name, true, Utc::now().naive_utc(), "Initialized").await?;
    }
    Ok(())
}

async fn install_update_files(
    conn: &mut MySqlConnection,
    files: &[PathBuf],
    skip: bool,
) -> Result<bool, UpdaterError> {
    for f in files {
        let (success, error) = if skip {
            (true, "Skipped".to_string())
        } else {
            let sql = std::fs::read_to_string(f)?;
            match sqlx::raw_sql(&sql).execute(&mut *conn).await {
                Ok(_) => (true, String::new()),
                Err(e) => (false, e.to_string()),
            }
        };
        let date = if success {
            Utc::now().naive_utc()
        } else {
            min_date()
        };
        record_update(conn, &file_name(f), success, date, &error).await?;
        if !success {
            tracing::error!("Failed to run update script: {}", f.display());
            tracing::error!("{error}");
            return Ok(false);
        }
        tracing::info!(
            "{}: {}",
            if skip { "Skipped" } else { "Installed" },
            f.display()
        );
    }
    Ok(true)
}

/// `FindUpdatesFolder`: crawls up from `start` (inclusive) to the root looking for a
/// `SQL/updates` folder containing `*<prefix>*.sql` files (top level only).
pub fn find_updates_folder(start: &Path, prefix: &str) -> Option<(PathBuf, Vec<PathBuf>)> {
    let mask = format!("*{prefix}*.sql");
    for dir in start.ancestors() {
        let test = dir.join("SQL").join("updates");
        if !test.is_dir() {
            continue;
        }
        let files = file_manager::get_matching_files_in_directory(&test, &mask, false);
        if !files.is_empty() {
            return Some((test, files));
        }
    }
    None
}

/// `MySqlDatabaseUpdater.Run`, searching for `SQL/updates` upwards from the executable's
/// directory. Returns `Ok(false)` if the database could not be brought up to date.
pub async fn run_updates(
    conn: &mut MySqlConnection,
    module_name_prefix: &str,
    database_schema_name: &str,
    auto_apply: bool,
) -> Result<bool, UpdaterError> {
    let start = PathBuf::from(file_manager::app_path());
    run_updates_from(
        conn,
        module_name_prefix,
        database_schema_name,
        auto_apply,
        &start,
    )
    .await
}

/// [`run_updates`] with an explicit directory to start searching for `SQL/updates` from.
pub async fn run_updates_from(
    conn: &mut MySqlConnection,
    module_name_prefix: &str,
    database_schema_name: &str,
    auto_apply: bool,
    start_dir: &Path,
) -> Result<bool, UpdaterError> {
    tracing::debug!("Updating database for {module_name_prefix}");

    let exists = updates_table_exists(conn, database_schema_name).await?;
    if !exists && !create_updates_table(conn).await {
        tracing::error!("Was unable to create updates table in {database_schema_name} !");
        return Ok(false);
    }

    let Some((folder, mut all_files)) = find_updates_folder(start_dir, module_name_prefix) else {
        tracing::info!("No sql update folder or files found.");
        return Ok(true);
    };
    all_files.sort();

    // If we're running this version for the first time, assume that all updates have been
    // installed before
    if !exists {
        initialize_updates_table(conn, &all_files, module_name_prefix).await?;
    }

    let rows: Vec<(String, i64)> = sqlx::query_as(
        "SELECT `script_name`, CAST(`installed` AS SIGNED) FROM `updates` ORDER BY `script_name`",
    )
    .fetch_all(&mut *conn)
    .await?;
    let (to_run, _) = plan_updates(&folder, &all_files, &rows);

    if to_run.is_empty() {
        tracing::debug!("No DB update required");
        return Ok(true);
    }

    tracing::warn!(
        "There are {} updates for the database that need to be installed:",
        to_run.len()
    );
    for f in &to_run {
        tracing::warn!("  > {}", f.display());
    }
    if auto_apply {
        tracing::info!("AutoApply is enabled — applying updates automatically.");
        return install_update_files(conn, &to_run, false).await;
    }

    tokio::time::sleep(Duration::from_secs(1)).await;
    println!(
        "Warning, there are {} updates for the database that need to be installed first!",
        to_run.len()
    );
    println!("-----");
    for f in &to_run {
        println!("> {}", f.display());
    }
    println!("-----");
    print!("Please type YES (all caps) to try and automatically install the updates, type SKIP if you already installed the update manually, or press Ctrl+C here to quit: ");
    let _ = std::io::stdout().flush();
    let answer = tokio::task::spawn_blocking(|| {
        let mut line = String::new();
        std::io::stdin()
            .read_line(&mut line)
            .map(|_| line.trim_end_matches(['\r', '\n']).to_string())
    })
    .await
    .map_err(|e| UpdaterError::Input(e.to_string()))??;
    if answer != "YES" && answer != "SKIP" {
        return Ok(false);
    }
    install_update_files(conn, &to_run, answer == "SKIP").await
}

/// Splits the update scripts into (to run, already installed) given the `updates` table rows
/// `(script_name, installed)`. Scripts not in the table are run.
///
/// Note (C# behaviour, kept): rows written by the first-run initialisation are lower-cased, so
/// on a case-insensitive file system a script with upper-case letters in its name is matched
/// by the DB row under its lower-case path and then again under its real name, and is re-run.
pub fn plan_updates(
    folder: &Path,
    all_files: &[PathBuf],
    rows: &[(String, i64)],
) -> (Vec<PathBuf>, Vec<PathBuf>) {
    let mut to_run = Vec::new();
    let mut already = Vec::new();
    for (name, installed) in rows {
        let full = folder.join(name);
        if full.is_file() {
            if *installed <= 0 {
                to_run.push(full);
            } else {
                already.push(full);
            }
        }
    }
    for f in all_files {
        if !to_run.contains(f) && !already.contains(f) && f.is_file() {
            to_run.push(f.clone());
        }
    }
    (to_run, already)
}

/// Whether `name` would be picked up as an update script for `prefix`.
pub fn is_update_script(name: &str, prefix: &str) -> bool {
    wildcard_match(&format!("*{prefix}*.sql"), name, FS_IGNORE_CASE)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::test_util::TempDir;

    #[test]
    fn options() {
        let o = connect_options(None);
        assert_eq!(o.get_host(), "localhost");
        assert_eq!(o.get_port(), 3306);
        assert_eq!(o.get_username(), "root");
        let s = MySqlConnectionSettings {
            host: "db".into(),
            port: 3307,
            user: "u".into(),
            password: "p".into(),
            database: "aaemu_game".into(),
        };
        let o = connect_options(Some(&s));
        assert_eq!(o.get_host(), "db");
        assert_eq!(o.get_database(), Some("aaemu_game"));
    }

    #[test]
    fn finds_folder_and_plans() {
        let tmp = TempDir::new("updater");
        let updates = tmp.path().join("SQL").join("updates");
        std::fs::create_dir_all(&updates).unwrap();
        let start = tmp.path().join("bin").join("Debug");
        std::fs::create_dir_all(&start).unwrap();
        for n in [
            "2024-01-01_aaemu_game_a.sql",
            "2024-02-01_aaemu_game_b.sql",
            "2024-01-01_aaemu_login_x.sql",
            "aaemu_game.txt",
        ] {
            std::fs::write(updates.join(n), "SELECT 1;").unwrap();
        }
        let (folder, mut files) = find_updates_folder(&start, "aaemu_game").unwrap();
        files.sort();
        assert_eq!(folder, updates);
        assert_eq!(files.len(), 2);
        assert!(find_updates_folder(&start, "aaemu_nothing").is_none());

        let rows = vec![
            ("2024-01-01_aaemu_game_a.sql".to_string(), 1),
            ("2024-02-01_aaemu_game_b.sql".to_string(), 0),
            ("deleted_aaemu_game.sql".to_string(), 0),
        ];
        let (run, done) = plan_updates(&folder, &files, &rows);
        assert_eq!(run, vec![updates.join("2024-02-01_aaemu_game_b.sql")]);
        assert_eq!(done, vec![updates.join("2024-01-01_aaemu_game_a.sql")]);
        let (run, _) = plan_updates(&folder, &files, &[]);
        assert_eq!(run.len(), 2);
        assert!(is_update_script("x_aaemu_game_y.sql", "aaemu_game"));
        assert!(!is_update_script("x_aaemu_game_y.sq", "aaemu_game"));
    }
}
