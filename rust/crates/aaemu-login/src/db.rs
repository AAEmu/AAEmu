//! MySQL access: connection settings (`MySqlConnectionFactory`), the startup connection test
//! (`MySqlInitializer`) and the schema updater (`AAEmu.Commons.Utils.Updater.MySqlDatabaseUpdater`).

use std::path::{Path, PathBuf};
use std::time::Duration;

use anyhow::Context;
use chrono::{NaiveDate, NaiveDateTime, Utc};
use sqlx::mysql::{MySqlConnectOptions, MySqlPoolOptions, MySqlSslMode};
use sqlx::{Connection, MySqlConnection, MySqlPool};

use crate::config::AppConfig;

/// Builds the connection options from `ConnectionStrings:MySqlConnection` (if set) or the
/// `Connections:MySQLProvider` section. Mirrors the C# builder: SSL preferred, utf8 charset.
/// (`DefaultCommandTimeout = 180` has no sqlx equivalent.)
pub fn connect_options(config: &AppConfig) -> anyhow::Result<MySqlConnectOptions> {
    let opts = match config.mysql_connection_string() {
        Some(cs) => parse_connection_string(cs)?,
        None => {
            let p = &config.connections.mysql_provider;
            MySqlConnectOptions::new()
                .host(&p.host)
                .port(p.port)
                .username(&p.user)
                .password(&p.password)
                .database(&p.database)
        }
    };
    Ok(opts.ssl_mode(MySqlSslMode::Preferred).charset("utf8"))
}

/// Parses an ADO.NET-style MySQL connection string (`Server=..;Port=..;User ID=..;...`).
pub fn parse_connection_string(cs: &str) -> anyhow::Result<MySqlConnectOptions> {
    let mut opts = MySqlConnectOptions::new();
    for part in cs.split(';').map(str::trim).filter(|p| !p.is_empty()) {
        let (k, v) = part
            .split_once('=')
            .with_context(|| format!("invalid connection string segment '{part}'"))?;
        let v = v.trim();
        match k.trim().to_ascii_lowercase().as_str() {
            "server" | "host" | "data source" | "datasource" | "address" | "addr"
            | "network address" => opts = opts.host(v),
            "port" => opts = opts.port(v.parse().with_context(|| format!("invalid port '{v}'"))?),
            "user id" | "uid" | "username" | "user name" | "user" | "userid" => {
                opts = opts.username(v)
            }
            "password" | "pwd" => opts = opts.password(v),
            "database" | "initial catalog" => opts = opts.database(v),
            _ => {} // pooling/timeouts/etc. are configured on the pool
        }
    }
    Ok(opts)
}

/// Pool settings mirror the C# connection string (min 0, max 10, lifetime 600 s).
pub fn create_pool(options: MySqlConnectOptions) -> MySqlPool {
    MySqlPoolOptions::new()
        .min_connections(0)
        .max_connections(10)
        .max_lifetime(Duration::from_secs(600))
        .connect_lazy_with(options)
}

/// `MySqlInitializer`: tests the DB connection at startup and returns the server version.
/// Opens a dedicated connection (not through the pool) so a failure reports the real error
/// instead of a pool timeout.
pub async fn test_connection(options: &MySqlConnectOptions) -> anyhow::Result<String> {
    let mut conn = MySqlConnection::connect_with(options).await?;
    let (version,): (String,) = sqlx::query_as("SELECT VERSION()")
        .fetch_one(&mut conn)
        .await?;
    let _ = conn.close().await;
    Ok(version)
}

// ---- MySqlDatabaseUpdater --------------------------------------------------------------------

async fn updates_table_exists(conn: &mut MySqlConnection, schema: &str) -> anyhow::Result<bool> {
    let (exists,): (i64,) = sqlx::query_as(
        "SELECT EXISTS( SELECT `TABLE_NAME` FROM `INFORMATION_SCHEMA`.`TABLES` WHERE (`TABLE_NAME` = 'updates') AND (`TABLE_SCHEMA` = ?) ) as `is-exists`",
    )
    .bind(schema)
    .fetch_one(&mut *conn)
    .await?;
    Ok(exists > 0)
}

async fn create_updates_table(conn: &mut MySqlConnection) -> bool {
    let sql = "CREATE TABLE `updates` ( \
               `script_name` varchar(255) NOT NULL, \
               `installed` tinyint NOT NULL DEFAULT '0', \
               `install_date` datetime NOT NULL, \
               `last_error` text NOT NULL \
               ) COMMENT='Table containing SQL update script information' COLLATE 'utf8mb4_general_ci';\
               ALTER TABLE `updates` ADD PRIMARY KEY `script_name` (`script_name`);";
    match sqlx::raw_sql(sql).execute(&mut *conn).await {
        Ok(_) => {
            tracing::info!("Created updates table");
            true
        }
        Err(e) => {
            tracing::error!("Failed to create updates table! {}", e);
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
) -> anyhow::Result<()> {
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

/// Marks all current updates as installed (first run of the updater on an existing DB).
async fn initialize_updates_table(
    conn: &mut MySqlConnection,
    files: &[PathBuf],
    prefix: &str,
) -> anyhow::Result<()> {
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
) -> anyhow::Result<bool> {
    for f in files {
        let (success, error) = if skip {
            (true, "Skipped".to_string())
        } else {
            let sql =
                std::fs::read_to_string(f).with_context(|| format!("reading {}", f.display()))?;
            match sqlx::raw_sql(&sql).execute(&mut *conn).await {
                Ok(_) => (true, String::new()),
                Err(e) => (false, e.to_string()),
            }
        };
        // DateTime.MinValue on failure
        let date = if success {
            Utc::now().naive_utc()
        } else {
            NaiveDate::from_ymd_opt(1, 1, 1)
                .and_then(|d| d.and_hms_opt(0, 0, 0))
                .unwrap_or_default()
        };
        record_update(conn, &file_name(f), success, date, &error).await?;
        if !success {
            tracing::error!("Failed to run update script: {}", f.display());
            tracing::error!("{}", error);
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

/// Crawls up from the executable's directory looking for `SQL/updates` with matching files.
fn find_updates_folder(prefix: &str) -> Option<(PathBuf, Vec<PathBuf>)> {
    let start = std::env::current_exe().ok()?.parent()?.to_path_buf();
    for dir in start.ancestors() {
        let test = dir.join("SQL").join("updates");
        let Ok(entries) = std::fs::read_dir(&test) else {
            continue;
        };
        let files: Vec<PathBuf> = entries
            .filter_map(Result::ok)
            .map(|e| e.path())
            .filter(|p| p.is_file())
            .filter(|p| {
                let n = file_name(p);
                // Directory.GetFiles(dir, "*prefix*.sql") (case-insensitive on Windows)
                n.to_lowercase().contains(&prefix.to_lowercase())
                    && n.to_lowercase().ends_with(".sql")
            })
            .collect();
        if !files.is_empty() {
            return Some((test, files));
        }
    }
    None
}

/// `MySqlDatabaseUpdater.Run`: applies pending `SQL/updates/*<prefix>*.sql` scripts.
/// Returns `false` if the database could not be brought up to date.
pub async fn run_updates(
    pool: &MySqlPool,
    prefix: &str,
    schema: &str,
    auto_apply: bool,
) -> anyhow::Result<bool> {
    tracing::debug!("Updating database for {}", prefix);
    let mut conn = pool.acquire().await?;

    let exists = updates_table_exists(&mut conn, schema).await?;
    if !exists && !create_updates_table(&mut conn).await {
        tracing::error!("Was unable to create updates table in {} !", schema);
        return Ok(false);
    }

    let Some((folder, mut all_files)) = find_updates_folder(prefix) else {
        tracing::info!("No sql update folder or files found.");
        return Ok(true);
    };
    all_files.sort();

    // If we're running this version for the first time, assume that all updates have been
    // installed before
    if !exists {
        initialize_updates_table(&mut conn, &all_files, prefix).await?;
    }

    let rows: Vec<(String, i64)> = sqlx::query_as(
        "SELECT script_name, CAST(installed AS SIGNED) FROM `updates` ORDER BY `script_name`",
    )
    .fetch_all(&mut *conn)
    .await?;
    let mut to_run = Vec::new();
    let mut already = Vec::new();
    for (name, installed) in rows {
        let full = folder.join(&name);
        if full.exists() {
            if installed <= 0 {
                to_run.push(full);
            } else {
                already.push(full);
            }
        }
    }
    // Add remaining files that are not in the DB yet
    for f in all_files {
        if !to_run.contains(&f) && !already.contains(&f) && f.exists() {
            to_run.push(f);
        }
    }

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
        return install_update_files(&mut conn, &to_run, false).await;
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
    use std::io::Write;
    let _ = std::io::stdout().flush();
    let answer = tokio::task::spawn_blocking(|| {
        let mut line = String::new();
        std::io::stdin()
            .read_line(&mut line)
            .map(|_| line.trim_end_matches(['\r', '\n']).to_string())
    })
    .await??;
    if answer != "YES" && answer != "SKIP" {
        return Ok(false);
    }
    install_update_files(&mut conn, &to_run, answer == "SKIP").await
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn connection_string() {
        let o = parse_connection_string(
            "Server=db;Port=3307;User ID=root;Password=a=b;Database=aaemu_login;Pooling=true",
        )
        .unwrap();
        assert_eq!(o.get_host(), "db");
        assert_eq!(o.get_port(), 3307);
        assert_eq!(o.get_username(), "root");
        assert_eq!(o.get_database(), Some("aaemu_login"));
    }
}
