//! Server wiring (`Program.cs` / `LoginService.cs`): shared state and startup sequence.

use std::sync::Arc;

use sqlx::MySqlPool;
use tokio_util::sync::CancellationToken;

use crate::backend::{AuthBackend, DbAuthBackend};
use crate::config::AppConfig;
use crate::controllers::game::GameController;
use crate::controllers::login::LoginController;
use crate::controllers::request::RequestController;
use crate::id_manager::SimpleIdManager;
use crate::network::internal::{self, InternalConnectionTable};
use crate::network::login::{self, LoginConnectionTable};
use crate::services::password::PasswordService;
use crate::services::two_factor::{
    ArsService, OtpService, PcCertService, TwoFactorRepository, TwoFactorService,
};
use crate::{db, health};

/// Shared server state (the C# DI singletons).
pub struct LoginServer {
    pub config: AppConfig,
    pub pool: MySqlPool,
    pub passwords: PasswordService,
    pub requests: Arc<RequestController>,
    pub connections: Arc<LoginConnectionTable>,
    pub internal_connections: InternalConnectionTable,
    pub connection_ids: Arc<SimpleIdManager>,
    pub games: GameController,
    pub logins: Arc<LoginController>,
    pub two_factor: TwoFactorService,
    pub otp: OtpService,
    pub pc_cert: PcCertService,
    pub ars: Arc<ArsService>,
    /// The account/2FA operations used by the authentication flows ([`DbAuthBackend`] in
    /// production).
    pub auth: Arc<dyn AuthBackend>,
}

impl LoginServer {
    pub fn new(config: AppConfig, pool: MySqlPool) -> Arc<Self> {
        Self::build(config, pool, None)
    }

    /// Like [`Self::new`], but the authentication flows use `auth` instead of the database-backed
    /// [`DbAuthBackend`] (the C# tests inject mocked controllers/services the same way).
    pub fn with_auth_backend(
        config: AppConfig,
        pool: MySqlPool,
        auth: Arc<dyn AuthBackend>,
    ) -> Arc<Self> {
        Self::build(config, pool, Some(auth))
    }

    fn build(config: AppConfig, pool: MySqlPool, auth: Option<Arc<dyn AuthBackend>>) -> Arc<Self> {
        let passwords = PasswordService::default();
        let requests = Arc::new(RequestController::new());
        let connections = Arc::new(LoginConnectionTable::default());
        let repo = TwoFactorRepository::new(pool.clone());
        let logins = Arc::new(LoginController::new(
            pool.clone(),
            passwords.clone(),
            config.auto_account,
            config.korea_auth.clone(),
        ));
        let two_factor = TwoFactorService::new(repo.clone());
        let otp = OtpService::new(repo.clone());
        let pc_cert = PcCertService::new(repo, passwords.clone());
        let ars = Arc::new(ArsService::default());
        let auth = auth.unwrap_or_else(|| {
            Arc::new(DbAuthBackend {
                logins: logins.clone(),
                two_factor: two_factor.clone(),
                otp: otp.clone(),
                pc_cert: pc_cert.clone(),
                ars: ars.clone(),
            })
        });
        Arc::new(Self {
            games: GameController::new(requests.clone(), connections.clone()),
            logins,
            two_factor,
            otp,
            pc_cert,
            ars,
            auth,
            internal_connections: InternalConnectionTable::default(),
            connection_ids: Arc::new(SimpleIdManager::new()),
            config,
            pool,
            passwords,
            requests,
            connections,
        })
    }
}

/// Runs the login server until `shutdown` is cancelled.
pub async fn run(config: AppConfig, shutdown: CancellationToken) -> anyhow::Result<()> {
    let options = db::connect_options(&config)?;
    let pool = db::create_pool(options.clone());

    // MySqlInitializer: test the DB connection
    match db::test_connection(&options).await {
        Ok(version) => tracing::info!(
            "MySQL connection established successfully. Server version {}",
            version
        ),
        Err(e) => {
            tracing::error!("MySQL connection failed, check your configuration! {:#}", e);
            return Err(e);
        }
    }

    // LoginService.StartAsync
    tracing::info!("Starting daemon: AAEmu.Login");
    let schema = config.connections.mysql_provider.database.clone();
    if !db::run_updates(
        &pool,
        "aaemu_login",
        &schema,
        config.connections.auto_apply_updates,
    )
    .await?
    {
        tracing::error!("Failed to update database!");
        tracing::error!("Press Ctrl+C to quit");
        shutdown.cancelled().await;
        return Ok(());
    }

    let server = LoginServer::new(config, pool);
    server.requests.initialize();
    server.games.load(&server.config).await;

    let internal_listener = internal::bind(
        &server.config.internal_network.host,
        server.config.internal_network.port,
    )
    .await?;
    let internal_task = tokio::spawn(internal::run(
        server.clone(),
        internal_listener,
        shutdown.clone(),
    ));

    let login_listener =
        login::bind(&server.config.network.host, server.config.network.port).await?;
    tracing::info!(
        "Login network listening on {}:{}",
        server.config.network.host,
        server.config.network.port
    );
    let login_task = tokio::spawn(login::run(server.clone(), login_listener, shutdown.clone()));

    // Health endpoints on the HTTP URLs of the `urls` setting (or `ASPNETCORE_URLS`)
    if let Some(urls) = &server.config.urls {
        for port in health::ports_from_urls(urls) {
            let shutdown = shutdown.clone();
            tokio::spawn(async move {
                if let Err(e) = health::run(port, shutdown).await {
                    tracing::error!("Health check listener on port {} failed: {}", port, e);
                }
            });
        }
    }

    shutdown.cancelled().await;
    tracing::info!("Stopping daemon.");
    let _ = internal_task.await;
    let _ = login_task.await;
    for conn in server.connections.all() {
        conn.shutdown();
    }
    server.pool.close().await;
    tracing::info!("Disposing...");
    Ok(())
}
