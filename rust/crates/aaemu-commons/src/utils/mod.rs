//! Ports of `AAEmu.Commons.Utils`.
//!
//! Intentionally not ported:
//! * `Singleton<T>` / `SingletonContainer`: reflection/DI-based lazy singletons. Rust code
//!   passes owned services (`Arc<T>`) explicitly, or uses `std::sync::OnceLock` / `LazyLock`
//!   for true process globals.
//! * `SequenceReaderExtensions`: unsigned reads for `SequenceReader<byte>`; covered by
//!   `u16/u32/u64::from_le_bytes` / `from_be_bytes` and [`crate::PacketStream`].
//! * `XML/Serialization`: generic `XmlSerializer` wrappers (no reflection in Rust); parse with
//!   `roxmltree` (see [`creature`]).
//! * `Helpers.Clone` (reflection `MemberwiseClone` → `Clone`), `Helpers.GetTypesInNamespace`
//!   (reflection), `Helpers.Crc8(byte[], int)` (→ `crc8(&data[..size])`).

pub mod bitset;
pub mod creature;
pub mod helper;
pub mod json_helper;
pub mod prime_finder;
pub mod random;
pub mod string_ext;
pub mod time_span;
pub mod xml_helper;

pub use bitset::BitSet;
pub use creature::Creature;
pub use helper::{check_interval, triggered};
pub use random::{RandomElementByWeight, RandomExt};
pub use string_ext::StringExtensions;
pub use time_span::{is_between, TimeSpanExtensions};
