//! Lossless storage for the original pak byte stream, including its encrypted FAT/footer.
//! No archive parsing or asset conversion occurs here. Readers pin one immutable manifest.
mod ffi;
mod format;
mod reader;
mod store;

pub use format::{Chunk, Manifest, Segment, DEFAULT_CHUNK_SIZE};
pub use reader::{Pak, CACHE_BYTES};
pub use store::{apply, build, diff, profile, ApplyResult, Receipt};

#[derive(Debug, thiserror::Error)]
pub enum Error {
    #[error("I/O error: {0}")]
    Io(#[from] std::io::Error),
    #[error("invalid compressed pak: {0}")]
    Invalid(String),
    #[error("invalid update receipt: {0}")]
    Receipt(#[from] serde_json::Error),
}
pub type Result<T> = std::result::Result<T, Error>;

fn invalid(message: impl Into<String>) -> Error {
    Error::Invalid(message.into())
}

pub(crate) fn hash(bytes: &[u8]) -> [u8; 32] {
    use sha2::{Digest, Sha256};
    Sha256::digest(bytes).into()
}

#[cfg(test)]
mod tests;
