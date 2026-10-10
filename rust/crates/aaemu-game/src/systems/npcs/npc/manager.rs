//! `NpcManager` runtime (C#: `Core/Managers/UnitManagers/NpcManager.cs`): `Create`,
//! `LoadCustom` (random looks for humanoid NPCs without a `total_custom_id`, seeded
//! `new Random(123456789)` — [`DotNetRandom`] reproduces .NET's seeded generator so the picks
//! match), `SetEquipItemTemplate`, and `Npc.InitializeSpawnBuffs` / `AnimActionId`.
//!
//! C# behaviour mirrored:
//! * `LoadCustom` writes its result back into the shared template (`template.HairId`,
//!   `ModelParams`, `BodyItems`), so every later `Create` of that template starts from the
//!   previous random look (its `HairId` feeds the next body-part scan). The template data is
//!   immutable here; the written-back look lives in [`NpcManager`] (`looks`).
//! * equipment items are created without an id (`ItemManager.Create(.., generateId: false)`)
//!   and with grade 0 (the pack grades are loaded but never passed).
//! * the hair slot always uses `template.HairId` (`ModelParams` is never null).
//!
//! Deviations: an equipment template id that is not an item template is skipped with a
//! warning (C#: `NullReferenceException` out of `Create`). `Ai` is not created (AI phase): an
//! NPC with `AiFileId > 0` gets `has_ai = true`.

use std::collections::{HashMap, HashSet};
use std::sync::Mutex;

use crate::data::items::TemplateKind;
use crate::data::GameData;
use crate::models::buff::{Buff, SkillCaster};
use crate::models::container::{equip_slot, slot_type as container_slot_type};
use crate::models::item::Item;
use crate::models::npc::{Npc, UNIT_EQUIPMENT_SLOTS};
use crate::models::unit::Bonus;
use crate::models::unit_model::UnitCustomModelParams;
use crate::server::GameServer;
use crate::world::{Entity, NpcRef, WorldObject};

use super::data::{
    apply_item_body_parts, skin_model_params, slot_type, BodyItems, NpcData, NpcTemplate,
};

/// `FactionsEnum.Monstrosity` / `Animal`.
const FACTION_MONSTROSITY: u32 = 115;
const FACTION_ANIMAL: u32 = 116;
/// `Gender.Male`.
const GENDER_MALE: u8 = 1;
/// `SkillCasterType.Unit`.
const SKILL_CASTER_UNIT: u8 = 0;

/// `System.Random(int seed)` as implemented by .NET for seeded instances
/// (`Net5CompatSeedImpl` / `CompatPrng`, Knuth's subtractive generator).
#[derive(Debug, Clone)]
pub struct DotNetRandom {
    seed_array: [i32; 56],
    inext: usize,
    inextp: usize,
}

impl DotNetRandom {
    /// `new Random(seed)`.
    pub fn new(seed: i32) -> Self {
        let mut seed_array = [0i32; 56];
        let subtraction = if seed == i32::MIN {
            i32::MAX
        } else {
            seed.abs()
        };
        let mut mj = 161_803_398i32.wrapping_sub(subtraction);
        seed_array[55] = mj;
        let mut mk = 1i32;
        let mut ii = 0usize;
        for _ in 1..55 {
            ii += 21;
            if ii >= 55 {
                ii -= 55;
            }
            seed_array[ii] = mk;
            mk = mj.wrapping_sub(mk);
            if mk < 0 {
                mk = mk.wrapping_add(i32::MAX);
            }
            mj = seed_array[ii];
        }
        for _ in 1..5 {
            for i in 1..56 {
                let mut n = i + 30;
                if n >= 55 {
                    n -= 55;
                }
                seed_array[i] = seed_array[i].wrapping_sub(seed_array[1 + n]);
                if seed_array[i] < 0 {
                    seed_array[i] = seed_array[i].wrapping_add(i32::MAX);
                }
            }
        }
        Self {
            seed_array,
            inext: 0,
            inextp: 21,
        }
    }

    fn internal_sample(&mut self) -> i32 {
        let mut loc_inext = self.inext + 1;
        if loc_inext >= 56 {
            loc_inext = 1;
        }
        let mut loc_inextp = self.inextp + 1;
        if loc_inextp >= 56 {
            loc_inextp = 1;
        }
        let mut ret = self.seed_array[loc_inext].wrapping_sub(self.seed_array[loc_inextp]);
        if ret == i32::MAX {
            ret -= 1;
        }
        if ret < 0 {
            ret = ret.wrapping_add(i32::MAX);
        }
        self.seed_array[loc_inext] = ret;
        self.inext = loc_inext;
        self.inextp = loc_inextp;
        ret
    }

    /// `Sample()`.
    fn sample(&mut self) -> f64 {
        f64::from(self.internal_sample()) * (1.0 / f64::from(i32::MAX))
    }

    /// `Next()`.
    pub fn next_int(&mut self) -> i32 {
        self.internal_sample()
    }

    /// `Next(maxValue)` (`maxValue >= 0`).
    pub fn next_max(&mut self, max_value: i32) -> i32 {
        (self.sample() * f64::from(max_value)) as i32
    }
}

/// The look fields `LoadCustom` writes back into `NpcTemplate`.
#[derive(Debug, Clone, PartialEq)]
pub struct TemplateLook {
    pub hair_id: u32,
    pub model_params: UnitCustomModelParams,
    pub body_items: BodyItems,
}

impl TemplateLook {
    fn of(t: &NpcTemplate) -> Self {
        Self {
            hair_id: t.hair_id,
            model_params: t.model_params.clone(),
            body_items: t.body_items,
        }
    }
}

#[derive(Debug)]
struct State {
    /// `_loadCustomRandom = new(123456789)`.
    random: DotNetRandom,
    /// Template looks written back by `LoadCustom` (template id -> look).
    looks: HashMap<u32, TemplateLook>,
}

/// `NpcManager` runtime state.
#[derive(Debug)]
pub struct NpcManager {
    state: Mutex<State>,
}

impl Default for NpcManager {
    fn default() -> Self {
        Self {
            state: Mutex::new(State {
                random: DotNetRandom::new(123_456_789),
                looks: HashMap::new(),
            }),
        }
    }
}

impl NpcManager {
    /// The template's current look (`template.HairId` / `ModelParams` / `BodyItems`).
    pub fn template_look(&self, template: &NpcTemplate) -> TemplateLook {
        let st = self.state.lock().expect("npc manager poisoned");
        st.looks
            .get(&template.id)
            .cloned()
            .unwrap_or_else(|| TemplateLook::of(template))
    }

    /// The look part of `Create`: `if (template.TotalCustomId == 0)` runs `LoadCustom` and
    /// writes the result back into the template. Returns the look the new NPC uses.
    pub fn resolve_look(&self, data: &GameData, template: &NpcTemplate) -> TemplateLook {
        let mut st = self.state.lock().expect("npc manager poisoned");
        let current = st
            .looks
            .get(&template.id)
            .cloned()
            .unwrap_or_else(|| TemplateLook::of(template));
        if template.total_custom_id != 0 {
            return current;
        }
        let new_look = load_custom(data, template, &current, &mut st.random);
        st.looks.insert(template.id, new_look.clone());
        new_look
    }
}

/// `NpcManager.LoadCustom(template)`: a random look for humanoid NPCs. `current` is the
/// template's current look; returns the look to write back (`current` when the C# returns the
/// template itself).
pub fn load_custom(
    data: &GameData,
    template: &NpcTemplate,
    current: &TemplateLook,
    random: &mut DotNetRandom,
) -> TemplateLook {
    let npcs: &NpcData = &data.systems.npcs.npc;
    let mut total_custom_id = template.total_custom_id;
    if total_custom_id != 0
        || template.faction_id == FACTION_MONSTROSITY
        || template.faction_id == FACTION_ANIMAL
    {
        return current.clone();
    }
    let male = template.gender == GENDER_MALE;
    // `switch ((Race)template.CharRaceId)` (byte enum: the int is truncated).
    let model_params_id: u32 = match template.char_race_id as u8 {
        0 | 1 => {
            if male {
                10
            } else {
                11
            }
        }
        4 => {
            if male {
                16
            } else {
                17
            }
        }
        5 => {
            if male {
                18
            } else {
                19
            }
        }
        6 => {
            if male {
                20
            } else {
                21
            }
        }
        2 | 3 | 7 | 8 => 0, // Fairy / Dwarf / Returned / Warborn: not implemented
        _ => return current.clone(),
    };
    let is_actor_model = npcs
        .model_type(template.model_id)
        .is_some_and(|m| m.sub_type == "ActorModel");
    if model_params_id == 0 || !is_actor_model {
        return current.clone();
    }
    // Every hair body part item of this model.
    let hairs: HashSet<u32> = data
        .items
        .templates
        .values()
        .filter_map(|t| match t.kind {
            TemplateKind::BodyPart {
                slot_type_id,
                model_id,
            } if model_id == template.model_id && slot_type_id == slot_type::HAIR => Some(t.id),
            _ => None,
        })
        .collect();
    if !hairs.is_empty() {
        let possible: Vec<u32> = npcs
            .total_character_customs
            .iter()
            .filter(|tc| tc.model_id == model_params_id && hairs.contains(&tc.hair_id))
            .map(|tc| tc.id)
            .collect();
        if !possible.is_empty() {
            let r = random.next_max(possible.len() as i32);
            total_custom_id = possible[r as usize];
        } else {
            tracing::trace!(
                "No compatible TotalCharacterCustoms hair found for NPC: {}",
                template.id
            );
        }
    }

    let mut look = TemplateLook {
        hair_id: 0,
        model_params: UnitCustomModelParams::default(),
        body_items: [(0, false); 7],
    };
    match (total_custom_id > 0)
        .then(|| npcs.total_character_custom(total_custom_id))
        .flatten()
    {
        Some(tc) => {
            look.hair_id = tc.hair_id;
            look.model_params = tc.model_params();
        }
        None => look.model_params = skin_model_params(),
    }
    // The body-part scan uses the (shared) template's current HairId.
    apply_item_body_parts(
        &npcs.item_body_parts,
        template.model_id,
        current.hair_id,
        &mut look.body_items,
    );
    look
}

/// `Npc.AnimActionId` for the time of day `tod` (hours).
pub fn anim_action_id(template: &NpcTemplate, tod: f32) -> u32 {
    match template.npc_posture_sets.len() {
        0 => 0,
        1 => template.npc_posture_sets[0].anim_action_id,
        _ => template
            .npc_posture_sets
            .iter()
            .find(|p| p.start_tod_time <= tod)
            .map_or(0, |p| p.anim_action_id),
    }
}

/// Recomputes `npc.anim_action_id` from the current game time (the C# getter is evaluated on
/// every read).
pub fn update_anim_action_id(server: &GameServer, npc: &NpcRef) {
    let template_id = npc.data().unit.template_id;
    let Some(t) = server.game_data.systems.npcs.npc.template(template_id) else {
        return;
    };
    let id = anim_action_id(t, server.time.get_time());
    npc.data().anim_action_id = id;
}

/// `EquipmentContainer.AddOrMoveExistingItem(0, item, slot)` for an NPC (no owner: `CanAccept`
/// always passes): the preferred slot when free, else the first free slot.
fn equipment_add(npc: &mut Npc, mut item: Item, preferred: usize) {
    let slot = if npc.equipment.get(preferred).is_some_and(Option::is_none) {
        Some(preferred)
    } else {
        npc.equipment.iter().position(Option::is_none)
    };
    let Some(slot) = slot else {
        return; // container full
    };
    item.slot_type = container_slot_type::EQUIPMENT;
    item.slot = slot as i32;
    npc.equipment[slot] = Some(item);
}

/// `SetEquipItemTemplate(npc, templateId, slot, grade, npcOnly)`. Returns the added item (for
/// `UpdateGearBonuses(item, null)`).
fn set_equip_item_template(
    server: &GameServer,
    npc: &NpcRef,
    template_id: u32,
    slot: usize,
    grade: u8,
    npc_only: bool,
) -> Option<Item> {
    let mut n = npc.data();
    if npc_only && n.equipment_item(slot).is_some() {
        return None;
    }
    if template_id == 0 {
        return None; // AddOrMoveExistingItem(.., null, ..) -> false
    }
    let data = &server.game_data.items;
    let Some(t) = data.template(template_id) else {
        tracing::warn!(
            "Npc {}: equipment item template {template_id} (slot {slot}) does not exist",
            n.unit.template_id
        );
        return None;
    };
    let now = chrono::Utc::now().timestamp();
    let item = Item::create(0, t, 1, grade, server.config.id, data, now);
    equipment_add(&mut n, item.clone(), slot);
    Some(item)
}

/// `NpcManager.Create(world, objectId, templateId)`: a new NPC (not yet in the world).
/// `obj_id == 0` allocates a new object id. `None` when the template does not exist.
pub fn create(server: &GameServer, obj_id: u32, template_id: u32) -> Option<NpcRef> {
    let gd = &server.game_data;
    let template = gd.systems.npcs.npc.template(template_id)?;
    let obj_id = if obj_id > 0 {
        obj_id
    } else {
        match server.ids.object.get_next_id() {
            Ok(id) => id,
            Err(e) => {
                tracing::error!("{e}");
                return None;
            }
        }
    };

    let look = server.systems.npcs.npcs.resolve_look(gd, template);

    let mut npc = Npc::new(template_id);
    npc.unit.model_id = template.model_id;
    npc.can_fly = gd.systems.npcs.npc.is_fly_or_swim(template.model_id);
    npc.unit.faction_id = gd.factions.get(template.faction_id).map_or(0, |f| f.id);
    npc.unit.level = template.level;
    npc.unit.scale = template.scale;
    npc.template_race = template.race;
    npc.template_gender = template.gender;
    npc.unit.model_params = look.model_params.clone();
    npc.anim_action_id = anim_action_id(template, server.time.get_time());
    let npc_ref = Entity::new(obj_id, npc);
    let obj = WorldObject::from(&npc_ref);
    let skills = server.systems.npcs.hooks.skills.get();

    let it = &template.items;
    let slots = [
        (it.headgear, equip_slot::HEAD),
        (it.necklace, equip_slot::NECK),
        (it.shirt, equip_slot::CHEST),
        (it.belt, equip_slot::WAIST),
        (it.pants, equip_slot::LEGS),
        (it.gloves, equip_slot::HANDS),
        (it.shoes, equip_slot::FEET),
        (it.bracelet, equip_slot::ARMS),
        (it.back, equip_slot::BACK),
        (it.undershirts, equip_slot::UNDERSHIRT),
        (it.underpants, equip_slot::UNDERPANTS),
        (it.mainhand, equip_slot::MAINHAND),
        (it.offhand, equip_slot::OFFHAND),
        (it.ranged, equip_slot::RANGED),
        (it.musical, equip_slot::MUSICAL),
        (it.cosplay, equip_slot::COSPLAY),
    ];
    let equip = |template_id: u32, slot: i32, npc_only: bool| {
        if let Some(item) =
            set_equip_item_template(server, &npc_ref, template_id, slot as usize, 0, npc_only)
        {
            // EquipmentContainer.OnEnterContainer -> ParentUnit.UpdateGearBonuses(item, null)
            if let Some(s) = &skills {
                s.update_gear_bonuses(server, &obj, Some(&item), None);
            }
        }
    };
    for (id, slot) in slots {
        equip(id, slot, false);
    }
    for i in 0..7usize {
        let slot = i as i32 + 19;
        if slot == equip_slot::HAIR {
            // `template.ModelParams != null` always holds.
            equip(look.hair_id, slot, false);
        } else {
            equip(look.body_items[i].0, slot, look.body_items[i].1);
        }
    }

    initialize_spawn_buffs(server, &npc_ref, template);
    if let Some(s) = &skills {
        s.update_gear_bonuses(server, &obj, None, None);
    }
    crate::systems::dispatch_buff_events(server, &obj);

    {
        let mut n = npc_ref.data();
        super::stats::refresh_cached_stats(gd, &mut n);
        n.unit.hp = n.unit.max_hp;
        n.unit.mp = n.unit.max_mp;
        // Without the AI subsystem: `has_ai` approximates `Ai != null`.
        n.has_ai = template.ai_file_id > 0;
    }
    // AiUtils.GetAiByType / AiManager.AddAi / Ai.Start (systems::ai)
    if let Some(ai) = server.systems.hooks.ai.get() {
        ai.npc_created(server, &npc_ref, template.ai_file_id);
    }
    debug_assert_eq!(npc_ref.data().equipment.len(), UNIT_EQUIPMENT_SLOTS);
    Some(npc_ref)
}

/// `Npc.InitializeSpawnBuffs()`: initial buffs (`BuffTemplate.Apply` with the NPC as caster),
/// passive buffs, then the template's stat bonuses (`AddBonus(0, ..)`, value = template value).
/// The buff events are dispatched by the caller.
pub fn initialize_spawn_buffs(server: &GameServer, npc: &NpcRef, template: &NpcTemplate) {
    let skills = &server.game_data.skills;
    let now = chrono::Utc::now().timestamp_millis();
    let mut n = npc.data();
    let caster = Some((n.unit.template_id, n.unit.level));
    for &buff_id in &template.buffs {
        let Some(buff) = skills.buff(buff_id) else {
            tracing::warn!("BuffId {buff_id} for npc {} not found", template.id);
            continue;
        };
        // BuffTemplate.Apply: `if (BuffId > 0 && target.Buffs.CheckBuff(BuffId)) return;`
        // (RequireBuffId / CheckBuffImmune: tolerance data belongs to the skills subsystem).
        if buff.id > 0 && n.unit.buffs.check_buff(buff.id) {
            continue;
        }
        let b = Buff::new(
            buff.clone(),
            SkillCaster {
                kind: SKILL_CASTER_UNIT,
                obj_id: npc.obj_id,
            },
            caster,
            now,
        );
        n.unit.buffs.add_buff(b, 0, 0, now);
    }
    for p in &template.passive_buffs {
        // `new PassiveBuff { Template = npcPassiveBuff.PassiveBuff }.Apply(this)`.
        let Some(pt) = skills.passive_buff(p.passive_buff_id) else {
            tracing::warn!(
                "PassiveBuff {} for npc {} not found",
                p.passive_buff_id,
                template.id
            );
            continue;
        };
        let Some(buff) = skills.buff(pt.buff_id) else {
            tracing::warn!(
                "Buff {} of passive buff {} not found",
                pt.buff_id,
                p.passive_buff_id
            );
            continue;
        };
        let mut b = Buff::new(
            buff.clone(),
            SkillCaster {
                kind: SKILL_CASTER_UNIT,
                obj_id: 0,
            },
            caster,
            now,
        );
        b.passive = true;
        n.unit.buffs.add_buff(b, 0, 0, now);
    }
    for bt in &template.bonuses {
        // TODO (C#): using LinearLevelBonus
        n.unit.bonuses.add_bonus(
            0,
            Bonus {
                template: bt.clone(),
                value: bt.value,
            },
        );
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;
    use crate::data::items::ItemTemplate;
    use crate::data::skills::{BuffKind, BuffTemplate};
    use crate::data::world_data::SystemFaction;
    use crate::models::unit::{BonusTemplate, UnitAttribute, UnitModifierType};
    use crate::models::unit_model::UnitCustomModelType;
    use crate::systems::npcs::npc::data::{
        BodyPartTemplate, ModelType, NpcPosture, TotalCharacterCustom,
    };
    use crate::test_support::*;

    fn body_part(id: u32, slot: u32, model: u32) -> ItemTemplate {
        ItemTemplate {
            kind: TemplateKind::BodyPart {
                slot_type_id: slot,
                model_id: model,
            },
            ..ItemTemplate::plain(id, 1)
        }
    }

    fn tcc(id: u32, model: u32, hair: u32) -> TotalCharacterCustom {
        TotalCharacterCustom {
            id,
            model_id: model,
            hair_id: hair,
            hair_color_id: id * 10,
            modifier: vec![0; 128],
            ..TotalCharacterCustom::default()
        }
    }

    /// Game data with a guard (template 100: equipment, custom look 5, postures, initial buff,
    /// +10% max hp bonus, merchant) and a humanoid without a custom look (template 200).
    pub(crate) fn npc_game_data() -> GameData {
        let mut gd = GameData::empty();
        gd.formulas = crate::systems::npcs::npc::stats::tests::npc_formulas();
        gd.factions.add(SystemFaction {
            id: 101,
            ..SystemFaction::default()
        });
        for id in [501, 601] {
            gd.items.templates.insert(id, ItemTemplate::plain(id, 1));
        }
        gd.items
            .templates
            .insert(23001, body_part(23001, slot_type::FACE, 1));
        for h in [24001, 24002, 24003, 24004] {
            gd.items
                .templates
                .insert(h, body_part(h, slot_type::HAIR, 1));
        }
        // hair of another model
        gd.items
            .templates
            .insert(24900, body_part(24900, slot_type::HAIR, 99));
        gd.skills
            .buffs
            .insert(900, BuffTemplate::simple(900, BuffKind::Good));
        let d = &mut gd.systems.npcs.npc;
        d.model_types.insert(
            1,
            ModelType {
                id: 1,
                sub_id: 1,
                sub_type: "ActorModel".into(),
            },
        );
        d.actor_model_movement.insert(1, 2);
        d.add_total_character_custom(tcc(5, 10, 24001));
        d.add_total_character_custom(tcc(6, 10, 24002));
        d.add_total_character_custom(tcc(7, 11, 24003)); // female model
        d.add_total_character_custom(tcc(8, 10, 24900)); // hair of another model
        d.add_total_character_custom(tcc(9, 10, 24004));
        let bp = |item, slot| BodyPartTemplate {
            item_id: item,
            model_id: 1,
            npc_only: false,
            slot_type_id: slot,
        };
        d.item_body_parts.insert(
            1,
            vec![
                (slot_type::FACE, vec![bp(23001, slot_type::FACE)]),
                (slot_type::HAIR, vec![bp(24002, slot_type::HAIR)]),
            ],
        );
        let posture = |anim, start| NpcPosture {
            npc_posture_set_id: 6,
            anim_action_id: anim,
            talk_anim: String::new(),
            start_tod_time: start,
        };
        let mut guard = NpcTemplate {
            id: 100,
            level: 50,
            npc_template_id: 9,
            npc_kind_id: 1,
            npc_grade_id: 1,
            model_id: 1,
            faction_id: 101,
            scale: 1.25,
            race: 1,
            gender: 1,
            char_race_id: 1,
            ai_file_id: 3,
            exp_multiplier: 1.0,
            merchant: true,
            total_custom_id: 5,
            hair_id: 24001,
            buffs: vec![900, 901],
            bonuses: vec![BonusTemplate {
                attribute: UnitAttribute::MaxHealth,
                modifier_type: UnitModifierType::Percent,
                value: 10,
                linear_level_bonus: 0,
            }],
            npc_posture_sets: vec![posture(3, 18.0), posture(2, 6.0), posture(1, 0.0)],
            ..NpcTemplate::default()
        };
        guard.items.headgear = 501;
        guard.items.mainhand = 601;
        guard.items.mainhand_grade = 3;
        guard.model_params = d.total_character_custom(5).unwrap().model_params();
        apply_item_body_parts(&d.item_body_parts, 1, 24001, &mut guard.body_items);
        let villager = NpcTemplate {
            id: 200,
            level: 10,
            npc_template_id: 9,
            npc_kind_id: 1,
            npc_grade_id: 1,
            model_id: 1,
            faction_id: 101,
            race: 1,
            gender: 1,
            char_race_id: 1,
            model_params: skin_model_params(),
            exp_multiplier: 1.0,
            ..NpcTemplate::default()
        };
        d.templates.insert(100, guard);
        d.templates.insert(200, villager);
        gd
    }

    #[tokio::test]
    async fn create_fills_the_npc_from_its_template() {
        let server = test_server_with(npc_game_data());
        assert!(create(&server, 0, 4242).is_none());
        let npc = create(&server, 0x777, 100).expect("created");
        assert_eq!(npc.obj_id, 0x777);
        let n = npc.data();
        assert_eq!(n.unit.template_id, 100);
        assert_eq!(
            (n.unit.level, n.unit.model_id, n.unit.faction_id),
            (50, 1, 101)
        );
        assert_eq!(n.unit.scale, 1.25);
        assert_eq!((n.template_race, n.template_gender), (1, 1));
        assert!(n.can_fly);
        assert!(n.has_ai);
        // 12:00 -> the posture starting at 6:00
        assert_eq!(n.anim_action_id, 2);
        assert_eq!(n.unit.model_params.kind, UnitCustomModelType::FACE);
        assert_eq!(n.unit.model_params.hair_color_id, 50);
        let slot = |s: i32| {
            n.equipment_item(s as usize)
                .map(|i| (i.template_id, i.slot))
        };
        assert_eq!(slot(equip_slot::HEAD), Some((501, equip_slot::HEAD)));
        assert_eq!(
            slot(equip_slot::MAINHAND),
            Some((601, equip_slot::MAINHAND))
        );
        // pack grades are never passed (grade 0)
        assert_eq!(
            n.equipment_item(equip_slot::MAINHAND as usize)
                .unwrap()
                .grade,
            0
        );
        assert_eq!(slot(equip_slot::FACE), Some((23001, equip_slot::FACE)));
        assert_eq!(slot(equip_slot::HAIR), Some((24001, equip_slot::HAIR)));
        assert_eq!(n.equipment.iter().flatten().count(), 4);
        assert!(n
            .equipment
            .iter()
            .flatten()
            .all(|i| i.id == 0 && i.slot_type == container_slot_type::EQUIPMENT));
        // initial buff 900 (901 does not exist), +10% max hp bonus at index 0
        assert!(n.unit.buffs.check_buff(900));
        assert_eq!(n.unit.buffs.effects.len(), 1);
        assert_eq!(n.unit.bonuses.bonuses(UnitAttribute::MaxHealth).len(), 1);
        let base = (5.0 * 50f64.powf(2.06) + 210f64.powf(1.05) * 9.3 + 102.0) as i32;
        let max_hp = base + (base as f32 * 10.0 / 100.0) as i32;
        assert_eq!((n.unit.max_hp, n.unit.hp), (max_hp, max_hp));
        assert_eq!((n.unit.max_mp, n.unit.mp), (3230, 3230));
        assert_eq!(n.unit.hp_regen, 57);
    }

    #[tokio::test]
    async fn load_custom_picks_with_the_seeded_random_and_writes_back() {
        let server = test_server_with(npc_game_data());
        // Candidates: customs of model 10 whose hair belongs to model 1: [5, 6, 9].
        // .NET Random(123456789).Next(3) -> 1, 0, 1, ...
        let hair = |npc: &NpcRef| {
            npc.data()
                .equipment_item(equip_slot::HAIR as usize)
                .map(|i| i.template_id)
        };
        let a = create(&server, 0, 200).unwrap();
        assert_eq!(hair(&a), Some(24002)); // custom 6
        assert_eq!(a.data().unit.model_params.hair_color_id, 60);
        assert_eq!(
            a.data()
                .equipment_item(equip_slot::FACE as usize)
                .map(|i| i.template_id),
            Some(23001)
        );
        let b = create(&server, 0, 200).unwrap();
        assert_eq!(hair(&b), Some(24001)); // custom 5
        let t = server.game_data.systems.npcs.npc.template(200).unwrap();
        let look = server.systems.npcs.npcs.template_look(t);
        assert_eq!(look.hair_id, 24001);
        // the written-back body items were scanned with the previous hair (24002, custom 6)
        assert_eq!(look.body_items[1], (24002, false));
        // a template with a custom look never rolls
        let g = create(&server, 0, 100).unwrap();
        assert_eq!(hair(&g), Some(24001));
        let c = create(&server, 0, 200).unwrap();
        assert_eq!(hair(&c), Some(24002)); // Next(3) -> 1 again
    }

    /// Creates every NPC template of the real `compact.sqlite3` (`AAEMU_COMPACT_DB`).
    #[tokio::test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    async fn real_compact_db_create_every_npc() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        use rusqlite::{Connection, OpenFlags};
        let conn = Connection::open_with_flags(&path, OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let mut gd = GameData::empty();
        gd.items = crate::data::ItemData::load(&conn).unwrap();
        gd.skills = crate::data::SkillData::load(&conn).unwrap();
        gd.formulas = crate::data::FormulaManager::load(&conn).unwrap();
        let loc = crate::data::world_data::Localization::load(&conn, "en_us").unwrap();
        gd.factions = crate::data::FactionData::load(&conn, &loc).unwrap();
        gd.systems.npcs.npc = NpcData::load_conn(&conn, "en_us").unwrap();
        let server = test_server_with(gd);
        let mut ids: Vec<u32> = server
            .game_data
            .systems
            .npcs
            .npc
            .templates
            .keys()
            .copied()
            .collect();
        ids.sort_unstable();
        let (mut equipped, mut buffed, mut posed, mut rolled) = (0, 0, 0, 0);
        for &id in &ids {
            let npc = create(&server, 0, id).expect("template exists");
            let n = npc.data();
            // npc 6466 has a -500% MaxHealth modifier: negative MaxHp, as in C#.
            assert!(
                n.unit.max_hp > 0 || id == 6466,
                "npc {id}: max hp {}",
                n.unit.max_hp
            );
            assert_eq!(n.unit.hp, n.unit.max_hp);
            assert_eq!(n.unit.mp, n.unit.max_mp);
            if n.equipment.iter().flatten().count() > 0 {
                equipped += 1;
            }
            if !n.unit.buffs.effects.is_empty() {
                buffed += 1;
            }
            if n.anim_action_id > 0 {
                posed += 1;
            }
            let t = server.game_data.systems.npcs.npc.template(id).unwrap();
            if t.total_custom_id == 0 && n.unit.model_params.kind == UnitCustomModelType::FACE {
                rolled += 1;
            }
        }
        eprintln!(
            "created {} npcs: {equipped} with equipment, {buffed} with buffs, {posed} with a \
             posture, {rolled} with a random custom look",
            ids.len()
        );
        assert!(equipped > 0 && buffed > 0 && posed > 0 && rolled > 0);
    }

    #[test]
    fn anim_action_by_time_of_day() {
        let gd = npc_game_data();
        let t = gd.systems.npcs.npc.template(100).unwrap();
        assert_eq!(anim_action_id(t, 23.0), 3);
        assert_eq!(anim_action_id(t, 18.0), 3);
        assert_eq!(anim_action_id(t, 5.9), 1);
        let mut one = t.clone();
        one.npc_posture_sets.truncate(1);
        assert_eq!(anim_action_id(&one, 1.0), 3);
        one.npc_posture_sets.clear();
        assert_eq!(anim_action_id(&one, 1.0), 0);
        let mut late = t.clone();
        late.npc_posture_sets.remove(2);
        assert_eq!(anim_action_id(&late, 1.0), 0); // nothing started yet
    }

    #[test]
    fn dotnet_random_parity() {
        // Values printed by .NET 10 (`new System.Random(123456789)`).
        let mut r = DotNetRandom::new(123_456_789);
        let first: Vec<i32> = (0..5).map(|_| r.next_int()).collect();
        assert_eq!(
            first,
            vec![1091672793, 381850644, 1335622286, 865414785, 1968738143]
        );
        assert_eq!(r.next_max(10), 6);
        assert_eq!(r.next_max(831), 66);
        assert_eq!(r.next_max(3), 2);
        assert_eq!(r.next_max(100), 55);
        let mut r = DotNetRandom::new(123_456_789);
        let picks: Vec<i32> = (0..10).map(|_| r.next_max(57)).collect();
        assert_eq!(picks, vec![28, 10, 35, 22, 52, 39, 4, 51, 31, 41]);
    }
}
