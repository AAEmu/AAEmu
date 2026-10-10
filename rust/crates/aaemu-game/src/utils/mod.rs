//! `AAEmu.Game/Utils`: the helpers that are not owned by a subsystem.
//!
//! | module | C# |
//! |---|---|
//! | [`math`] | `MathUtil` |
//! | [`numeric`] | `NumericExtensions` |
//! | [`chat`] | `Converters/ChatConverter` |
//! | [`json_models`] | `Converters/Json*Converter`, `Models/Json/*` |
//!
//! `Utils/Scripts/**` is `systems::services::commands`, `Utils/DB/*` is `data::sqlite` (+
//! [`holdables_schema`]), `Utils/IdManager` is `managers::id`, `SphereCommandUtil` /
//! `QuestCommandUtil` are the bodies of the `SphereCmd` / `QuestCmd` GM commands (scripts phase).

pub mod chat;
pub mod holdables_schema;
pub mod json_models;
pub mod math;
pub mod numeric;
