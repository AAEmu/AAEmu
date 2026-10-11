//! Configuration (`AppConfiguration`, `NetworkConfig`, `DBConnectionsConfig`, `KoreaAuthOptions`).
//!
//! Mirrors the ASP.NET configuration pipeline of `Program.cs`:
//! `Config.json` <- `Config.Local.json` <- environment variables (`Section__Key`) <- command line
//! (`--Section:Key=value` / `--Section:Key value`), later sources winning. Like
//! `Microsoft.Extensions.Configuration`:
//! * keys are case-insensitive (all keys are lower-cased before binding),
//! * arrays merge element-wise by index (they are `Section:0:Key` paths in .NET),
//! * scalars may be given as strings (`"Port": "3306"`, `"AutoAccount": "true"`),
//! * `TimeSpan` values use the .NET `[d.]hh:mm[:ss[.fffffff]]` format.
//!
//! Not ported: user secrets and the `Logging` section (logging is configured via `RUST_LOG`).

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::time::Duration;

use serde::de::{self, Deserializer};
use serde::Deserialize;
use serde_json::{Map, Value};

#[derive(Debug, thiserror::Error)]
pub enum ConfigError {
    #[error("failed to read {path}: {source}")]
    Io {
        path: PathBuf,
        source: std::io::Error,
    },
    #[error("failed to parse {path}: {source}")]
    Json {
        path: PathBuf,
        source: serde_json::Error,
    },
    #[error("invalid configuration: {0}")]
    Bind(#[from] serde_json::Error),
    #[error("invalid configuration: {0}")]
    Validation(String),
}

/// General application configuration (`AppConfiguration`, bound from the configuration root).
#[derive(Clone, Debug, Deserialize)]
pub struct AppConfig {
    /// The secret key used to verify game servers when registering with the login server.
    #[serde(rename = "secretkey")]
    pub secret_key: String,
    /// Whether accounts should be created automatically if they do not exist.
    #[serde(rename = "autoaccount", default, deserialize_with = "lenient_bool")]
    pub auto_account: bool,
    /// Whether to skip host resolution of game server hostnames.
    #[serde(rename = "skiphostresolve", default, deserialize_with = "lenient_bool")]
    pub skip_host_resolve: bool,
    /// Timeout for the game server to respond to an EnterWorld request.
    #[serde(
        rename = "enterworldtimeout",
        default = "default_enter_world_timeout",
        deserialize_with = "timespan"
    )]
    pub enter_world_timeout: Duration,
    #[serde(rename = "gameservers")]
    pub game_servers: Vec<GameServerConfig>,
    /// `Network` section: public listener for game clients.
    #[serde(rename = "network")]
    pub network: NetworkConfig,
    /// `InternalNetwork` section: listener for game servers.
    #[serde(rename = "internalnetwork")]
    pub internal_network: NetworkConfig,
    /// `Connections` section.
    #[serde(rename = "connections")]
    pub connections: DbConnectionsConfig,
    /// `KoreaAuth` section.
    #[serde(rename = "koreaauth", default)]
    pub korea_auth: KoreaAuthOptions,
    /// `ConnectionStrings` section (`ConnectionStrings:MySqlConnection` overrides `Connections`).
    #[serde(rename = "connectionstrings", default)]
    pub connection_strings: HashMap<String, String>,
    /// `urls` (`ASPNETCORE_URLS`): extra HTTP endpoints, used for the health checks.
    #[serde(rename = "urls", default)]
    pub urls: Option<String>,
}

fn default_enter_world_timeout() -> Duration {
    Duration::from_secs(10)
}

/// Configuration of a single game server.
#[derive(Clone, Debug, Deserialize)]
pub struct GameServerConfig {
    /// The unique identifier of the game server.
    #[serde(rename = "id", deserialize_with = "lenient_u8")]
    pub id: u8,
    /// Display name, as shown on the client's server selection screen.
    #[serde(rename = "name")]
    pub name: String,
    /// Host address (IP or domain name) of the game server; must be reachable by clients.
    #[serde(rename = "host")]
    pub host: String,
    /// Port the game server listens on for clients.
    #[serde(rename = "port", deserialize_with = "lenient_u16")]
    pub port: u16,
    /// Whether to hide the server from the client's server selection screen.
    #[serde(rename = "hidden", default, deserialize_with = "lenient_bool")]
    pub hidden: bool,
}

/// `NetworkConfig` (`PublicNetworkConfig` / `InternalNetworkConfig`).
#[derive(Clone, Debug, Deserialize)]
pub struct NetworkConfig {
    /// Host address to listen on (`*` = any).
    #[serde(rename = "host")]
    pub host: String,
    #[serde(rename = "port", deserialize_with = "lenient_u16")]
    pub port: u16,
    /// Number of simultaneous connections allowed (not enforced, as in the C# server).
    #[serde(rename = "numconnections", default, deserialize_with = "lenient_i32")]
    pub num_connections: i32,
}

/// `DBConnectionsConfig`.
#[derive(Clone, Debug, Deserialize)]
pub struct DbConnectionsConfig {
    #[serde(rename = "mysqlprovider")]
    pub mysql_provider: MySqlConnectionSettings,
    /// Apply database schema updates without an interactive prompt (`cargo xtask dev`, Docker, CI).
    #[serde(
        rename = "autoapplyupdates",
        default,
        deserialize_with = "lenient_bool"
    )]
    pub auto_apply_updates: bool,
}

/// `AAEmu.Commons.Models.MySqlConnectionSettings`.
#[derive(Clone, Debug, Deserialize)]
pub struct MySqlConnectionSettings {
    #[serde(rename = "host", default)]
    pub host: String,
    #[serde(rename = "port", default, deserialize_with = "lenient_u16")]
    pub port: u16,
    #[serde(rename = "user", default)]
    pub user: String,
    #[serde(rename = "password", default)]
    pub password: String,
    #[serde(rename = "database", default)]
    pub database: String,
}

/// `KoreaAuthOptions`: Korea challenge-response authentication (V2, AES-256 with a
/// sha256_crypt key).
///
/// Security trade-off: sha256_crypt at 100,000 rounds is approximately 3x weaker than PBKDF2@310k
/// for offline attacks against a stolen DB; at 300,000 rounds it reaches approximate parity. The
/// hash is never transmitted to clients — only the salt and round count are sent.
#[derive(Clone, Debug, Deserialize)]
pub struct KoreaAuthOptions {
    /// When `false` (default), `korea_challenge_hash` is never written to the DB and clients
    /// using Korea auth cannot log in.
    #[serde(rename = "enabled", default, deserialize_with = "lenient_bool")]
    pub enabled: bool,
    /// sha256_crypt rounds for newly computed hashes. Valid range: 1,000–999,999,999.
    #[serde(
        rename = "rounds",
        default = "default_rounds",
        deserialize_with = "lenient_i32"
    )]
    pub rounds: i32,
    /// Maximum number of OTP entry attempts before denying authentication.
    #[serde(
        rename = "maxotpattempts",
        default = "default_attempts",
        deserialize_with = "lenient_i32"
    )]
    pub max_otp_attempts: i32,
    /// Maximum number of certificate entry attempts before denying authentication.
    #[serde(
        rename = "maxcertattempts",
        default = "default_attempts",
        deserialize_with = "lenient_i32"
    )]
    pub max_cert_attempts: i32,
    /// Timeout for ARS (phone verification).
    #[serde(
        rename = "arstimeout",
        default = "default_ars_timeout",
        deserialize_with = "timespan"
    )]
    pub ars_timeout: Duration,
}

fn default_rounds() -> i32 {
    100_000
}

fn default_attempts() -> i32 {
    3
}

fn default_ars_timeout() -> Duration {
    Duration::from_secs(180)
}

impl Default for KoreaAuthOptions {
    fn default() -> Self {
        Self {
            enabled: false,
            rounds: default_rounds(),
            max_otp_attempts: default_attempts(),
            max_cert_attempts: default_attempts(),
            ars_timeout: default_ars_timeout(),
        }
    }
}

impl AppConfig {
    /// `ValidateDataAnnotations` / `ValidateOnStart`.
    pub fn validate(&self) -> Result<(), ConfigError> {
        let k = &self.korea_auth;
        if !(1_000..=999_999_999).contains(&k.rounds) {
            return Err(ConfigError::Validation(format!(
                "KoreaAuth:Rounds must be between 1000 and 999999999 (got {})",
                k.rounds
            )));
        }
        if k.max_otp_attempts < 1 || k.max_cert_attempts < 1 {
            return Err(ConfigError::Validation(
                "KoreaAuth:MaxOtpAttempts/MaxCertAttempts must be >= 1".into(),
            ));
        }
        for (name, n) in [
            ("Network", &self.network),
            ("InternalNetwork", &self.internal_network),
        ] {
            if n.host.is_empty() {
                return Err(ConfigError::Validation(format!("{name}:Host is required")));
            }
        }
        Ok(())
    }

    /// The `ConnectionStrings:MySqlConnection` override, if configured.
    pub fn mysql_connection_string(&self) -> Option<&str> {
        self.connection_strings
            .get("mysqlconnection")
            .map(String::as_str)
    }

    /// Loads `Config.json` and `Config.Local.json` from `dir` (both optional), then applies
    /// environment variables and command-line arguments.
    pub fn load(dir: &Path, args: &[String]) -> Result<Self, ConfigError> {
        let mut root = Value::Object(Map::new());
        for name in ["Config.json", "Config.Local.json"] {
            let path = dir.join(name);
            if let Some(v) = read_json_file(&path)? {
                merge(&mut root, lowercase_keys(v));
            }
        }
        apply_env(&mut root, std::env::vars());
        apply_args(&mut root, args);
        Self::from_value(root)
    }

    /// Binds and validates an already-merged (lower-cased) configuration tree.
    pub fn from_value(root: Value) -> Result<Self, ConfigError> {
        let cfg: Self = serde_json::from_value(root)?;
        cfg.validate()?;
        Ok(cfg)
    }
}

fn read_json_file(path: &Path) -> Result<Option<Value>, ConfigError> {
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
    let text = text.trim_start_matches('\u{feff}');
    serde_json::from_str(text)
        .map(Some)
        .map_err(|source| ConfigError::Json {
            path: path.to_path_buf(),
            source,
        })
}

/// Lower-cases every object key (configuration keys are case-insensitive).
pub fn lowercase_keys(v: Value) -> Value {
    match v {
        Value::Object(m) => Value::Object(
            m.into_iter()
                .map(|(k, v)| (k.to_lowercase(), lowercase_keys(v)))
                .collect(),
        ),
        Value::Array(a) => Value::Array(a.into_iter().map(lowercase_keys).collect()),
        other => other,
    }
}

/// Deep merge with `Microsoft.Extensions.Configuration` semantics: objects merge by key, arrays
/// by index, scalars are replaced.
pub fn merge(base: &mut Value, overlay: Value) {
    match (base, overlay) {
        (Value::Object(b), Value::Object(o)) => {
            for (k, v) in o {
                match b.get_mut(&k) {
                    Some(existing) => merge(existing, v),
                    None => {
                        b.insert(k, v);
                    }
                }
            }
        }
        (Value::Array(b), Value::Array(o)) => {
            for (i, v) in o.into_iter().enumerate() {
                if i < b.len() {
                    merge(&mut b[i], v);
                } else {
                    b.push(v);
                }
            }
        }
        (b, o) => *b = o,
    }
}

/// Sets a `:`-separated (case-insensitive) key path to a string value.
pub fn set_path(root: &mut Value, path: &str, value: &str) {
    let segments: Vec<String> = path
        .split(':')
        .filter(|s| !s.is_empty())
        .map(str::to_lowercase)
        .collect();
    if segments.is_empty() {
        return;
    }
    let mut node = root;
    for seg in &segments {
        if let (true, Ok(idx)) = (node.is_array(), seg.parse::<usize>()) {
            let arr = node.as_array_mut().unwrap();
            while arr.len() <= idx {
                arr.push(Value::Object(Map::new()));
            }
            node = &mut arr[idx];
            continue;
        }
        if !node.is_object() {
            *node = Value::Object(Map::new());
        }
        node = node
            .as_object_mut()
            .unwrap()
            .entry(seg.clone())
            .or_insert(Value::Null);
    }
    *node = Value::String(value.to_string());
}

const TOP_LEVEL_KEYS: [&str; 11] = [
    "secretkey",
    "autoaccount",
    "skiphostresolve",
    "enterworldtimeout",
    "gameservers",
    "network",
    "internalnetwork",
    "connections",
    "koreaauth",
    "connectionstrings",
    "urls",
];

/// Environment variables: `Section__Key` maps to `Section:Key`. Only keys whose first segment is
/// a known configuration key are applied. `ASPNETCORE_URLS` maps to `urls`.
pub fn apply_env(root: &mut Value, vars: impl IntoIterator<Item = (String, String)>) {
    for (name, value) in vars {
        let key = if name.eq_ignore_ascii_case("ASPNETCORE_URLS") {
            "urls".to_string()
        } else {
            name.replace("__", ":")
        };
        let first = key.split(':').next().unwrap_or_default().to_lowercase();
        if TOP_LEVEL_KEYS.contains(&first.as_str()) {
            set_path(root, &key, &value);
        }
    }
}

/// Command line: `--Key=Value`, `/Key=Value`, `Key=Value` and `--Key Value` / `/Key Value`.
pub fn apply_args(root: &mut Value, args: &[String]) {
    let mut i = 0;
    while i < args.len() {
        let arg = &args[i];
        let (stripped, prefixed) = if let Some(s) = arg.strip_prefix("--") {
            (s, true)
        } else if let Some(s) = arg.strip_prefix('/') {
            (s, true)
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

/// Parses a .NET `TimeSpan` string: `[-][d.]hh:mm[:ss[.fffffff]]` or a plain number of days.
pub fn parse_timespan(s: &str) -> Option<Duration> {
    let s = s.trim();
    if s.starts_with('-') {
        return None; // negative timeouts are meaningless here
    }
    if !s.contains(':') {
        return s
            .parse::<u64>()
            .ok()
            .map(|d| Duration::from_secs(d * 86_400));
    }
    let (days, rest) = match s.split_once('.') {
        Some((d, r)) if !d.contains(':') => (d.parse::<u64>().ok()?, r),
        _ => (0, s),
    };
    let parts: Vec<&str> = rest.split(':').collect();
    if parts.len() < 2 || parts.len() > 3 {
        return None;
    }
    let hours: u64 = parts[0].parse().ok()?;
    let minutes: u64 = parts[1].parse().ok()?;
    let (secs, nanos) = if parts.len() == 3 {
        match parts[2].split_once('.') {
            Some((sec, frac)) => {
                let digits: String = frac.chars().take(9).collect();
                let nanos = format!("{digits:0<9}").parse::<u32>().ok()?;
                (sec.parse::<u64>().ok()?, nanos)
            }
            None => (parts[2].parse::<u64>().ok()?, 0),
        }
    } else {
        (0, 0)
    };
    if hours > 23 || minutes > 59 || secs > 59 {
        return None;
    }
    Some(Duration::new(
        days * 86_400 + hours * 3600 + minutes * 60 + secs,
        nanos,
    ))
}

// ---- lenient scalar deserializers (the .NET binder converts strings) ---------------------------

fn scalar_string<'de, D: Deserializer<'de>>(d: D) -> Result<String, D::Error> {
    match Value::deserialize(d)? {
        Value::String(s) => Ok(s),
        Value::Number(n) => Ok(n.to_string()),
        Value::Bool(b) => Ok(b.to_string()),
        other => Err(de::Error::custom(format!("expected a scalar, got {other}"))),
    }
}

fn lenient_bool<'de, D: Deserializer<'de>>(d: D) -> Result<bool, D::Error> {
    let s = scalar_string(d)?;
    match s.trim().to_ascii_lowercase().as_str() {
        "true" => Ok(true),
        "false" => Ok(false),
        _ => Err(de::Error::custom(format!("'{s}' is not a valid boolean"))),
    }
}

macro_rules! lenient_int {
    ($name:ident, $t:ty) => {
        fn $name<'de, D: Deserializer<'de>>(d: D) -> Result<$t, D::Error> {
            let s = scalar_string(d)?;
            s.trim()
                .parse::<$t>()
                .map_err(|_| de::Error::custom(format!("'{s}' is not a valid {}", stringify!($t))))
        }
    };
}

lenient_int!(lenient_u8, u8);
lenient_int!(lenient_u16, u16);
lenient_int!(lenient_i32, i32);

fn timespan<'de, D: Deserializer<'de>>(d: D) -> Result<Duration, D::Error> {
    let s = scalar_string(d)?;
    parse_timespan(&s).ok_or_else(|| de::Error::custom(format!("'{s}' is not a valid TimeSpan")))
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn base() -> Value {
        lowercase_keys(json!({
            "SecretKey": "test",
            "AutoAccount": true,
            "InternalNetwork": { "Host": "*", "Port": 1234 },
            "Network": { "Host": "*", "NumConnections": 10, "Port": 1237 },
            "Connections": { "MySQLProvider": {
                "Database": "aaemu_login", "Host": "%db_host%", "Password": "%db_password%",
                "Port": "%db_port%", "User": "%db_user%" } },
            "GameServers": []
        }))
    }

    #[test]
    fn merges_local_over_base() {
        let mut root = base();
        merge(
            &mut root,
            lowercase_keys(json!({
                "connections": { "MySQLProvider": { "Host": "127.0.0.1", "Port": "3307", "User": "root", "Password": "pw" } },
                "GameServers": [ { "Host": "127.0.0.1", "Id": 1, "Name": "AAEmu", "Port": 1239 } ]
            })),
        );
        let cfg = AppConfig::from_value(root).unwrap();
        assert_eq!(cfg.secret_key, "test");
        assert!(cfg.auto_account);
        assert_eq!(cfg.connections.mysql_provider.port, 3307);
        assert_eq!(cfg.connections.mysql_provider.database, "aaemu_login");
        assert_eq!(cfg.game_servers.len(), 1);
        assert_eq!(cfg.game_servers[0].port, 1239);
        assert_eq!(cfg.network.port, 1237);
        assert_eq!(cfg.enter_world_timeout, Duration::from_secs(10));
        assert_eq!(cfg.korea_auth.rounds, 100_000);
    }

    #[test]
    fn unresolved_placeholder_port_fails() {
        assert!(AppConfig::from_value(base()).is_err());
    }

    #[test]
    fn env_and_args_override() {
        let mut root = base();
        apply_env(
            &mut root,
            vec![
                (
                    "Connections__MySQLProvider__Port".to_string(),
                    "3306".to_string(),
                ),
                ("KoreaAuth__Enabled".to_string(), "True".to_string()),
                ("PATH".to_string(), "ignored".to_string()),
            ],
        );
        apply_args(
            &mut root,
            &[
                "--EnterWorldTimeout=00:00:30".into(),
                "--GameServers:0:Id".into(),
                "2".into(),
            ],
        );
        set_path(&mut root, "GameServers:0:Name", "X");
        set_path(&mut root, "GameServers:0:Host", "h");
        set_path(&mut root, "GameServers:0:Port", "1");
        let cfg = AppConfig::from_value(root).unwrap();
        assert_eq!(cfg.connections.mysql_provider.port, 3306);
        assert!(cfg.korea_auth.enabled);
        assert_eq!(cfg.enter_world_timeout, Duration::from_secs(30));
        assert_eq!(cfg.game_servers[0].id, 2);
    }

    #[test]
    fn timespans() {
        assert_eq!(parse_timespan("00:03:00"), Some(Duration::from_secs(180)));
        assert_eq!(
            parse_timespan("1.02:00:00"),
            Some(Duration::from_secs(93_600))
        );
        assert_eq!(
            parse_timespan("00:00:01.5"),
            Some(Duration::from_millis(1500))
        );
        assert_eq!(parse_timespan("2"), Some(Duration::from_secs(172_800)));
        assert_eq!(parse_timespan("10:30"), Some(Duration::from_secs(37_800)));
        assert_eq!(parse_timespan("bogus"), None);
    }

    #[test]
    fn validation() {
        let mut root = base();
        set_path(&mut root, "Connections:MySQLProvider:Port", "3306");
        set_path(&mut root, "KoreaAuth:Rounds", "10");
        assert!(matches!(
            AppConfig::from_value(root),
            Err(ConfigError::Validation(_))
        ));
    }
}
