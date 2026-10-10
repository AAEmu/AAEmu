//! `UnitReqs.Validate` and `UnitRequirementsGameData.CanUseSkill`, plus
//! `SkillResultHelper.SkillResultErrorKeyToId` (1.2 / r208022 numbering).

use crate::server::GameServer;
use crate::world::WorldObject;

use super::data::{SkillTemplate, UnitReq};
use super::types::SkillCaster;

/// `UnitReqsValidationResult` (`ResultKey` as the C# key name, `ResultUInt`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ReqResult {
    pub key: &'static str,
    pub value: u32,
}

impl ReqResult {
    pub const OK: ReqResult = ReqResult {
        key: "ok",
        value: 0,
    };

    pub fn is_ok(&self) -> bool {
        self.key == "ok"
    }
}

/// `SkillResultHelper.SkillResultErrorKeyToIdFor_r208022`.
pub fn skill_result_for_key(key: &str) -> u8 {
    const TABLE: &[(&str, u8)] = &[
        ("", 0),
        ("ok", 0),
        ("skill_success", 0),
        ("skill_failure", 1),
        ("skill_source_died", 2),
        ("skill_source_alive", 3),
        ("skill_target_died", 4),
        ("skill_target_destroyed", 5),
        ("skill_target_alive", 6),
        ("skill_on_casting", 7),
        ("skill_cooldown_time", 8),
        ("skill_no_target", 9),
        ("skill_lack_health", 10),
        ("skill_lack_mana", 11),
        ("skill_obstacle", 12),
        ("skill_outof_height", 13),
        ("skill_too_close_range", 14),
        ("skill_too_far_range", 15),
        ("skill_outof_angle", 16),
        ("skill_cannot_cast_in_combat", 17),
        ("skill_cannot_cast_while_moving", 18),
        ("skill_cannot_cast_in_stun", 19),
        ("skill_cannot_cast_while_walking", 20),
        ("skill_cannot_cast_in_swimming", 21),
        ("skill_blank_minded", 22),
        ("skill_silence", 23),
        ("skill_crippled", 24),
        ("skill_cannot_cast_in_channeling", 25),
        ("skill_cannot_cast_in_prison", 26),
        ("skill_need_stealth", 27),
        ("skill_need_nocombat_target", 28),
        ("skill_target_immune", 29),
        ("skill_invalid_skill", 30),
        ("skill_inactive_ability", 31),
        ("skill_not_enough_ability_level", 32),
        ("skill_invalid_source", 33),
        ("skill_invalid_target", 34),
        ("skill_invalid_location", 35),
        ("skill_need_reagent", 36),
        ("skill_item_locked", 37),
        ("skill_need_money", 38),
        ("skill_need_labor_power", 39),
        ("skill_source_is_hanging", 40),
        ("skill_source_is_riding", 41),
        ("skill_higher_buff", 42),
        ("skill_not_pvp_area", 43),
        ("skill_not_now", 44),
        ("skill_no_perm", 45),
        ("skill_bag_full", 46),
        ("skill_protected_faction", 47),
        ("skill_protected_level", 48),
        ("skill_unit_reqs_or_fail", 49),
        ("backpack_occupied", 51),
        ("skill_obstacle_for_spawn_doodad", 52),
        ("skill_cannot_spawn_doodad_in_house", 53),
        ("skill_cannot_use_for_self", 54),
        ("skill_not_preoccupied", 55),
        ("skill_not_my_npc", 56),
        ("skill_not_checked_second_pass", 57),
        ("SKILL_CANNOT_USE_HERE", 58),
        ("skill_invalid_grade_enchant_support_item", 59),
        ("skill_check_character_p_stat_min", 60),
        ("skill_check_character_p_stat_max", 61),
        ("skill_item_secured", 62),
        ("skill_invalid_account_attribute", 63),
        ("skill_urk_level", 64),
        ("skill_urk_ability", 65),
        ("skill_urk_race", 66),
        ("skill_urk_gender", 67),
        ("skill_urk_equip_slot", 68),
        ("skill_urk_equip_item", 69),
        ("skill_urk_own_item", 70),
        ("skill_urk_trained_skill", 71),
        ("skill_urk_combat", 72),
        ("skill_urk_stealth", 73),
        ("skill_urk_health", 74),
        ("skill_urk_buff", 75),
        ("skill_urk_target_buff", 76),
        ("skill_urk_target_combat", 77),
        ("skill_urk_can_learn_craft", 78),
        ("skill_urk_doodad_range", 79),
        ("skill_urk_equip_shield", 80),
        ("skill_urk_nobuff", 81),
        ("skill_urk_target_buff_tag", 82),
        ("skill_urk_corpse_range", 83),
        ("skill_urk_equip_weapon_type", 84),
        ("skill_urk_target_health_less_than", 85),
        ("skill_urk_target_npc", 86),
        ("skill_urk_target_doodad", 87),
        ("skill_urk_equip_ranged", 88),
        ("skill_urk_no_buff_tag", 89),
        ("skill_urk_complete_quest_context", 90),
        ("skill_urk_progress_quest_context", 91),
        ("skill_urk_ready_quest_context", 92),
        ("skill_urk_target_npc_group", 93),
        ("skill_urk_area_sphere", 94),
        ("skill_urk_except_complete_quest_context", 95),
        ("skill_urk_precomplete_quest_context", 96),
        ("skill_urk_target_owner_type", 97),
        ("skill_urk_not_under_water", 98),
        ("skill_urk_faction_match", 99),
        ("skill_urk_tod", 100),
        ("skill_urk_mother_faction", 101),
        ("skill_urk_actability_point", 102),
        ("skill_urk_crime_point", 103),
        ("skill_urk_honor_point", 104),
        ("skill_urk_living_point", 105),
        ("skill_urk_crime_record", 106),
        ("skill_urk_jury_point", 107),
        ("skill_urk_source_owner_type", 108),
        ("skill_urk_appelation", 109),
        ("skill_urk_in_zone", 110),
        ("skill_urk_out_zone", 111),
        ("skill_urk_dominion_owner", 112),
        ("skill_urk_verdict_only", 113),
        ("skill_urk_faction_match_only", 114),
        ("skill_urk_mother_faction_only", 115),
        ("skill_urk_nation_owner", 116),
        ("skill_urk_faction_match_only_not", 117),
        ("skill_urk_mother_faction_only_not", 118),
        ("skill_urk_nation_member", 119),
        ("skill_urk_nation_member_not", 120),
        ("skill_urk_nation_owner_at_pos", 121),
        ("skill_urk_dominion_owner_at_pos", 122),
        ("skill_urk_housing", 123),
        ("skill_urk_health_margin", 124),
        ("skill_urk_mana_margin", 125),
        ("skill_urk_labor_power_margin", 126),
        ("skill_urk_unknown", 127),
        ("skill_urk_max_level", 128),
        ("skill_urk_expedition_owner", 129),
        ("skill_urk_expedition_member", 130),
    ];
    TABLE.iter().find(|(k, _)| *k == key).map_or(1, |(_, v)| *v)
}

fn ret(key: &'static str, success: bool) -> ReqResult {
    if success {
        ReqResult::OK
    } else {
        ReqResult { key, value: 0 }
    }
}

fn ret_value(key: &'static str, value: u32, success: bool) -> ReqResult {
    if success {
        ReqResult::OK
    } else {
        ReqResult { key, value }
    }
}

/// `UnitReqs.Validate(owner, target)`.
pub fn validate(
    server: &GameServer,
    req: &UnitReq,
    owner: &WorldObject,
    target: Option<&WorldObject>,
) -> ReqResult {
    let unit = owner.is_unit().then_some(owner);
    let target_unit = target.filter(|t| t.is_unit());
    let player = owner.as_player();
    let (v1, v2) = (req.value1, req.value2);
    let items = server.systems.skills.ext.items();
    let quests = server.systems.skills.ext.quests();
    match req.kind {
        0x01 => ret(
            "skill_urk_level",
            unit.and_then(|u| u.with_unit(|x| x.unit().level))
                .is_some_and(|l| u32::from(l) >= v1 && (v2 == 0 || u32::from(l) <= v2)),
        ),
        0x02 => ret(
            "skill_urk_ability",
            player.is_some_and(|p| {
                let c = p.data();
                crate::db_character::ab_level(server, &c, v1 as u8) >= v2 as i32
            }),
        ),
        0x03 => ret(
            "skill_urk_race",
            player.is_some_and(|p| u32::from(p.data().race) == v1),
        ),
        0x04 => ret(
            "skill_urk_gender",
            player.is_some_and(|p| u32::from(p.data().gender) == v1),
        ),
        0x08 => ret(
            "skill_urk_equip_slot",
            player.is_some_and(|p| items.has_equipped(server, p, v1 as i32)),
        ),
        0x09 => ret(
            "skill_urk_equip_item",
            player.is_some_and(|p| items.item_count(server, p, v1) > 0),
        ),
        0x0A => ret_value(
            "skill_urk_own_item",
            v1,
            player.is_some_and(|p| items.item_count(server, p, v1) > 0),
        ),
        0x0B => ret(
            "skill_urk_trained_skill",
            player.is_some_and(|p| p.data().skills.skills.iter().any(|s| s.id == v1)),
        ),
        0x0C => ret(
            "skill_urk_combat",
            unit.and_then(|u| u.with_unit(|x| x.unit().is_in_battle)) == Some(false),
        ),
        0x0F => ret_value(
            "skill_urk_buff",
            v1,
            unit.is_some_and(|u| u.with_base_unit(|b| b.buffs().check_buff(v1))),
        ),
        0x10 => ret_value(
            "skill_urk_target_buff",
            v1,
            target_unit.is_some_and(|t| t.with_base_unit(|b| b.buffs().check_buff(v1))),
        ),
        0x11 => ret(
            "skill_urk_target_combat",
            target_unit.and_then(|t| t.with_unit(|x| x.unit().is_in_battle)) == Some(false),
        ),
        0x14 => {
            let range = v2 as f32 / 1000.0;
            let o = super::geometry::Target::Obj(owner.clone());
            let found = super::geometry::around_radius(server, &o, range * 2.0, true)
                .into_iter()
                .filter(|d| d.as_doodad().is_some())
                .any(|d| {
                    super::geometry::distance_to(
                        server,
                        &o,
                        &super::geometry::Target::Obj(d.clone()),
                        true,
                    ) <= range
                        && d.with_base_unit(|b| b.template_id()) == v1
                });
            ret_value("skill_urk_doodad_range", v1, found)
        }
        0x15 => ret(
            "skill_urk_equip_shield",
            unit.is_some_and(|u| {
                items
                    .weapon_holdable(server, u, super::stats::equip_slot::OFFHAND)
                    .is_some_and(|h| h.slot_type_id == super::stats::slot_type::SHIELD)
            }),
        ),
        0x16 => ret_value(
            "skill_urk_nobuff",
            v1,
            unit.is_some_and(|u| !u.with_base_unit(|b| b.buffs().check_buff(v1))),
        ),
        0x17 => {
            let t = target_unit.or(unit);
            ret_value(
                "skill_urk_target_buff_tag",
                v1,
                t.is_some_and(|t| super::buffs::check_buff_tag(server, t, v1)),
            )
        }
        0x1A => ret(
            "skill_urk_target_health_less_than",
            target_unit
                .and_then(|t| t.with_unit(|x| x.unit().hpp()))
                .is_some_and(|h| h >= v1 as i32 && h <= v2 as i32),
        ),
        0x1B => ret_value(
            "skill_urk_target_npc",
            v1,
            target_unit.is_some_and(|t| {
                t.as_npc().is_some() && t.with_base_unit(|b| b.template_id()) == v1
            }),
        ),
        0x1C => {
            let current = unit
                .and_then(|u| u.with_unit(|x| x.unit().current_target))
                .unwrap_or(0);
            let doodad = server
                .world
                .object(current)
                .filter(|o| o.as_doodad().is_some())
                .or_else(|| {
                    unit.and_then(|u| {
                        super::geometry::around_radius(
                            server,
                            &super::geometry::Target::Obj(u.clone()),
                            5.0,
                            true,
                        )
                        .into_iter()
                        .find(|d| {
                            d.as_doodad().is_some() && d.with_base_unit(|b| b.template_id()) == v1
                        })
                    })
                });
            ret(
                "skill_urk_target_doodad",
                doodad.is_some_and(|d| d.with_base_unit(|b| b.template_id()) == v1),
            )
        }
        0x1D => ret(
            "skill_urk_equip_ranged",
            player.is_some_and(|p| {
                items.has_equipped(
                    server,
                    p,
                    if v1 == 1 {
                        super::stats::equip_slot::MUSICAL
                    } else {
                        super::stats::equip_slot::RANGED
                    },
                )
            }),
        ),
        0x1E => ret(
            "skill_urk_no_buff_tag",
            unit.is_some_and(|u| !super::buffs::check_buff_tag(server, u, v1)),
        ),
        0x1F => ret_value(
            "skill_urk_complete_quest_context",
            v1,
            player.is_some_and(|p| quests.quest_completed(server, p, v1)),
        ),
        0x20 => ret_value(
            "skill_urk_progress_quest_context",
            v1,
            player.is_some_and(|p| quests.quest_step(server, p, v1) == Some(QUEST_STEP_PROGRESS)),
        ),
        0x21 => ret_value(
            "skill_urk_ready_quest_context",
            v1,
            player.is_some_and(|p| quests.quest_step(server, p, v1) == Some(QUEST_STEP_READY)),
        ),
        0x22 => ret(
            "skill_urk_target_npc_group",
            target_unit.is_some_and(|t| t.as_npc().is_some()),
        ),
        0x23 => {
            let p =
                super::geometry::world_pos(server, &super::geometry::Target::Obj(owner.clone()));
            ret_value(
                "skill_urk_area_sphere",
                v1,
                quests.inside_area_sphere(server, v1, v2, (p.x, p.y, p.z)),
            )
        }
        0x24 => ret(
            "skill_failure",
            player.is_some_and(|p| !quests.quest_completed(server, p, v1)),
        ),
        0x25 => ret_value(
            "skill_urk_precomplete_quest_context",
            v1,
            player.is_some_and(|p| {
                !quests.quest_completed(server, p, v1) && quests.quest_step(server, p, v1).is_some()
            }),
        ),
        0x26 => ret(
            "skill_urk_target_owner_type",
            target_unit
                .and_then(|t| t.with_unit(|x| x.base_unit_type() as u8))
                .is_some_and(|k| u32::from(k) == v1),
        ),
        0x27 => ret(
            "skill_urk_not_under_water",
            unit.and_then(|u| u.with_unit(|x| x.unit().is_under_water)) == Some(false),
        ),
        0x28 | 0x37 => ret_value(
            if req.kind == 0x28 {
                "skill_urk_faction_match"
            } else {
                "skill_urk_faction_match_only"
            },
            v1,
            unit.map_or(0, |u| u.with_base_unit(|b| b.faction_id())) == v1,
        ),
        0x29 => {
            let t = (f64::from(server.time.get_time()) * 100.0).floor() as u32;
            ret("skill_urk_tod", t >= v1 && t <= v2)
        }
        0x2A | 0x38 => {
            let mother = unit
                .map(|u| u.with_base_unit(|b| b.faction_id()))
                .and_then(|f| server.game_data.factions.get(f).map(|x| x.mother_id))
                .unwrap_or(0);
            ret(
                if req.kind == 0x2A {
                    "skill_urk_mother_faction"
                } else {
                    "skill_urk_mother_faction_only"
                },
                mother == v1,
            )
        }
        0x2B => ret_value(
            "skill_urk_actability_point",
            v1,
            player.is_some_and(|p| {
                p.data()
                    .actability
                    .actabilities
                    .iter()
                    .find(|a| a.id == v1)
                    .is_some_and(|a| a.point >= v2 as i32)
            }),
        ),
        0x2C | 0x2E | 0x44 => ret("skill_failure", true),
        0x2D => ret(
            "skill_urk_honor_point",
            player.is_some_and(|p| {
                let h = p.data().honor_point;
                h >= v1 as i32 && h <= v2 as i32
            }),
        ),
        0x2F => ret(
            "skill_failure",
            player.is_some_and(|p| p.data().jury_point >= v1 as i32),
        ),
        0x30 => ret(
            "skill_failure",
            unit.and_then(|u| u.with_unit(|x| x.base_unit_type() as u8))
                .is_some_and(|k| u32::from(k) == v1),
        ),
        0x31 => ret(
            "skill_failure",
            player.is_some_and(|p| p.data().appellations.appellations.contains(&v1)),
        ),
        0x32 => ret(
            "skill_failure",
            player.is_some_and(|p| {
                let v = p.data().vocation_point;
                v >= v1 as i32 && v <= v2 as i32
            }),
        ),
        0x33 | 0x34 => {
            let zone_key = owner.transform().zone_id;
            let zone_id = server.game_data.zones.zone_by_key(zone_key).map(|z| z.id);
            if req.kind == 0x33 {
                ret_value("skill_urk_in_zone", v1, zone_id == Some(v1))
            } else {
                ret_value("skill_urk_out_zone", v1, zone_id != Some(v1))
            }
        }
        0x36 => ret("skill_urk_verdict_only", true),
        0x3A | 0x3B => {
            let f = unit.map_or(0, |u| u.with_base_unit(|b| b.faction_id()));
            if req.kind == 0x3A {
                ret("skill_urk_faction_match_only_not", f != v1)
            } else {
                let mother = server.game_data.factions.get(f).map_or(0, |x| x.mother_id);
                ret("skill_urk_mother_faction_only_not", mother != v1)
            }
        }
        0x43 => {
            // TimedRewardsManager.GetMaxLabor(premium) - LaborPower
            let remaining = player.map_or(0, |p| MAX_LABOR - i32::from(p.data().labor_power));
            ret_value("skill_urk_labor_power_margin", v1, v1 as i32 <= remaining)
        }
        0x45 => ret(
            "skill_urk_max_level",
            player.is_some_and(|p| u32::from(p.data().unit.level) <= v1),
        ),
        0x46 => ret(
            "skill_failure",
            player.is_some_and(|p| {
                let c = p.data();
                c.unit.expedition_id != 0
                    && server
                        .expeditions
                        .get(c.unit.expedition_id)
                        .is_some_and(|e| e.faction.owner_id == c.id)
            }),
        ),
        0x47 => ret(
            "skill_failure",
            player.is_some_and(|p| p.data().unit.expedition_id > 0),
        ),
        0x48 => ret(
            "skill_failure",
            player.is_some_and(|p| {
                !quests.quest_completed(server, p, v1)
                    && quests.quest_step(server, p, v1) != Some(QUEST_STEP_PROGRESS)
            }),
        ),
        0x49 => ret(
            "skill_failure",
            player.is_some_and(|p| {
                !quests.quest_completed(server, p, v1)
                    && quests.quest_step(server, p, v1) != Some(QUEST_STEP_READY)
            }),
        ),
        _ => ReqResult::OK,
    }
}

/// `QuestComponentKind.Progress` / `Ready`.
pub const QUEST_STEP_PROGRESS: u8 = 4;
pub const QUEST_STEP_READY: u8 = 6;

/// `TimedRewardsManager.GetMaxLabor(isPremium: false)` (the premium state lives on the
/// connection, which the skill code does not see: the non-premium cap is used).
const MAX_LABOR: i32 = 2000;

/// `UnitRequirementsGameData.CanUseSkill(skillTemplate, ownerUnit, skillCaster)`.
pub fn can_use_skill(
    server: &GameServer,
    template: &SkillTemplate,
    owner: &WorldObject,
    _skill_caster: &SkillCaster,
) -> ReqResult {
    let reqs: Vec<UnitReq> = server
        .game_data
        .systems
        .skills
        .skill_requirements(template.id)
        .into_iter()
        .cloned()
        .collect();
    if reqs.is_empty() {
        return ReqResult::OK;
    }
    let target = if template.target_type == 0 {
        Some(owner.clone())
    } else {
        let current = owner.with_unit(|u| u.unit().current_target).unwrap_or(0);
        Some(
            (current != 0)
                .then(|| server.world.object(current))
                .flatten()
                .unwrap_or_else(|| owner.clone()),
        )
    };
    let mut res = !template.or_unit_reqs;
    let mut last_failed = ReqResult {
        key: "skill_failure",
        value: 0,
    };
    for req in &reqs {
        let r = validate(server, req, owner, target.as_ref());
        let ok = r.is_ok();
        if !ok {
            last_failed = r;
        }
        if template.or_unit_reqs {
            // The C# stops at the first requirement and passes, whatever its result.
            res = true;
            break;
        }
        res &= ok;
    }
    if res {
        ReqResult::OK
    } else {
        last_failed
    }
}
