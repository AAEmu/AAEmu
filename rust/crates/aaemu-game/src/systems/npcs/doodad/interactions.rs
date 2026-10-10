//! `Models/Game/World/Interactions` (41 `IWorldInteraction` classes) and the
//! `WorldInteractionType` enum. `InteractionEffect.Apply` (skills) resolves the class by the
//! enum name (`Type.GetType("...Interactions." + WorldInteraction)`); [`execute_interaction`]
//! is that lookup + `Execute`. Values without a class return `false` (the C#
//! "Unknown world interaction" error).

use super::funcs::{economy, error_message as err, send_error, send_error_unit, skills_hook};
use super::phase;
use super::runtime::DoodadCtx;
use super::templates::FuncTemplate;
use super::{coffer, manager};
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;
use crate::server::GameServer;
use crate::systems::npcs::public_farm;
use crate::world::{now_ms, DoodadRef, WorldObject};
use aaemu_commons::{Marshal, PacketStream};

/// `WorldInteractionType`.
pub mod world_interaction_type {
    pub const LOOTING: u32 = 0;
    pub const CUTDOWN: u32 = 1;
    pub const SEEDING: u32 = 2;
    pub const WATERING: u32 = 3;
    pub const HARVEST: u32 = 4;
    pub const REMOVE: u32 = 5;
    pub const CANCEL: u32 = 6;
    pub const ERROR: u32 = 7;
    pub const CHECK_WATER: u32 = 8;
    pub const CHECK_GROWTH: u32 = 9;
    pub const DIG_TERRAIN: u32 = 10;
    pub const SPRAY: u32 = 11;
    pub const LINE_SPRAY: u32 = 12;
    pub const WATER_LEVEL: u32 = 13;
    pub const SUMMON_MINE_SPOT: u32 = 14;
    pub const DIG_MINE: u32 = 15;
    pub const SUMMON_CATTLE: u32 = 16;
    pub const SHEARING: u32 = 17;
    pub const FEEDING: u32 = 18;
    pub const USE: u32 = 19;
    pub const BUTCHER: u32 = 20;
    pub const CRAFT_START: u32 = 21;
    pub const CRAFT_ACT: u32 = 22;
    pub const CRAFT_INFO: u32 = 23;
    pub const CRAFT_GET_ITEM: u32 = 24;
    pub const CRAFT_CANCEL: u32 = 25;
    pub const DIRECT_LOOT: u32 = 26;
    pub const HANG: u32 = 27;
    pub const BINDING: u32 = 28;
    pub const SUMMON_BEANSTALK: u32 = 29;
    pub const GIVE_QUEST: u32 = 30;
    pub const SUMMON_DOODAD: u32 = 31;
    pub const CRAFT_DEF_INTERACTION: u32 = 32;
    pub const MOW: u32 = 33;
    pub const COMPLETE_QUEST: u32 = 34;
    pub const BUILDING: u32 = 35;
    pub const DOORING: u32 = 36;
    pub const ALCHEMY: u32 = 51;
    pub const SKIN_OFF: u32 = 67;
    pub const CATCH: u32 = 71;
    pub const FIBER_COLLECT: u32 = 72;
    pub const ORE_MINE: u32 = 73;
    pub const FRUIT_PICK: u32 = 76;
    pub const CROP_HARVEST: u32 = 78;
    pub const PLANT_COLLECT: u32 = 83;
    pub const REPAIR_HOUSE: u32 = 90;
    pub const MAGICAL_ENCHANT: u32 = 92;
    pub const RECOVER_ITEM: u32 = 93;
    pub const DEMOLISH: u32 = 94;
    pub const SUMMON_DOODAD_WITH_UCC: u32 = 97;
    pub const BUY_SIEGE_TICKET: u32 = 104;
    pub const SELL_BACKPACK: u32 = 105;
}

use world_interaction_type as wi;

/// The `IWorldInteraction` class of a `WorldInteractionType` value (`None`: no class, the C#
/// `Type.GetType` returns null).
pub fn interaction_class(wi_type: u32) -> Option<&'static str> {
    Some(match wi_type {
        wi::LOOTING => "Looting",
        wi::CUTDOWN => "Cutdown",
        wi::SEEDING => "Seeding",
        wi::WATERING => "Watering",
        wi::HARVEST => "Harvest",
        wi::REMOVE => "Remove",
        wi::CANCEL => "Cancel",
        wi::ERROR => "Error",
        wi::CHECK_WATER => "CheckWater",
        wi::CHECK_GROWTH => "CheckGrowth",
        wi::SPRAY => "Spray",
        wi::LINE_SPRAY => "LineSpray",
        wi::WATER_LEVEL => "WaterLevel",
        wi::DIG_MINE => "DigMine",
        wi::FEEDING => "Feeding",
        wi::USE => "Use",
        wi::BUTCHER => "Butcher",
        wi::CRAFT_START => "CraftStart",
        wi::CRAFT_ACT => "CraftAct",
        wi::DIRECT_LOOT => "DirectLoot",
        wi::HANG => "Hang",
        wi::BINDING => "Binding",
        wi::GIVE_QUEST => "GiveQuest",
        wi::SUMMON_DOODAD => "SummonDoodad",
        wi::COMPLETE_QUEST => "CompleteQuest",
        wi::DOORING => "Dooring",
        wi::ALCHEMY => "Alchemy",
        wi::SKIN_OFF => "SkinOff",
        wi::CATCH => "Catch",
        wi::FIBER_COLLECT => "FiberCollect",
        wi::ORE_MINE => "OreMine",
        wi::FRUIT_PICK => "FruitPick",
        wi::CROP_HARVEST => "CropHarvest",
        wi::PLANT_COLLECT => "PlantCollect",
        wi::REPAIR_HOUSE => "RepairHouse",
        wi::MAGICAL_ENCHANT => "MagicalEnchant",
        wi::RECOVER_ITEM => "RecoverItem",
        wi::DEMOLISH => "Demolish",
        wi::SUMMON_DOODAD_WITH_UCC => "SummonDoodadWithUcc",
        wi::BUY_SIEGE_TICKET => "BuySiegeTicket",
        wi::SELL_BACKPACK => "SellBackpack",
        _ => return None,
    })
}

/// The `SkillCaster` / `SkillCastTarget` details only `MagicalEnchant` reads.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct InteractionCasts {
    /// `casterType is SkillItem` -> `ItemTemplateId`.
    pub caster_item_template_id: Option<u32>,
    /// `targetType is SkillCastItemTarget` -> `Id`.
    pub target_item_id: Option<u64>,
}

/// `SCVegetationCutdowningPacket(unitObjId, doodadObjId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCVegetationCutdowningPacket {
    pub unit_obj_id: u32,
    pub doodad_obj_id: u32,
}
packet_def!(
    SCVegetationCutdowningPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCVegetationCutdowningPacket
);
impl Marshal for SCVegetationCutdowningPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.unit_obj_id).write_bc(self.doodad_obj_id);
    }
}

/// `SCHungPacket(unitObjId, targetObjId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCHungPacket {
    pub unit_obj_id: u32,
    pub target_obj_id: u32,
}
packet_def!(SCHungPacket, level = LEVEL_PLAIN, opcode = sc::SCHungPacket);
impl Marshal for SCHungPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.unit_obj_id).write_bc(self.target_obj_id);
    }
}

/// `InteractionEffect.Apply`'s class lookup + `IWorldInteraction.Execute(caster, casterType,
/// target, targetType, skillId, doodadId, objectFunc)`. Returns `false` when the type has no
/// class (nothing executed).
pub fn execute_interaction(
    server: &GameServer,
    wi_type: u32,
    caster: &WorldObject,
    target: Option<&WorldObject>,
    skill_id: u32,
    doodad_id: u32,
    func: Option<&FuncTemplate>,
) -> bool {
    execute_interaction_with(
        server,
        wi_type,
        caster,
        target,
        skill_id,
        doodad_id,
        func,
        InteractionCasts::default(),
    )
}

/// [`execute_interaction`] with the caster / target cast details (`MagicalEnchant`).
#[allow(clippy::too_many_arguments)]
pub fn execute_interaction_with(
    server: &GameServer,
    wi_type: u32,
    caster: &WorldObject,
    target: Option<&WorldObject>,
    skill_id: u32,
    doodad_id: u32,
    _func: Option<&FuncTemplate>,
    casts: InteractionCasts,
) -> bool {
    let Some(class) = interaction_class(wi_type) else {
        return false;
    };
    let target_doodad = target.and_then(WorldObject::as_doodad);
    match wi_type {
        wi::CHECK_GROWTH => check_growth(server, caster, target_doodad, skill_id),
        wi::CROP_HARVEST => crop_harvest(server, caster, target_doodad, skill_id),
        wi::CUTDOWN => {
            if let Some(d) = target_doodad {
                use_doodad(server, d, caster, skill_id);
                if let Some(world) = server.world.world_of(caster) {
                    let packet = SCVegetationCutdowningPacket {
                        unit_obj_id: caster.obj_id(),
                        doodad_obj_id: d.obj_id,
                    };
                    crate::world::broadcast(&world, caster, &packet, true);
                }
            }
        }
        wi::DEMOLISH => {
            if let (Some(house), Some(player)) =
                (target.and_then(WorldObject::as_house), caster.as_player())
            {
                economy(server).demolish_house(server, player, house);
            }
        }
        wi::HANG => hang(server, caster, target, skill_id),
        wi::MAGICAL_ENCHANT => {
            if let (Some(player), Some(target_item), Some(rune)) = (
                caster.as_player(),
                casts.target_item_id,
                casts.caster_item_template_id,
            ) {
                economy(server).magical_enchant(server, player, target_item, rune);
            }
        }
        wi::RECOVER_ITEM => recover_item(server, caster, target_doodad, skill_id),
        wi::SUMMON_DOODAD => summon_doodad(server, caster, target, doodad_id),
        wi::USE => {
            tracing::debug!("World interaction SkillID: {skill_id}");
            if let Some(d) = target_doodad {
                use_doodad(server, d, caster, skill_id);
            }
            // TODO (C#): ID=21902, View Fish Finder (commented out in C#).
        }
        // Every other class: `if (target is Doodad doodad) doodad.Use(caster, skillId);`
        _ => {
            if let Some(d) = target_doodad {
                use_doodad(server, d, caster, skill_id);
            }
        }
    }
    tracing::trace!("World interaction {class} executed");
    true
}

/// `doodad.Use(caster, skillId)`.
fn use_doodad(server: &GameServer, doodad: &DoodadRef, caster: &WorldObject, skill_id: u32) {
    if let Some(ctx) = DoodadCtx::of(server, doodad) {
        phase::use_doodad(&ctx, Some(caster), skill_id, 0);
    }
}

/// `DoodadFunc.Use(caster, owner, skillId)` (`nextPhase` defaults to 0).
fn use_doodad_func(
    ctx: &DoodadCtx<'_>,
    func: &super::templates::DoodadFunc,
    caster: &WorldObject,
    skill_id: u32,
) {
    if let Some(t) = ctx.data().func_template(func.func_id, &func.func_type) {
        super::funcs::use_func(t, ctx, Some(caster), skill_id, 0);
    }
}

/// `CheckGrowth.Execute`.
fn check_growth(
    server: &GameServer,
    caster: &WorldObject,
    doodad: Option<&DoodadRef>,
    skill_id: u32,
) {
    // TODO (C#): Verification Needed
    let Some(doodad) = doodad else {
        return;
    };
    let Some(ctx) = DoodadCtx::of(server, doodad) else {
        return;
    };
    let group = doodad.data().func_group_id;
    let Some(func) = ctx.data().func_for_skill(group, skill_id).cloned() else {
        return;
    };
    let grp = func.group_id;
    use_doodad_func(&ctx, &func, caster, skill_id);
    let group = doodad.data().func_group_id;
    let Some(next) = ctx.data().func_for_skill(group, skill_id).cloned() else {
        return;
    };
    if i64::from(next.next_phase) == i64::from(grp) || next.next_phase == -1 {
        return;
    }
    use_doodad_func(&ctx, &next, caster, skill_id);
}

/// `CropHarvest.Execute`.
fn crop_harvest(
    server: &GameServer,
    caster: &WorldObject,
    doodad: Option<&DoodadRef>,
    skill_id: u32,
) {
    let Some(doodad) = doodad else {
        return;
    };
    if let Some(world) = server.world.world_of(&WorldObject::from(doodad)) {
        let pos = world.world_pos_rot(&WorldObject::from(doodad));
        if public_farm::in_public_farm(server, &world.template, (pos.x, pos.y, pos.z)) {
            let owner_id = doodad.data().owner_id;
            if public_farm::is_protected_by_public_farm(server, doodad, now_ms()) && owner_id != 0 {
                if let Some(character) = caster.as_player() {
                    let (id, name) = {
                        let c = character.data();
                        (c.id, c.unit.name.clone())
                    };
                    if owner_id != id {
                        send_error(character, err::CANNOT_HARVEST_YET);
                        tracing::debug!(
                            "This should never happen character {name} attempted to bypass harvest protection (clienthacks?)"
                        );
                        skills_hook(server).cancel_skill(server, character);
                        return;
                    }
                }
            }
        }
    }
    use_doodad(server, doodad, caster, skill_id);
}

/// `Hang.Execute`.
fn hang(server: &GameServer, caster: &WorldObject, target: Option<&WorldObject>, skill_id: u32) {
    let (Some(target), Some(_)) = (target, caster.as_player()) else {
        return;
    };
    // `character.Transform.StickyParent = target.Transform`: sticky parents are not modelled
    // (TODO(world): StickyParent).
    if let Some(world) = server.world.world_of(caster) {
        let packet = SCHungPacket {
            unit_obj_id: caster.obj_id(),
            target_obj_id: target.obj_id(),
        };
        crate::world::broadcast(&world, caster, &packet, false);
    }
    if let Some(d) = target.as_doodad() {
        use_doodad(server, d, caster, skill_id);
    }
}

/// `Doodad.AllowRemoval()` (`DoodadCoffer`: the container must be empty too): no persistent
/// doodad is stacked on top of it.
pub fn doodad_allow_removal(server: &GameServer, doodad: &DoodadRef) -> bool {
    if !coffer::coffer_is_empty(server, doodad) {
        return false;
    }
    !doodad
        .core()
        .children()
        .iter()
        .filter_map(WorldObject::as_doodad)
        .any(|c| c.data().is_persistent)
}

/// `RecoverItem.Execute`.
fn recover_item(
    server: &GameServer,
    caster: &WorldObject,
    doodad: Option<&DoodadRef>,
    skill_id: u32,
) {
    // `((Character)caster)` throws for other units.
    let Some(player) = caster.as_player() else {
        return;
    };
    // check if you are equipped with a backpack or glider
    let has_back_pack = !economy(server).can_replace_glider_in_backpack_slot(server, player);
    if let Some(doodad) = doodad {
        if doodad_allow_removal(server, doodad) && !has_back_pack {
            if let Some(ctx) = DoodadCtx::of(server, doodad) {
                let group = doodad.data().func_group_id;
                // Check if the phase contains a DoodadFuncRecoverItem func
                for func in ctx.data().funcs_for_group(group) {
                    let Some(t) = ctx.data().func_template(func.func_id, &func.func_type) else {
                        continue;
                    };
                    if matches!(t, FuncTemplate::RecoverItem(_)) {
                        super::funcs::use_func(t, &ctx, Some(caster), skill_id, 0);
                        // Move to next phase to remove the doodad
                        manager::delete(server, &ctx.world, doodad);
                        return;
                    }
                }
            }
        }
    }
    // Something wasn't found or is invalid, so cancel whatever we're doing
    send_error_unit(caster, err::FAILED_TO_USE_ITEM);
    skills_hook(server).interrupt_skills(server, caster);
}

/// `SummonDoodad.Execute`.
fn summon_doodad(
    server: &GameServer,
    caster: &WorldObject,
    target: Option<&WorldObject>,
    doodad_id: u32,
) {
    let Some(world) = server.world.world_of(caster) else {
        return;
    };
    let Some(doodad) = manager::create(server, &world, 0, doodad_id, Some(caster), true) else {
        return;
    };
    // `target.Transform.CloneDetached(doodad)` (null target: NullReferenceException).
    let Some(target) = target else {
        return;
    };
    let mut t = target.transform();
    let p = world.world_pos_rot(target);
    t.parent_obj_id = 0;
    t.set_position(p.x, p.y, p.z, p.rot_x, p.rot_y, p.rot_z);
    doodad.data().transform = t;
    phase::init_doodad(&DoodadCtx::new(server, world.clone(), doodad.clone()));
    manager::spawn(server, &world, &doodad);
}
