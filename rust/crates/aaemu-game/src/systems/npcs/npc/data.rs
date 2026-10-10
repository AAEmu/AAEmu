//! `NpcManager.Load` (C#: `Core/Managers/UnitManagers/NpcManager.cs`): NPC templates
//! (`Models/Game/NPChar/NpcTemplate`) and their look (`total_character_customs`,
//! `item_body_parts`, `characters` race / gender), equipment packs (`equip_pack_cloths`,
//! `equip_pack_weapons`, `equip_pack_body_parts`), postures (`npc_postures`), stat bonuses
//! (`unit_modifiers` of `owner_type='Npc'`), initial buffs (`npc_initial_buffs`), merchant packs
//! (`merchants`, `merchant_goods` -> `Models/Game/Merchant/MerchantGoods`).
//!
//! Also loaded here because they are pure template data bound to `NpcTemplate`:
//! * `NpcGameData.Load` / `PostLoad` skill part: `np_skills` (`BindSkillsToTemplate`) and
//!   `np_passive_buffs` (`NpcTemplate.PassiveBuffs`; the `PassiveBuffTemplate` is resolved at use
//!   time from `game_data.skills`, which `SkillManager` loads before `PostLoad` runs);
//! * the part of `ModelManager.Load` `NpcManager` uses (`models`, `actor_models.movement_id`:
//!   `GetModelType`, `IsFlyOrSwim`);
//! * the weapon fields `Weapon.Dps` / `MDps` read (`holdables.formula_dps` / `formula_mdps`,
//!   `items.level`, `item_grades.var_holdable_dps` / `var_holdable_magic_dps`), for the NPC
//!   attribute getters (`npc::stats`);
//! * the localized NPC names (`LocalizationManager.Get("npcs", "name", id)`, used by
//!   `SCNpcChatMessagePacket`).
//!
//! Not ported: `Creatures.xml` (`GetSpawnName`, GM commands only), `LoadAiParams` (AI phase).
//!
//! Failures that throw in C# (duplicate `Dictionary.Add` keys, NULL in a non-defaulted column)
//! abort the load with an error, like the C# exception would abort startup.

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::formulas::Formula;
use crate::data::sqlite::{for_each_row, SqlRow};
use crate::models::unit::{BonusTemplate, UnitAttribute, UnitModifierType};
use crate::models::unit_model::{FaceModel, UnitCustomModelParams, UnitCustomModelType};
use crate::systems::DataLoadCtx;

/// `EquipmentItemSlotType` values used by the body-part code.
pub mod slot_type {
    pub const FACE: u32 = 23;
    pub const HAIR: u32 = 24;
    pub const GLASSES: u32 = 25;
    pub const TAIL: u32 = 27;
    pub const BODY: u32 = 28;
    pub const BEARD: u32 = 29;
}

/// `NpcPosture`.
#[derive(Debug, Clone, PartialEq)]
pub struct NpcPosture {
    pub npc_posture_set_id: u32,
    pub anim_action_id: u32,
    pub talk_anim: String,
    pub start_tod_time: f32,
}

/// `NpcSkill` (`np_skills` row).
#[derive(Debug, Clone, PartialEq)]
pub struct NpcSkill {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub skill_id: u32,
    /// `SkillUseConditionKind` (raw).
    pub skill_use_condition: u32,
    pub skill_use_param1: f32,
    pub skill_use_param2: f32,
}

/// `NpcPassiveBuff` (`np_passive_buffs` row). `PassiveBuff` (the template) is
/// `game_data.skills.passive_buff(passive_buff_id)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NpcPassiveBuff {
    pub id: u32,
    pub owner_id: u32,
    pub owner_type: String,
    pub passive_buff_id: u32,
}

/// `EquipItemsTemplate`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct EquipItemsTemplate {
    pub headgear: u32,
    pub headgear_grade: u8,
    pub necklace: u32,
    pub necklace_grade: u8,
    pub shirt: u32,
    pub shirt_grade: u8,
    pub belt: u32,
    pub belt_grade: u8,
    pub pants: u32,
    pub pants_grade: u8,
    pub gloves: u32,
    pub gloves_grade: u8,
    pub shoes: u32,
    pub shoes_grade: u8,
    pub bracelet: u32,
    pub bracelet_grade: u8,
    pub back: u32,
    pub back_grade: u8,
    pub cosplay: u32,
    pub cosplay_grade: u8,
    pub undershirts: u32,
    pub undershirts_grade: u8,
    pub underpants: u32,
    pub underpants_grade: u8,
    pub mainhand: u32,
    pub mainhand_grade: u8,
    pub offhand: u32,
    pub offhand_grade: u8,
    pub ranged: u32,
    pub ranged_grade: u8,
    pub musical: u32,
    pub musical_grade: u8,
}

/// `NpcTemplate.BodyItems` (`(ItemId, NpcOnly)[7]`, index = `SlotTypeId - 23`).
pub type BodyItems = [(u32, bool); 7];

/// `NpcTemplate`.
#[derive(Debug, Clone, PartialEq)]
pub struct NpcTemplate {
    pub id: u32,
    pub name: String,
    pub char_race_id: i32,
    /// `NpcGradeType` (raw).
    pub npc_grade_id: u8,
    /// `NpcKindType` (raw).
    pub npc_kind_id: u8,
    pub level: u8,
    /// `NpcTemplateType` (raw).
    pub npc_template_id: u8,
    pub model_id: u32,
    /// `FactionsEnum` (raw).
    pub faction_id: u32,
    pub skill_trainer: bool,
    pub ai_file_id: i32,
    pub merchant: bool,
    pub npc_nickname_id: i32,
    pub auctioneer: bool,
    pub show_name_tag: bool,
    pub visible_to_creator_only: bool,
    pub no_exp: bool,
    pub pet_item_id: u32,
    pub base_skill_id: i32,
    pub track_friendship: bool,
    pub priest: bool,
    pub npc_tedency_id: i32,
    pub blacksmith: bool,
    pub teleporter: bool,
    pub opacity: f32,
    pub ability_changer: bool,
    pub scale: f32,
    pub sight_range_scale: f32,
    pub sight_fov_scale: f32,
    pub milestone_id: i32,
    pub attack_start_range_scale: f32,
    pub aggression: bool,
    pub exp_multiplier: f32,
    pub exp_adder: i32,
    pub stabler: bool,
    pub accept_aggro_link: bool,
    pub recruting_battlefield_id: i32,
    pub return_distance: f32,
    pub npc_ai_param_id: i32,
    pub non_pushable_by_actor: bool,
    pub banker: bool,
    /// `AggroLinkSpecialRuleKind` (raw).
    pub aggro_link_special_rule_id: i32,
    pub aggro_link_help_dist: f32,
    pub aggro_link_sight_check: bool,
    pub expedition: bool,
    pub honor_point: i32,
    pub trader: bool,
    pub aggro_link_special_guard: bool,
    pub aggro_link_special_ignore_npc_attacker: bool,
    pub absolute_return_distance: f32,
    pub repairman: bool,
    pub activate_ai_always: bool,
    pub specialty: bool,
    pub specialty_coin_id: u32,
    pub use_range_mod: bool,
    pub npc_posture_set_id: i32,
    /// Sorted by `start_tod_time DESC` (as loaded).
    pub npc_posture_sets: Vec<NpcPosture>,
    pub mate_equip_slot_pack_id: i32,
    pub mate_kind_id: i32,
    pub engage_combat_give_quest_id: u32,
    pub no_apply_total_custom: bool,
    pub base_skill_strafe: bool,
    pub base_skill_delay: f32,
    pub npc_interaction_set_id: i32,
    pub use_abuser_list: bool,
    pub return_when_enter_housing_area: bool,
    pub look_converter: bool,
    pub use_ddcms_mount_skill: bool,
    pub crowd_effect: bool,
    /// From `characters` (by `model_id`), 0 when the model is not a player model.
    pub race: u8,
    pub gender: u8,
    pub merchant_pack_id: u32,
    pub equip_bodies_id: u32,
    pub equip_cloths_id: u32,
    pub equip_weapons_id: u32,
    pub hair_id: u32,
    pub model_params: UnitCustomModelParams,
    pub items: EquipItemsTemplate,
    pub body_items: BodyItems,
    /// `npc_initial_buffs`.
    pub buffs: Vec<u32>,
    /// `unit_modifiers` (`owner_type='Npc'`).
    pub bonuses: Vec<BonusTemplate>,
    /// `Skills` (`SkillUseConditionKind` -> skills), in first-seen order.
    pub skills: Vec<(u32, Vec<NpcSkill>)>,
    pub passive_buffs: Vec<NpcPassiveBuff>,
    pub total_custom_id: u32,
}

impl Default for NpcTemplate {
    fn default() -> Self {
        Self {
            id: 0,
            name: String::new(),
            char_race_id: 0,
            npc_grade_id: 0,
            npc_kind_id: 0,
            level: 0,
            npc_template_id: 0,
            model_id: 0,
            faction_id: 0,
            skill_trainer: false,
            ai_file_id: 0,
            merchant: false,
            npc_nickname_id: 0,
            auctioneer: false,
            show_name_tag: false,
            visible_to_creator_only: false,
            no_exp: false,
            pet_item_id: 0,
            base_skill_id: 0,
            track_friendship: false,
            priest: false,
            npc_tedency_id: 0,
            blacksmith: false,
            teleporter: false,
            opacity: 0.0,
            ability_changer: false,
            scale: 0.0,
            sight_range_scale: 0.0,
            sight_fov_scale: 0.0,
            milestone_id: 0,
            attack_start_range_scale: 0.0,
            aggression: false,
            exp_multiplier: 0.0,
            exp_adder: 0,
            stabler: false,
            accept_aggro_link: false,
            recruting_battlefield_id: 0,
            return_distance: 0.0,
            npc_ai_param_id: 0,
            non_pushable_by_actor: false,
            banker: false,
            aggro_link_special_rule_id: 0,
            aggro_link_help_dist: 0.0,
            aggro_link_sight_check: false,
            expedition: false,
            honor_point: 0,
            trader: false,
            aggro_link_special_guard: false,
            aggro_link_special_ignore_npc_attacker: false,
            absolute_return_distance: 0.0,
            repairman: false,
            activate_ai_always: false,
            specialty: false,
            specialty_coin_id: 0,
            use_range_mod: false,
            npc_posture_set_id: 0,
            npc_posture_sets: Vec::new(),
            mate_equip_slot_pack_id: 0,
            mate_kind_id: 0,
            engage_combat_give_quest_id: 0,
            no_apply_total_custom: false,
            base_skill_strafe: false,
            base_skill_delay: 0.0,
            npc_interaction_set_id: 0,
            use_abuser_list: false,
            return_when_enter_housing_area: false,
            look_converter: false,
            use_ddcms_mount_skill: false,
            crowd_effect: false,
            race: 0,
            gender: 0,
            merchant_pack_id: 0,
            equip_bodies_id: 0,
            equip_cloths_id: 0,
            equip_weapons_id: 0,
            hair_id: 0,
            // `new UnitCustomModelParams()` (type None).
            model_params: UnitCustomModelParams::default(),
            items: EquipItemsTemplate::default(),
            body_items: [(0, false); 7],
            buffs: Vec::new(),
            bonuses: Vec::new(),
            skills: Vec::new(),
            passive_buffs: Vec::new(),
            total_custom_id: 0,
        }
    }
}

impl NpcTemplate {
    /// `BindSkills(skills)`.
    pub fn bind_skills(&mut self, skills: &[NpcSkill]) {
        for s in skills {
            match self
                .skills
                .iter_mut()
                .find(|(c, _)| *c == s.skill_use_condition)
            {
                Some((_, l)) => l.push(s.clone()),
                None => self.skills.push((s.skill_use_condition, vec![s.clone()])),
            }
        }
    }
}

/// `TotalCharacterCustom`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct TotalCharacterCustom {
    pub id: u32,
    pub model_id: u32,
    pub name: String,
    pub npc_only: bool,
    pub hair_id: u32,
    pub hair_color_id: u32,
    pub skin_color_id: u32,
    pub face_movable_decal_asset_id: u32,
    pub face_movable_decal_scale: f32,
    pub face_movable_decal_rotate: f32,
    pub face_movable_decal_move_x: i16,
    pub face_movable_decal_move_y: i16,
    pub face_fixed_decal_asset_ids: [u32; 4],
    pub face_diffuse_map_id: u32,
    pub face_normal_map_id: u32,
    pub face_eyelash_map_id: u32,
    pub lip_color: u32,
    pub left_pupil_color: u32,
    pub right_pupil_color: u32,
    pub eyebrow_color: u32,
    pub modifier: Vec<u8>,
    pub owner_type_id: u32,
    pub face_movable_decal_weight: f32,
    pub face_fixed_decal_asset_weights: [f32; 4],
    pub face_normal_map_weight: f32,
    pub deco_color: u32,
}

impl TotalCharacterCustom {
    /// The `UnitCustomModelParams(Face)` block `Load` / `LoadCustom` build from a custom.
    pub fn model_params(&self) -> UnitCustomModelParams {
        let mut face = FaceModel {
            movable_decal_asset_id: self.face_movable_decal_asset_id,
            movable_decal_scale: self.face_movable_decal_scale,
            movable_decal_rotate: self.face_movable_decal_rotate,
            movable_decal_move_x: self.face_movable_decal_move_x,
            movable_decal_move_y: self.face_movable_decal_move_y,
            diffuse_map_id: self.face_diffuse_map_id,
            normal_map_id: self.face_normal_map_id,
            eyelash_map_id: self.face_eyelash_map_id,
            lip_color: self.lip_color,
            left_pupil_color: self.left_pupil_color,
            right_pupil_color: self.right_pupil_color,
            eyebrow_color: self.eyebrow_color,
            movable_decal_weight: self.face_movable_decal_weight,
            normal_map_weight: self.face_normal_map_weight,
            deco_color: self.deco_color,
            // `Face.Modifier = tc.Modifier` (the blob as read).
            modifier: self.modifier.clone(),
            ..FaceModel::default()
        };
        for i in 0..4 {
            face.fixed_decal_asset[i].asset_id = self.face_fixed_decal_asset_ids[i];
            face.fixed_decal_asset[i].asset_weight = self.face_fixed_decal_asset_weights[i];
        }
        UnitCustomModelParams {
            kind: UnitCustomModelType::FACE,
            hair_color_id: self.hair_color_id,
            skin_color_id: self.skin_color_id,
            model_id: self.model_id,
            face: Some(face),
        }
    }
}

/// `new UnitCustomModelParams(UnitCustomModelType.Skin)`.
pub fn skin_model_params() -> UnitCustomModelParams {
    UnitCustomModelParams {
        kind: UnitCustomModelType::SKIN,
        ..UnitCustomModelParams::default()
    }
}

/// `BodyPartTemplate` as kept in `ItemBodyParts`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BodyPartTemplate {
    pub item_id: u32,
    pub model_id: u32,
    pub npc_only: bool,
    pub slot_type_id: u32,
}

/// `EquipBodyPartPack`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct EquipBodyPartPack {
    pub id: u32,
    pub name: String,
    pub model_id: u32,
    pub hair_color_id: u32,
    pub face_id: u32,
    pub hair_id: u32,
    pub beard_id: u32,
    pub skin_color_id: u32,
    pub body_diffuse_map_id: u32,
}

/// `MerchantGoodsItem`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct MerchantGoodsItem {
    pub item_template_id: u32,
    pub grade: u8,
}

/// `MerchantGoods`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct MerchantGoods {
    pub id: u32,
    pub items: Vec<MerchantGoodsItem>,
}

impl MerchantGoods {
    /// `SellsItem(itemTemplateId)`.
    pub fn sells_item(&self, item_template_id: u32) -> bool {
        self.items
            .iter()
            .any(|i| i.item_template_id == item_template_id)
    }

    /// `AddItemToStock` (the first grade of an item wins).
    pub fn add_item_to_stock(&mut self, item_template_id: u32, grade: u8) {
        if self.sells_item(item_template_id) {
            return;
        }
        self.items.push(MerchantGoodsItem {
            item_template_id,
            grade,
        });
    }
}

/// `ModelType` (`models` row).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ModelType {
    pub id: u32,
    pub sub_id: u32,
    pub sub_type: String,
}

/// The `Weapon` getter inputs: `WeaponTemplate.Level`, `HoldableTemplate.FormulaDps` / `FormulaMDps`.
#[derive(Debug, Clone, PartialEq)]
pub struct WeaponDpsTemplate {
    pub level: i32,
    pub formula_dps: Formula,
    pub formula_mdps: Formula,
}

/// `NpcManager` tables.
#[derive(Debug, Default)]
pub struct NpcData {
    pub templates: HashMap<u32, NpcTemplate>,
    /// `Goods` (merchant pack id -> goods).
    pub goods: HashMap<u32, MerchantGoods>,
    /// `TotalCharacterCustoms`, in load (dictionary insertion) order.
    pub total_character_customs: Vec<TotalCharacterCustom>,
    tcc_index: HashMap<u32, usize>,
    /// `TccLookup` (model id -> custom ids).
    pub tcc_lookup: HashMap<u32, Vec<u32>>,
    /// `ItemBodyParts`: model id -> (slot type id -> body parts), insertion ordered.
    pub item_body_parts: HashMap<u32, Vec<(u32, Vec<BodyPartTemplate>)>>,
    /// `EquipPackBodyParts`.
    pub equip_pack_body_parts: HashMap<u32, EquipBodyPartPack>,
    /// `ModelManager._modelTypes`.
    pub model_types: HashMap<u32, ModelType>,
    /// `ModelManager._models["ActorModel"]` (id -> `MovementId`).
    pub actor_model_movement: HashMap<u32, i32>,
    /// Weapon templates (item id) for `Weapon.Dps` / `MDps`.
    pub weapons: HashMap<u32, WeaponDpsTemplate>,
    /// `GradeTemplate.HoldableDps` / `HoldableMagicDps` by grade.
    pub grade_holdable_dps: HashMap<i32, (f32, f32)>,
    /// `localized_texts` `npcs.name` in the configured language.
    pub localized_names: HashMap<u32, String>,
}

fn dup(what: &str, id: u32) -> crate::data::GameDataError {
    crate::data::GameDataError::File(format!(
        "{what}: duplicate key {id} (Dictionary.Add ArgumentException)"
    ))
}

fn read_bonus(r: &SqlRow<'_, '_>) -> rusqlite::Result<Option<BonusTemplate>> {
    let attr = r.u8("unit_attribute_id")?;
    let Some(attribute) = UnitAttribute::from_u8(attr) else {
        tracing::warn!("unit_modifiers: unknown UnitAttribute {attr}, skipped");
        return Ok(None);
    };
    Ok(Some(BonusTemplate {
        attribute,
        modifier_type: UnitModifierType::from_u8(r.u8("unit_modifier_type_id")?),
        value: r.i32("value")?,
        linear_level_bonus: r.i32("linear_level_bonus")?,
    }))
}

/// The `ItemBodyParts` scan shared by `Load` and `LoadCustom`: for the template's model, the
/// last body part of each slot fills `BodyItems[SlotTypeId - 23]` (hair: the template's hair
/// when it differs and is set).
pub fn apply_item_body_parts(
    item_body_parts: &HashMap<u32, Vec<(u32, Vec<BodyPartTemplate>)>>,
    model_id: u32,
    hair_id: u32,
    body_items: &mut BodyItems,
) {
    let Some(ibp) = item_body_parts.get(&model_id) else {
        return;
    };
    for (slot_type_id, bp) in ibp {
        let Some(rbp) = bp.last() else { continue };
        let idx = rbp.slot_type_id.wrapping_sub(23) as usize;
        match *slot_type_id {
            slot_type::FACE
            | slot_type::BEARD
            | slot_type::BODY
            | slot_type::GLASSES
            | slot_type::TAIL => {
                body_items[idx] = (rbp.item_id, rbp.npc_only);
            }
            slot_type::HAIR => {
                if rbp.item_id == hair_id {
                    body_items[idx] = (rbp.item_id, rbp.npc_only);
                } else if hair_id != 0 {
                    body_items[idx] = (hair_id, rbp.npc_only);
                }
            }
            _ => {}
        }
    }
}

impl NpcData {
    pub fn load(ctx: &DataLoadCtx<'_>) -> Result<Self, crate::data::GameDataError> {
        Self::load_conn(ctx.conn, ctx.language)
    }

    /// `NpcManager.Load` (+ the `NpcGameData` / `ModelManager` / weapon parts, see the module
    /// docs) from `conn`.
    pub fn load_conn(
        conn: &Connection,
        language: &str,
    ) -> Result<Self, crate::data::GameDataError> {
        let mut d = NpcData::default();
        tracing::info!("Loading npc templates...");

        // Pre-Load customs
        let mut dup_err = None;
        for_each_row(conn, "SELECT * FROM total_character_customs", [], |r| {
            let mut c = TotalCharacterCustom {
                id: r.u32("id")?,
                model_id: r.u32("model_id")?,
                name: r.string("name")?,
                npc_only: r.bool_str("npcOnly")?,
                hair_id: r.u32("hair_id")?,
                hair_color_id: r.u32("hair_color_id")?,
                skin_color_id: r.u32("skin_color_id")?,
                face_movable_decal_asset_id: r.u32("face_movable_decal_asset_id")?,
                face_movable_decal_scale: r.f32("face_movable_decal_scale")?,
                face_movable_decal_rotate: r.f32("face_movable_decal_rotate")?,
                face_movable_decal_move_x: r.i16("face_movable_decal_move_x")?,
                face_movable_decal_move_y: r.i16("face_movable_decal_move_y")?,
                face_fixed_decal_asset_ids: [
                    r.u32("face_fixed_decal_asset_0_id")?,
                    r.u32("face_fixed_decal_asset_1_id")?,
                    r.u32("face_fixed_decal_asset_2_id")?,
                    r.u32("face_fixed_decal_asset_3_id")?,
                ],
                face_diffuse_map_id: r.u32("face_diffuse_map_id")?,
                face_normal_map_id: r.u32("face_normal_map_id")?,
                face_eyelash_map_id: r.u32("face_eyelash_map_id")?,
                lip_color: r.u32("lip_color")?,
                left_pupil_color: r.u32("left_pupil_color")?,
                right_pupil_color: r.u32("right_pupil_color")?,
                eyebrow_color: r.u32("eyebrow_color")?,
                ..TotalCharacterCustom::default()
            };
            // `reader.GetValue("modifier")` cast to byte[]: filled below.
            c.owner_type_id = r.u32("owner_type_id")?;
            c.face_movable_decal_weight = r.f32("face_movable_decal_weight")?;
            c.face_fixed_decal_asset_weights = [
                r.f32("face_fixed_decal_asset_0_weight")?,
                r.f32("face_fixed_decal_asset_1_weight")?,
                r.f32("face_fixed_decal_asset_2_weight")?,
                r.f32("face_fixed_decal_asset_3_weight")?,
            ];
            c.face_normal_map_weight = r.f32("face_normal_map_weight")?;
            c.deco_color = r.u32("deco_color")?;
            if d.tcc_index.contains_key(&c.id) {
                dup_err.get_or_insert(c.id);
                return Ok(());
            }
            d.tcc_index.insert(c.id, d.total_character_customs.len());
            d.total_character_customs.push(c);
            Ok(())
        })?;
        if let Some(id) = dup_err {
            return Err(dup("total_character_customs", id));
        }
        // `(byte[])reader.GetValue("modifier")` (always a 128 byte blob in 1.2; a NULL leaves the
        // default empty array, any other storage class would be an InvalidCastException).
        {
            let mut stmt = conn.prepare("SELECT id, modifier FROM total_character_customs")?;
            let mut rows = stmt.query([])?;
            while let Some(row) = rows.next()? {
                let id = row.get::<_, i64>(0)? as i32 as u32;
                if let rusqlite::types::ValueRef::Blob(b) = row.get_ref(1)? {
                    if let Some(&i) = d.tcc_index.get(&id) {
                        if d.total_character_customs[i].modifier.is_empty() {
                            d.total_character_customs[i].modifier = b.to_vec();
                        }
                    }
                }
            }
        }
        for c in &d.total_character_customs {
            d.tcc_lookup.entry(c.model_id).or_default().push(c.id);
        }

        // Pre-Load body parts
        for_each_row(conn, "SELECT * FROM item_body_parts ORDER BY id", [], |r| {
            let bp = BodyPartTemplate {
                item_id: r.u32_or("item_id", 0)?,
                model_id: r.u32_or("model_id", 0)?,
                npc_only: r.bool_str("npc_only")?,
                slot_type_id: r.u32("slot_type_id")?,
            };
            let slots = d.item_body_parts.entry(bp.model_id).or_default();
            match slots.iter_mut().find(|(s, _)| *s == bp.slot_type_id) {
                Some((_, l)) => l.push(bp),
                None => slots.push((bp.slot_type_id, vec![bp])),
            }
            Ok(())
        })?;

        // `characters` race / gender by model (first row wins).
        let mut char_models: HashMap<u32, (u8, u8)> = HashMap::new();
        for_each_row(conn, "SELECT * FROM characters", [], |r| {
            let model = r.u32("model_id")?;
            let race_gender = (r.u8("char_race_id")?, r.u8("char_gender_id")?);
            char_models.entry(model).or_insert(race_gender);
            Ok(())
        })?;

        let mut cloths: HashMap<u32, EquipItemsTemplate> = HashMap::new();
        for_each_row(conn, "SELECT * FROM equip_pack_cloths", [], |r| {
            // `while (reader2.Read())` over `WHERE id=@id`: the last row of an id wins.
            let it = cloths.entry(r.u32("id")?).or_default();
            it.headgear = r.u32("headgear_id")?;
            it.headgear_grade = r.u8("headgear_grade_id")?;
            it.necklace = r.u32("necklace_id")?;
            it.necklace_grade = r.u8("necklace_grade_id")?;
            it.shirt = r.u32("shirt_id")?;
            it.shirt_grade = r.u8("shirt_grade_id")?;
            it.belt = r.u32("belt_id")?;
            it.belt_grade = r.u8("belt_grade_id")?;
            it.pants = r.u32("pants_id")?;
            it.pants_grade = r.u8("pants_grade_id")?;
            it.gloves = r.u32("glove_id")?;
            it.gloves_grade = r.u8("glove_grade_id")?;
            it.shoes = r.u32("shoes_id")?;
            it.shoes_grade = r.u8("shoes_grade_id")?;
            it.bracelet = r.u32("bracelet_id")?;
            it.bracelet_grade = r.u8("bracelet_grade_id")?;
            it.back = r.u32("back_id")?;
            it.back_grade = r.u8("back_grade_id")?;
            it.cosplay = r.u32("cosplay_id")?;
            it.cosplay_grade = r.u8("cosplay_grade_id")?;
            it.undershirts = r.u32("undershirt_id")?;
            it.undershirts_grade = r.u8("undershirt_grade_id")?;
            it.underpants = r.u32("underpants_id")?;
            it.underpants_grade = r.u8("underpants_grade_id")?;
            Ok(())
        })?;
        let mut weapon_packs: HashMap<u32, [(u32, u8); 4]> = HashMap::new();
        for_each_row(conn, "SELECT * FROM equip_pack_weapons", [], |r| {
            weapon_packs.insert(
                r.u32("id")?,
                [
                    (r.u32("mainhand_id")?, r.u8("mainhand_grade_id")?),
                    (r.u32("offhand_id")?, r.u8("offhand_grade_id")?),
                    (r.u32("ranged_id")?, r.u8("ranged_grade_id")?),
                    (r.u32("musical_id")?, r.u8("musical_grade_id")?),
                ],
            );
            Ok(())
        })?;
        // `npc_postures` per set, the C# query (`ORDER BY start_tod_time DESC`), cached by set.
        let mut postures: HashMap<i32, Vec<NpcPosture>> = HashMap::new();

        // Load the actual Npc list
        let mut dup_npc = None;
        for_each_row(conn, "SELECT * from npcs", [], |r| {
            let mut t = NpcTemplate {
                id: r.u32("id")?,
                name: r.string("name")?,
                char_race_id: r.i32("char_race_id")?,
                npc_grade_id: r.u8("npc_grade_id")?,
                npc_kind_id: r.u8("npc_kind_id")?,
                level: r.u8("level")?,
                npc_template_id: r.u8("npc_template_id")?,
                model_id: r.u32("model_id")?,
                faction_id: r.u32("faction_id")?,
                skill_trainer: r.bool_str("skill_trainer")?,
                ai_file_id: r.i32("ai_file_id")?,
                merchant: r.bool_str("merchant")?,
                npc_nickname_id: r.i32("npc_nickname_id")?,
                auctioneer: r.bool_str("auctioneer")?,
                show_name_tag: r.bool_str("show_name_tag")?,
                visible_to_creator_only: r.bool_str("visible_to_creator_only")?,
                no_exp: r.bool_str("no_exp")?,
                pet_item_id: r.u32_or("pet_item_id", 0)?,
                base_skill_id: r.i32("base_skill_id")?,
                track_friendship: r.bool_str("track_friendship")?,
                priest: r.bool_str("priest")?,
                npc_tedency_id: r.i32_or("npc_tendency_id", 0)?,
                blacksmith: r.bool_str("blacksmith")?,
                teleporter: r.bool_str("teleporter")?,
                opacity: r.f32("opacity")?,
                ability_changer: r.bool_str("ability_changer")?,
                scale: r.f32("scale")?,
                sight_range_scale: r.f32("sight_range_scale")?,
                sight_fov_scale: r.f32("sight_fov_scale")?,
                milestone_id: r.i32_or("milestone_id", 0)?,
                attack_start_range_scale: r.f32("attack_start_range_scale")?,
                aggression: r.bool_str("aggression")?,
                exp_multiplier: r.f32("exp_multiplier")?,
                exp_adder: r.i32("exp_adder")?,
                stabler: r.bool_str("stabler")?,
                accept_aggro_link: r.bool_str("accept_aggro_link")?,
                recruting_battlefield_id: r.i32("recruiting_battle_field_id")?,
                return_distance: r.f32("return_distance")?,
                npc_ai_param_id: r.i32("npc_ai_param_id")?,
                non_pushable_by_actor: r.bool_str("non_pushable_by_actor")?,
                banker: r.bool_str("banker")?,
                aggro_link_special_rule_id: r.i32("aggro_link_special_rule_id")?,
                aggro_link_help_dist: r.f32("aggro_link_help_dist")?,
                aggro_link_sight_check: r.bool_str("aggro_link_sight_check")?,
                expedition: r.bool_str("expedition")?,
                honor_point: r.i32("honor_point")?,
                trader: r.bool_str("trader")?,
                aggro_link_special_guard: r.bool_str("aggro_link_special_guard")?,
                aggro_link_special_ignore_npc_attacker: r
                    .bool_str("aggro_link_special_ignore_npc_attacker")?,
                absolute_return_distance: r.f32("absolute_return_distance")?,
                repairman: r.bool_str("repairman")?,
                activate_ai_always: r.bool_str("activate_ai_always")?,
                specialty: r.bool_str("specialty")?,
                specialty_coin_id: r.u32_or("specialty_coin_id", 0)?,
                use_range_mod: r.bool_str("use_range_mod")?,
                npc_posture_set_id: r.i32("npc_posture_set_id")?,
                mate_equip_slot_pack_id: r.i32_or("mate_equip_slot_pack_id", 0)?,
                mate_kind_id: r.i32_or("mate_kind_id", 0)?,
                engage_combat_give_quest_id: r.u32_or("engage_combat_give_quest_id", 0)?,
                no_apply_total_custom: r.bool_str("no_apply_total_custom")?,
                base_skill_strafe: r.bool_str("base_skill_strafe")?,
                base_skill_delay: r.f32("base_skill_delay")?,
                npc_interaction_set_id: r.i32_or("npc_interaction_set_id", 0)?,
                use_abuser_list: r.bool_str("use_abuser_list")?,
                return_when_enter_housing_area: r.bool_str("return_when_enter_housing_area")?,
                look_converter: r.bool_str("look_converter")?,
                use_ddcms_mount_skill: r.bool_str("use_ddcms_mount_skill")?,
                crowd_effect: r.bool_str("crowd_effect")?,
                equip_bodies_id: r.u32_or("equip_bodies_id", 0)?,
                equip_cloths_id: r.u32_or("equip_cloths_id", 0)?,
                equip_weapons_id: r.u32_or("equip_weapons_id", 0)?,
                total_custom_id: r.u32_or("total_custom_id", 0)?,
                ..NpcTemplate::default()
            };
            if let Some(&(race, gender)) = char_models.get(&t.model_id) {
                t.race = race;
                t.gender = gender;
            }
            if d.templates.contains_key(&t.id) {
                dup_npc.get_or_insert(t.id);
                return Ok(());
            }
            if t.equip_cloths_id > 0 {
                if let Some(c) = cloths.get(&t.equip_cloths_id) {
                    t.items = *c;
                }
            }
            if t.equip_weapons_id > 0 {
                if let Some(w) = weapon_packs.get(&t.equip_weapons_id) {
                    (t.items.mainhand, t.items.mainhand_grade) = w[0];
                    (t.items.offhand, t.items.offhand_grade) = w[1];
                    (t.items.ranged, t.items.ranged_grade) = w[2];
                    (t.items.musical, t.items.musical_grade) = w[3];
                }
            }
            let tc = (t.total_custom_id > 0)
                .then(|| d.tcc_index.get(&t.total_custom_id))
                .flatten()
                .map(|&i| &d.total_character_customs[i]);
            match tc {
                Some(tc) => {
                    t.hair_id = tc.hair_id;
                    t.model_params = tc.model_params();
                }
                None => t.model_params = skin_model_params(),
            }
            if t.npc_posture_set_id > 0 {
                if let std::collections::hash_map::Entry::Vacant(e) =
                    postures.entry(t.npc_posture_set_id)
                {
                    let mut list = Vec::new();
                    for_each_row(
                        conn,
                        "SELECT * FROM npc_postures WHERE npc_posture_set_id=?1 \
                         ORDER BY start_tod_time DESC",
                        [t.npc_posture_set_id],
                        |r| {
                            list.push(NpcPosture {
                                npc_posture_set_id: r.u32("npc_posture_set_id")?,
                                anim_action_id: r.u32("anim_action_id")?,
                                talk_anim: r.string("talk_anim")?,
                                start_tod_time: r.f32("start_tod_time")?,
                            });
                            Ok(())
                        },
                    )?;
                    e.insert(list);
                }
                t.npc_posture_sets = postures[&t.npc_posture_set_id].clone();
            }
            apply_item_body_parts(&d.item_body_parts, t.model_id, t.hair_id, &mut t.body_items);
            d.templates.insert(t.id, t);
            Ok(())
        })?;
        if let Some(id) = dup_npc {
            return Err(dup("npcs", id));
        }

        // This table seems to no longer be in some of the later versions
        let mut dup_pack = None;
        for_each_row(conn, "SELECT * FROM equip_pack_body_parts", [], |r| {
            let p = EquipBodyPartPack {
                id: r.u32("id")?,
                name: r.string("name")?,
                model_id: r.u32("model_id")?,
                hair_color_id: r.u32("hair_color_id")?,
                face_id: r.u32("face_id")?,
                hair_id: r.u32("hair_id")?,
                beard_id: r.u32("beard_id")?,
                skin_color_id: r.u32("skin_color_id")?,
                body_diffuse_map_id: r.u32("body_diffuse_map_id")?,
            };
            if d.equip_pack_body_parts.contains_key(&p.id) {
                dup_pack.get_or_insert(p.id);
                return Ok(());
            }
            d.equip_pack_body_parts.insert(p.id, p);
            Ok(())
        })?;
        if let Some(id) = dup_pack {
            return Err(dup("equip_pack_body_parts", id));
        }

        // Load Unit modifiers for NPCs
        for_each_row(
            conn,
            "SELECT * FROM unit_modifiers WHERE owner_type='Npc'",
            [],
            |r| {
                let npc_id = r.u32("owner_id")?;
                let Some(t) = d.templates.get_mut(&npc_id) else {
                    return Ok(());
                };
                if let Some(b) = read_bonus(r)? {
                    t.bonuses.push(b);
                }
                Ok(())
            },
        )?;

        // Load initial Npc buffs
        for_each_row(conn, "SELECT * FROM npc_initial_buffs", [], |r| {
            let id = r.u32("npc_id")?;
            let buff_id = r.u32("buff_id")?;
            if let Some(t) = d.templates.get_mut(&id) {
                t.buffs.push(buff_id);
            }
            Ok(())
        })?;

        // Load merchant list
        for_each_row(conn, "SELECT * FROM merchants", [], |r| {
            let id = r.u32("npc_id")?;
            if let Some(t) = d.templates.get_mut(&id) {
                t.merchant_pack_id = r.u32("merchant_pack_id")?;
            }
            Ok(())
        })?;
        tracing::info!("Loaded {} npc templates", d.templates.len());

        tracing::info!("Loading merchant packs...");
        for_each_row(conn, "SELECT * FROM merchant_goods", [], |r| {
            let id = r.u32("merchant_pack_id")?;
            let item_id = r.u32("item_id")?;
            let grade = r.u8("grade_id")?;
            d.goods
                .entry(id)
                .or_insert_with(|| MerchantGoods {
                    id,
                    items: Vec::new(),
                })
                .add_item_to_stock(item_id, grade);
            Ok(())
        })?;
        tracing::info!("Loaded {} merchant packs", d.goods.len());

        d.load_npc_game_data_skills(conn)?;
        d.load_models(conn)?;
        d.load_weapons(conn)?;
        d.load_localized_names(conn, language)?;
        Ok(d)
    }

    /// `NpcGameData.Load` (`np_skills`, `np_passive_buffs`) + the `PostLoad` binding.
    fn load_npc_game_data_skills(&mut self, conn: &Connection) -> rusqlite::Result<()> {
        // `SkillsForNpc` / `PassivesForNpc` (dictionaries in first-seen order).
        let mut skills: Vec<(u32, Vec<NpcSkill>)> = Vec::new();
        let mut skill_idx: HashMap<u32, usize> = HashMap::new();
        for_each_row(conn, "SELECT * FROM np_skills", [], |r| {
            let s = NpcSkill {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                owner_type: r.string("owner_type")?,
                skill_id: r.u32("skill_id")?,
                skill_use_condition: r.u32("skill_use_condition_id")?,
                skill_use_param1: r.f32("skill_use_param1")?,
                skill_use_param2: r.f32("skill_use_param2")?,
            };
            let i = *skill_idx.entry(s.owner_id).or_insert_with(|| {
                skills.push((s.owner_id, Vec::new()));
                skills.len() - 1
            });
            skills[i].1.push(s);
            Ok(())
        })?;
        let mut passives: Vec<(u32, Vec<NpcPassiveBuff>)> = Vec::new();
        let mut passive_idx: HashMap<u32, usize> = HashMap::new();
        for_each_row(conn, "SELECT * FROM np_passive_buffs", [], |r| {
            let p = NpcPassiveBuff {
                id: r.u32("id")?,
                owner_id: r.u32("owner_id")?,
                owner_type: r.string("owner_type")?,
                passive_buff_id: r.u32("passive_buff_id")?,
            };
            let i = *passive_idx.entry(p.owner_id).or_insert_with(|| {
                passives.push((p.owner_id, Vec::new()));
                passives.len() - 1
            });
            passives[i].1.push(p);
            Ok(())
        })?;
        // PostLoad: BindSkillsToTemplate / PassiveBuffs.AddRange.
        for (template_id, list) in &skills {
            if let Some(t) = self.templates.get_mut(template_id) {
                t.bind_skills(list);
            }
        }
        for (template_id, list) in passives {
            if let Some(t) = self.templates.get_mut(&template_id) {
                t.passive_buffs.extend(list);
            }
        }
        Ok(())
    }

    /// The `ModelManager.Load` part used here (`models`, `actor_models`; `TryAdd`: first wins).
    fn load_models(&mut self, conn: &Connection) -> rusqlite::Result<()> {
        for_each_row(conn, "SELECT * FROM actor_models", [], |r| {
            let id = r.u32("id")?;
            let movement = r.i32("movement_id")?;
            self.actor_model_movement.entry(id).or_insert(movement);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM models", [], |r| {
            let m = ModelType {
                id: r.u32("id")?,
                sub_id: r.u32("sub_id")?,
                sub_type: r.string("sub_type")?,
            };
            self.model_types.entry(m.id).or_insert(m);
            Ok(())
        })?;
        Ok(())
    }

    /// The `Weapon.Dps` / `MDps` inputs (`ItemManager.Load`: `item_grades`, `holdables`,
    /// `item_weapons`, `items.level`).
    fn load_weapons(&mut self, conn: &Connection) -> rusqlite::Result<()> {
        for_each_row(conn, "SELECT * FROM item_grades", [], |r| {
            self.grade_holdable_dps.insert(
                r.i32("id")?,
                (r.f32("var_holdable_dps")?, r.f32("var_holdable_magic_dps")?),
            );
            Ok(())
        })?;
        let mut holdables: HashMap<u32, (Formula, Formula)> = HashMap::new();
        for_each_row(conn, "SELECT * FROM holdables", [], |r| {
            holdables.insert(
                r.u32("id")?,
                (
                    Formula::new(r.string("formula_dps")?),
                    Formula::new(r.string("formula_mdps")?),
                ),
            );
            Ok(())
        })?;
        let mut weapon_holdables: Vec<(u32, u32)> = Vec::new();
        for_each_row(conn, "SELECT * FROM item_weapons", [], |r| {
            weapon_holdables.push((r.u32("item_id")?, r.u32("holdable_id")?));
            Ok(())
        })?;
        let mut levels: HashMap<u32, i32> = HashMap::new();
        for_each_row(conn, "SELECT id, level FROM items", [], |r| {
            levels.insert(r.u32("id")?, r.i32("level")?);
            Ok(())
        })?;
        for (item_id, holdable_id) in weapon_holdables {
            let Some((dps, mdps)) = holdables.get(&holdable_id) else {
                continue;
            };
            self.weapons.insert(
                item_id,
                WeaponDpsTemplate {
                    level: levels.get(&item_id).copied().unwrap_or(0),
                    formula_dps: dps.clone(),
                    formula_mdps: mdps.clone(),
                },
            );
        }
        Ok(())
    }

    /// `localized_texts` rows for `npcs.name` (`LocalizationManager`).
    fn load_localized_names(&mut self, conn: &Connection, language: &str) -> rusqlite::Result<()> {
        let sql = format!(
            "SELECT idx, \"{}\" AS txt FROM localized_texts \
             WHERE tbl_name = 'npcs' AND tbl_column_name = 'name'",
            language.replace('"', "")
        );
        // A missing language column / table only loses the translations.
        let res = for_each_row(conn, &sql, [], |r| {
            let text = r.string_or("txt", "")?;
            self.localized_names.insert(r.u32("idx")?, text);
            Ok(())
        });
        if let Err(e) = res {
            tracing::warn!("npc localized names not loaded: {e}");
        }
        Ok(())
    }

    /// `NpcManager.Exist`.
    pub fn exist(&self, template_id: u32) -> bool {
        self.templates.contains_key(&template_id)
    }

    /// `NpcManager.GetTemplate`.
    pub fn template(&self, template_id: u32) -> Option<&NpcTemplate> {
        self.templates.get(&template_id)
    }

    /// `NpcManager.GetGoods`.
    pub fn goods(&self, id: u32) -> Option<&MerchantGoods> {
        self.goods.get(&id)
    }

    /// `TotalCharacterCustoms[id]`.
    pub fn total_character_custom(&self, id: u32) -> Option<&TotalCharacterCustom> {
        self.tcc_index
            .get(&id)
            .map(|&i| &self.total_character_customs[i])
    }

    /// Adds a custom (tests / synthetic data), keeping load order.
    pub fn add_total_character_custom(&mut self, c: TotalCharacterCustom) {
        self.tcc_lookup.entry(c.model_id).or_default().push(c.id);
        self.tcc_index
            .insert(c.id, self.total_character_customs.len());
        self.total_character_customs.push(c);
    }

    /// `ModelManager.GetModelType`.
    pub fn model_type(&self, model_id: u32) -> Option<&ModelType> {
        self.model_types.get(&model_id)
    }

    /// `ModelManager.IsFlyOrSwim`: an `ActorModel` with `MovementId == 2`.
    pub fn is_fly_or_swim(&self, model_id: u32) -> bool {
        let Some(t) = self.model_types.get(&model_id) else {
            return false;
        };
        // Only the ActorModel dictionary can hold an `ActorModel`.
        t.sub_type == "ActorModel" && self.actor_model_movement.get(&t.sub_id) == Some(&2)
    }

    /// `LocalizationManager.Get("npcs", "name", id, fallback)` (empty texts fall back).
    pub fn localized_name(&self, id: u32, fallback: &str) -> String {
        match self.localized_names.get(&id) {
            Some(t) if !t.is_empty() => t.clone(),
            _ => fallback.to_string(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A minimal compact.sqlite3 with every table / column the loaders read.
    pub(crate) const TEST_SCHEMA: &str = "
        CREATE TABLE total_character_customs (id INT, model_id INT, name TEXT, npcOnly NUM,
            hair_id INT, hair_color_id INT, skin_color_id INT, face_movable_decal_asset_id INT,
            face_movable_decal_scale REAL, face_movable_decal_rotate REAL,
            face_movable_decal_move_x INT, face_movable_decal_move_y INT,
            face_fixed_decal_asset_0_id INT, face_fixed_decal_asset_1_id INT,
            face_fixed_decal_asset_2_id INT, face_fixed_decal_asset_3_id INT,
            face_diffuse_map_id INT, face_normal_map_id INT, face_eyelash_map_id INT,
            lip_color INT, left_pupil_color INT, right_pupil_color INT, eyebrow_color INT,
            modifier, owner_type_id INT, face_movable_decal_weight REAL,
            face_fixed_decal_asset_0_weight REAL, face_fixed_decal_asset_1_weight REAL,
            face_fixed_decal_asset_2_weight REAL, face_fixed_decal_asset_3_weight REAL,
            face_normal_map_weight REAL, deco_color INT);
        INSERT INTO total_character_customs VALUES (5, 10, 'c', 'f', 24001, 7, 8, 0, 1.0, 0.0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, zeroblob(128), 0, 0.5, 0, 0, 0, 0, 0.25, 9);
        CREATE TABLE item_body_parts (id INT, item_id INT, slot_type_id INT, model_id INT,
            npc_only NUM);
        INSERT INTO item_body_parts VALUES (1, 23001, 23, 1, 'f'), (2, 24002, 24, 1, 't');
        CREATE TABLE characters (id INT, char_race_id INT, char_gender_id INT, model_id INT);
        INSERT INTO characters VALUES (1, 1, 1, 1), (2, 1, 2, 1);
        CREATE TABLE equip_pack_cloths (id INT, headgear_id INT, necklace_id INT, shirt_id INT,
            belt_id INT, pants_id INT, glove_id INT, shoes_id INT, bracelet_id INT,
            headgear_grade_id INT, necklace_grade_id INT, shirt_grade_id INT, belt_grade_id INT,
            pants_grade_id INT, glove_grade_id INT, shoes_grade_id INT, bracelet_grade_id INT,
            back_id INT, back_grade_id INT, cosplay_id INT, cosplay_grade_id INT,
            undershirt_id INT, undershirt_grade_id INT, underpants_id INT,
            underpants_grade_id INT);
        INSERT INTO equip_pack_cloths VALUES (3, 501, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0);
        CREATE TABLE equip_pack_weapons (id INT, mainhand_id INT, offhand_id INT, ranged_id INT,
            musical_id INT, mainhand_grade_id INT, offhand_grade_id INT, ranged_grade_id INT,
            musical_grade_id INT);
        INSERT INTO equip_pack_weapons VALUES (4, 601, 0, 0, 0, 2, 0, 0, 0);
        CREATE TABLE npc_postures (id INT, npc_posture_set_id INT, anim_action_id INT,
            talk_anim TEXT, start_tod_time INT);
        INSERT INTO npc_postures VALUES (1, 6, 1, '', 0), (2, 6, 3, '', 18), (3, 6, 2, '', 6);
        CREATE TABLE npcs (id INT, name TEXT, char_race_id INT, npc_grade_id INT,
            npc_kind_id INT, level INT, npc_template_id INT, equip_bodies_id INT,
            equip_cloths_id INT, equip_weapons_id INT, model_id INT, faction_id INT,
            skill_trainer NUM, ai_file_id INT, merchant NUM, npc_nickname_id INT,
            auctioneer NUM, show_name_tag NUM, visible_to_creator_only NUM, no_exp NUM,
            pet_item_id INT, base_skill_id INT, track_friendship NUM, priest NUM,
            npc_tendency_id INT, blacksmith NUM, teleporter NUM, opacity REAL,
            ability_changer NUM, scale REAL, sight_range_scale REAL, sight_fov_scale REAL,
            milestone_id INT, attack_start_range_scale REAL, aggression NUM,
            exp_multiplier REAL, exp_adder INT, stabler NUM, accept_aggro_link NUM,
            recruiting_battle_field_id INT, return_distance REAL, npc_ai_param_id INT,
            non_pushable_by_actor NUM, banker NUM, aggro_link_special_rule_id INT,
            aggro_link_help_dist REAL, aggro_link_sight_check NUM, expedition NUM,
            honor_point INT, trader NUM, aggro_link_special_guard NUM,
            aggro_link_special_ignore_npc_attacker NUM, absolute_return_distance REAL,
            repairman NUM, activate_ai_always NUM, specialty NUM, specialty_coin_id INT,
            use_range_mod NUM, npc_posture_set_id INT, mate_equip_slot_pack_id INT,
            mate_kind_id INT, engage_combat_give_quest_id INT, total_custom_id INT,
            no_apply_total_custom NUM, base_skill_strafe NUM, base_skill_delay REAL,
            npc_interaction_set_id INT, use_abuser_list NUM,
            return_when_enter_housing_area NUM, look_converter NUM,
            use_ddcms_mount_skill NUM, crowd_effect NUM);
        INSERT INTO npcs VALUES (100, 'Guard', 1, 1, 1, 50, 9, NULL, 3, 4, 1, 101, 't', 0, 'f',
            0, 'f', 't', 'f', 'f', NULL, 0, 'f', 'f', NULL, 'f', 'f', 1.0, 'f', 1.0, 1.0, 1.0,
            NULL, 1.0, 'f', 1.0, 0, 'f', 'f', 0, 0.0, 0, 'f', 'f', 0, 0.0, 'f', 'f', 0, 'f',
            'f', 'f', 0.0, 'f', 'f', 'f', NULL, 'f', 6, NULL, NULL, NULL, 5, 'f', 'f', 0.0,
            NULL, 'f', 'f', 'f', 'f', 'f');
        INSERT INTO npcs VALUES (101, 'Wolf', 0, 2, 2, 10, 9, NULL, NULL, NULL, 2, 3, 'f', 1,
            'f', 0, 'f', 't', 'f', 'f', NULL, 0, 'f', 'f', NULL, 'f', 'f', 1.0, 'f', 1.5, 1.0,
            1.0, NULL, 1.0, 't', 1.0, 0, 'f', 'f', 0, 0.0, 0, 'f', 'f', 0, 0.0, 'f', 'f', 0,
            'f', 'f', 'f', 0.0, 'f', 'f', 'f', NULL, 'f', 0, NULL, NULL, NULL, NULL, 'f', 'f',
            0.0, NULL, 'f', 'f', 'f', 'f', 'f');
        CREATE TABLE equip_pack_body_parts (id INT, name TEXT, model_id INT, hair_color_id INT,
            face_id INT, hair_id INT, beard_id INT, skin_color_id INT, body_diffuse_map_id INT);
        INSERT INTO equip_pack_body_parts VALUES (1, 'p', 1, 0, 0, 0, 0, 0, 0);
        CREATE TABLE unit_modifiers (id INT, owner_id INT, owner_type TEXT,
            unit_attribute_id INT, unit_modifier_type_id INT, value INT, linear_level_bonus INT);
        INSERT INTO unit_modifiers VALUES (1, 100, 'Npc', 6, 1, 10, 0),
            (2, 100, 'Item', 6, 0, 10, 0), (3, 999, 'Npc', 6, 0, 10, 0);
        CREATE TABLE npc_initial_buffs (id INT, npc_id INT, buff_id INT);
        INSERT INTO npc_initial_buffs VALUES (1, 100, 900), (2, 999, 901);
        CREATE TABLE merchants (id INT, npc_id INT, merchant_pack_id INT);
        INSERT INTO merchants VALUES (1, 100, 77);
        CREATE TABLE merchant_goods (id INT, merchant_pack_id INT, item_id INT, grade_id INT);
        INSERT INTO merchant_goods VALUES (1, 77, 10, 0), (2, 77, 11, 0), (3, 77, 10, 2);
        CREATE TABLE np_skills (id INT, owner_id INT, owner_type TEXT, skill_id INT,
            skill_use_condition_id INT, skill_use_param1 REAL, skill_use_param2 REAL);
        INSERT INTO np_skills VALUES (1, 100, 'Npc', 11, 1, 0, 0), (2, 100, 'Npc', 12, 2, 0, 0),
            (3, 100, 'Npc', 13, 1, 0, 0);
        CREATE TABLE np_passive_buffs (id INT, owner_id INT, owner_type TEXT,
            passive_buff_id INT);
        INSERT INTO np_passive_buffs VALUES (1, 100, 'Npc', 55);
        CREATE TABLE models (id INT, sub_id INT, sub_type TEXT);
        INSERT INTO models VALUES (1, 1, 'ActorModel'), (2, 2, 'ActorModel'),
            (3, 2, 'PrefabModel');
        CREATE TABLE actor_models (id INT, movement_id INT);
        INSERT INTO actor_models VALUES (1, 0), (2, 2);
        CREATE TABLE item_grades (id INT, var_holdable_dps REAL, var_holdable_magic_dps REAL);
        INSERT INTO item_grades VALUES (0, 1.0, 1.0), (2, 1.05, 1.05);
        CREATE TABLE holdables (id INT, formula_dps TEXT, formula_mdps TEXT);
        INSERT INTO holdables VALUES (1, 'item_level * item_grade', '0');
        CREATE TABLE item_weapons (id INT, item_id INT, holdable_id INT);
        INSERT INTO item_weapons VALUES (1, 601, 1);
        CREATE TABLE items (id INT, level INT);
        INSERT INTO items VALUES (601, 20);
        CREATE TABLE localized_texts (id INT, tbl_name TEXT, tbl_column_name TEXT, idx INT,
            en_us TEXT);
        INSERT INTO localized_texts VALUES (1, 'npcs', 'name', 100, 'Guard'),
            (2, 'npcs', 'name', 101, '');
    ";

    #[test]
    fn merchant_goods_first_grade_wins() {
        let mut g = MerchantGoods::default();
        g.add_item_to_stock(5, 1);
        g.add_item_to_stock(5, 3);
        g.add_item_to_stock(6, 0);
        assert_eq!(g.items.len(), 2);
        assert_eq!(g.items[0].grade, 1);
        assert!(g.sells_item(6) && !g.sells_item(7));
    }

    #[test]
    fn body_parts_take_last_entry_and_template_hair() {
        let mut ibp = HashMap::new();
        let bp = |item, slot, npc_only| BodyPartTemplate {
            item_id: item,
            model_id: 9,
            npc_only,
            slot_type_id: slot,
        };
        ibp.insert(
            9,
            vec![
                (slot_type::FACE, vec![bp(1, 23, false), bp(2, 23, true)]),
                (slot_type::HAIR, vec![bp(10, 24, false)]),
                (26, vec![bp(30, 26, false)]), // Reserved: ignored
            ],
        );
        let mut b = [(0, false); 7];
        apply_item_body_parts(&ibp, 9, 77, &mut b);
        assert_eq!(b[0], (2, true));
        assert_eq!(b[1], (77, false)); // template hair replaces the body part
        assert_eq!(b[3], (0, false));
        let mut b = [(0, false); 7];
        apply_item_body_parts(&ibp, 9, 0, &mut b);
        assert_eq!(b[1], (0, false)); // no hair: untouched
        apply_item_body_parts(&ibp, 9, 10, &mut b);
        assert_eq!(b[1], (10, false));
    }

    #[test]
    fn loads_synthetic_tables() {
        let c = Connection::open_in_memory().unwrap();
        c.execute_batch(TEST_SCHEMA).unwrap();
        let d = NpcData::load_conn(&c, "en_us").unwrap();
        assert_eq!(d.templates.len(), 2);
        let t = d.template(100).unwrap();
        assert_eq!((t.race, t.gender), (1, 1));
        assert_eq!(t.items.headgear, 501);
        assert_eq!(t.items.mainhand, 601);
        assert_eq!(t.items.mainhand_grade, 2);
        assert_eq!(t.hair_id, 24001);
        assert_eq!(t.model_params.kind, UnitCustomModelType::FACE);
        assert_eq!(t.model_params.hair_color_id, 7);
        assert_eq!(
            t.npc_posture_sets
                .iter()
                .map(|p| p.anim_action_id)
                .collect::<Vec<_>>(),
            vec![3, 2, 1]
        );
        assert_eq!(t.buffs, vec![900]);
        assert_eq!(t.bonuses.len(), 1);
        assert_eq!(t.merchant_pack_id, 77);
        assert_eq!(t.skills.len(), 2);
        assert_eq!(t.skills[0].1.len(), 2);
        assert_eq!(t.passive_buffs.len(), 1);
        assert_eq!(t.body_items[0], (23001, false));
        assert_eq!(t.body_items[1], (24001, true));
        assert!(t.skill_trainer && !t.banker);
        let t2 = d.template(101).unwrap();
        assert_eq!(t2.model_params.kind, UnitCustomModelType::SKIN);
        assert_eq!((t2.race, t2.gender), (0, 0));
        assert_eq!(d.goods(77).unwrap().items.len(), 2);
        assert!(d.is_fly_or_swim(2) && !d.is_fly_or_swim(10) && !d.is_fly_or_swim(3));
        assert_eq!(d.weapons[&601].level, 20);
        assert_eq!(d.localized_name(100, "x"), "Guard");
        assert_eq!(d.localized_name(101, "x"), "x");
        assert_eq!(d.total_character_customs.len(), 1);
        assert_eq!(d.total_character_custom(5).unwrap().modifier.len(), 128);
        assert_eq!(d.equip_pack_body_parts.len(), 1);
    }

    /// Real `compact.sqlite3` (`AAEMU_COMPACT_DB`).
    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_compact_db_npc_data() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn =
            Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let d = NpcData::load_conn(&conn, "en_us").unwrap();
        let count = |sql: &str| -> usize {
            conn.query_row(sql, [], |r| r.get::<_, i64>(0)).unwrap() as usize
        };
        assert_eq!(d.templates.len(), count("SELECT COUNT(*) FROM npcs"));
        assert_eq!(
            d.total_character_customs.len(),
            count("SELECT COUNT(*) FROM total_character_customs")
        );
        assert_eq!(
            d.goods.len(),
            count("SELECT COUNT(DISTINCT merchant_pack_id) FROM merchant_goods")
        );
        assert_eq!(
            d.equip_pack_body_parts.len(),
            count("SELECT COUNT(*) FROM equip_pack_body_parts")
        );
        let sum = |f: &dyn Fn(&NpcTemplate) -> usize| d.templates.values().map(f).sum::<usize>();
        let buffs = sum(&|t| t.buffs.len());
        let bonuses = sum(&|t| t.bonuses.len());
        let skills = sum(&|t| t.skills.iter().map(|s| s.1.len()).sum());
        let passives = sum(&|t| t.passive_buffs.len());
        let with_postures = d
            .templates
            .values()
            .filter(|t| !t.npc_posture_sets.is_empty())
            .count();
        let merchants = d
            .templates
            .values()
            .filter(|t| t.merchant_pack_id > 0)
            .count();
        assert_eq!(
            buffs,
            count("SELECT COUNT(*) FROM npc_initial_buffs WHERE npc_id IN (SELECT id FROM npcs)")
        );
        assert_eq!(
            bonuses,
            count(
                "SELECT COUNT(*) FROM unit_modifiers WHERE owner_type='Npc' \
                 AND owner_id IN (SELECT id FROM npcs)"
            )
        );
        assert_eq!(
            skills,
            count("SELECT COUNT(*) FROM np_skills WHERE owner_id IN (SELECT id FROM npcs)")
        );
        assert_eq!(
            passives,
            count("SELECT COUNT(*) FROM np_passive_buffs WHERE owner_id IN (SELECT id FROM npcs)")
        );
        assert_eq!(
            with_postures,
            count(
                "SELECT COUNT(*) FROM npcs WHERE npc_posture_set_id IN \
                 (SELECT npc_posture_set_id FROM npc_postures)"
            )
        );
        eprintln!(
            "npc templates {}, customs {}, merchant packs {}, buffs {buffs}, bonuses {bonuses}, \
             skills {skills}, passives {passives}, with postures {with_postures}, merchants \
             {merchants}, models {}, weapons {}, names {}",
            d.templates.len(),
            d.total_character_customs.len(),
            d.goods.len(),
            d.model_types.len(),
            d.weapons.len(),
            d.localized_names.len()
        );
        // Every posture list is sorted by start time, descending.
        for t in d.templates.values() {
            assert!(t
                .npc_posture_sets
                .windows(2)
                .all(|w| w[0].start_tod_time >= w[1].start_tod_time));
        }
    }
}
