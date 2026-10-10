//! `aaemu-game [APP_PATH]` — APP_PATH holds `Config.json`, `Configurations/`, `Config.Local.json`
//! and `Data/compact.sqlite3` (defaults to the current directory, the C# server uses its exe dir).

use std::path::PathBuf;
use std::sync::Arc;

use aaemu_game::config::AppConfig;
use aaemu_game::data::GameData;
use aaemu_game::{login, net, GameServer};
use anyhow::Context;
use sqlx::mysql::MySqlPoolOptions;
use tracing_subscriber::EnvFilter;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")),
        )
        .init();
    tracing::info!("Starting daemon: AAEmu.Game (Rust)");

    let app_path = std::env::args()
        .nth(1)
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("."));
    let config = AppConfig::load(&app_path).context("loading configuration")?;

    // Program.cs: "MySQL connection failed, check your configuration!" is fatal.
    let db = MySqlPoolOptions::new()
        .max_connections(16)
        .connect_with(config.connections.mysql_provider.connect_options())
        .await
        .context("MySQL connection failed, check your configuration!")?;

    // Program.cs: "Failed to load compact.sqlite3 database check if it exists!" is fatal, and so
    // is any failing loader (the 1.2 ManagerOrchestrator / GameDataManager are not tolerant).
    let game_data = GameData::load(
        &config.app_path,
        &config.client_data.sources,
        &config.default_language,
    )
    .context("Failed to load compact.sqlite3 database check if it exists!")?;

    let server = Arc::new(GameServer::new(config, db, game_data));
    server.set_self();
    tracing::info!("{} packet handlers registered", server.registry.len());

    // ManagerOrchestrator (the DB-backed managers of this phase).
    server.ids.initialize(&server.db).await;
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
        .items
        .load_user_items(
            &server.db,
            &server.game_data.items,
            server.config.id,
            &server.ids.container,
        )
        .await
        .context("ItemManager.LoadUserItems")?;
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
    server.time.start();
    aaemu_game::managers::save::SaveManager::start(server.clone());
    aaemu_game::managers::character::schedule_deleted_characters_check(server.clone()).await;

    // UccManager.Load (+ UccIdManager) from the `uccs` table.
    server
        .streams
        .ucc
        .load(&server.db)
        .await
        .context("loading UCCs")?;

    // Phase-3 subsystems (their managers' loads, timers and hooks).
    aaemu_game::systems::start_all(&server)
        .await
        .context("starting subsystems")?;

    tokio::spawn(login::run(server.clone()));
    let network = tokio::spawn(net::listener::run(server.clone()));
    let stream_network = tokio::spawn(aaemu_game::stream::listener::run(server.clone()));

    tokio::select! {
        res = network => { res.context("network task panicked")?.context("game network")?; }
        res = stream_network => { res.context("stream network task panicked")?.context("stream network")?; }
        _ = tokio::signal::ctrl_c() => tracing::info!("Stopping daemon ..."),
    }
    // SaveManager.StopAsync: one final save.
    server.save.stop(&server).await;
    Ok(())
}
