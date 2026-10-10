//! Port of `AAEmu.Game.Models.AppConfiguration` and the configuration loading in `Program.cs`.
//!
//! The C# server binds `Microsoft.Extensions.Configuration` over, in order:
//! `Config.json`, every `Configurations/**/*.json` (ordinal-sorted), then `Config.Local.json`.
//! Later files override earlier ones key-by-key; keys are case-insensitive and scalar values may
//! be given as strings (e.g. `"Port": "1234"`). Arrays are merged element-by-element by index, as
//! the Microsoft binder flattens them to `Key:0`, `Key:1`, ...
//!
//! After the files come the user secrets (`AddUserSecrets<GameService>`: `secrets.json` of the
//! `UserSecretsId` of `AAEmu.Game.csproj`), the environment variables (`Section__Key`) and the
//! command line (`--Section:Key=value`, `/Key value`, `Key=value`), as in `BuildConfiguration`.
//!
//! Every member of `AppConfiguration` / `Models/Game/Configurations.cs` is bound (full parity);
//! unknown keys are ignored. [`ConfigCell`] gives the runtime the C# `AppConfiguration.Instance`
//! semantics: reads see the current values, GM commands (`World set ...`) and
//! `Program.ReloadConfiguration` replace them.

use std::fmt::Display;
use std::path::{Path, PathBuf};
use std::str::FromStr;

use serde::de::{self, Deserializer};
use serde::Deserialize;
use serde_json::{Map, Value};

/// Implements [`ConfigEnum`] (the binder accepts the member name, case-insensitively, or its
/// numeric value).
macro_rules! impl_config_enum {
    ($t:ident { $($v:ident = $n:literal),* $(,)? }) => {
        impl ConfigEnum for $t {
            fn parse_config(s: &str) -> Option<Self> {
                $(if s.eq_ignore_ascii_case(stringify!($v)) || s == stringify!($n) {
                    return Some(Self::$v);
                })*
                None
            }
        }
    };
}

/// An enum bound from the configuration (`Enum.Parse(value, ignoreCase: true)`).
pub trait ConfigEnum: Sized {
    fn parse_config(s: &str) -> Option<Self>;
}

#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct AppConfig {
    #[serde(rename = "id", deserialize_with = "lenient")]
    pub id: u8,
    #[serde(rename = "additionalesid", deserialize_with = "lenient_vec")]
    pub additionales_id: Vec<u8>,
    #[serde(rename = "secretkey")]
    pub secret_key: String,
    #[serde(rename = "connections")]
    pub connections: DbConnections,
    #[serde(rename = "network")]
    pub network: NetworkConfig,
    #[serde(rename = "streamnetwork")]
    pub stream_network: NetworkConfig,
    #[serde(rename = "loginnetwork")]
    pub login_network: NetworkConfig,
    /// `WebApiNetwork` (`None` = section missing: "WebApi will not start").
    #[serde(rename = "webapinetwork")]
    pub web_api_network: Option<NetworkConfig>,
    /// `MaxConcurencyThreadPool` (bound but unused by the C# server as well).
    #[serde(rename = "maxconcurencythreadpool", deserialize_with = "lenient")]
    pub max_concurency_thread_pool: i32,
    /// `DiscordToken` (`None` / empty = the Discord bot does not start).
    #[serde(rename = "discordtoken")]
    pub discord_token: Option<String>,
    /// `AccessLevel` (command name -> level). Keys keep their spelling (the C# lookup in
    /// `AccessLevelManager.GetLevel` is case-sensitive); filled by [`AppConfig::from_value`].
    #[serde(skip)]
    pub access_level: Vec<(String, i32)>,
    #[serde(rename = "account")]
    pub account: AccountConfig,
    #[serde(rename = "labor")]
    pub labor: CurrencyValuesConfig,
    /// `LaborOffline` (labor granted per offline tick on login).
    #[serde(rename = "laboroffline")]
    pub labor_offline: CurrencyValuesConfig,
    #[serde(rename = "credits")]
    pub credits: CurrencyValuesConfig,
    #[serde(rename = "loyalty")]
    pub loyalty: CurrencyValuesConfig,
    #[serde(rename = "world")]
    pub world: WorldConfig,
    #[serde(rename = "clientdata")]
    pub client_data: ClientDataConfig,
    /// `Specialty`.
    #[serde(rename = "specialty")]
    pub specialty: SpecialtyConfig,
    /// `Scripts`.
    #[serde(rename = "scripts")]
    pub scripts: ScriptsConfig,
    /// `CharacterNameRegex` (`None` = NameManager's default pattern).
    #[serde(rename = "characternameregex")]
    pub character_name_regex: Option<String>,
    #[serde(rename = "defaultlanguage")]
    pub default_language: String,
    #[serde(rename = "debuginfo", deserialize_with = "lenient")]
    pub debug_info: bool,
    #[serde(rename = "debuginfolevel", deserialize_with = "lenient")]
    pub debug_info_level: u32,
    /// `Expedition` / `Justice` (`systems::social::config`).
    #[serde(rename = "expedition")]
    pub expedition: crate::systems::social::config::ExpeditionConfig,
    #[serde(rename = "justice")]
    pub justice: crate::systems::social::config::JusticeConfig,
    /// `Dungeons` (`systems::instances::config`).
    #[serde(rename = "dungeons")]
    pub dungeons: crate::systems::instances::config::DungeonsConfig,
    /// `HeightMapsEnable` (C# default false; the shipped Config.json sets it to true).
    #[serde(rename = "heightmapsenable", deserialize_with = "lenient")]
    pub heightmaps_enable: bool,
    /// `FileManager.AppPath`: the directory holding `Config.json` and `Data/compact.sqlite3`.
    #[serde(skip)]
    pub app_path: PathBuf,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            id: 0,
            additionales_id: Vec::new(),
            secret_key: String::new(),
            connections: DbConnections::default(),
            network: NetworkConfig::default(),
            stream_network: NetworkConfig::default(),
            login_network: NetworkConfig::default(),
            web_api_network: None,
            max_concurency_thread_pool: 0,
            discord_token: None,
            access_level: Vec::new(),
            account: AccountConfig::default(),
            labor: CurrencyValuesConfig::default(),
            labor_offline: CurrencyValuesConfig::default(),
            credits: CurrencyValuesConfig::default(),
            loyalty: CurrencyValuesConfig::default(),
            world: WorldConfig::default(),
            client_data: ClientDataConfig::default(),
            specialty: SpecialtyConfig::default(),
            scripts: ScriptsConfig::default(),
            character_name_regex: None,
            default_language: "en_us".into(),
            debug_info: true,
            debug_info_level: 100,
            expedition: Default::default(),
            justice: Default::default(),
            dungeons: Default::default(),
            heightmaps_enable: false,
            app_path: PathBuf::from("."),
        }
    }
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct NetworkConfig {
    #[serde(rename = "host")]
    pub host: String,
    #[serde(rename = "port", deserialize_with = "lenient")]
    pub port: u16,
    #[serde(rename = "numconnections", deserialize_with = "lenient")]
    pub num_connections: i32,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct DbConnections {
    #[serde(rename = "mysqlprovider")]
    pub mysql_provider: MySqlConnectionSettings,
    /// `AutoApplyUpdates` (apply DB schema updates without a prompt).
    #[serde(rename = "autoapplyupdates", deserialize_with = "lenient")]
    pub auto_apply_updates: bool,
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct MySqlConnectionSettings {
    #[serde(rename = "host")]
    pub host: String,
    #[serde(rename = "port", deserialize_with = "lenient")]
    pub port: u16,
    #[serde(rename = "user")]
    pub user: String,
    #[serde(rename = "password")]
    pub password: String,
    #[serde(rename = "database")]
    pub database: String,
}

impl MySqlConnectionSettings {
    pub fn connect_options(&self) -> sqlx::mysql::MySqlConnectOptions {
        sqlx::mysql::MySqlConnectOptions::new()
            .host(&self.host)
            .port(self.port)
            .username(&self.user)
            .password(&self.password)
            .database(&self.database)
    }
}

/// `AccountConfig`.
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct AccountConfig {
    /// `NameRegex` (allowed account names).
    #[serde(rename = "nameregex")]
    pub name_regex: String,
    #[serde(rename = "accessleveldefault", deserialize_with = "lenient")]
    pub access_level_default: i32,
    #[serde(rename = "accesslevelfirstaccount", deserialize_with = "lenient")]
    pub access_level_first_account: i32,
    #[serde(rename = "accesslevelfirstcharacter", deserialize_with = "lenient")]
    pub access_level_first_character: i32,
    #[serde(rename = "deletereleasename", deserialize_with = "lenient")]
    pub delete_release_name: bool,
    #[serde(rename = "deletetimings")]
    pub delete_timings: Vec<AccountDeleteDelayTiming>,
}

impl Default for AccountConfig {
    fn default() -> Self {
        Self {
            name_regex: "^[a-zA-Z0-9]{1,18}$".into(),
            access_level_default: 0,
            access_level_first_account: 100,
            access_level_first_character: 100,
            delete_release_name: false,
            delete_timings: Vec::new(),
        }
    }
}

/// `AccountDeleteDelayTiming`.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct AccountDeleteDelayTiming {
    #[serde(rename = "level", deserialize_with = "lenient")]
    pub level: u8,
    /// Minutes.
    #[serde(rename = "delay", deserialize_with = "lenient")]
    pub delay: i32,
}

/// `WindModelType`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum WindModelType {
    /// Retail-like: wind only along the N-S axis.
    #[default]
    Official,
    /// Wind direction rotates over the day.
    Realistic,
}

/// `SeaWeatherModelType`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum SeaWeatherModelType {
    #[default]
    Official,
    Realistic,
}

impl_config_enum!(WindModelType { Official = 0, Realistic = 1 });
impl_config_enum!(SeaWeatherModelType { Official = 0, Realistic = 1 });

/// `WorldConfig` (`Configurations/World.json`).
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct WorldConfig {
    /// `MOTD`.
    #[serde(rename = "motd")]
    pub motd: String,
    #[serde(rename = "logoutmessage")]
    pub logout_message: String,
    /// Minutes.
    #[serde(rename = "autosaveinterval", deserialize_with = "lenient")]
    pub auto_save_interval: f64,
    #[serde(rename = "exprate", deserialize_with = "lenient")]
    pub exp_rate: f64,
    #[serde(rename = "honorrate", deserialize_with = "lenient")]
    pub honor_rate: f64,
    #[serde(rename = "pvphonorrate", deserialize_with = "lenient")]
    pub pvp_honor_rate: f64,
    #[serde(rename = "vocationrate", deserialize_with = "lenient")]
    pub vocation_rate: f64,
    #[serde(rename = "lootrate", deserialize_with = "lenient")]
    pub loot_rate: f64,
    #[serde(rename = "goldlootmultiplier", deserialize_with = "lenient")]
    pub gold_loot_multiplier: f64,
    /// `GrowthRate` (doodad growth time divisor).
    #[serde(rename = "growthrate", deserialize_with = "lenient")]
    pub growth_rate: f64,
    #[serde(rename = "daysfortaxpayment", deserialize_with = "lenient")]
    pub days_for_tax_payment: u32,
    #[serde(rename = "ignorefalldamageaccesslevel", deserialize_with = "lenient")]
    pub ignore_fall_damage_access_level: i32,
    #[serde(rename = "godmode", deserialize_with = "lenient")]
    pub god_mode: bool,
    /// `GeoDataMode` (navmesh heights and A* paths, `systems::ai::geodata`).
    #[serde(rename = "geodatamode", deserialize_with = "lenient")]
    pub geo_data_mode: bool,
    /// `PreLoadTerrain`: load every heightmap cell at startup instead of on demand.
    #[serde(rename = "preloadterrain", deserialize_with = "lenient")]
    pub pre_load_terrain: bool,
    #[serde(rename = "maxinstances", deserialize_with = "lenient")]
    pub max_instances: u32,
    #[serde(rename = "targetphysicstps", deserialize_with = "lenient")]
    pub target_physics_tps: f32,
    #[serde(rename = "windmodel", deserialize_with = "lenient_enum")]
    pub wind_model: WindModelType,
    #[serde(rename = "seaweathermodel", deserialize_with = "lenient_enum")]
    pub sea_weather_model: SeaWeatherModelType,
    #[serde(rename = "actabilityrate", deserialize_with = "lenient")]
    pub actability_rate: f64,
    #[serde(rename = "tagshareenabled", deserialize_with = "lenient")]
    pub tag_share_enabled: bool,
    #[serde(rename = "usepersistenthousedoodads", deserialize_with = "lenient")]
    pub use_persistent_house_doodads: bool,
    #[serde(rename = "explossrateatdeath", deserialize_with = "lenient")]
    pub exp_loss_rate_at_death: f32,
    #[serde(rename = "pvedurabilitylossrate", deserialize_with = "lenient")]
    pub pve_durability_loss_rate: f32,
    #[serde(rename = "pvpdurabilitylossrate", deserialize_with = "lenient")]
    pub pvp_durability_loss_rate: f32,
    #[serde(rename = "minimumexplosslevel", deserialize_with = "lenient")]
    pub minimum_exp_loss_level: i32,
}

impl Default for WorldConfig {
    fn default() -> Self {
        Self {
            motd: String::new(),
            logout_message: String::new(),
            auto_save_interval: 5.0,
            exp_rate: 1.0,
            honor_rate: 1.0,
            pvp_honor_rate: 1.0,
            vocation_rate: 1.0,
            loot_rate: 1.0,
            gold_loot_multiplier: 1.0,
            growth_rate: 1.0,
            days_for_tax_payment: 7,
            ignore_fall_damage_access_level: 100,
            god_mode: false,
            geo_data_mode: false,
            pre_load_terrain: false,
            max_instances: 32,
            target_physics_tps: 25.0,
            wind_model: WindModelType::Official,
            sea_weather_model: SeaWeatherModelType::Official,
            actability_rate: 1.0,
            tag_share_enabled: false,
            use_persistent_house_doodads: false,
            exp_loss_rate_at_death: 0.05,
            pve_durability_loss_rate: 1.0,
            pvp_durability_loss_rate: 1.0,
            minimum_exp_loss_level: 10,
        }
    }
}

/// `SpecialtyConfig` (`Configurations/Specialty.json`).
#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(default)]
pub struct SpecialtyConfig {
    #[serde(rename = "maxspecialtyratio", deserialize_with = "lenient")]
    pub max_specialty_ratio: i32,
    #[serde(rename = "minspecialtyratio", deserialize_with = "lenient")]
    pub min_specialty_ratio: i32,
    #[serde(rename = "ratiodecreaseperpack", deserialize_with = "lenient")]
    pub ratio_decrease_per_pack: f64,
    #[serde(rename = "ratioincreasepertick", deserialize_with = "lenient")]
    pub ratio_increase_per_tick: f64,
    #[serde(rename = "ratiodecreasetickminutes", deserialize_with = "lenient")]
    pub ratio_decrease_tick_minutes: f64,
    #[serde(rename = "ratioregentickminutes", deserialize_with = "lenient")]
    pub ratio_regen_tick_minutes: f64,
    #[serde(rename = "tradepackmaildelayinminutes", deserialize_with = "lenient")]
    pub trade_pack_mail_delay_in_minutes: f64,
}

impl Default for SpecialtyConfig {
    fn default() -> Self {
        Self {
            max_specialty_ratio: 130,
            min_specialty_ratio: 70,
            // `0.5f` widened to double.
            ratio_decrease_per_pack: f64::from(0.5f32),
            ratio_increase_per_tick: 5.0,
            ratio_decrease_tick_minutes: 1.0,
            ratio_regen_tick_minutes: 60.0,
            trade_pack_mail_delay_in_minutes: 480.0,
        }
    }
}

/// `ScriptsConfig.LoadStrategyType`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum LoadStrategyType {
    Compilation,
    #[default]
    Reflection,
}

impl_config_enum!(LoadStrategyType { Compilation = 0, Reflection = 1 });

/// `ScriptsConfig`.
#[derive(Debug, Clone, Default, PartialEq, Deserialize)]
#[serde(default)]
pub struct ScriptsConfig {
    #[serde(rename = "loadstrategy", deserialize_with = "lenient_enum")]
    pub load_strategy: LoadStrategyType,
}

/// `ClientDataConfig`.
#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct ClientDataConfig {
    #[serde(rename = "sources")]
    pub sources: Vec<String>,
}

/// `CurrencyValuesConfig` (Labor / Credits / Loyalty).
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct CurrencyValuesConfig {
    #[serde(rename = "default", deserialize_with = "lenient")]
    pub default: i32,
    #[serde(rename = "dailylogin", deserialize_with = "lenient")]
    pub daily_login: i32,
    #[serde(rename = "tickminutes", deserialize_with = "lenient")]
    pub tick_minutes: i32,
    #[serde(rename = "tickamount", deserialize_with = "lenient")]
    pub tick_amount: i32,
    #[serde(rename = "tickamountpremium", deserialize_with = "lenient")]
    pub tick_amount_premium: i32,
}

impl CurrencyValuesConfig {
    /// `GetTickAmount(isPremium)`.
    pub fn get_tick_amount(&self, is_premium: bool) -> i32 {
        if is_premium {
            self.tick_amount_premium
        } else {
            self.tick_amount
        }
    }
}

impl Default for CurrencyValuesConfig {
    fn default() -> Self {
        Self {
            default: 0,
            daily_login: 0,
            tick_minutes: 5,
            tick_amount: 0,
            tick_amount_premium: 0,
        }
    }
}

#[derive(Debug, thiserror::Error)]
pub enum ConfigError {
    #[error("{path}: {source}")]
    Io {
        path: PathBuf,
        source: std::io::Error,
    },
    #[error("{path}: {source}")]
    Json {
        path: PathBuf,
        source: serde_json::Error,
    },
    #[error("binding configuration: {0}")]
    Bind(serde_json::Error),
}

/// `UserSecretsId` of `AAEmu.Game.csproj` (`AddUserSecrets<GameService>()`).
pub const USER_SECRETS_ID: &str = "426ad5c3-9a72-44e9-88f5-4b5940b98c28";

/// The top-level `AppConfiguration` members (environment variables are only applied below
/// these keys; anything else could never bind).
const TOP_LEVEL_KEYS: [&str; 28] = [
    "id",
    "additionalesid",
    "secretkey",
    "connections",
    "network",
    "streamnetwork",
    "loginnetwork",
    "webapinetwork",
    "characternameregex",
    "maxconcurencythreadpool",
    "heightmapsenable",
    "discordtoken",
    "expedition",
    "world",
    "dungeons",
    "accesslevel",
    "account",
    "labor",
    "laboroffline",
    "credits",
    "loyalty",
    "clientdata",
    "specialty",
    "scripts",
    "justice",
    "defaultlanguage",
    "debuginfo",
    "debuginfolevel",
];

impl AppConfig {
    /// Loads `Config.json`, `Configurations/**/*.json` and `Config.Local.json` from `app_path`
    /// (every file optional, as `AddJsonFile(file, optional: true)`), then the user secrets
    /// and the environment variables (no command line).
    pub fn load(app_path: &Path) -> Result<Self, ConfigError> {
        Self::load_with(app_path, &[])
    }

    /// `Program.BuildConfiguration(args)` + bind: the JSON files, the user secrets
    /// ([`user_secrets_path`]), the environment variables and the command line `args`.
    pub fn load_with(app_path: &Path, args: &[String]) -> Result<Self, ConfigError> {
        Self::load_from(
            app_path,
            user_secrets_path().as_deref(),
            std::env::vars(),
            args,
        )
    }

    /// [`AppConfig::load_with`] with explicit sources (tests).
    pub fn load_from(
        app_path: &Path,
        secrets: Option<&Path>,
        env: impl IntoIterator<Item = (String, String)>,
        args: &[String],
    ) -> Result<Self, ConfigError> {
        let mut merged = Value::Object(Map::new());
        for file in config_files(app_path) {
            tracing::info!("Config: {}", file.display());
            if let Some(v) = read_config_json(&file)? {
                merge(&mut merged, expand_colon_keys(v));
            }
        }
        if let Some(path) = secrets {
            if let Some(v) = read_config_json(path)? {
                merge(&mut merged, expand_colon_keys(v));
            }
        }
        apply_env(&mut merged, env);
        apply_args(&mut merged, args);
        let mut cfg = Self::from_value(merged)?;
        cfg.app_path = app_path.to_path_buf();
        Ok(cfg)
    }

    /// Binds an already merged JSON document (keys in any case).
    pub fn from_value(value: Value) -> Result<Self, ConfigError> {
        let access_level = access_levels(&value)?;
        let mut cfg: Self =
            serde_json::from_value(lowercase_keys(value)).map_err(ConfigError::Bind)?;
        cfg.access_level = access_level;
        Ok(cfg)
    }

    /// `AccessLevel[command]` with the C# lookup (exact key).
    pub fn access_level_of(&self, command: &str) -> Option<i32> {
        self.access_level
            .iter()
            .find(|(k, _)| k == command)
            .map(|(_, v)| *v)
    }
}

/// The configuration files in `BuildConfiguration` order: `Config.json`,
/// `Configurations/**/*.json` (ordinal order of the full paths), `Config.Local.json`.
pub fn config_files(app_path: &Path) -> Vec<PathBuf> {
    let mut files = vec![app_path.join("Config.json")];
    let mut extra = Vec::new();
    collect_json(&app_path.join("Configurations"), &mut extra);
    extra.sort();
    files.extend(extra);
    files.push(app_path.join("Config.Local.json"));
    files
}

/// Reads one optional JSON configuration file (`None` when it does not exist).
fn read_config_json(path: &Path) -> Result<Option<Value>, ConfigError> {
    let text = match std::fs::read_to_string(path) {
        Ok(t) => t,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(source) => {
            return Err(ConfigError::Io {
                path: path.to_path_buf(),
                source,
            })
        }
    };
    serde_json::from_str(&relax_json(text.trim_start_matches('\u{feff}')))
        .map(Some)
        .map_err(|source| ConfigError::Json {
            path: path.to_path_buf(),
            source,
        })
}

/// `PathHelper.GetSecretsPathFromSecretsId`: `%APPDATA%/Microsoft/UserSecrets/<id>/secrets.json`
/// on Windows, `~/.microsoft/usersecrets/<id>/secrets.json` elsewhere (`None` when the base
/// directory is unknown).
pub fn user_secrets_path() -> Option<PathBuf> {
    if cfg!(windows) {
        std::env::var_os("APPDATA").map(|d| {
            PathBuf::from(d)
                .join("Microsoft")
                .join("UserSecrets")
                .join(USER_SECRETS_ID)
                .join("secrets.json")
        })
    } else {
        std::env::var_os("HOME").map(|d| {
            PathBuf::from(d)
                .join(".microsoft")
                .join("usersecrets")
                .join(USER_SECRETS_ID)
                .join("secrets.json")
        })
    }
}

/// JSON keys containing `:` are configuration paths (`"World:ExpRate": 2` in `secrets.json`).
pub fn expand_colon_keys(v: Value) -> Value {
    match v {
        Value::Object(m) => {
            let mut out = Value::Object(Map::new());
            for (k, v) in m {
                let mut nested = expand_colon_keys(v);
                for seg in k.rsplit(':').filter(|s| !s.is_empty()) {
                    let mut o = Map::new();
                    o.insert(seg.to_string(), nested);
                    nested = Value::Object(o);
                }
                merge(&mut out, nested);
            }
            out
        }
        Value::Array(a) => Value::Array(a.into_iter().map(expand_colon_keys).collect()),
        other => other,
    }
}

/// Sets a `:`-separated (case-insensitive) key path to a string value (an existing key keeps
/// its spelling; array elements are addressed by index).
pub fn set_path(root: &mut Value, path: &str, value: &str) {
    let segments: Vec<&str> = path.split(':').filter(|s| !s.is_empty()).collect();
    if segments.is_empty() {
        return;
    }
    let mut node = root;
    for seg in segments {
        if let (true, Ok(idx)) = (node.is_array(), seg.parse::<usize>()) {
            let arr = node.as_array_mut().expect("array");
            while arr.len() <= idx {
                arr.push(Value::Object(Map::new()));
            }
            node = &mut arr[idx];
            continue;
        }
        if !node.is_object() {
            *node = Value::Object(Map::new());
        }
        let obj = node.as_object_mut().expect("object");
        let key = obj
            .keys()
            .find(|k| k.eq_ignore_ascii_case(seg))
            .cloned()
            .unwrap_or_else(|| seg.to_string());
        node = obj.entry(key).or_insert(Value::Null);
    }
    *node = Value::String(value.to_string());
}

/// `AddEnvironmentVariables()`: `Section__Key` is `Section:Key`. Only keys below a known
/// `AppConfiguration` member are applied (others can never bind).
pub fn apply_env(root: &mut Value, vars: impl IntoIterator<Item = (String, String)>) {
    for (name, value) in vars {
        let key = name.replace("__", ":");
        let first = key
            .split(':')
            .next()
            .unwrap_or_default()
            .to_ascii_lowercase();
        if TOP_LEVEL_KEYS.contains(&first.as_str()) {
            set_path(root, &key, &value);
        }
    }
}

/// `AddCommandLine(args)`: `--Key=Value`, `/Key=Value`, `Key=Value`, `--Key Value` and
/// `/Key Value` (single-dash keys need a switch mapping in .NET and are ignored).
pub fn apply_args(root: &mut Value, args: &[String]) {
    let mut i = 0;
    while i < args.len() {
        let arg = &args[i];
        let (stripped, prefixed) = if let Some(s) = arg.strip_prefix("--") {
            (s, true)
        } else if let Some(s) = arg.strip_prefix('/') {
            (s, true)
        } else if arg.starts_with('-') {
            i += 1;
            continue;
        } else {
            (arg.as_str(), false)
        };
        if let Some((k, v)) = stripped.split_once('=') {
            set_path(root, k, v);
        } else if prefixed && i + 1 < args.len() {
            set_path(root, stripped, &args[i + 1]);
            i += 1;
        }
        i += 1;
    }
}

/// The `AccessLevel` dictionary with the keys' spelling, in `GetChildren` order (keys sorted
/// case-insensitively).
fn access_levels(root: &Value) -> Result<Vec<(String, i32)>, ConfigError> {
    let section = root.as_object().and_then(|m| {
        m.iter()
            .find(|(k, _)| k.eq_ignore_ascii_case("accesslevel"))
            .map(|(_, v)| v)
    });
    let Some(map) = section.and_then(Value::as_object) else {
        return Ok(Vec::new());
    };
    let mut out = Vec::with_capacity(map.len());
    for (k, v) in map {
        let level: i32 = parse_scalar::<i32, serde_json::Error>(v).map_err(ConfigError::Bind)?;
        out.push((k.clone(), level));
    }
    out.sort_by_key(|(k, _)| k.to_ascii_lowercase());
    Ok(out)
}

/// `AppConfiguration.Instance`: the current configuration, replaceable at runtime (GM
/// commands, `Program.ReloadConfiguration`). Reads deref to a snapshot that stays valid
/// forever: a replaced configuration is intentionally leaked (a few KiB per GM change).
pub struct ConfigCell(std::sync::RwLock<&'static AppConfig>);

impl ConfigCell {
    pub fn new(config: AppConfig) -> Self {
        Self(std::sync::RwLock::new(Box::leak(Box::new(config))))
    }

    /// The current configuration.
    pub fn get(&self) -> &'static AppConfig {
        *self.0.read().expect("config poisoned")
    }

    /// Replaces the configuration (`ReloadConfiguration`).
    pub fn replace(&self, config: AppConfig) {
        *self.0.write().expect("config poisoned") = Box::leak(Box::new(config));
    }

    /// Changes values (`AppConfiguration.Instance.World.ExpRate = v`).
    pub fn update(&self, f: impl FnOnce(&mut AppConfig)) {
        let mut guard = self.0.write().expect("config poisoned");
        let mut next = (*guard).clone();
        f(&mut next);
        *guard = Box::leak(Box::new(next));
    }
}

impl std::ops::Deref for ConfigCell {
    type Target = AppConfig;

    fn deref(&self) -> &AppConfig {
        self.get()
    }
}

impl std::fmt::Debug for ConfigCell {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        self.get().fmt(f)
    }
}

impl From<AppConfig> for ConfigCell {
    fn from(c: AppConfig) -> Self {
        Self::new(c)
    }
}

/// The JSON configuration provider accepts `//` and `/* */` comments and trailing commas
/// (`JsonCommentHandling.Skip`, `AllowTrailingCommas`); strip them so serde_json can parse.
pub fn relax_json(src: &str) -> String {
    let chars: Vec<char> = src.chars().collect();
    let skip_trivia = |mut j: usize| -> usize {
        loop {
            while j < chars.len() && chars[j].is_whitespace() {
                j += 1;
            }
            match (chars.get(j), chars.get(j + 1)) {
                (Some('/'), Some('/')) => {
                    while j < chars.len() && chars[j] != '\n' {
                        j += 1;
                    }
                }
                (Some('/'), Some('*')) => {
                    j += 2;
                    while j < chars.len() && !(chars[j] == '*' && chars.get(j + 1) == Some(&'/')) {
                        j += 1;
                    }
                    j += 2;
                }
                _ => return j,
            }
        }
    };

    let mut out = String::with_capacity(src.len());
    let mut i = 0;
    let mut in_str = false;
    while i < chars.len() {
        let c = chars[i];
        if in_str {
            out.push(c);
            if c == '\\' && i + 1 < chars.len() {
                out.push(chars[i + 1]);
                i += 1;
            } else if c == '"' {
                in_str = false;
            }
            i += 1;
            continue;
        }
        match c {
            '"' => {
                in_str = true;
                out.push(c);
                i += 1;
            }
            '/' if matches!(chars.get(i + 1), Some('/') | Some('*')) => {
                i = skip_trivia(i);
            }
            ',' => {
                // Drop a trailing comma (next significant character closes the container).
                if !matches!(chars.get(skip_trivia(i + 1)), Some('}') | Some(']')) {
                    out.push(c);
                }
                i += 1;
            }
            _ => {
                out.push(c);
                i += 1;
            }
        }
    }
    out
}

fn collect_json(dir: &Path, out: &mut Vec<PathBuf>) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            collect_json(&path, out);
        } else if path
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("json"))
        {
            out.push(path);
        }
    }
}

/// Merges `src` over `dst` with Microsoft.Extensions.Configuration semantics.
pub fn merge(dst: &mut Value, src: Value) {
    match (dst, src) {
        (Value::Object(d), Value::Object(s)) => {
            for (k, v) in s {
                let existing = d.keys().find(|dk| dk.eq_ignore_ascii_case(&k)).cloned();
                match existing {
                    Some(key) => merge(d.get_mut(&key).expect("key exists"), v),
                    None => {
                        d.insert(k, v);
                    }
                }
            }
        }
        (Value::Array(d), Value::Array(s)) => {
            for (i, v) in s.into_iter().enumerate() {
                if i < d.len() {
                    merge(&mut d[i], v);
                } else {
                    d.push(v);
                }
            }
        }
        (d, s) => *d = s,
    }
}

fn lowercase_keys(v: Value) -> Value {
    match v {
        Value::Object(m) => Value::Object(
            m.into_iter()
                .map(|(k, v)| (k.to_ascii_lowercase(), lowercase_keys(v)))
                .collect(),
        ),
        Value::Array(a) => Value::Array(a.into_iter().map(lowercase_keys).collect()),
        other => other,
    }
}

fn parse_scalar<T, E>(v: &Value) -> Result<T, E>
where
    T: FromStr,
    T::Err: Display,
    E: de::Error,
{
    let s = match v {
        Value::String(s) => s.trim().to_string(),
        Value::Number(n) => n.to_string(),
        Value::Bool(b) => b.to_string(),
        Value::Null => return Err(E::custom("null")),
        other => return Err(E::custom(format!("expected a scalar, got {other}"))),
    };
    // The binder accepts "True"/"False" for booleans.
    let s = if s.eq_ignore_ascii_case("true") {
        "true".to_string()
    } else if s.eq_ignore_ascii_case("false") {
        "false".to_string()
    } else {
        s
    };
    s.parse::<T>()
        .map_err(|e| E::custom(format!("invalid value {s:?}: {e}")))
}

/// Accepts a JSON number, bool or numeric string (the Microsoft binder stores everything as
/// strings).
pub(crate) fn lenient<'de, D, T>(d: D) -> Result<T, D::Error>
where
    D: Deserializer<'de>,
    T: FromStr,
    T::Err: Display,
{
    let v = Value::deserialize(d)?;
    parse_scalar(&v)
}

/// A configuration enum: its member name (any case) or numeric value.
pub(crate) fn lenient_enum<'de, D, T>(d: D) -> Result<T, D::Error>
where
    D: Deserializer<'de>,
    T: ConfigEnum,
{
    let v = Value::deserialize(d)?;
    let s = match &v {
        Value::String(s) => s.trim().to_string(),
        Value::Number(n) => n.to_string(),
        other => {
            return Err(de::Error::custom(format!(
                "expected an enum value, got {other}"
            )))
        }
    };
    T::parse_config(&s).ok_or_else(|| de::Error::custom(format!("invalid enum value {s:?}")))
}

fn lenient_vec<'de, D, T>(d: D) -> Result<Vec<T>, D::Error>
where
    D: Deserializer<'de>,
    T: FromStr,
    T::Err: Display,
{
    match Value::deserialize(d)? {
        Value::Array(items) => items.iter().map(parse_scalar).collect(),
        Value::Null => Ok(Vec::new()),
        other => Err(de::Error::custom(format!("expected an array, got {other}"))),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn merges_case_insensitively_and_binds_strings() {
        let mut base = json!({
            "Id": 1, "SecretKey": "test", "AdditionalesId": [],
            "Network": { "Host": "*", "Port": 1239, "NumConnections": 10 },
            "LoginNetwork": { "Host": "%login_host%", "Port": "%login_port%" }
        });
        merge(
            &mut base,
            json!({ "loginnetwork": { "host": "127.0.0.1", "PORT": "1234" }, "Account": { "DeleteReleaseName": "True" } }),
        );
        let cfg = AppConfig::from_value(base).unwrap();
        assert_eq!(cfg.id, 1);
        assert_eq!(cfg.network.port, 1239);
        assert_eq!(cfg.login_network.host, "127.0.0.1");
        assert_eq!(cfg.login_network.port, 1234);
        assert!(cfg.account.delete_release_name);
        assert_eq!(cfg.account.access_level_first_account, 100);
    }

    #[test]
    fn relaxed_json() {
        let src = "{\n // c\n \"a\": \"x//y\\\"\", /* b */ \"b\": [1, 2, // t\n],\n}";
        let v: Value = serde_json::from_str(&relax_json(src)).unwrap();
        assert_eq!(v, json!({ "a": "x//y\"", "b": [1, 2] }));
    }

    #[test]
    fn arrays_merge_by_index() {
        let mut a = json!({ "A": [1, 2, 3] });
        merge(&mut a, json!({ "a": [9] }));
        assert_eq!(a, json!({ "A": [9, 2, 3] }));
    }

    /// The shipped application directory (`aaemu-game/app/`).
    fn shipped_app_path() -> PathBuf {
        Path::new(env!("CARGO_MANIFEST_DIR")).join("app")
    }

    #[test]
    fn shipped_configuration_binds_every_section() {
        let app = shipped_app_path();
        if !app.join("Config.json").exists() {
            return;
        }
        // Config.json keeps `%login_port%` / `%db_port%` placeholders (filled by the installer):
        // override them like the C# command line would.
        let args: Vec<String> = [
            "--LoginNetwork:Port=1234",
            "/Connections:MySQLProvider:Port",
            "3306",
        ]
        .iter()
        .map(|s| s.to_string())
        .collect();
        let cfg = AppConfig::load_from(&app, None, Vec::new(), &args).unwrap();
        assert_eq!(cfg.login_network.port, 1234);
        assert_eq!(cfg.connections.mysql_provider.port, 3306);
        let web = cfg.web_api_network.as_ref().expect("WebApiNetwork");
        assert_eq!((web.host.as_str(), web.port), ("*", 1280));
        assert_eq!(cfg.max_concurency_thread_pool, 8);
        assert!(cfg.discord_token.is_none());
        // World.json
        assert_eq!(cfg.world.motd, "Welcome to AAEmu!");
        assert_eq!(cfg.world.days_for_tax_payment, 7);
        assert!(cfg.world.geo_data_mode);
        assert_eq!(cfg.world.max_instances, 32);
        assert_eq!(cfg.world.wind_model, WindModelType::Official);
        assert_eq!(cfg.world.exp_loss_rate_at_death, 0.05);
        assert_eq!(cfg.world.minimum_exp_loss_level, 10);
        // Dungeons.json
        assert_eq!(cfg.dungeons.auto_team_disband_kick_time, 15);
        assert_eq!(
            cfg.dungeons.auto_create,
            vec![crate::systems::instances::config::DungeonLoadConfig {
                name: "arche_mall_world".into(),
                channel: 0,
                id: 1
            }]
        );
        // Specialty.json, AccessLevels.json, CharacterSettings.json
        assert_eq!(cfg.specialty.trade_pack_mail_delay_in_minutes, 480.0);
        assert_eq!(cfg.access_level_of("help"), Some(0));
        assert_eq!(cfg.access_level_of("announce"), Some(50));
        assert_eq!(cfg.access_level_of("Announce"), None);
        assert!(cfg.account.delete_release_name);
        assert_eq!(cfg.account.name_regex, "^[a-zA-Z0-9]{1,18}$");
        assert_eq!(cfg.scripts.load_strategy, LoadStrategyType::Reflection);
        assert_eq!(cfg.client_data.sources.len(), 4);
    }

    #[test]
    fn overrides_apply_in_builder_order() {
        let dir = std::env::temp_dir().join(format!("aaemu-cfg-{}", std::process::id()));
        std::fs::create_dir_all(dir.join("Configurations")).unwrap();
        std::fs::write(
            dir.join("Config.json"),
            r#"{ "Id": 1, "World": { "ExpRate": 1.0 }, "WebApiNetwork": { "Host": "*", "Port": 1 } }"#,
        )
        .unwrap();
        std::fs::write(
            dir.join("Configurations/World.json"),
            r#"{ "World": { "ExpRate": 2.0, "WindModel": "realistic" } }"#,
        )
        .unwrap();
        let secrets = dir.join("secrets.json");
        std::fs::write(&secrets, r#"{ "World:ExpRate": 3, "DiscordToken": "tok" }"#).unwrap();
        // Secrets beat the files.
        let cfg = AppConfig::load_from(&dir, Some(&secrets), Vec::new(), &[]).unwrap();
        assert_eq!(cfg.world.exp_rate, 3.0);
        assert_eq!(cfg.world.wind_model, WindModelType::Realistic);
        assert_eq!(cfg.discord_token.as_deref(), Some("tok"));
        // Environment beats secrets; unknown top-level variables are ignored.
        let env = vec![
            ("WORLD__EXPRATE".to_string(), "4".to_string()),
            ("Path".to_string(), "C:/x".to_string()),
            ("WebApiNetwork__Port".to_string(), "1280".to_string()),
        ];
        let cfg = AppConfig::load_from(&dir, Some(&secrets), env.clone(), &[]).unwrap();
        assert_eq!(cfg.world.exp_rate, 4.0);
        assert_eq!(cfg.web_api_network.unwrap().port, 1280);
        // The command line beats everything.
        let args = vec!["World:ExpRate=5".to_string(), "-x".to_string()];
        let cfg = AppConfig::load_from(&dir, Some(&secrets), env, &args).unwrap();
        assert_eq!(cfg.world.exp_rate, 5.0);
        std::fs::remove_dir_all(&dir).ok();
    }

    #[test]
    fn missing_sections_keep_the_csharp_defaults() {
        let cfg = AppConfig::from_value(json!({})).unwrap();
        assert!(cfg.web_api_network.is_none());
        assert_eq!(cfg.labor_offline.tick_minutes, 5);
        assert_eq!(cfg.world.target_physics_tps, 25.0);
        assert!(cfg.dungeons.auto_cleanup_after_kick);
        assert_eq!(cfg.specialty.ratio_decrease_per_pack, f64::from(0.5f32));
        assert_eq!(cfg.credits.get_tick_amount(true), 0);
        let cfg = AppConfig::from_value(json!({
            "Credits": { "TickAmount": 1, "TickAmountPremium": "2" },
            "Scripts": { "LoadStrategy": 0 },
            "AccessLevel": { "Zeta": "5", "alpha": 1 }
        }))
        .unwrap();
        assert_eq!(cfg.credits.get_tick_amount(false), 1);
        assert_eq!(cfg.credits.get_tick_amount(true), 2);
        assert_eq!(cfg.scripts.load_strategy, LoadStrategyType::Compilation);
        assert_eq!(
            cfg.access_level,
            vec![("alpha".to_string(), 1), ("Zeta".to_string(), 5)]
        );
        assert!(AppConfig::from_value(json!({ "World": { "WindModel": "Nope" } })).is_err());
    }

    #[test]
    fn config_cell_replaces_values_at_runtime() {
        let cell = ConfigCell::new(AppConfig::default());
        let before = cell.get();
        cell.update(|c| c.world.exp_rate = 7.0);
        assert_eq!(cell.world.exp_rate, 7.0);
        // Earlier snapshots stay valid and unchanged.
        assert_eq!(before.world.exp_rate, 1.0);
        cell.replace(AppConfig::default());
        assert_eq!(cell.world.exp_rate, 1.0);
    }
}
