//! `Models/Game/Quests/Templates/*` and the `quest_act_*` detail templates
//! (`Models/Game/Quests/Acts/*`, `UnusedActs/*`).
//!
//! The C# act classes are one subclass of `QuestActTemplate` each; here they are the
//! [`ActKind`] variants of one [`QuestActTemplate`]. Acts are stored once in
//! `Data::acts` and referenced by index ([`ActRef`]).

use super::model::kind;

/// Index of an act template in `Data::acts`.
pub type ActRef = usize;

/// `QuestTemplate` (`quest_contexts`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct QuestTemplate {
    pub id: u32,
    pub repeatable: bool,
    pub level: u8,
    pub selective: bool,
    pub successive: bool,
    pub restart_on_fail: bool,
    pub chapter_idx: u32,
    pub quest_idx: u32,
    pub milestone_id: u32,
    pub let_it_done: bool,
    /// `QuestDetail`.
    pub detail_id: u32,
    pub zone_id: u32,
    pub category_id: u32,
    pub degree: i32,
    pub use_quest_camera: bool,
    pub score: i32,
    pub use_accept_message: bool,
    pub use_complete_message: bool,
    pub grade_id: u32,
    /// `Components` (component ids in dictionary insertion order).
    pub components: Vec<u32>,
}

/// `QuestComponentTemplate` (`quest_components`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct QuestComponentTemplate {
    pub id: u32,
    /// `QuestComponentKind`.
    pub kind_id: u8,
    pub next_component: u32,
    /// `QuestNpcAiName`.
    pub npc_ai_id: u32,
    pub npc_id: u32,
    pub skill_id: u32,
    pub skill_self: bool,
    pub ai_path_name: String,
    pub ai_path_type_id: u32,
    pub npc_spawner_id: u32,
    pub play_cinema_before_bubble: bool,
    pub ai_command_set_id: u32,
    pub or_unit_reqs: bool,
    pub cinema_id: u32,
    pub buff_id: u32,
    /// `ParentQuestTemplate.Id`.
    pub quest_id: u32,
    /// `ActTemplates` (in the order `LoadDetailQuestActTemplates` added them).
    pub acts: Vec<ActRef>,
}

/// `QuestActTemplate` (base members) + the detail class ([`ActKind`]).
#[derive(Debug, Clone, PartialEq)]
pub struct QuestActTemplate {
    /// `quest_acts.id` of the first base act referencing this detail.
    pub act_id: u32,
    /// `quest_act_xxx.id`.
    pub detail_id: u32,
    /// `ParentComponent.Id`.
    pub component_id: u32,
    /// `ParentComponent.KindId`.
    pub component_kind: u8,
    /// `ParentQuestTemplate.Id`.
    pub quest_id: u32,
    /// `Count` (0 for the classes without a count column).
    pub count: i32,
    /// `ThisComponentObjectiveIndex` (0xFF = not an objective).
    pub objective_index: u8,
    /// `ThisSelectiveIndex`.
    pub selective_index: i32,
    pub kind: ActKind,
}

/// The `quest_act_*` detail classes with their columns.
#[derive(Debug, Clone, PartialEq)]
pub enum ActKind {
    CheckCompleteComponent {
        complete_component: u32,
    },
    CheckDistance {
        within: bool,
        npc_id: u32,
        distance: i32,
    },
    CheckGuard {
        npc_id: u32,
    },
    CheckSphere {
        sphere_id: u32,
    },
    CheckTimer {
        limit_time: i32,
        force_change_component: bool,
        next_component: u32,
        play_skill: bool,
        skill_id: u32,
        check_buff: bool,
        buff_id: u32,
        sustain_buff: bool,
        timer_npc_id: u32,
        is_skill_player: bool,
    },
    ConAcceptBuff {
        buff_id: u32,
    },
    ConAcceptComponent {
        quest_context_id: u32,
    },
    ConAcceptDoodad {
        doodad_id: u32,
    },
    ConAcceptItemEquip {
        item_id: u32,
    },
    ConAcceptItemGain {
        item_id: u32,
    },
    ConAcceptItem {
        item_id: u32,
        cleanup: bool,
        drop_when_destroy: bool,
        destroy_when_drop: bool,
    },
    ConAcceptLevelUp {
        level: u8,
    },
    ConAcceptNpcEmotion {
        npc_id: u32,
        emotion: String,
    },
    ConAcceptNpcKill {
        npc_id: u32,
    },
    ConAcceptNpc {
        npc_id: u32,
    },
    ConAcceptSkill {
        skill_id: u32,
    },
    ConAcceptSphere {
        sphere_id: u32,
    },
    ConAutoComplete,
    ConFail {
        force_change_component: bool,
    },
    ConReportDoodad {
        doodad_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ConReportJournal,
    ConReportNpc {
        npc_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    EtcItemObtain {
        item_id: u32,
        highlight_doodad_id: u32,
        cleanup: bool,
    },
    ObjAbilityLevel {
        /// `AbilityType`.
        ability_id: u8,
        level: u8,
        use_alias: bool,
        alias_id: u32,
    },
    ObjAggro {
        range: i32,
        rank1: i32,
        rank1_ratio: i32,
        rank1_item: bool,
        rank2: i32,
        rank2_ratio: i32,
        rank2_item: bool,
        rank3: i32,
        rank3_ratio: i32,
        rank3_item: bool,
        use_alias: bool,
        alias_id: u32,
    },
    ObjCinema {
        cinema_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjCompleteQuest {
        quest_id: u32,
        accept_with: bool,
        use_alias: bool,
        alias_id: u32,
    },
    ObjCondition {
        condition_id: u32,
        quest_context_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjCraft {
        craft_id: u32,
        use_alias: bool,
        alias_id: u32,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
    },
    ObjDistance {
        within: bool,
        npc_id: u32,
        distance: i32,
        highlight_doodad_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjDoodadPhaseCheck {
        doodad_id: u32,
        phase1: u32,
        phase2: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjEffectFire {
        effect_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjExpressFire {
        express_key_id: u32,
        npc_group_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjInteraction {
        /// `WorldInteractionType`.
        wi_id: i32,
        doodad_id: u32,
        use_alias: bool,
        team_share: bool,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
        alias_id: u32,
        phase: u32,
    },
    ObjItemGather {
        item_id: u32,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
        use_alias: bool,
        alias_id: u32,
        cleanup: bool,
        drop_when_destroy: bool,
        destroy_when_drop: bool,
    },
    ObjItemGroupGather {
        item_group_id: u32,
        cleanup: bool,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
        use_alias: bool,
        alias_id: u32,
        drop_when_destroy: bool,
        destroy_when_drop: bool,
    },
    ObjItemGroupUse {
        item_group_id: u32,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
        use_alias: bool,
        alias_id: u32,
        drop_when_destroy: bool,
    },
    ObjItemUse {
        item_id: u32,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
        use_alias: bool,
        alias_id: u32,
        drop_when_destroy: bool,
    },
    ObjLevel {
        level: u8,
        use_alias: bool,
        alias_id: u32,
    },
    ObjMateLevel {
        item_id: u32,
        level: u8,
        cleanup: bool,
        use_alias: bool,
        alias_id: u32,
    },
    ObjMonsterGroupHunt {
        quest_monster_group_id: u32,
        use_alias: bool,
        alias_id: u32,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
    },
    ObjMonsterHunt {
        npc_id: u32,
        use_alias: bool,
        alias_id: u32,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
    },
    ObjSendMail {
        item_id1: u32,
        count1: i32,
        item_id2: u32,
        count2: i32,
        item_id3: u32,
        count3: i32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjSphere {
        sphere_id: u32,
        npc_id: u32,
        highlight_doodad_id: u32,
        highlight_doodad_phase: i32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjTalkNpcGroup {
        npc_group_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjTalk {
        npc_id: u32,
        team_share: bool,
        item_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjZoneKill {
        count_player_kill: i32,
        count_npc: i32,
        zone_id: u32,
        team_share: bool,
        use_alias: bool,
        alias_id: u32,
        lvl_min: i32,
        lvl_max: i32,
        is_party: bool,
        lvl_min_npc: i32,
        lvl_max_npc: i32,
        pc_faction_id: u32,
        pc_faction_exclusive: bool,
        npc_faction_id: u32,
        npc_faction_exclusive: bool,
    },
    ObjZoneMonsterHunt {
        zone_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjZoneNpcTalk {
        npc_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    ObjZoneQuestComplete {
        zone_id: u32,
        use_alias: bool,
        alias_id: u32,
    },
    SupplyAaPoint {
        point: i32,
    },
    SupplyAppellation {
        appellation_id: u32,
    },
    SupplyCopper {
        amount: i32,
    },
    SupplyCrimePoint {
        point: i16,
    },
    SupplyExp {
        exp: i32,
    },
    SupplyHonorPoint {
        point: i32,
    },
    SupplyInteraction {
        wi_id: u32,
    },
    SupplyItem {
        item_id: u32,
        grade_id: u8,
        show_action_bar: bool,
        cleanup: bool,
        drop_when_destroy: bool,
        destroy_when_drop: bool,
    },
    SupplyJuryPoint {
        point: i32,
    },
    SupplyLivingPoint {
        point: i32,
    },
    SupplyLp {
        labor_power: i32,
    },
    SupplyRemoveItem {
        item_id: u32,
    },
    SupplySelectiveItem {
        item_id: u32,
        grade_id: u8,
    },
    SupplySkill {
        skill_id: u32,
    },
}

impl ActKind {
    /// The C# class name (`GetType().Name`, `quest_acts.act_detail_type`).
    pub fn class_name(&self) -> &'static str {
        use ActKind::*;
        match self {
            CheckCompleteComponent { .. } => "QuestActCheckCompleteComponent",
            CheckDistance { .. } => "QuestActCheckDistance",
            CheckGuard { .. } => "QuestActCheckGuard",
            CheckSphere { .. } => "QuestActCheckSphere",
            CheckTimer { .. } => "QuestActCheckTimer",
            ConAcceptBuff { .. } => "QuestActConAcceptBuff",
            ConAcceptComponent { .. } => "QuestActConAcceptComponent",
            ConAcceptDoodad { .. } => "QuestActConAcceptDoodad",
            ConAcceptItemEquip { .. } => "QuestActConAcceptItemEquip",
            ConAcceptItemGain { .. } => "QuestActConAcceptItemGain",
            ConAcceptItem { .. } => "QuestActConAcceptItem",
            ConAcceptLevelUp { .. } => "QuestActConAcceptLevelUp",
            ConAcceptNpcEmotion { .. } => "QuestActConAcceptNpcEmotion",
            ConAcceptNpcKill { .. } => "QuestActConAcceptNpcKill",
            ConAcceptNpc { .. } => "QuestActConAcceptNpc",
            ConAcceptSkill { .. } => "QuestActConAcceptSkill",
            ConAcceptSphere { .. } => "QuestActConAcceptSphere",
            ConAutoComplete => "QuestActConAutoComplete",
            ConFail { .. } => "QuestActConFail",
            ConReportDoodad { .. } => "QuestActConReportDoodad",
            ConReportJournal => "QuestActConReportJournal",
            ConReportNpc { .. } => "QuestActConReportNpc",
            EtcItemObtain { .. } => "QuestActEtcItemObtain",
            ObjAbilityLevel { .. } => "QuestActObjAbilityLevel",
            ObjAggro { .. } => "QuestActObjAggro",
            ObjCinema { .. } => "QuestActObjCinema",
            ObjCompleteQuest { .. } => "QuestActObjCompleteQuest",
            ObjCondition { .. } => "QuestActObjCondition",
            ObjCraft { .. } => "QuestActObjCraft",
            ObjDistance { .. } => "QuestActObjDistance",
            ObjDoodadPhaseCheck { .. } => "QuestActObjDoodadPhaseCheck",
            ObjEffectFire { .. } => "QuestActObjEffectFire",
            ObjExpressFire { .. } => "QuestActObjExpressFire",
            ObjInteraction { .. } => "QuestActObjInteraction",
            ObjItemGather { .. } => "QuestActObjItemGather",
            ObjItemGroupGather { .. } => "QuestActObjItemGroupGather",
            ObjItemGroupUse { .. } => "QuestActObjItemGroupUse",
            ObjItemUse { .. } => "QuestActObjItemUse",
            ObjLevel { .. } => "QuestActObjLevel",
            ObjMateLevel { .. } => "QuestActObjMateLevel",
            ObjMonsterGroupHunt { .. } => "QuestActObjMonsterGroupHunt",
            ObjMonsterHunt { .. } => "QuestActObjMonsterHunt",
            ObjSendMail { .. } => "QuestActObjSendMail",
            ObjSphere { .. } => "QuestActObjSphere",
            ObjTalkNpcGroup { .. } => "QuestActObjTalkNpcGroup",
            ObjTalk { .. } => "QuestActObjTalk",
            ObjZoneKill { .. } => "QuestActObjZoneKill",
            ObjZoneMonsterHunt { .. } => "QuestActObjZoneMonsterHunt",
            ObjZoneNpcTalk { .. } => "QuestActObjZoneNpcTalk",
            ObjZoneQuestComplete { .. } => "QuestActObjZoneQuestComplete",
            SupplyAaPoint { .. } => "QuestActSupplyAaPoint",
            SupplyAppellation { .. } => "QuestActSupplyAppellation",
            SupplyCopper { .. } => "QuestActSupplyCopper",
            SupplyCrimePoint { .. } => "QuestActSupplyCrimePoint",
            SupplyExp { .. } => "QuestActSupplyExp",
            SupplyHonorPoint { .. } => "QuestActSupplyHonorPoint",
            SupplyInteraction { .. } => "QuestActSupplyInteraction",
            SupplyItem { .. } => "QuestActSupplyItem",
            SupplyJuryPoint { .. } => "QuestActSupplyJuryPoint",
            SupplyLivingPoint { .. } => "QuestActSupplyLivingPoint",
            SupplyLp { .. } => "QuestActSupplyLp",
            SupplyRemoveItem { .. } => "QuestActSupplyRemoveItem",
            SupplySelectiveItem { .. } => "QuestActSupplySelectiveItem",
            SupplySkill { .. } => "QuestActSupplySkill",
        }
    }

    /// `CountsAsAnObjective` (the classes overriding it with `true`).
    pub fn counts_as_objective(&self) -> bool {
        use ActKind::*;
        matches!(
            self,
            ConAcceptBuff { .. }
                | ConAcceptItemEquip { .. }
                | ConFail { .. }
                | ObjAbilityLevel { .. }
                | ObjAggro { .. }
                | ObjCinema { .. }
                | ObjCompleteQuest { .. }
                | ObjCraft { .. }
                | ObjDistance { .. }
                | ObjExpressFire { .. }
                | ObjInteraction { .. }
                | ObjItemGather { .. }
                | ObjItemUse { .. }
                | ObjLevel { .. }
                | ObjMateLevel { .. }
                | ObjMonsterGroupHunt { .. }
                | ObjMonsterHunt { .. }
                | ObjSphere { .. }
                | ObjTalkNpcGroup { .. }
                | ObjTalk { .. }
                | ObjZoneKill { .. }
                | ObjZoneMonsterHunt { .. }
        )
    }

    /// The generic item members (`IQuestActGenericItem`: `QuestActConAcceptItem`,
    /// `QuestActObjItemGather`, `QuestActSupplyItem`) as (ItemId, DropWhenDestroy).
    pub fn generic_item(&self) -> Option<(u32, bool)> {
        match self {
            ActKind::ConAcceptItem {
                item_id,
                drop_when_destroy,
                ..
            }
            | ActKind::ObjItemGather {
                item_id,
                drop_when_destroy,
                ..
            }
            | ActKind::SupplyItem {
                item_id,
                drop_when_destroy,
                ..
            } => Some((*item_id, *drop_when_destroy)),
            _ => None,
        }
    }
}

impl QuestActTemplate {
    /// `MaxObjective()`; `None` where the C# throws (`Score / Count` with `Count == 0`).
    pub fn max_objective(&self, quest: &QuestTemplate) -> Option<i32> {
        let mut val = self.count;
        // If Score-base, calculate max objective count needed to get score
        if quest.score > 0 {
            val = quest.score.checked_div(self.count)?.wrapping_add(1);
        }
        Some(if val > 0 {
            if quest.let_it_done {
                (val as f32 * 3.0 / 2.0).ceil() as i32
            } else {
                val
            }
        } else {
            1
        })
    }

    /// True for the acts of a Start / Ready component (`actsOrCheck`).
    pub fn in_or_step(&self) -> bool {
        self.component_kind == kind::START || self.component_kind == kind::READY
    }
}

impl QuestTemplate {
    /// `GetFirstComponent(step)`.
    pub fn first_component<'a>(
        &self,
        components: &'a std::collections::HashMap<u32, QuestComponentTemplate>,
        step: u8,
    ) -> Option<&'a QuestComponentTemplate> {
        self.components
            .iter()
            .filter_map(|id| components.get(id))
            .find(|c| c.kind_id == step)
    }

    /// `GetComponents(step)`.
    pub fn components_of<'a>(
        &self,
        components: &'a std::collections::HashMap<u32, QuestComponentTemplate>,
        step: u8,
    ) -> Vec<&'a QuestComponentTemplate> {
        self.components
            .iter()
            .filter_map(|id| components.get(id))
            .filter(|c| c.kind_id == step)
            .collect()
    }
}
