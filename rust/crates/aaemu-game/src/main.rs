//! `aaemu-game [APP_PATH] [configuration overrides...]` — `Program.Main` + the hosted services
//! (`GameService`, `WebApiService`, `DiscordBotService`).
//!
//! APP_PATH holds `Config.json`, `Configurations/`, `Config.Local.json` and
//! `Data/compact.sqlite3`. It is taken from the first argument when that names an existing
//! directory; otherwise it is `AAEMU_APP_PATH`, else the executable's directory or the current
//! directory when it holds a `Config.json` (published layout), else this crate's `app/` folder
//! (`cargo run` in a source checkout). Every other
//! argument is a configuration override like the C# command-line provider
//! (`--World:ExpRate=2`, `/Network:Port 1239`, `Id=2`). `compiler-check` as the first
//! argument checks the scripts and exits (they are compiled in: always successful).

use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Instant;

use aaemu_game::config::AppConfig;
use aaemu_game::data::client_files::ClientFileManager;
use aaemu_game::data::GameData;
use aaemu_game::systems::services::{self, game_service};
use aaemu_game::{login, net, GameServer};
use anyhow::Context;
use sqlx::mysql::MySqlPoolOptions;

/// Splits the command line into the app path and the configuration arguments.
fn split_args(args: Vec<String>) -> (PathBuf, Vec<String>) {
    match args.first() {
        Some(first)
            if !first.starts_with("--") && !first.contains('=') && Path::new(first).is_dir() =>
        {
            (PathBuf::from(first), args[1..].to_vec())
        }
        _ => (
            aaemu_commons::file_manager::resolve_app_dir(
                &Path::new(env!("CARGO_MANIFEST_DIR")).join("app"),
            ),
            args,
        ),
    }
}

/// `Program.Initialization()`.
fn initialization() {
    game_service::program_initialization();
    tracing::info!(
        "AAEmu.Game version {} ({})",
        env!("CARGO_PKG_VERSION"),
        env!("CARGO_PKG_NAME")
    );
    tracing::info!(
        "Running as {}-bits on {} ({})",
        usize::BITS,
        std::env::consts::OS,
        std::env::consts::ARCH
    );
    if usize::BITS < 64 {
        tracing::warn!("Running in 32-bits mode is not recommended to do memory constraints");
    }
}

/// "Press Ctrl+C to quit": a failed `GameService.StartAsync` returns early and the host keeps
/// running until it is stopped.
async fn wait_for_ctrl_c() -> anyhow::Result<()> {
    tracing::error!("Press Ctrl+C to quit");
    let _ = tokio::signal::ctrl_c().await;
    Ok(())
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let result = run().await;
    if let Err(e) = &result {
        tracing::error!("Fatal: {e:#}");
    }
    aaemu_commons::logging::flush();
    result
}

async fn run() -> anyhow::Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let (app_path, config_args) = split_args(args.clone());
    // NLog.config: console, Logs/Server.log and Logs/Error.log. Under the application
    // directory (the executable's directory in the published layout), so login and game
    // servers built into one target directory do not share (and delete) each other's logs.
    aaemu_commons::logging::init(&app_path);
    initialization();

    if args.first().map(String::as_str) == Some("compiler-check") {
        tracing::info!("Check compilation");
        tracing::info!("Compilation successful");
        return Ok(());
    }

    aaemu_commons::file_manager::remember_app_dir(&app_path);
    tracing::info!("Application directory: {}", app_path.display());
    // BuildConfiguration: JSON files, user secrets, environment, command line.
    let config = AppConfig::load_with(&app_path, &config_args).context("loading configuration")?;

    // Test the MySQL connection ("MySQL connection failed, check your configuration!" is fatal).
    let db = MySqlPoolOptions::new()
        .max_connections(16)
        // Saves run concurrently (logouts, SaveManager): `DELETE ... WHERE character_id = ?` on
        // an empty range takes next-key gap locks under REPEATABLE READ, and the following
        // INSERTs of other characters' rows deadlock on them (seen with 50 bots logging out at
        // once). READ COMMITTED takes no gap locks.
        .after_connect(|conn, _meta| {
            Box::pin(async move {
                use sqlx::Executor;
                conn.execute("SET SESSION TRANSACTION ISOLATION LEVEL READ COMMITTED")
                    .await?;
                Ok(())
            })
        })
        .connect_with(config.connections.mysql_provider.connect_options())
        .await
        .context("MySQL connection failed, check your configuration!")?;

    // Test the SQLite connection.
    let sqlite = config.app_path.join("Data").join("compact.sqlite3");
    rusqlite::Connection::open_with_flags(&sqlite, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
        .ok()
        .filter(|_| sqlite.exists())
        .context("Failed to load compact.sqlite3 database check if it exists!")?;

    // ---- GameService.StartAsync ----
    tracing::info!("Starting daemon: AAEmu.Game");
    let _game_service = game_service::GameService::new(
        game_service::ManagerOrchestrator::default(),
        Arc::new(game_service::SystemTimeProvider),
    );

    // Check for updates.
    {
        let mut conn = db.acquire().await.context("MySQL connection")?;
        let updated = aaemu_commons::mysql::run_updates(
            &mut conn,
            "aaemu_game",
            &config.connections.mysql_provider.database,
            config.connections.auto_apply_updates,
        )
        .await
        .unwrap_or_else(|e| {
            tracing::error!("{e}");
            false
        });
        if !updated {
            tracing::error!("Failed to update database!");
            return wait_for_ctrl_c().await;
        }
    }

    // ClientFileManager.Initialize.
    if ClientFileManager::from_sources(&config.client_data.sources, &config.app_path)
        .sources()
        .is_empty()
    {
        tracing::error!(
            "Failed up load client files! ({})",
            config.client_data.sources.join(", ")
        );
        return wait_for_ctrl_c().await;
    }

    let stop_watch = Instant::now();

    // Auto-attack: make sure `holdables` has the weapon animation columns.
    aaemu_game::utils::holdables_schema::ensure_columns(&config.app_path);

    // FormulaManager.Load, ItemManager.Load and the orchestrated ILoadable game data (a failing
    // loader is fatal, as in the 1.2 C#).
    let mut game_data = GameData::load(
        &config.app_path,
        &config.client_data.sources,
        &config.default_language,
    )
    .context("Failed to load compact.sqlite3 database check if it exists!")?;
    // The retail Halloween `game_schedules` rows are dated 2013/2014 and compact.sqlite3 is
    // read-only: `Events.Halloween` repeats the window in memory (`systems::npcs::schedule`).
    game_data
        .systems
        .npcs
        .schedule
        .apply_halloween_window(&config.events.halloween, chrono::Utc::now().timestamp());

    // WorldManager.CreateStaticInstances (main_world) happens in GameServer::new.
    let server = Arc::new(GameServer::new(config, db, game_data));
    server.set_self();
    tracing::info!("{} packet handlers registered", server.registry.len());

    // ItemManager.LoadUserItems + the orchestrated DB loads (id managers, names, expeditions,
    // friends, UCCs).
    server.ids.initialize(&server.db).await;
    server
        .items
        .load_user_items(
            &server.db,
            &server.game_data.items,
            server.config.id,
            &server.ids.container,
        )
        .await
        .context("ItemManager.LoadUserItems")?;
    server.names.load(&server.db).await.context("NameManager")?;
    server
        .expeditions
        .load(&server.db)
        .await
        .context("ExpeditionManager")?;
    server
        .friends
        .load(&server.db)
        .await
        .context("FriendManager")?;
    server
        .streams
        .ucc
        .load(&server.db)
        .await
        .context("loading UCCs")?;
    server
        .streams
        .set_world(Arc::new(aaemu_game::stream_world::GameStreamWorld {
            world: server.world.clone(),
            names: server.names.clone(),
            items: server.items.clone(),
            ids: server.ids.clone(),
            game_data: server.game_data.clone(),
            server_id: server.config.id,
        }));

    // TimeManager.Start, then the subsystems' loads / TaskManager.Start / Initialize (incl.
    // SaveManager.Initialize).
    server.time.start();
    aaemu_game::systems::start_all(&server)
        .await
        .context("starting subsystems")?;
    aaemu_game::managers::save::SaveManager::start(server.clone());

    // Stage 5: deleted characters, online tracking, networks.
    aaemu_game::managers::character::schedule_deleted_characters_check(server.clone()).await;
    services::character::start_online_tracking(&server);

    let network = tokio::spawn(net::listener::run(server.clone()));
    let stream_network = tokio::spawn(aaemu_game::stream::listener::run(server.clone()));
    tokio::spawn(login::run(server.clone()));
    tracing::info!("Server started! Took {:?}", stop_watch.elapsed());

    // WebApiService / DiscordBotService.StartAsync.
    services::System::start_hosted_services(&server).await?;

    tokio::select! {
        res = network => { res.context("network task panicked")?.context("game network")?; }
        res = stream_network => { res.context("stream network task panicked")?.context("stream network")?; }
        _ = tokio::signal::ctrl_c() => {}
    }

    // ---- shutdown: hosted services in reverse order ----
    // (shared with the graceful `/scripts shutdown`)
    aaemu_game::systems::services::shutdown::stop_server(&server).await;
    // Dispose.
    tracing::info!("Disposing...");
    Ok(())
}
