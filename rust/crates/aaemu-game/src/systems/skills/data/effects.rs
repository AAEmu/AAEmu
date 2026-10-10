//! Effect templates (`Models/Game/Skills/Effects/*` data members) and their loaders, plus the
//! `effects` table (`EffectType`: id -> actual type / id).

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::sqlite::{for_each_row, SqlRow};
use crate::models::unit::BonusTemplate;

use super::templates::SkillControllerTemplate;

macro_rules! effect_kinds {
    ($($var:ident => $name:literal),* $(,)?) => {
        /// The keys of `SkillManager._effects` (C# effect class names).
        #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
        pub enum EffectKind { $($var),* }
        impl EffectKind {
            pub const ALL: &'static [EffectKind] = &[$(EffectKind::$var),*];
            /// `_effects.TryGetValue(name)`.
            pub fn from_name(name: &str) -> Option<Self> {
                match name { $($name => Some(EffectKind::$var),)* _ => None }
            }
            pub fn name(self) -> &'static str {
                match self { $(EffectKind::$var => $name),* }
            }
        }
    };
}

effect_kinds!(
    Buff => "Buff",
    AcceptQuestEffect => "AcceptQuestEffect",
    AccountAttributeEffect => "AccountAttributeEffect",
    AggroEffect => "AggroEffect",
    BubbleEffect => "BubbleEffect",
    BuffEffect => "BuffEffect",
    CinemaEffect => "CinemaEffect",
    CleanupUccEffect => "CleanupUccEffect",
    ConversionEffect => "ConversionEffect",
    CraftEffect => "CraftEffect",
    DamageEffect => "DamageEffect",
    DispelEffect => "DispelEffect",
    FlyingStateChangeEffect => "FlyingStateChangeEffect",
    GainLootPackItemEffect => "GainLootPackItemEffect",
    HealEffect => "HealEffect",
    ImprintUccEffect => "ImprintUccEffect",
    ImpulseEffect => "ImpulseEffect",
    InteractionEffect => "InteractionEffect",
    KillNpcWithoutCorpseEffect => "KillNpcWithoutCorpseEffect",
    ManaBurnEffect => "ManaBurnEffect",
    MoveToRezPointEffect => "MoveToRezPointEffect",
    NpcControlEffect => "NpcControlEffect",
    NpcSpawnerDespawnEffect => "NpcSpawnerDespawnEffect",
    NpcSpawnerSpawnEffect => "NpcSpawnerSpawnEffect",
    OpenPortalEffect => "OpenPortalEffect",
    PhysicalExplosionEffect => "PhysicalExplosionEffect",
    PutDownBackpackEffect => "PutDownBackpackEffect",
    RecoverExpEffect => "RecoverExpEffect",
    RepairSlaveEffect => "RepairSlaveEffect",
    ReportCrimeEffect => "ReportCrimeEffect",
    RestoreManaEffect => "RestoreManaEffect",
    ScopedFEffect => "ScopedFEffect",
    SpawnEffect => "SpawnEffect",
    SpawnGimmickEffect => "SpawnGimmickEffect",
    SpecialEffect => "SpecialEffect",
    TrainCraftEffect => "TrainCraftEffect",
    TrainCraftRankEffect => "TrainCraftRankEffect",
    SkillController => "SkillController",
    SpawnFishEffect => "SpawnFishEffect",
    ResetAoeDiminishingEffect => "ResetAoeDiminishingEffect",
);

#[derive(Debug, Clone, PartialEq)]
pub struct AccountAttributeEffect {
    pub id: u32,
    pub kind_id: u32,
    pub bind_world: bool,
    pub is_add: bool,
    pub count: u32,
    pub time: u32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct AggroEffect {
    pub id: u32,
    pub use_fixed_aggro: bool,
    pub fixed_min: i32,
    pub fixed_max: i32,
    pub use_level_aggro: bool,
    pub level_md: f32,
    pub level_va_start: i32,
    pub level_va_end: i32,
    pub use_charged_buff: bool,
    pub charged_buff_id: u32,
    pub charged_mul: f32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct BuffEffect {
    pub id: u32,
    /// `Buff?.Id` (`None` when buff_id has no template; `BuffId` then throws in C#).
    pub buff_id: Option<u32>,
    pub chance: i32,
    pub stack: i32,
    pub ab_level: i32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ConversionEffect {
    pub id: u32,
    pub category_id: u32,
    pub source_category_id: u32,
    pub source_value: i32,
    pub target_category_id: u32,
    pub target_value: i32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct DamageEffect {
    pub id: u32,
    pub damage_type: i32,
    pub fixed_min: i32,
    pub fixed_max: i32,
    pub multiplier: f32,
    pub use_mainhand_weapon: bool,
    pub use_offhand_weapon: bool,
    pub use_ranged_weapon: bool,
    pub critical_bonus: i32,
    pub target_buff_tag_id: u32,
    pub target_buff_bonus: i32,
    pub use_fixed_damage: bool,
    pub use_level_damage: bool,
    pub level_md: f32,
    pub level_va_start: i32,
    pub level_va_end: i32,
    pub target_buff_bonus_mul: f32,
    pub use_charged_buff: bool,
    pub charged_buff_id: u32,
    pub charged_mul: f32,
    pub aggro_multiplier: f32,
    pub health_steal_ratio: i32,
    pub mana_steal_ratio: i32,
    pub dps_multiplier: f32,
    pub weapon_slot_id: i32,
    pub check_crime: bool,
    pub hit_anim_timing_id: u32,
    pub use_target_charged_buff: bool,
    pub target_charged_buff_id: u32,
    pub target_charged_mul: f32,
    pub dps_inc_multiplier: f32,
    pub engage_combat: bool,
    pub synergy: bool,
    pub actability_group_id: u32,
    pub actability_step: i32,
    pub actability_mul: f32,
    pub actability_add: f32,
    pub charged_level_mul: f32,
    pub adjust_damage_by_height: bool,
    pub use_percent_damage: bool,
    pub percent_min: i32,
    pub percent_max: i32,
    pub use_current_health: bool,
    pub target_health_min: i32,
    pub target_health_max: i32,
    pub target_health_mul: f32,
    pub target_health_add: i32,
    pub fire_proc: bool,
    /// `Bonuses` (never filled by the 1.2 loader).
    pub bonuses: Vec<BonusTemplate>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct DispelEffect {
    pub id: u32,
    pub dispel_count: i32,
    pub cure_count: i32,
    pub buff_tag_id: u32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct GainLootPackItemEffect {
    pub id: u32,
    pub loot_pack_id: u32,
    pub consume_source_item: bool,
    pub consume_item_id: u32,
    pub consume_count: i32,
    pub inherit_grade: bool,
}

#[derive(Debug, Clone, PartialEq)]
pub struct HealEffect {
    pub id: u32,
    pub use_fixed_heal: bool,
    pub fixed_min: i32,
    pub fixed_max: i32,
    pub use_level_heal: bool,
    pub level_md: f32,
    pub level_va_start: i32,
    pub level_va_end: i32,
    pub percent: bool,
    pub use_charged_buff: bool,
    pub charged_buff_id: u32,
    pub charged_mul: f32,
    pub slave_applicable: bool,
    pub ignore_heal_aggro: bool,
    pub dps_multiplier: f32,
    pub actability_group_id: u32,
    pub actability_step: i32,
    pub actability_mul: f32,
    pub actability_add: f32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ImpulseEffect {
    pub id: u32,
    pub vel_impulse: [f32; 3],
    pub angvel_impulse: [f32; 3],
    pub impulse: [f32; 3],
    pub ang_impulse: [f32; 3],
}

#[derive(Debug, Clone, PartialEq)]
pub struct KillNpcWithoutCorpseEffect {
    pub id: u32,
    pub npc_id: u32,
    pub radius: f32,
    pub give_exp: bool,
    pub vanish: bool,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ManaBurnEffect {
    pub id: u32,
    pub base_min: i32,
    pub base_max: i32,
    pub damage_ratio: i32,
    pub level_md: f32,
    pub level_va_start: i32,
    pub level_va_end: i32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct NpcControlEffect {
    pub id: u32,
    pub category_id: u32,
    pub param_string: String,
    pub param_int: u32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct PhysicalExplosionEffect {
    pub id: u32,
    pub radius: f32,
    pub hole_size: f32,
    pub pressure: f32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct RecoverExpEffect {
    pub id: u32,
    pub need_money: bool,
    pub need_labor_power: bool,
    pub need_priest: bool,
}

#[derive(Debug, Clone, PartialEq)]
pub struct RestoreManaEffect {
    pub id: u32,
    pub use_fixed_value: bool,
    pub fixed_min: i32,
    pub fixed_max: i32,
    pub use_level_value: bool,
    pub level_md: f32,
    pub level_va_start: i32,
    pub level_va_end: i32,
    pub percent: bool,
}

#[derive(Debug, Clone, PartialEq)]
pub struct SpawnEffect {
    pub id: u32,
    pub owner_type_id: u32,
    pub sub_type: u32,
    pub pos_dir_id: u32,
    pub pos_angle: f32,
    pub pos_distance: f32,
    pub ori_dir_id: u32,
    pub ori_angle: f32,
    pub use_summoner_faction: bool,
    pub life_time: f32,
    pub despawn_on_creator_death: bool,
    pub use_summoner_aggro_target: bool,
    pub mate_state_id: u32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct NpcSpawnerSpawnEffect {
    pub id: u32,
    pub spawner_id: u32,
    pub life_time: f32,
    pub despawn_on_creator_death: bool,
    pub use_summoner_aggro_target: bool,
    pub activation_state: bool,
}

#[derive(Debug, Clone, PartialEq)]
pub struct SpawnGimmickEffect {
    pub id: u32,
    pub gimmick_id: u32,
    pub offset_from_source: bool,
    pub offset_coordinate_id: u32,
    pub offset: [f32; 3],
    pub scale: f32,
    pub velocity_coordinate_id: u32,
    pub velocity: [f32; 3],
    pub ang_vel_coordinate_id: u32,
    pub ang_vel: [f32; 3],
}

#[derive(Debug, Clone, PartialEq)]
pub struct SpecialEffect {
    pub id: u32,
    pub special_effect_type_id: i32,
    pub value1: i32,
    pub value2: i32,
    pub value3: i32,
    pub value4: i32,
}

/// Every effect template kind (the C# `EffectTemplate` subclasses).
#[derive(Debug, Clone, PartialEq)]
pub enum EffectTemplate {
    AcceptQuest {
        id: u32,
        quest_id: u32,
    },
    AccountAttribute(AccountAttributeEffect),
    Aggro(AggroEffect),
    Bubble {
        id: u32,
        kind_id: u32,
    },
    Buff(BuffEffect),
    Cinema {
        id: u32,
        cinema_id: u32,
    },
    CleanupUcc {
        id: u32,
    },
    Conversion(ConversionEffect),
    Craft {
        id: u32,
        world_interaction: u32,
    },
    Damage(Box<DamageEffect>),
    Dispel(DispelEffect),
    FlyingStateChange {
        id: u32,
        flying_state: bool,
    },
    GainLootPackItem(GainLootPackItemEffect),
    Heal(HealEffect),
    ImprintUcc {
        id: u32,
        item_id: u32,
    },
    Impulse(ImpulseEffect),
    Interaction {
        id: u32,
        world_interaction: i32,
        doodad_id: u32,
    },
    KillNpcWithoutCorpse(KillNpcWithoutCorpseEffect),
    ManaBurn(ManaBurnEffect),
    MoveToRezPoint {
        id: u32,
    },
    NpcControl(NpcControlEffect),
    OpenPortal {
        id: u32,
        distance: f32,
    },
    PhysicalExplosion(PhysicalExplosionEffect),
    PutDownBackpack {
        id: u32,
        backpack_doodad_id: u32,
    },
    RecoverExp(RecoverExpEffect),
    RepairSlave {
        id: u32,
        health: i32,
        mana: i32,
    },
    ReportCrime {
        id: u32,
        value: i32,
        crime_kind_id: u32,
    },
    ResetAoeDiminishing {
        id: u32,
    },
    RestoreMana(RestoreManaEffect),
    ScopedF {
        id: u32,
        range: i32,
        key: bool,
        doodad_id: u32,
    },
    Spawn(SpawnEffect),
    NpcSpawnerSpawn(NpcSpawnerSpawnEffect),
    NpcSpawnerDespawn {
        id: u32,
        spawner_id: u32,
    },
    SpawnFish {
        id: u32,
        range: u32,
        doodad_id: u32,
    },
    SpawnGimmick(SpawnGimmickEffect),
    Special(SpecialEffect),
    TrainCraft {
        id: u32,
        craft_id: u32,
    },
    TrainCraftRank {
        id: u32,
        kind_id: u32,
        rank_id: u32,
    },
    SkillController(SkillControllerTemplate),
}

impl EffectTemplate {
    pub fn id(&self) -> u32 {
        use EffectTemplate::*;
        match self {
            AcceptQuest { id, .. }
            | Bubble { id, .. }
            | Cinema { id, .. }
            | CleanupUcc { id }
            | Craft { id, .. }
            | FlyingStateChange { id, .. }
            | ImprintUcc { id, .. }
            | Interaction { id, .. }
            | MoveToRezPoint { id }
            | OpenPortal { id, .. }
            | PutDownBackpack { id, .. }
            | RepairSlave { id, .. }
            | ReportCrime { id, .. }
            | ResetAoeDiminishing { id }
            | ScopedF { id, .. }
            | NpcSpawnerDespawn { id, .. }
            | SpawnFish { id, .. }
            | TrainCraft { id, .. }
            | TrainCraftRank { id, .. } => *id,
            AccountAttribute(e) => e.id,
            Aggro(e) => e.id,
            Buff(e) => e.id,
            Conversion(e) => e.id,
            Damage(e) => e.id,
            Dispel(e) => e.id,
            GainLootPackItem(e) => e.id,
            Heal(e) => e.id,
            Impulse(e) => e.id,
            KillNpcWithoutCorpse(e) => e.id,
            ManaBurn(e) => e.id,
            NpcControl(e) => e.id,
            PhysicalExplosion(e) => e.id,
            RecoverExp(e) => e.id,
            RestoreMana(e) => e.id,
            Spawn(e) => e.id,
            NpcSpawnerSpawn(e) => e.id,
            SpawnGimmick(e) => e.id,
            Special(e) => e.id,
            SkillController(e) => e.id,
        }
    }

    /// `BuffId` (`BuffEffect.BuffId` = its buff, every other effect = its own id).
    pub fn buff_id(&self) -> u32 {
        match self {
            EffectTemplate::Buff(b) => b.buff_id.unwrap_or(0),
            other => other.id(),
        }
    }
}

/// `EffectType` (effects rows): `Id -> (ActualType, ActualId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EffectType {
    pub id: u32,
    pub actual_id: u32,
    pub actual_type: String,
}

fn v3(r: &SqlRow<'_, '_>, a: &str, b: &str, c: &str) -> rusqlite::Result<[f32; 3]> {
    Ok([r.f32(a)?, r.f32(b)?, r.f32(c)?])
}

/// The per-type effect tables in `SkillManager.Load` order. `buff_exists` resolves
/// `BuffEffect.Buff`. Duplicate ids within a type are an error (`Dictionary.Add`).
pub fn load_effect_tables(
    conn: &Connection,
    buff_exists: &dyn Fn(u32) -> bool,
    out: &mut HashMap<EffectKind, HashMap<u32, EffectTemplate>>,
) -> rusqlite::Result<()> {
    for k in EffectKind::ALL {
        out.entry(*k).or_default();
    }
    let mut add = |kind: EffectKind, t: EffectTemplate| -> rusqlite::Result<()> {
        let map = out.entry(kind).or_default();
        let id = t.id();
        if map.insert(id, t).is_some() {
            return Err(rusqlite::Error::InvalidParameterName(format!(
                "duplicate {} {id} (Dictionary.Add)",
                kind.name()
            )));
        }
        Ok(())
    };
    use EffectKind as K;
    use EffectTemplate as T;

    for_each_row(conn, "SELECT * FROM buff_effects", [], |r| {
        let buff_id = r.u32("buff_id")?;
        add(
            K::BuffEffect,
            T::Buff(BuffEffect {
                id: r.u32("id")?,
                buff_id: buff_exists(buff_id).then_some(buff_id),
                chance: r.i32("chance")?,
                stack: r.i32("stack")?,
                ab_level: r.i32("ab_level")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM skill_controllers", [], |r| {
        add(
            K::SkillController,
            T::SkillController(super::templates::read_skill_controller(r)?),
        )
    })?;
    for_each_row(conn, "SELECT * FROM account_attribute_effects", [], |r| {
        add(
            K::AccountAttributeEffect,
            T::AccountAttribute(AccountAttributeEffect {
                id: r.u32("id")?,
                kind_id: r.u32("kind_id")?,
                bind_world: r.bool("bind_world")?,
                is_add: r.bool("is_add")?,
                count: r.u32("count")?,
                time: r.u32("time")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM accept_quest_effects", [], |r| {
        add(
            K::AcceptQuestEffect,
            T::AcceptQuest {
                id: r.u32("id")?,
                quest_id: r.u32("quest_id")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM aggro_effects", [], |r| {
        add(
            K::AggroEffect,
            T::Aggro(AggroEffect {
                id: r.u32("id")?,
                use_fixed_aggro: r.bool_str("use_fixed_aggro")?,
                fixed_min: r.i32("fixed_min")?,
                fixed_max: r.i32("fixed_max")?,
                use_level_aggro: r.bool_str("use_level_aggro")?,
                level_md: r.f32("level_md")?,
                level_va_start: r.i32("level_va_start")?,
                level_va_end: r.i32("level_va_end")?,
                use_charged_buff: r.bool_str("use_charged_buff")?,
                charged_buff_id: r.u32_or("charged_buff_id", 0)?,
                charged_mul: r.f32("charged_mul")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM bubble_effects", [], |r| {
        add(
            K::BubbleEffect,
            T::Bubble {
                id: r.u32("id")?,
                kind_id: r.u32("kind_id")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM cinema_effects", [], |r| {
        add(
            K::CinemaEffect,
            T::Cinema {
                id: r.u32("id")?,
                cinema_id: r.u32("cinema_id")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM cleanup_ucc_effects", [], |r| {
        add(K::CleanupUccEffect, T::CleanupUcc { id: r.u32("id")? })
    })?;
    for_each_row(conn, "SELECT * FROM conversion_effects", [], |r| {
        add(
            K::ConversionEffect,
            T::Conversion(ConversionEffect {
                id: r.u32("id")?,
                category_id: r.u32("category_id")?,
                source_category_id: r.u32("source_category_id")?,
                source_value: r.i32("source_value")?,
                target_category_id: r.u32("target_category_id")?,
                target_value: r.i32("target_value")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM craft_effects", [], |r| {
        add(
            K::CraftEffect,
            T::Craft {
                id: r.u32("id")?,
                world_interaction: r.u32("wi_id")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM damage_effects", [], |r| {
        add(
            K::DamageEffect,
            T::Damage(Box::new(DamageEffect {
                id: r.u32("id")?,
                damage_type: r.i32("damage_type_id")?,
                fixed_min: r.i32("fixed_min")?,
                fixed_max: r.i32("fixed_max")?,
                multiplier: r.f32("multiplier")?,
                use_mainhand_weapon: r.bool_str("use_mainhand_weapon")?,
                use_offhand_weapon: r.bool_str("use_offhand_weapon")?,
                use_ranged_weapon: r.bool_str("use_ranged_weapon")?,
                critical_bonus: r.i32("critical_bonus")?,
                target_buff_tag_id: r.u32_or("target_buff_tag_id", 0)?,
                target_buff_bonus: r.i32("target_buff_bonus")?,
                use_fixed_damage: r.bool_str("use_fixed_damage")?,
                use_level_damage: r.bool_str("use_level_damage")?,
                level_md: r.f32("level_md")?,
                level_va_start: r.i32("level_va_start")?,
                level_va_end: r.i32("level_va_end")?,
                target_buff_bonus_mul: r.f32("target_buff_bonus_mul")?,
                use_charged_buff: r.bool_str("use_charged_buff")?,
                charged_buff_id: r.u32_or("charged_buff_id", 0)?,
                charged_mul: r.f32("charged_mul")?,
                aggro_multiplier: r.f32("aggro_multiplier")?,
                health_steal_ratio: r.i32("health_steal_ratio")?,
                mana_steal_ratio: r.i32("mana_steal_ratio")?,
                dps_multiplier: r.f32("dps_multiplier")?,
                weapon_slot_id: r.i32("weapon_slot_id")?,
                check_crime: r.bool_str("check_crime")?,
                hit_anim_timing_id: r.u32("hit_anim_timing_id")?,
                use_target_charged_buff: r.bool_str("use_target_charged_buff")?,
                target_charged_buff_id: r.u32_or("target_charged_buff_id", 0)?,
                target_charged_mul: r.f32("target_charged_mul")?,
                dps_inc_multiplier: r.f32("dps_inc_multiplier")?,
                engage_combat: r.bool_str("engage_combat")?,
                synergy: r.bool_str("synergy")?,
                actability_group_id: r.u32_or("actability_group_id", 0)?,
                actability_step: r.i32("actability_step")?,
                actability_mul: r.f32("actability_mul")?,
                actability_add: r.f32("actability_add")?,
                charged_level_mul: r.f32("charged_level_mul")?,
                adjust_damage_by_height: r.bool_str("adjust_damage_by_height")?,
                use_percent_damage: r.bool_str("use_percent_damage")?,
                percent_min: r.i32("percent_min")?,
                percent_max: r.i32("percent_max")?,
                use_current_health: r.bool_str("use_current_health")?,
                target_health_min: r.i32("target_health_min")?,
                target_health_max: r.i32("target_health_max")?,
                target_health_mul: r.f32("target_health_mul")?,
                target_health_add: r.i32("target_health_add")?,
                fire_proc: r.bool_str("fire_proc")?,
                bonuses: Vec::new(),
            })),
        )
    })?;
    for_each_row(conn, "SELECT * FROM dispel_effects", [], |r| {
        add(
            K::DispelEffect,
            T::Dispel(DispelEffect {
                id: r.u32("id")?,
                dispel_count: r.i32("dispel_count")?,
                cure_count: r.i32("cure_count")?,
                buff_tag_id: r.u32_or("buff_tag_id", 0)?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM flying_state_change_effects", [], |r| {
        add(
            K::FlyingStateChangeEffect,
            T::FlyingStateChange {
                id: r.u32("id")?,
                flying_state: r.bool_str("flying_state")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM gain_loot_pack_item_effects", [], |r| {
        add(
            K::GainLootPackItemEffect,
            T::GainLootPackItem(GainLootPackItemEffect {
                id: r.u32("id")?,
                loot_pack_id: r.u32("loot_pack_id")?,
                consume_source_item: r.bool_str("consume_source_item")?,
                consume_item_id: r.u32_or("consume_item_id", 0)?,
                consume_count: r.i32("consume_count")?,
                inherit_grade: r.bool_str("inherit_grade")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM heal_effects", [], |r| {
        add(
            K::HealEffect,
            T::Heal(HealEffect {
                id: r.u32("id")?,
                use_fixed_heal: r.bool_str("use_fixed_heal")?,
                fixed_min: r.i32("fixed_min")?,
                fixed_max: r.i32("fixed_max")?,
                use_level_heal: r.bool_str("use_level_heal")?,
                level_md: r.f32("level_md")?,
                level_va_start: r.i32("level_va_start")?,
                level_va_end: r.i32("level_va_end")?,
                percent: r.bool_str("percent")?,
                use_charged_buff: r.bool_str("use_charged_buff")?,
                charged_buff_id: r.u32_or("charged_buff_id", 0)?,
                charged_mul: r.f32("charged_mul")?,
                slave_applicable: r.bool_str("slave_applicable")?,
                ignore_heal_aggro: r.bool_str("ignore_heal_aggro")?,
                dps_multiplier: r.f32("dps_multiplier")?,
                actability_group_id: r.u32_or("actability_group_id", 0)?,
                actability_step: r.i32("actability_step")?,
                actability_mul: r.f32("actability_mul")?,
                actability_add: r.f32("actability_add")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM imprint_ucc_effects", [], |r| {
        add(
            K::ImprintUccEffect,
            T::ImprintUcc {
                id: r.u32("id")?,
                item_id: r.u32_or("item_id", 0)?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM impulse_effects", [], |r| {
        add(
            K::ImpulseEffect,
            T::Impulse(ImpulseEffect {
                id: r.u32("id")?,
                vel_impulse: v3(r, "vel_impulse_x", "vel_impulse_y", "vel_impulse_z")?,
                angvel_impulse: v3(
                    r,
                    "angvel_impulse_x",
                    "angvel_impulse_y",
                    "angvel_impulse_z",
                )?,
                impulse: v3(r, "impulse_x", "impulse_y", "impulse_z")?,
                ang_impulse: v3(r, "ang_impulse_x", "ang_impulse_y", "ang_impulse_z")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM interaction_effects", [], |r| {
        add(
            K::InteractionEffect,
            T::Interaction {
                id: r.u32("id")?,
                world_interaction: r.i32("wi_id")?,
                doodad_id: r.u32_or("doodad_id", 0)?,
            },
        )
    })?;
    for_each_row(
        conn,
        "SELECT * FROM kill_npc_without_corpse_effects",
        [],
        |r| {
            add(
                K::KillNpcWithoutCorpseEffect,
                T::KillNpcWithoutCorpse(KillNpcWithoutCorpseEffect {
                    id: r.u32("id")?,
                    npc_id: r.u32("npc_id")?,
                    radius: r.f32("radius")?,
                    give_exp: r.bool_str("give_exp")?,
                    vanish: r.bool_str("vanish")?,
                }),
            )
        },
    )?;
    for_each_row(conn, "SELECT * FROM mana_burn_effects", [], |r| {
        add(
            K::ManaBurnEffect,
            T::ManaBurn(ManaBurnEffect {
                id: r.u32("id")?,
                base_min: r.i32("base_min")?,
                base_max: r.i32("base_max")?,
                damage_ratio: r.i32("damage_ratio")?,
                level_md: r.f32("level_md")?,
                level_va_start: r.i32("level_va_start")?,
                level_va_end: r.i32("level_va_end")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM move_to_rez_point_effects", [], |r| {
        add(
            K::MoveToRezPointEffect,
            T::MoveToRezPoint { id: r.u32("id")? },
        )
    })?;
    for_each_row(conn, "SELECT * FROM npc_control_effects", [], |r| {
        add(
            K::NpcControlEffect,
            T::NpcControl(NpcControlEffect {
                id: r.u32("id")?,
                category_id: r.u32("category_id")?,
                param_string: r.string_or("param_string", "")?,
                param_int: r.u32("param_int")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM open_portal_effects", [], |r| {
        add(
            K::OpenPortalEffect,
            T::OpenPortal {
                id: r.u32("id")?,
                distance: r.f32("distance")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM physical_explosion_effects", [], |r| {
        add(
            K::PhysicalExplosionEffect,
            T::PhysicalExplosion(PhysicalExplosionEffect {
                id: r.u32("id")?,
                radius: r.f32("radius")?,
                hole_size: r.f32("hole_size")?,
                pressure: r.f32("pressure")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM put_down_backpack_effects", [], |r| {
        add(
            K::PutDownBackpackEffect,
            T::PutDownBackpack {
                id: r.u32("id")?,
                backpack_doodad_id: r.u32("backpack_doodad_id")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM recover_exp_effects", [], |r| {
        add(
            K::RecoverExpEffect,
            T::RecoverExp(RecoverExpEffect {
                id: r.u32("id")?,
                need_money: r.bool_str("need_money")?,
                need_labor_power: r.bool_str("need_labor_power")?,
                need_priest: r.bool_str("need_priest")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM repair_slave_effects", [], |r| {
        add(
            K::RepairSlaveEffect,
            T::RepairSlave {
                id: r.u32("id")?,
                health: r.i32("health")?,
                mana: r.i32("mana")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM report_crime_effects", [], |r| {
        add(
            K::ReportCrimeEffect,
            T::ReportCrime {
                id: r.u32("id")?,
                value: r.i32("value")?,
                crime_kind_id: r.u32("crime_kind_id")?,
            },
        )
    })?;
    for_each_row(
        conn,
        "SELECT * FROM reset_aoe_diminishing_effects",
        [],
        |r| {
            add(
                K::ResetAoeDiminishingEffect,
                T::ResetAoeDiminishing { id: r.u32("id")? },
            )
        },
    )?;
    for_each_row(conn, "SELECT * FROM restore_mana_effects", [], |r| {
        add(
            K::RestoreManaEffect,
            T::RestoreMana(RestoreManaEffect {
                id: r.u32("id")?,
                use_fixed_value: r.bool_str("use_fixed_value")?,
                fixed_min: r.i32("fixed_min")?,
                fixed_max: r.i32("fixed_max")?,
                use_level_value: r.bool_str("use_level_value")?,
                level_md: r.f32("level_md")?,
                level_va_start: r.i32("level_va_start")?,
                level_va_end: r.i32("level_va_end")?,
                percent: r.bool_str("percent")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM scoped_f_effects", [], |r| {
        add(
            K::ScopedFEffect,
            T::ScopedF {
                id: r.u32("id")?,
                range: r.i32("range")?,
                key: r.bool_str("key")?,
                doodad_id: r.u32("doodad_id")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM spawn_effects", [], |r| {
        add(
            K::SpawnEffect,
            T::Spawn(SpawnEffect {
                id: r.u32("id")?,
                owner_type_id: r.u32("owner_type_id")?,
                sub_type: r.u32("sub_type")?,
                pos_dir_id: r.u32("pos_dir_id")?,
                pos_angle: r.f32("pos_angle")?,
                pos_distance: r.f32("pos_distance")?,
                ori_dir_id: r.u32("ori_dir_id")?,
                ori_angle: r.f32("ori_angle")?,
                use_summoner_faction: r.bool_str("use_summoner_faction")?,
                life_time: r.f32("life_time")?,
                despawn_on_creator_death: r.bool_str("despawn_on_creator_death")?,
                use_summoner_aggro_target: r.bool_str("use_summoner_aggro_target")?,
                mate_state_id: r.u32_or("mate_state_id", 0)?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM npc_spawner_spawn_effects", [], |r| {
        add(
            K::NpcSpawnerSpawnEffect,
            T::NpcSpawnerSpawn(NpcSpawnerSpawnEffect {
                id: r.u32("id")?,
                spawner_id: r.u32_or("spawner_id", 0)?,
                life_time: r.f32("life_time")?,
                despawn_on_creator_death: r.bool_str("despawn_on_creator_death")?,
                use_summoner_aggro_target: r.bool_str("use_summoner_aggro_target")?,
                activation_state: r.bool_str("activation_state")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM npc_spawner_despawn_effects", [], |r| {
        add(
            K::NpcSpawnerDespawnEffect,
            T::NpcSpawnerDespawn {
                id: r.u32("id")?,
                spawner_id: r.u32("spawner_id")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM spawn_fish_effects", [], |r| {
        add(
            K::SpawnFishEffect,
            T::SpawnFish {
                id: r.u32("id")?,
                range: r.u32("range")?,
                doodad_id: r.u32_or("doodad_id", 0)?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM spawn_gimmick_effects", [], |r| {
        add(
            K::SpawnGimmickEffect,
            T::SpawnGimmick(SpawnGimmickEffect {
                id: r.u32("id")?,
                gimmick_id: r.u32("gimmick_id")?,
                offset_from_source: r.bool_str("offset_from_source")?,
                offset_coordinate_id: r.u32("offset_coordiate_id")?,
                offset: v3(r, "offset_x", "offset_y", "offset_z")?,
                scale: r.f32("scale")?,
                velocity_coordinate_id: r.u32("velocity_coordiate_id")?,
                velocity: v3(r, "velocity_x", "velocity_y", "velocity_z")?,
                ang_vel_coordinate_id: r.u32("ang_vel_coordiate_id")?,
                ang_vel: v3(r, "ang_vel_x", "ang_vel_y", "ang_vel_z")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM special_effects", [], |r| {
        add(
            K::SpecialEffect,
            T::Special(SpecialEffect {
                id: r.u32("id")?,
                special_effect_type_id: r.i32("special_effect_type_id")?,
                value1: r.i32("value1")?,
                value2: r.i32("value2")?,
                value3: r.i32("value3")?,
                value4: r.i32("value4")?,
            }),
        )
    })?;
    for_each_row(conn, "SELECT * FROM train_craft_effects", [], |r| {
        add(
            K::TrainCraftEffect,
            T::TrainCraft {
                id: r.u32("id")?,
                craft_id: r.u32("craft_id")?,
            },
        )
    })?;
    for_each_row(conn, "SELECT * FROM train_craft_rank_effects", [], |r| {
        add(
            K::TrainCraftRankEffect,
            T::TrainCraftRank {
                id: r.u32("id")?,
                kind_id: r.u32("kind_id")?,
                rank_id: r.u32("rank_id")?,
            },
        )
    })?;
    Ok(())
}

/// `SELECT * FROM effects` (duplicate ids are an error, `Dictionary.Add`).
pub fn load_effect_types(conn: &Connection) -> rusqlite::Result<HashMap<u32, EffectType>> {
    let mut out = HashMap::new();
    for_each_row(conn, "SELECT * FROM effects", [], |r| {
        let t = EffectType {
            id: r.u32("id")?,
            actual_id: r.u32("actual_id")?,
            actual_type: r.string("actual_type")?,
        };
        if out.insert(t.id, t.clone()).is_some() {
            return Err(rusqlite::Error::InvalidParameterName(format!(
                "duplicate effect {} (Dictionary.Add)",
                t.id
            )));
        }
        Ok(())
    })?;
    Ok(out)
}
