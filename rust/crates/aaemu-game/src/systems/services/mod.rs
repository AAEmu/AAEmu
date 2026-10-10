//! Phase-4 subsystem: game services and the remaining core managers.
//!
//! C# scope: `Services/**` (WebApi, DiscordBot), `GameService` / `Program` / `ManagerOrchestrator`
//! support, `Core/Managers/{TickManager, TaskManager2, TimedRewardsManager, AccountManager
//! (remainder), AccessLevelManager, CommandManager, ExpressTextManager, LocalizationManager,
//! MusicManager, RadarManager, SusManager, CharacterManager (remainder)}` and `Utils/Scripts/**`
//! (the command infrastructure the GM scripts build on).
//!
//! | module | C# |
//! |---|---|
//! | [`tick`] | `TickManager` (+ `TickEventHandler`) |
//! | [`tasks`] | `TaskManager2`, `Models/Tasks/Task` |
//! | [`accounts`] | `AccountManager` remainder (Add, dead connections, account values) |
//! | [`timed_rewards`] | `TimedRewardsManager`, `TimedRewardsTask` |
//! | [`beauty_salon`] | `CharacterManager.ApplyBeautySalon`, beauty salon packets |
//! | [`character`] | `CharacterManager` remainder (access level, dice, online tracking, offline crime points) |
//! | [`character_packets`] | the small character packets (weapon, visual options, appellation, idle, UI data, action slots, restrict check, LP, log-only requests) |
//! | [`commands`] | `CommandManager`, `AccessLevelManager`, `ICommand`, `Utils/Scripts/**` |
//! | [`expands`] | `bag_expands` / `expand_expert_limits`, `Inventory.ExpandSlot`, `CharacterActability.Regrade` / `ExpandExpert` + packets |
//! | [`item_ops`] | the inventory operations needed from the economy (`CHARACTER_ITEM_OPS`) |
//! | [`express_text`] | `ExpressTextManager`, `CSExpressEmotionPacket` |
//! | [`localization`] | `LocalizationManager` |
//! | [`models`] | `ModelManager` (complete) |
//! | [`music`] | `MusicManager`, music packets |
//! | [`radar`] | `RadarManager`, telescope / fish finder packets |
//! | [`shutdown`] | `Models/Tasks/ShutdownTask`, `SCNoticeMessagePacket` |
//! | [`sus`] | `SusManager` |
//! | [`webapi`] | `Services/WebApi/**` |
//! | [`discord`] | `Services/DiscordBotService` |
//! | [`game_service`] | `GameService` clock, `Program.UpTime`, `ManagerOrchestrator` |

pub mod accounts;
pub mod beauty_salon;
pub mod character;
pub mod character_packets;
pub mod commands;
pub mod discord;
pub mod expands;
pub mod express_text;
pub mod game_service;
pub mod item_ops;
pub mod localization;
pub mod models;
pub mod music;
pub mod radar;
pub mod shutdown;
pub mod sus;
pub mod tasks;
pub mod tick;
pub mod timed_rewards;
pub mod webapi;

#[cfg(test)]
mod manager_tests;

use std::sync::Arc;

use crate::data::FeatureSet;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::server::GameServer;
use crate::systems::{DataLoadCtx, HookSlot, SaveStage};

/// Game-data tables of this subsystem (`server.game_data.systems.services`).
#[derive(Debug, Default)]
pub struct Data {
    /// `ExpressTextManager`.
    pub express_texts: express_text::ExpressTexts,
    /// `LocalizationManager` (complete).
    pub localization: localization::LocalizationManager,
    /// `ModelManager` (complete).
    pub models: models::ModelManager,
    /// `CharacterManager` `bag_expands` / `expand_expert_limits`.
    pub expands: expands::ExpandData,
}

impl Data {
    /// `ExpressTextManager.Load`, `LocalizationManager.Load`.
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Ok(Self {
            express_texts: express_text::ExpressTexts::load(ctx.conn)?,
            localization: localization::LocalizationManager::load(ctx.conn, ctx.language)?,
            models: models::ModelManager::load(ctx.conn)?,
            expands: expands::ExpandData::load(ctx.conn)?,
        })
    }
}

/// Managers of this subsystem (`server.systems.services`).
#[derive(Debug, Default)]
pub struct System {
    /// `TickManager`.
    pub tick: tick::TickManager,
    /// `TaskManager`.
    pub tasks: Arc<tasks::TaskManager>,
    /// `CommandManager` (+ `AccessLevelManager`).
    pub commands: commands::CommandManager,
    /// `MusicManager`.
    pub music: music::MusicManager,
    /// `RadarManager`.
    pub radar: radar::RadarManager,
    /// `SusManager`.
    pub sus: sus::SusManager,
    /// `WebApiService`.
    pub webapi: webapi::WebApiService,
    /// `DiscordBotService`.
    pub discord: discord::DiscordBotService,
    /// The economy behaviour the WebApi mail / auction controllers call.
    pub webapi_economy_hooks: HookSlot<dyn webapi::hooks::WebApiEconomyHooks>,
    /// `SaveManager.ShutdownTask` (the pending GM shutdown).
    pub shutdown_task: std::sync::Mutex<Option<Arc<shutdown::ShutdownTask>>>,
    /// The runtime `FeaturesManager.Fsets` (`None` = the loaded game data).
    features: commands::sub::RuntimeFeatures,
}

impl System {
    /// The startup work of the managers of this subsystem, in `GameService.StartAsync` order:
    /// `ILoadable.Load` (access levels, music), the command scripts, `TaskManager.Start` and
    /// `IInitializable.Initialize` (tick thread, dead-connection sweep, timed rewards, radar).
    /// `CharacterManager.StartOnlineTracking` (stage 5) and the WebApi / Discord hosted services
    /// are started by `main.rs`.
    pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
        let s = &server.systems.services;
        s.commands.access_levels.load(&server.config.access_level);
        s.music.load(server).await?;
        // Scripts (`ScriptReflector.Reflect` / `ScriptCompiler.Compile`).
        commands::scripts::install(
            &s.commands,
            commands::scripts::ScriptEnv {
                app_path: server.config.app_path.clone(),
            },
        );
        s.commands.reload();
        s.tasks.start(&s.tick.on_tick);
        // Stage 4: `IInitializable.Initialize()`.
        s.tick.initialize();
        s.tasks.initialize();
        accounts::initialize(server);
        timed_rewards::initialize(server);
        radar::RadarManager::initialize(server);
        Ok(())
    }

    /// The hosted services after `GameService.StartAsync` (`WebApiService`,
    /// `DiscordBotService`).
    pub async fn start_hosted_services(server: &Arc<GameServer>) -> anyhow::Result<()> {
        let s = &server.systems.services;
        s.webapi.start(server).await?;
        s.discord.start(server);
        Ok(())
    }

    /// The hosted services stop in reverse registration order (Discord, then WebApi).
    pub async fn stop_hosted_services(&self) {
        self.discord.stop().await;
        self.webapi.stop().await;
    }

    /// This subsystem's part of `SaveManager.DoSave` (nothing: its managers write directly).
    pub async fn save(
        &self,
        _server: &GameServer,
        _tx: &mut sqlx::Transaction<'_, sqlx::MySql>,
        _stage: SaveStage,
    ) -> crate::Result<usize> {
        Ok(0)
    }

    /// The installed economy hooks of the WebApi (the "not available" defaults otherwise).
    pub fn webapi_economy(&self) -> Arc<dyn webapi::hooks::WebApiEconomyHooks> {
        self.webapi_economy_hooks
            .get()
            .unwrap_or_else(|| Arc::new(webapi::hooks::NotPorted))
    }

    /// `FeaturesManager.Fsets`.
    pub fn features(&self, server: &GameServer) -> FeatureSet {
        self.features
            .0
            .read()
            .ok()
            .and_then(|f| f.clone())
            .unwrap_or_else(|| server.game_data.features.clone())
    }

    /// `FeaturesManager.Fsets.Set(feature, enable)` on the runtime copy.
    pub fn set_feature(&self, server: &GameServer, bit: u8, enable: bool) -> bool {
        let mut guard = self.features.0.write().expect("features poisoned");
        let fs = guard.get_or_insert_with(|| server.game_data.features.clone());
        fs.set_bit(bit, enable)
    }
}

/// Registers this subsystem's C->S packets.
pub fn register(reg: &mut PacketRegistry) {
    character_packets::register(reg);
    expands::register(reg);
    reg.register::<character::CSRollDicePacket>(LEVEL_PLAIN, cs::CSRollDicePacket);
    reg.register::<beauty_salon::CSEnterBeautySalonPacket>(
        LEVEL_PLAIN,
        cs::CSEnterBeautySalonPacket,
    );
    reg.register::<beauty_salon::CSExitBeautySalonPacket>(LEVEL_PLAIN, cs::CSExitBeautySalonPacket);
    reg.register::<beauty_salon::CSBeautyshopDataPacket>(LEVEL_PLAIN, cs::CSBeautyshopDataPacket);
    reg.register::<express_text::CSExpressEmotionPacket>(LEVEL_PLAIN, cs::CSExpressEmotionPacket);
    reg.register::<music::CSSaveUserMusicNotesPacket>(LEVEL_PLAIN, cs::CSSaveUserMusicNotesPacket);
    reg.register::<music::CSSendUserMusicPacket>(LEVEL_PLAIN, cs::CSSendUserMusicPacket);
    reg.register::<music::CSRequestMusicNotesPacket>(LEVEL_PLAIN, cs::CSRequestMusicNotesPacket);
    reg.register::<music::CSEndMusicPacket>(LEVEL_PLAIN, cs::CSEndMusicPacket);
}
