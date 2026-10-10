//! Game models (`AAEmu.Game.Models`).
//!
//! World object kinds (data halves; the world handles live in `world::object`):
//! [`character`] (players), [`npc`] (incl. portals), [`doodad`], [`slave`], [`mate`], [`house`],
//! [`transfer`], [`shipyard`], [`gimmick`]. Shared unit state: [`unit`]; positions: [`transform`].

pub mod buff;
pub mod char_data;
pub mod character;
pub mod container;
pub mod doodad;
pub mod gimmick;
pub mod house;
pub mod item;
pub mod mate;
pub mod movement;
pub mod npc;
pub mod shipyard;
pub mod slave;
pub mod stats;
pub mod transfer;
pub mod transform;
pub mod unit;
pub mod unit_model;
