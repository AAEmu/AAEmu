//! Port of `AAEmu.Game.Models.AppConfiguration` and the configuration loading in `Program.cs`.
//!
//! The C# server binds `Microsoft.Extensions.Configuration` over, in order:
//! `Config.json`, every `Configurations/**/*.json` (ordinal-sorted), then `Config.Local.json`.
//! Later files override earlier ones key-by-key; keys are case-insensitive and scalar values may
//! be given as strings (e.g. `"Port": "1234"`). Arrays are merged element-by-element by index, as
//! the Microsoft binder flattens them to `Key:0`, `Key:1`, ...
//!
//! Only the sections this phase uses are modelled; unknown keys are ignored.
//! Not ported (later phase): user secrets, environment-variable and command-line overrides.

use std::fmt::Display;
use std::path::{Path, PathBuf};
use std::str::FromStr;

use serde::de::{self, Deserializer};
use serde::Deserialize;
use serde_json::{Map, Value};

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
    #[serde(rename = "account")]
    pub account: AccountConfig,
    #[serde(rename = "labor")]
    pub labor: CurrencyValuesConfig,
    #[serde(rename = "credits")]
    pub credits: CurrencyValuesConfig,
    #[serde(rename = "loyalty")]
    pub loyalty: CurrencyValuesConfig,
    #[serde(rename = "world")]
    pub world: WorldConfig,
    #[serde(rename = "clientdata")]
    pub client_data: ClientDataConfig,
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
            account: AccountConfig::default(),
            labor: CurrencyValuesConfig::default(),
            credits: CurrencyValuesConfig::default(),
            loyalty: CurrencyValuesConfig::default(),
            world: WorldConfig::default(),
            client_data: ClientDataConfig::default(),
            character_name_regex: None,
            default_language: "en_us".into(),
            debug_info: true,
            debug_info_level: 100,
            expedition: Default::default(),
            justice: Default::default(),
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

/// `AccountConfig` (only the members used so far).
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct AccountConfig {
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

/// `WorldConfig` (the members used so far).
#[derive(Debug, Clone, Deserialize)]
#[serde(default)]
pub struct WorldConfig {
    #[serde(rename = "motd")]
    pub motd: String,
    #[serde(rename = "logoutmessage")]
    pub logout_message: String,
    /// Minutes.
    #[serde(rename = "autosaveinterval", deserialize_with = "lenient")]
    pub auto_save_interval: f64,
    #[serde(rename = "exprate", deserialize_with = "lenient")]
    pub exp_rate: f64,
    #[serde(rename = "ignorefalldamageaccesslevel", deserialize_with = "lenient")]
    pub ignore_fall_damage_access_level: i32,
    /// `GeoDataMode` (navmesh heights; geodata is not ported, the `false` path is used).
    #[serde(rename = "geodatamode", deserialize_with = "lenient")]
    pub geo_data_mode: bool,
    /// `PreLoadTerrain`: load every heightmap cell at startup instead of on demand.
    #[serde(rename = "preloadterrain", deserialize_with = "lenient")]
    pub pre_load_terrain: bool,
    /// `GrowthRate` (doodad growth time divisor).
    #[serde(rename = "growthrate", deserialize_with = "lenient")]
    pub growth_rate: f64,
}

impl Default for WorldConfig {
    fn default() -> Self {
        Self {
            motd: String::new(),
            logout_message: String::new(),
            auto_save_interval: 5.0,
            exp_rate: 1.0,
            ignore_fall_damage_access_level: 100,
            geo_data_mode: false,
            pre_load_terrain: false,
            growth_rate: 1.0,
        }
    }
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

impl AppConfig {
    /// Loads `Config.json`, `Configurations/**/*.json` and `Config.Local.json` from `app_path`
    /// (every file optional, as `AddJsonFile(file, optional: true)`).
    pub fn load(app_path: &Path) -> Result<Self, ConfigError> {
        let mut files = vec![app_path.join("Config.json")];
        let mut extra = Vec::new();
        collect_json(&app_path.join("Configurations"), &mut extra);
        extra.sort();
        files.extend(extra);
        files.push(app_path.join("Config.Local.json"));

        let mut merged = Value::Object(Map::new());
        for file in &files {
            let text = match std::fs::read_to_string(file) {
                Ok(t) => t,
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => continue,
                Err(source) => {
                    return Err(ConfigError::Io {
                        path: file.clone(),
                        source,
                    })
                }
            };
            tracing::info!("Config: {}", file.display());
            let value: Value = serde_json::from_str(&relax_json(
                text.trim_start_matches('\u{feff}'),
            ))
            .map_err(|source| ConfigError::Json {
                path: file.clone(),
                source,
            })?;
            merge(&mut merged, value);
        }
        let mut cfg = Self::from_value(merged)?;
        cfg.app_path = app_path.to_path_buf();
        Ok(cfg)
    }

    /// Binds an already merged JSON document (keys in any case).
    pub fn from_value(value: Value) -> Result<Self, ConfigError> {
        serde_json::from_value(lowercase_keys(value)).map_err(ConfigError::Bind)
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
}
