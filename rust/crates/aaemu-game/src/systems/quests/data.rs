//! `QuestManager.Load` (quest templates, components, acts, supplies, item / NPC groups),
//! the quest part of `UnitRequirementsGameData` (`unit_reqs`), the quest part of
//! `SphereGameData` (`spheres`, `sphere_quests`) and the `quest_sign_sphere.g` client files of
//! `SphereQuestManager.LoadQuestSpheres`.

use std::collections::HashMap;

use rusqlite::Connection;

use super::model::{kind, QuestSupplies};
use super::templates::{ActKind, ActRef, QuestActTemplate, QuestComponentTemplate, QuestTemplate};
use super::unit_reqs::UnitReqsData;
use crate::data::client_files::ClientFileManager;
use crate::data::sqlite::{for_each_row, SqlRow};
use crate::data::GameDataError;

/// `QuestCategoryTutorial` (quests of this category are not loaded).
pub const QUEST_CATEGORY_TUTORIAL: u32 = 45;

/// A `Dictionary<uint, List<uint>>` that keeps the C# insertion order (`_groupItems`,
/// `_groupNpcs`; their `Where(..).Select(x => x.Key)` iteration order is observable).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct OrderedGroups {
    order: Vec<u32>,
    map: HashMap<u32, Vec<u32>>,
}

impl OrderedGroups {
    pub fn add(&mut self, group: u32, member: u32) {
        self.map
            .entry(group)
            .or_insert_with(|| {
                self.order.push(group);
                Vec::new()
            })
            .push(member);
    }

    /// `GetGroupItems(groupId)` / the group list.
    pub fn members(&self, group: u32) -> &[u32] {
        self.map.get(&group).map_or(&[], Vec::as_slice)
    }

    /// `CheckGroupItem` / `CheckGroupNpc`.
    pub fn contains(&self, group: u32, member: u32) -> bool {
        self.map.get(&group).is_some_and(|m| m.contains(&member))
    }

    /// `_groups.Where(x => x.Value.Contains(member)).Select(x => x.Key)`.
    pub fn groups_of(&self, member: u32) -> Vec<u32> {
        self.order
            .iter()
            .copied()
            .filter(|g| self.map[g].contains(&member))
            .collect()
    }

    pub fn len(&self) -> usize {
        self.order.len()
    }

    pub fn is_empty(&self) -> bool {
        self.order.is_empty()
    }

    /// Total number of (group, member) rows.
    pub fn rows(&self) -> usize {
        self.map.values().map(Vec::len).sum()
    }
}

/// A `quest_acts` row (`_actsBaseByActId`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BaseAct {
    pub id: u32,
    pub component_id: u32,
    pub detail_id: u32,
    pub detail_type: String,
}

/// `Spheres` (`spheres`).
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct DbSphere {
    pub id: u32,
    pub name: String,
    pub enter_or_leave: bool,
    pub sphere_detail_id: u32,
    pub sphere_detail_type: String,
    /// `AreaSphereTriggerCondition`.
    pub trigger_condition_id: u32,
    pub trigger_condition_time: u32,
    pub team_msg: String,
    pub category_id: u32,
    pub or_unit_reqs: bool,
    pub is_personal_msg: bool,
    pub milestone_id: u32,
    pub name_tr: bool,
    pub team_msg_tr: bool,
}

/// `SphereQuests` (`sphere_quests`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct DbSphereQuest {
    pub id: u32,
    pub quest_id: u32,
    /// `QuestTrigger`.
    pub quest_trigger_id: u32,
}

/// One `area` block of a `quest_sign_sphere.g` file, in zone-local coordinates (converted to
/// world coordinates by the sphere manager, which has the world templates).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct RawSignSphere {
    pub zone_id: u32,
    pub quest_id: u32,
    pub component_id: u32,
    pub local: [f32; 3],
    pub radius: f32,
}

/// The quest tables (`server.game_data.systems.quests`).
#[derive(Debug, Default)]
pub struct QuestData {
    /// `_questTemplates`.
    pub templates: HashMap<u32, QuestTemplate>,
    /// `_questTemplates` keys in insertion order (`ORDER BY id`).
    pub template_order: Vec<u32>,
    /// `_supplies` (by level).
    pub supplies: HashMap<u8, QuestSupplies>,
    /// `_componentTemplates`.
    pub components: HashMap<u32, QuestComponentTemplate>,
    /// Every detail act template (`_actTemplatesByDetailType` values).
    pub acts: Vec<QuestActTemplate>,
    /// `_actTemplatesByDetailType[type]` values in insertion order.
    pub acts_by_type: HashMap<&'static str, Vec<ActRef>>,
    act_lookup: HashMap<(&'static str, u32), ActRef>,
    /// `_actsBaseByActId` (quest_acts rows whose component exists), in insertion order.
    pub base_acts: Vec<BaseAct>,
    /// First base act per (detail type, detail id): (act id, component id)
    /// (`GetComponentByActTemplate` / `AddActTemplate`'s `FirstOrDefault`).
    first_base: HashMap<(String, u32), (u32, u32)>,
    /// `_groupItems` (`quest_item_group_items`).
    pub group_items: OrderedGroups,
    /// `_groupNpcs` (`quest_monster_npcs`).
    pub group_npcs: OrderedGroups,
    /// Explicit quest-starter bindings (`item_accept_quests`), item template -> quest.
    pub starter_quests: HashMap<u32, u32>,
    /// `UnitRequirementsGameData`.
    pub unit_reqs: UnitReqsData,
    /// `SphereGameData._spheres`.
    pub spheres: HashMap<u32, DbSphere>,
    /// `SphereGameData._sphereQuests` (+ key order).
    pub sphere_quests: HashMap<u32, DbSphereQuest>,
    pub sphere_quest_order: Vec<u32>,
    /// `spheres` keys in table order (`_spheres.Values` iteration).
    pub sphere_order: Vec<u32>,
    /// `quest_sign_sphere.g` areas of `main_world` (zone-local coordinates).
    pub sign_spheres: Vec<RawSignSphere>,
}

fn err(msg: impl Into<String>) -> GameDataError {
    GameDataError::File(msg.into())
}

impl QuestData {
    /// `QuestManager.Load()` + the quest parts of the game data loaders.
    pub fn load_from(conn: &Connection, files: &ClientFileManager) -> Result<Self, GameDataError> {
        let mut d = QuestData::default();
        d.load_supplies(conn)?;
        d.load_contexts(conn)?;
        d.load_components(conn)?;
        d.load_base_acts(conn)?;
        d.load_detail_acts(conn)?;
        d.load_item_starters(conn)?;
        for_each_row(conn, "SELECT * FROM quest_item_group_items", [], |r| {
            d.group_items
                .add(r.u32("quest_item_group_id")?, r.u32("item_id")?);
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM quest_monster_npcs", [], |r| {
            d.group_npcs
                .add(r.u32("quest_monster_group_id")?, r.u32("npc_id")?);
            Ok(())
        })?;
        d.update_quest_component_acts();
        d.unit_reqs = UnitReqsData::load(conn)?;
        d.load_spheres(conn)?;
        d.sign_spheres = load_sign_spheres(files, "main_world")?;
        tracing::info!("Loaded {} quests", d.templates.len());
        Ok(d)
    }

    fn load_item_starters(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        // 10.x zone 0x39AF5840 loads this relation directly; the same table is in 1.2.
        for_each_row(
            conn,
            "SELECT item_id, quest_id FROM item_accept_quests",
            [],
            |r| {
                let item = r.u32("item_id")?;
                if self
                    .starter_quests
                    .insert(item, r.u32("quest_id")?)
                    .is_some()
                {
                    return Err(rusqlite::Error::InvalidParameterName(format!(
                        "duplicate quest starter item {item}"
                    )));
                }
                Ok(())
            },
        )?;
        Ok(())
    }

    fn load_supplies(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        let mut dup = None;
        for_each_row(conn, "SELECT * FROM quest_supplies", [], |r| {
            let t = QuestSupplies {
                id: r.u32("id")?,
                level: r.u8("level")?,
                exp: r.i32("exp")?,
                copper: r.i32("copper")?,
            };
            if self.supplies.insert(t.level, t).is_some() {
                dup = Some(t.level);
            }
            Ok(())
        })?;
        match dup {
            Some(l) => Err(err(format!("quest_supplies: duplicate level {l}"))),
            None => Ok(()),
        }
    }

    fn load_contexts(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        let mut dup = None;
        for_each_row(conn, "SELECT * FROM quest_contexts ORDER BY id", [], |r| {
            let t = QuestTemplate {
                id: r.u32("id")?,
                repeatable: r.bool_str("repeatable")?,
                level: r.u8_or("level", 0)?,
                selective: r.bool_str("selective")?,
                successive: r.bool_str("successive")?,
                restart_on_fail: r.bool_str("restart_on_fail")?,
                chapter_idx: r.u32_or("chapter_idx", 0)?,
                quest_idx: r.u32_or("quest_idx", 0)?,
                milestone_id: r.u32_or("milestone_id", 0)?,
                let_it_done: r.bool_str("let_it_done")?,
                detail_id: r.u32("detail_id")?,
                zone_id: r.u32("zone_id")?,
                category_id: r.u32("category_id")?,
                degree: r.i32_or("degree", 0)?,
                use_quest_camera: r.bool_str("use_quest_camera")?,
                score: r.i32_or("score", 0)?,
                use_accept_message: r.bool_str("use_accept_message")?,
                use_complete_message: r.bool_str("use_complete_message")?,
                grade_id: r.u32_or("grade_id", 0)?,
                components: Vec::new(),
            };
            // Skip "loading" tutorial quests
            if t.category_id != QUEST_CATEGORY_TUTORIAL {
                let id = t.id;
                if self.templates.insert(id, t).is_some() {
                    dup = Some(id);
                } else {
                    self.template_order.push(id);
                }
            }
            Ok(())
        })?;
        match dup {
            Some(id) => Err(err(format!("quest_contexts: duplicate id {id}"))),
            None => Ok(()),
        }
    }

    fn load_components(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        let mut dup = None;
        for_each_row(
            conn,
            "SELECT * FROM quest_components ORDER BY quest_context_id, component_kind_id, id",
            [],
            |r| {
                let quest_id = r.u32("quest_context_id")?;
                let Some(quest) = self.templates.get_mut(&quest_id) else {
                    return Ok(());
                };
                let t = QuestComponentTemplate {
                    id: r.u32("id")?,
                    kind_id: r.u8("component_kind_id")?,
                    next_component: r.u32_or("next_component", 0)?,
                    npc_ai_id: r.u32_or("npc_ai_id", 0)?,
                    npc_id: r.u32_or("npc_id", 0)?,
                    skill_id: r.u32_or("skill_id", 0)?,
                    skill_self: r.bool_str("skill_self")?,
                    ai_path_name: r.string_or("ai_path_name", "")?,
                    ai_path_type_id: r.u32("ai_path_type_id")?,
                    npc_spawner_id: r.u32_or("npc_spawner_id", 0)?,
                    play_cinema_before_bubble: r.bool_str("play_cinema_before_bubble")?,
                    ai_command_set_id: r.u32_or("ai_command_set_id", 0)?,
                    or_unit_reqs: r.bool_str("or_unit_reqs")?,
                    cinema_id: r.u32_or("cinema_id", 0)?,
                    buff_id: r.u32_or("buff_id", 0)?,
                    quest_id,
                    acts: Vec::new(),
                };
                let id = t.id;
                if self.components.contains_key(&id) || quest.components.contains(&id) {
                    dup = Some(id);
                    return Ok(());
                }
                quest.components.push(id);
                self.components.insert(id, t);
                Ok(())
            },
        )?;
        match dup {
            Some(id) => Err(err(format!("quest_components: duplicate id {id}"))),
            None => Ok(()),
        }
    }

    fn load_base_acts(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        let mut seen = std::collections::HashSet::new();
        let mut dup = None;
        for_each_row(
            conn,
            "SELECT * FROM quest_acts ORDER BY quest_component_id, id",
            [],
            |r| {
                let component_id = r.u32("quest_component_id")?;
                let id = r.u32("id")?;
                if !self.components.contains_key(&component_id) {
                    tracing::trace!(
                        "LoadQuestActs: ActId {id} references quest ComponentId {component_id} that does not exist"
                    );
                    return Ok(());
                }
                let act = BaseAct {
                    id,
                    component_id,
                    detail_id: r.u32("act_detail_id")?,
                    detail_type: r.string("act_detail_type")?,
                };
                if !seen.insert(id) {
                    dup = Some(id);
                    return Ok(());
                }
                self.first_base
                    .entry((act.detail_type.clone(), act.detail_id))
                    .or_insert((act.id, act.component_id));
                self.base_acts.push(act);
                Ok(())
            },
        )?;
        match dup {
            Some(id) => Err(err(format!("quest_acts: duplicate id {id}"))),
            None => Ok(()),
        }
    }

    /// `AddActTemplate`: links the template to the component of the first base act with its
    /// detail type / id (`GetComponentByActTemplate`) and registers it by type.
    fn link_act(
        &mut self,
        class: &'static str,
        detail_id: u32,
        count: i32,
        kind_data: ActKind,
    ) -> Result<(), GameDataError> {
        let Some(&(act_id, component_id)) = self.first_base.get(&(class.to_string(), detail_id))
        else {
            tracing::trace!(
                "GetComponentByActTemplate no Component found that holds {class} {detail_id}"
            );
            return Ok(());
        };
        if self.act_lookup.contains_key(&(class, detail_id)) {
            // Dictionary.Add throws on a duplicate detail id
            return Err(err(format!("{class}: duplicate detail id {detail_id}")));
        }
        let component = self
            .components
            .get_mut(&component_id)
            .expect("base acts only reference loaded components");
        let idx = self.acts.len();
        self.acts.push(QuestActTemplate {
            act_id,
            detail_id,
            component_id,
            component_kind: component.kind_id,
            quest_id: component.quest_id,
            count,
            objective_index: 0xFF,
            selective_index: 0,
            kind: kind_data,
        });
        component.acts.push(idx);
        self.acts_by_type.entry(class).or_default().push(idx);
        self.act_lookup.insert((class, detail_id), idx);
        Ok(())
    }

    /// Runs one `quest_act_xxx` table (`SELECT *`, table order).
    fn load_table(
        &mut self,
        conn: &Connection,
        table: &str,
        class: &'static str,
        mut read: impl FnMut(&SqlRow<'_, '_>) -> rusqlite::Result<(i32, ActKind)>,
    ) -> Result<(), GameDataError> {
        let mut rows = Vec::new();
        for_each_row(conn, &format!("SELECT * FROM {table}"), [], |r| {
            let id = r.u32("id")?;
            // Only rows whose component exists are read further (`parentComponent == null`
            // -> continue happens before the other columns are read).
            if !self.first_base.contains_key(&(class.to_string(), id)) {
                tracing::trace!(
                    "GetComponentByActTemplate no Component found that holds {class} {id}"
                );
                return Ok(());
            }
            let (count, k) = read(r)?;
            rows.push((id, count, k));
            Ok(())
        })?;
        for (id, count, k) in rows {
            self.link_act(class, id, count, k)?;
        }
        Ok(())
    }

    /// `LoadDetailQuestActTemplates` (the C# table order decides the act order inside a
    /// component, hence the objective indices).
    fn load_detail_acts(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        use ActKind::*;
        self.load_table(
            conn,
            "quest_act_check_complete_components",
            "QuestActCheckCompleteComponent",
            |r| {
                Ok((
                    0,
                    CheckCompleteComponent {
                        complete_component: r.u32("complete_component")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_check_distances",
            "QuestActCheckDistance",
            |r| {
                Ok((
                    0,
                    CheckDistance {
                        within: r.bool_str("within")?,
                        npc_id: r.u32("npc_id")?,
                        distance: r.i32("distance")?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_check_guards", "QuestActCheckGuard", |r| {
            Ok((
                0,
                CheckGuard {
                    npc_id: r.u32("npc_id")?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_check_spheres",
            "QuestActCheckSphere",
            |r| {
                Ok((
                    0,
                    CheckSphere {
                        sphere_id: r.u32("sphere_id")?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_check_timers", "QuestActCheckTimer", |r| {
            Ok((
                0,
                CheckTimer {
                    limit_time: r.i32("limit_time")?,
                    force_change_component: r.bool_str("force_change_component")?,
                    next_component: r.u32("next_component")?,
                    play_skill: r.bool_str("play_skill")?,
                    skill_id: r.u32_or("skill_id", 0)?,
                    check_buff: r.bool_str("check_buf")?,
                    buff_id: r.u32_or("buff_id", 0)?,
                    sustain_buff: r.bool_str("sustain_buf")?,
                    timer_npc_id: r.u32_or("timer_npc_id", 0)?,
                    is_skill_player: r.bool_str("is_skill_player")?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_con_accept_buffs",
            "QuestActConAcceptBuff",
            |r| {
                Ok((
                    0,
                    ConAcceptBuff {
                        buff_id: r.u32("buff_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_components",
            "QuestActConAcceptComponent",
            |r| {
                Ok((
                    0,
                    ConAcceptComponent {
                        quest_context_id: r.u32("quest_context_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_doodads",
            "QuestActConAcceptDoodad",
            |r| {
                Ok((
                    0,
                    ConAcceptDoodad {
                        doodad_id: r.u32("doodad_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_item_equips",
            "QuestActConAcceptItemEquip",
            |r| {
                Ok((
                    0,
                    ConAcceptItemEquip {
                        item_id: r.u32("item_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_item_gains",
            "QuestActConAcceptItemGain",
            |r| {
                Ok((
                    r.i32("count")?,
                    ConAcceptItemGain {
                        item_id: r.u32("item_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_items",
            "QuestActConAcceptItem",
            |r| {
                Ok((
                    0,
                    ConAcceptItem {
                        item_id: r.u32("item_id")?,
                        cleanup: r.bool_str("cleanup")?,
                        drop_when_destroy: r.bool_str("drop_when_destroy")?,
                        destroy_when_drop: r.bool_str("destroy_when_drop")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_level_ups",
            "QuestActConAcceptLevelUp",
            |r| {
                Ok((
                    0,
                    ConAcceptLevelUp {
                        level: r.u8("level")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_npc_emotions",
            "QuestActConAcceptNpcEmotion",
            |r| {
                Ok((
                    0,
                    ConAcceptNpcEmotion {
                        npc_id: r.u32("npc_id")?,
                        emotion: r.string("emotion")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_npc_kills",
            "QuestActConAcceptNpcKill",
            |r| {
                Ok((
                    0,
                    ConAcceptNpcKill {
                        npc_id: r.u32("npc_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_npcs",
            "QuestActConAcceptNpc",
            |r| {
                Ok((
                    0,
                    ConAcceptNpc {
                        npc_id: r.u32("npc_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_skills",
            "QuestActConAcceptSkill",
            |r| {
                Ok((
                    0,
                    ConAcceptSkill {
                        skill_id: r.u32("skill_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_accept_spheres",
            "QuestActConAcceptSphere",
            |r| {
                Ok((
                    0,
                    ConAcceptSphere {
                        sphere_id: r.u32("sphere_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_auto_completes",
            "QuestActConAutoComplete",
            |_| Ok((0, ConAutoComplete)),
        )?;
        self.load_table(conn, "quest_act_con_fails", "QuestActConFail", |r| {
            Ok((
                0,
                ConFail {
                    force_change_component: r.bool_str("force_change_component")?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_con_report_doodads",
            "QuestActConReportDoodad",
            |r| {
                Ok((
                    0,
                    ConReportDoodad {
                        doodad_id: r.u32("doodad_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32("quest_act_obj_alias_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_con_report_journals",
            "QuestActConReportJournal",
            |_| Ok((0, ConReportJournal)),
        )?;
        self.load_table(
            conn,
            "quest_act_con_report_npcs",
            "QuestActConReportNpc",
            |r| {
                Ok((
                    0,
                    ConReportNpc {
                        npc_id: r.u32("npc_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_etc_item_obtains",
            "QuestActEtcItemObtain",
            |r| {
                Ok((
                    r.i32("count")?,
                    EtcItemObtain {
                        item_id: r.u32("item_id")?,
                        highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                        cleanup: r.bool_str("cleanup")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_ability_levels",
            "QuestActObjAbilityLevel",
            |r| {
                Ok((
                    0,
                    ObjAbilityLevel {
                        ability_id: r.u8("ability_id")?,
                        level: r.u8("level")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_obj_aggros", "QuestActObjAggro", |r| {
            // `Count => MaxRankObjective` (3)
            Ok((
                3,
                ObjAggro {
                    range: r.i32("range")?,
                    rank1: r.i32_or("rank1", 0)?,
                    rank1_ratio: r.i32_or("rank1_ratio", 0)?,
                    rank1_item: r.bool_str("rank1_item")?,
                    rank2: r.i32_or("rank2", 0)?,
                    rank2_ratio: r.i32_or("rank2_ratio", 0)?,
                    rank2_item: r.bool_str("rank2_item")?,
                    rank3: r.i32_or("rank3", 0)?,
                    rank3_ratio: r.i32_or("rank3_ratio", 0)?,
                    rank3_item: r.bool_str("rank3_item")?,
                    use_alias: r.bool_str("use_alias")?,
                    alias_id: r.u32("quest_act_obj_alias_id")?,
                },
            ))
        })?;
        self.load_table(conn, "quest_act_obj_cinemas", "QuestActObjCinema", |r| {
            Ok((
                0,
                ObjCinema {
                    cinema_id: r.u32("cinema_id")?,
                    use_alias: r.bool_str("use_alias")?,
                    alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_obj_complete_quests",
            "QuestActObjCompleteQuest",
            |r| {
                Ok((
                    0,
                    ObjCompleteQuest {
                        quest_id: r.u32("quest_id")?,
                        accept_with: r.bool_str("accept_with")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_conditions",
            "QuestActObjCondition",
            |r| {
                Ok((
                    0,
                    ObjCondition {
                        condition_id: r.u32("condition_id")?,
                        quest_context_id: r.u32("quest_context_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_obj_crafts", "QuestActObjCraft", |r| {
            Ok((
                r.i32("count")?,
                ObjCraft {
                    craft_id: r.u32("craft_id")?,
                    use_alias: r.bool_str("use_alias")?,
                    alias_id: r.u32("quest_act_obj_alias_id")?,
                    highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                    highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_obj_distances",
            "QuestActObjDistance",
            |r| {
                Ok((
                    0,
                    ObjDistance {
                        within: r.bool_str("within")?,
                        npc_id: r.u32("npc_id")?,
                        distance: r.i32("distance")?,
                        highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_doodad_phase_checks",
            "QuestActObjDoodadPhaseCheck",
            |r| {
                Ok((
                    0,
                    ObjDoodadPhaseCheck {
                        doodad_id: r.u32("doodad_id")?,
                        phase1: r.u32_or("phase1", 0)?,
                        phase2: r.u32_or("phase2", 0)?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_effect_fires",
            "QuestActObjEffectFire",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjEffectFire {
                        effect_id: r.u32("effect_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_express_fires",
            "QuestActObjExpressFire",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjExpressFire {
                        express_key_id: r.u32("express_key_id")?,
                        npc_group_id: r.u32("npc_group_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_interactions",
            "QuestActObjInteraction",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjInteraction {
                        wi_id: r.i32("wi_id")?,
                        doodad_id: r.u32_or("doodad_id", 0)?,
                        use_alias: r.bool_str("use_alias")?,
                        team_share: r.bool_str("team_share")?,
                        highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                        highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                        phase: r.u32_or("phase", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_item_gathers",
            "QuestActObjItemGather",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjItemGather {
                        item_id: r.u32("item_id")?,
                        highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                        highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                        cleanup: r.bool_str("cleanup")?,
                        drop_when_destroy: r.bool_str("drop_when_destroy")?,
                        destroy_when_drop: r.bool_str("destroy_when_drop")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_item_group_gathers",
            "QuestActObjItemGroupGather",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjItemGroupGather {
                        item_group_id: r.u32("item_group_id")?,
                        cleanup: r.bool_str("cleanup")?,
                        highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                        highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                        drop_when_destroy: r.bool_str("drop_when_destroy")?,
                        destroy_when_drop: r.bool_str("destroy_when_drop")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_item_group_uses",
            "QuestActObjItemGroupUse",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjItemGroupUse {
                        item_group_id: r.u32("item_group_id")?,
                        highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                        highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                        drop_when_destroy: r.bool_str("drop_when_destroy")?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_obj_item_uses", "QuestActObjItemUse", |r| {
            Ok((
                r.i32("count")?,
                ObjItemUse {
                    item_id: r.u32("item_id")?,
                    highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                    highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                    use_alias: r.bool_str("use_alias")?,
                    alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    drop_when_destroy: r.bool_str("drop_when_destroy")?,
                },
            ))
        })?;
        self.load_table(conn, "quest_act_obj_levels", "QuestActObjLevel", |r| {
            Ok((
                0,
                ObjLevel {
                    level: r.u8("level")?,
                    use_alias: r.bool_str("use_alias")?,
                    alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_obj_mate_levels",
            "QuestActObjMateLevel",
            |r| {
                Ok((
                    0,
                    ObjMateLevel {
                        item_id: r.u32("item_id")?,
                        level: r.u8("level")?,
                        cleanup: r.bool_str("cleanup")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_monster_group_hunts",
            "QuestActObjMonsterGroupHunt",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjMonsterGroupHunt {
                        quest_monster_group_id: r.u32("quest_monster_group_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                        highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                        highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_monster_hunts",
            "QuestActObjMonsterHunt",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjMonsterHunt {
                        npc_id: r.u32("npc_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                        highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                        highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_send_mails",
            "QuestActObjSendMail",
            |r| {
                Ok((
                    0,
                    ObjSendMail {
                        item_id1: r.u32_or("item1_id", 0)?,
                        count1: r.i32("count1")?,
                        item_id2: r.u32_or("item2_id", 0)?,
                        count2: r.i32("count2")?,
                        item_id3: r.u32_or("item3_id", 0)?,
                        count3: r.i32("count3")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_obj_spheres", "QuestActObjSphere", |r| {
            Ok((
                0,
                ObjSphere {
                    sphere_id: r.u32("sphere_id")?,
                    npc_id: r.u32_or("npc_id", 0)?,
                    highlight_doodad_id: r.u32_or("highlight_doodad_id", 0)?,
                    highlight_doodad_phase: r.i32_or("highlight_doodad_phase", -1)?,
                    use_alias: r.bool_str("use_alias")?,
                    alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_obj_talk_npc_groups",
            "QuestActObjTalkNpcGroup",
            |r| {
                Ok((
                    0,
                    ObjTalkNpcGroup {
                        npc_group_id: r.u32("npc_group_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_obj_talks", "QuestActObjTalk", |r| {
            Ok((
                0,
                ObjTalk {
                    npc_id: r.u32("npc_id")?,
                    team_share: r.bool_str("team_share")?,
                    item_id: r.u32_or("item_id", 0)?,
                    use_alias: r.bool_str("use_alias")?,
                    alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_obj_zone_kills",
            "QuestActObjZoneKill",
            |r| {
                let count_player_kill = r.i32("count_pk")?;
                let count_npc = r.i32("count_npc")?;
                // Exception since we have 2 possible values here
                Ok((
                    count_npc.max(count_player_kill),
                    ObjZoneKill {
                        count_player_kill,
                        count_npc,
                        zone_id: r.u32_or("zone_id", 0)?,
                        team_share: r.bool_str("team_share")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                        lvl_min: r.i32("lv_min")?,
                        lvl_max: r.i32("lv_max")?,
                        is_party: r.bool_str("is_party")?,
                        lvl_min_npc: r.i32("lv_min_npc")?,
                        lvl_max_npc: r.i32("lv_max_npc")?,
                        pc_faction_id: r.u32_or("pc_faction_id", 0)?,
                        pc_faction_exclusive: r.bool_str("pc_faction_exclusive")?,
                        npc_faction_id: r.u32_or("npc_faction_id", 0)?,
                        npc_faction_exclusive: r.bool_str("npc_faction_exclusive")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_zone_monster_hunts",
            "QuestActObjZoneMonsterHunt",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjZoneMonsterHunt {
                        zone_id: r.u32("zone_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_zone_npc_talks",
            "QuestActObjZoneNpcTalk",
            |r| {
                Ok((
                    0,
                    ObjZoneNpcTalk {
                        npc_id: r.u32("npc_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_obj_zone_quest_completes",
            "QuestActObjZoneQuestComplete",
            |r| {
                Ok((
                    r.i32("count")?,
                    ObjZoneQuestComplete {
                        zone_id: r.u32("zone_id")?,
                        use_alias: r.bool_str("use_alias")?,
                        alias_id: r.u32_or("quest_act_obj_alias_id", 0)?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_supply_aa_points",
            "QuestActSupplyAaPoint",
            |r| {
                Ok((
                    0,
                    SupplyAaPoint {
                        point: r.i32("point")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_supply_appellations",
            "QuestActSupplyAppellation",
            |r| {
                Ok((
                    0,
                    SupplyAppellation {
                        appellation_id: r.u32("appellation_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_supply_coppers",
            "QuestActSupplyCopper",
            |r| {
                Ok((
                    0,
                    SupplyCopper {
                        amount: r.i32("amount")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_supply_crime_points",
            "QuestActSupplyCrimePoint",
            |r| {
                Ok((
                    0,
                    SupplyCrimePoint {
                        point: r.i16("point")?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_supply_exps", "QuestActSupplyExp", |r| {
            Ok((0, SupplyExp { exp: r.i32("exp")? }))
        })?;
        self.load_table(
            conn,
            "quest_act_supply_honor_points",
            "QuestActSupplyHonorPoint",
            |r| {
                Ok((
                    0,
                    SupplyHonorPoint {
                        point: r.i32("point")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_supply_interactions",
            "QuestActSupplyInteraction",
            |r| {
                Ok((
                    0,
                    SupplyInteraction {
                        wi_id: r.u32("wi_id")?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_supply_items", "QuestActSupplyItem", |r| {
            Ok((
                r.i32("count")?,
                SupplyItem {
                    item_id: r.u32("item_id")?,
                    grade_id: r.u8("grade_id")?,
                    show_action_bar: r.bool_str("show_action_bar")?,
                    cleanup: r.bool_str("cleanup")?,
                    drop_when_destroy: r.bool_str("drop_when_destroy")?,
                    destroy_when_drop: r.bool_str("destroy_when_drop")?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_supply_jury_points",
            "QuestActSupplyJuryPoint",
            |r| {
                Ok((
                    0,
                    SupplyJuryPoint {
                        point: r.i32("point")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_supply_living_points",
            "QuestActSupplyLivingPoint",
            |r| {
                Ok((
                    0,
                    SupplyLivingPoint {
                        point: r.i32("point")?,
                    },
                ))
            },
        )?;
        self.load_table(conn, "quest_act_supply_lps", "QuestActSupplyLp", |r| {
            Ok((
                0,
                SupplyLp {
                    labor_power: r.i32("lp")?,
                },
            ))
        })?;
        self.load_table(
            conn,
            "quest_act_supply_remove_items",
            "QuestActSupplyRemoveItem",
            |r| {
                Ok((
                    r.i32("count")?,
                    SupplyRemoveItem {
                        item_id: r.u32("item_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_supply_selective_items",
            "QuestActSupplySelectiveItem",
            |r| {
                Ok((
                    r.i32("count")?,
                    SupplySelectiveItem {
                        item_id: r.u32("item_id")?,
                        grade_id: r.u8("grade_id")?,
                    },
                ))
            },
        )?;
        self.load_table(
            conn,
            "quest_act_supply_skills",
            "QuestActSupplySkill",
            |r| {
                Ok((
                    0,
                    SupplySkill {
                        skill_id: r.u32("skill_id")?,
                    },
                ))
            },
        )?;
        Ok(())
    }

    /// `UpdateQuestComponentActs`: objective indices (per component kind) and selective
    /// reward indices (per quest).
    fn update_quest_component_acts(&mut self) {
        for qid in &self.template_order {
            let quest = &self.templates[qid];
            let mut act_index: u8 = 0;
            let mut selective_index: i32 = 0;
            let mut last_key = kind::NONE;
            for cid in &quest.components {
                let component = &self.components[cid];
                if component.kind_id != last_key {
                    act_index = 0;
                    last_key = component.kind_id;
                }
                for &a in &component.acts {
                    let act = &mut self.acts[a];
                    let counts = act.kind.counts_as_objective();
                    act.objective_index = if counts { act_index } else { 0xFF };
                    act.quest_id = quest.id;
                    if matches!(act.kind, ActKind::SupplySelectiveItem { .. }) {
                        selective_index += 1;
                        act.selective_index = selective_index;
                    }
                    if counts {
                        act_index = act_index.wrapping_add(1);
                    }
                }
            }
        }
    }

    fn load_spheres(&mut self, conn: &Connection) -> Result<(), GameDataError> {
        let mut dup = None;
        for_each_row(conn, "SELECT * FROM spheres", [], |r| {
            // `GetBoolean(name)` without fromString: GetInt64 of 't' / 'f' is 0 -> false.
            let t = DbSphere {
                id: r.u32("id")?,
                name: r.string("name")?,
                enter_or_leave: r.bool("enter_or_leave")?,
                sphere_detail_id: r.u32("sphere_detail_id")?,
                sphere_detail_type: r.string("sphere_detail_type")?,
                trigger_condition_id: r.u32("trigger_condition_id")?,
                trigger_condition_time: r.u32_or("trigger_condition_time", 0)?,
                team_msg: r.string("team_msg")?,
                category_id: r.u32("category_id")?,
                or_unit_reqs: r.bool("or_unit_reqs")?,
                is_personal_msg: r.bool("is_personal_msg")?,
                milestone_id: r.u32("milestone_id")?,
                name_tr: r.bool("name_tr")?,
                team_msg_tr: r.bool("team_msg_tr")?,
            };
            let id = t.id;
            if self.spheres.insert(id, t).is_some() {
                dup = Some(id);
            } else {
                self.sphere_order.push(id);
            }
            Ok(())
        })?;
        for_each_row(conn, "SELECT * FROM sphere_quests", [], |r| {
            let t = DbSphereQuest {
                id: r.u32("id")?,
                quest_id: r.u32("quest_id")?,
                quest_trigger_id: r.u32("quest_trigger_id")?,
            };
            if self.sphere_quests.insert(t.id, t).is_some() {
                dup = Some(t.id);
            } else {
                self.sphere_quest_order.push(t.id);
            }
            Ok(())
        })?;
        match dup {
            Some(id) => Err(err(format!("spheres / sphere_quests: duplicate id {id}"))),
            None => Ok(()),
        }
    }

    // ----- hand-built data (tests, tools): same linking as the loaders -----

    /// Adds a quest template (its `components` are filled by [`Self::add_component`]).
    pub fn add_template(&mut self, mut t: QuestTemplate) {
        t.components.clear();
        self.template_order.push(t.id);
        self.templates.insert(t.id, t);
    }

    /// Adds a component to its quest (`quest_id` must exist).
    pub fn add_component(&mut self, mut c: QuestComponentTemplate) {
        c.acts.clear();
        if let Some(q) = self.templates.get_mut(&c.quest_id) {
            q.components.push(c.id);
        }
        self.components.insert(c.id, c);
    }

    /// Adds a `quest_acts` row and its detail template to a component.
    pub fn add_act(
        &mut self,
        component_id: u32,
        act_id: u32,
        detail_id: u32,
        count: i32,
        k: ActKind,
    ) -> ActRef {
        let class = k.class_name();
        self.base_acts.push(BaseAct {
            id: act_id,
            component_id,
            detail_id,
            detail_type: class.to_string(),
        });
        self.first_base
            .entry((class.to_string(), detail_id))
            .or_insert((act_id, component_id));
        self.link_act(class, detail_id, count, k)
            .expect("add_act: duplicate detail id");
        self.acts.len() - 1
    }

    /// `UpdateQuestComponentActs` (call after adding everything).
    pub fn finish(&mut self) {
        self.update_quest_component_acts();
    }

    // ----- QuestManager getters -----

    /// `GetTemplate(id)`.
    pub fn template(&self, id: u32) -> Option<&QuestTemplate> {
        self.templates.get(&id)
    }

    /// `GetComponent(componentId)`.
    pub fn component(&self, id: u32) -> Option<&QuestComponentTemplate> {
        self.components.get(&id)
    }

    /// `GetSupplies(level)`.
    pub fn supplies(&self, level: u8) -> Option<&QuestSupplies> {
        self.supplies.get(&level)
    }

    /// `GetActsInComponent(componentId)`.
    pub fn acts_in_component(&self, component_id: u32) -> &[ActRef] {
        self.components
            .get(&component_id)
            .map_or(&[], |c| c.acts.as_slice())
    }

    /// `GetActTemplate(id, type)`.
    pub fn act_template(&self, detail_id: u32, class: &str) -> Option<&QuestActTemplate> {
        self.acts_by_type.get(class).and_then(|v| {
            v.iter()
                .map(|&i| &self.acts[i])
                .find(|a| a.detail_id == detail_id)
        })
    }

    /// The act templates of one class (`_actTemplatesByDetailType[type].Values`).
    pub fn acts_of_type(&self, class: &str) -> impl Iterator<Item = (ActRef, &QuestActTemplate)> {
        self.acts_by_type
            .get(class)
            .into_iter()
            .flatten()
            .map(|&i| (i, &self.acts[i]))
    }

    /// `GetQuestIdFromStarterItem(itemTemplateId)`.
    pub fn quest_id_from_starter_item(&self, item_template_id: u32) -> u32 {
        if let Some(&quest) = self.starter_quests.get(&item_template_id) {
            return quest;
        }
        for (_, found) in self.acts_of_type("QuestActConAcceptItem") {
            if !matches!(found.kind, ActKind::ConAcceptItem { item_id, .. } if item_id == item_template_id)
            {
                continue;
            }
            if let Some((_, m)) = self
                .acts_of_type("QuestActConAcceptItem")
                .find(|(_, a)| a.component_kind == kind::START && a.detail_id == found.detail_id)
            {
                return m.quest_id;
            }
        }
        0
    }

    /// `SphereGameData.GetSphereIdFromQuest(questId)`.
    pub fn sphere_id_from_quest(&self, quest_id: u32) -> u32 {
        for sq_id in &self.sphere_quest_order {
            let sq = &self.sphere_quests[sq_id];
            if sq.quest_id != quest_id {
                continue;
            }
            if let Some(s) = self
                .sphere_order
                .iter()
                .map(|i| &self.spheres[i])
                .find(|s| s.sphere_detail_id == sq.id && s.sphere_detail_type == "SphereQuest")
            {
                return s.id;
            }
        }
        0
    }
}

/// `C# string.Trim(' ').Trim('\t').Trim('\r')` (three separate passes).
fn cs_trim(s: &str) -> &str {
    s.trim_matches(' ').trim_matches('\t').trim_matches('\r')
}

/// `uint.Parse` (NumberStyles.Integer: surrounding white space and a leading sign allowed).
fn cs_parse_u32(s: &str) -> Option<u32> {
    let t = s.trim_matches(|c: char| c.is_whitespace());
    let t = t.strip_prefix('+').unwrap_or(t);
    t.parse::<u32>().ok()
}

/// `float.Parse(s, NumberStyles.Float, InvariantCulture)`.
fn cs_parse_f32(s: &str) -> Option<f32> {
    let t = s.trim_matches(|c: char| c.is_whitespace());
    if t.is_empty() || t.contains(|c: char| !(c.is_ascii_digit() || "+-.eE".contains(c))) {
        return None;
    }
    t.parse::<f32>().ok()
}

/// The directory name two levels above a file (`Path.GetFileName(GetDirectoryName(
/// GetDirectoryName(path)))`), for both `/` and `\` separators.
fn zone_dir_name(path: &str) -> &str {
    let mut parts: Vec<&str> = path.split(['/', '\\']).collect();
    parts.pop(); // file
    parts.pop(); // parent dir
    parts.pop().unwrap_or("")
}

/// `SphereQuestManager.LoadQuestSpheres(worldTemplate)` (zone-local coordinates).
pub fn load_sign_spheres(
    files: &ClientFileManager,
    world_name: &str,
) -> Result<Vec<RawSignSphere>, GameDataError> {
    let mut out = Vec::new();
    let dir = format!("game/worlds/{world_name}/level_design/zone");
    let paths = files.files_in_directory(&dir, "quest_sign_sphere.g", true);
    tracing::debug!("Loading {} quest sign sphere data files", paths.len());
    for path in paths {
        let Some(zone_id) = cs_parse_u32(zone_dir_name(&path)) else {
            tracing::warn!("Unable to parse zoneId from {path}");
            continue;
        };
        let contents = files.read_to_string(&path);
        if contents.trim().is_empty() {
            tracing::warn!("{path} doesn't exists or is empty.");
            continue;
        }
        out.extend(
            parse_sign_spheres(&contents, zone_id).map_err(|e| err(format!("{path}: {e}")))?,
        );
    }
    Ok(out)
}

/// The `area / qtype / ctype / pos / radius` blocks of one file.
pub fn parse_sign_spheres(contents: &str, zone_id: u32) -> Result<Vec<RawSignSphere>, String> {
    let lower = contents.to_lowercase();
    let area: Vec<&str> = lower.split('\n').collect();
    let mut out = Vec::new();
    let mut i = 0usize;
    while i + 4 < area.len() {
        let l0 = cs_trim(area[i]);
        let l1 = cs_trim(area[i + 1]);
        let l2 = cs_trim(area[i + 2]);
        let l3 = cs_trim(area[i + 3]);
        let l4 = cs_trim(area[i + 4]);
        if l0.starts_with("area")
            && l1.starts_with("qtype")
            && l2.starts_with("ctype")
            && l3.starts_with("pos")
            && l4.starts_with("radius")
        {
            let bad = || "Loading SphereQuest error!".to_string();
            let quest_id = l1.get(6..).and_then(cs_parse_u32).ok_or_else(bad)?;
            let component_id = l2.get(6..).and_then(cs_parse_u32).ok_or_else(bad)?;
            let mut local = [0f32; 3];
            let sub: String = l3
                .get(4..)
                .ok_or_else(bad)?
                .chars()
                .filter(|c| !matches!(c, '(' | ')' | 'x' | 'y' | 'z' | ' '))
                .collect();
            let pos: Vec<&str> = sub.split(',').collect();
            if pos.len() == 3 {
                for (k, p) in pos.iter().enumerate() {
                    local[k] = cs_parse_f32(p).ok_or_else(bad)?;
                }
            }
            let radius = l4.get(7..).and_then(cs_parse_f32).ok_or_else(bad)?;
            out.push(RawSignSphere {
                zone_id,
                quest_id,
                component_id,
                local,
                radius,
            });
            i += 4;
        }
        i += 1;
    }
    Ok(out)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn explicit_item_starter_binding_is_loaded_and_preferred() {
        let conn = Connection::open_in_memory().unwrap();
        conn.execute_batch(
            "CREATE TABLE item_accept_quests (item_id INT, quest_id INT);
            INSERT INTO item_accept_quests VALUES (19572,3101);",
        )
        .unwrap();
        let mut data = QuestData::default();
        data.load_item_starters(&conn).unwrap();
        assert_eq!(data.quest_id_from_starter_item(19572), 3101);
        assert_eq!(data.quest_id_from_starter_item(9999), 0);
        conn.execute("INSERT INTO item_accept_quests VALUES (19572,9999)", [])
            .unwrap();
        assert!(QuestData::default().load_item_starters(&conn).is_err());
    }

    #[test]
    #[ignore = "needs AAEMU_COMPACT_DB"]
    fn real_item_starter_bindings_load_the_1_2_data() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn =
            Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let mut data = QuestData::default();
        data.load_item_starters(&conn).unwrap();
        assert_eq!(data.starter_quests.len(), 367);
        assert_eq!(data.quest_id_from_starter_item(19572), 3101);
    }

    #[test]
    fn sign_sphere_parsing() {
        let text = "area 1\r\n qtype 1234\r\n\tctype 5678\r\npos (x 10.5, y 20, z -3)\r\nradius 7.5\r\nfoo\r\n";
        let s = parse_sign_spheres(text, 129).unwrap();
        assert_eq!(
            s,
            vec![RawSignSphere {
                zone_id: 129,
                quest_id: 1234,
                component_id: 5678,
                local: [10.5, 20.0, -3.0],
                radius: 7.5
            }]
        );
        // A block ending on the last line is read (i < Count - 4); a truncated one is not.
        assert_eq!(
            parse_sign_spheres("area\nqtype 1\nctype 2\npos (1,2,3)\nradius 1", 1)
                .unwrap()
                .len(),
            1
        );
        assert!(parse_sign_spheres("area\nqtype 1\nctype 2\npos (1,2,3)", 1)
            .unwrap()
            .is_empty());
        assert!(parse_sign_spheres("area\nqtype x\nctype 2\npos (1,2,3)\nradius 1\n", 1).is_err());
        assert_eq!(
            zone_dir_name(
                "game/worlds/main_world/level_design/zone/129/client/quest_sign_sphere.g"
            ),
            "129"
        );
        assert_eq!(
            zone_dir_name(r"C:\x\zone\130\client\quest_sign_sphere.g"),
            "130"
        );
    }

    #[test]
    fn ordered_groups_keep_insertion_order() {
        let mut g = OrderedGroups::default();
        g.add(5, 1);
        g.add(2, 1);
        g.add(5, 3);
        assert_eq!(g.groups_of(1), vec![5, 2]);
        assert_eq!(g.groups_of(3), vec![5]);
        assert!(g.contains(5, 3));
        assert_eq!(g.members(9), &[] as &[u32]);
        assert_eq!(g.rows(), 3);
    }
}
