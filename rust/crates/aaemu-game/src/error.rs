use aaemu_commons::MarshalError;

/// Errors raised while handling a game connection. Any error returned from a packet handler closes
/// the connection, mirroring the C# `GameProtocolHandler.OnReceive` catch-all (`connection.Shutdown()`).
#[derive(Debug, thiserror::Error)]
pub enum GameError {
    #[error("marshal: {0}")]
    Marshal(#[from] MarshalError),
    #[error("database: {0}")]
    Db(#[from] sqlx::Error),
    #[error("game data: {0}")]
    GameData(#[from] rusqlite::Error),
    #[error("io: {0}")]
    Io(#[from] std::io::Error),
    #[error("protocol: {0}")]
    Protocol(String),
}

pub type Result<T, E = GameError> = std::result::Result<T, E>;
