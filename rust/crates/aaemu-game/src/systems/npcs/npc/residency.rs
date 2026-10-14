//! Resident NPC metadata: which ordinary templates a spawner may retain as residents near
//! players.
//!
//! [`ResidentNpcTemplates`] is an immutable snapshot built once from the static game data: the
//! NPC template ids ordinary quests accept from / report to (scanned from
//! `GameData.systems.quests.acts`) plus the service-role template flags. [`is_resident`]
//! answers for one `NpcTemplate`: nonaggressive, not creator-only, and either a quest giver /
//! reporter or filling a service role. Neutral or no-AI NPCs without such a role are not
//! residents.
//!
//! This is metadata only: it spawns, despawns and retains nothing. The caller still enforces
//! ordinary placement, the authored schedules / management spawners, runtime ownership and the
//! configuration gates (`World.NpcResidentServiceNpcs`) before it keeps an instance alive.
//!
//! [`is_resident`]: ResidentNpcTemplates::is_resident

use std::collections::HashSet;

use crate::data::GameData;
use crate::systems::quests::templates::ActKind;

use super::data::NpcTemplate;

/// Quest giver / reporter and service-role templates of the static game data.
#[derive(Debug, Clone, Default)]
pub struct ResidentNpcTemplates {
    /// NPC template ids an ordinary quest accepts from or reports to (`ConAcceptNpc`,
    /// `ConAcceptNpcEmotion`, `ConReportNpc`). Kill and hunt targets are not collected.
    quest_npcs: HashSet<u32>,
}

impl ResidentNpcTemplates {
    /// Scans `GameData.systems.quests.acts` once (the quest metadata is static, there is no
    /// per-tick scan).
    pub fn from_game_data(data: &GameData) -> Self {
        let mut quest_npcs = HashSet::new();
        for act in &data.systems.quests.acts {
            match act.kind {
                ActKind::ConAcceptNpc { npc_id }
                | ActKind::ConAcceptNpcEmotion { npc_id, .. }
                | ActKind::ConReportNpc { npc_id, .. } => {
                    quest_npcs.insert(npc_id);
                }
                _ => {}
            }
        }
        Self { quest_npcs }
    }

    /// Whether `template` is an ordinary resident candidate: nonaggressive, not visible to the
    /// creator only, and either a quest giver / reporter or an actual service role (merchant,
    /// banker, skill trainer, auctioneer, priest, blacksmith, teleporter, ability changer,
    /// stabler, trader, repairman, specialty, expedition, look converter).
    pub fn is_resident(&self, template: &NpcTemplate) -> bool {
        !template.aggression
            && !template.visible_to_creator_only
            && (self.quest_npcs.contains(&template.id) || has_service_role(template))
    }
}

/// The service-role template flags of [`ResidentNpcTemplates::is_resident`].
fn has_service_role(t: &NpcTemplate) -> bool {
    t.merchant
        || t.banker
        || t.skill_trainer
        || t.auctioneer
        || t.priest
        || t.blacksmith
        || t.teleporter
        || t.ability_changer
        || t.stabler
        || t.trader
        || t.repairman
        || t.specialty
        || t.expedition
        || t.look_converter
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::quests::data::QuestData;
    use crate::systems::quests::model::kind;
    use crate::systems::quests::templates::{QuestComponentTemplate, QuestTemplate};

    /// Adds one quest holding `act` in a single component.
    fn add_quest_act(q: &mut QuestData, id: u32, component_kind: u8, act: ActKind) {
        q.add_template(QuestTemplate {
            id,
            ..QuestTemplate::default()
        });
        q.add_component(QuestComponentTemplate {
            id,
            quest_id: id,
            kind_id: component_kind,
            ..QuestComponentTemplate::default()
        });
        q.add_act(id, id, id, 0, act);
    }

    /// Small quest fixture: NPC 500 accepts, NPC 501 reports, NPC 502 accepts via emotion;
    /// NPC 600 starts the quest by being killed and NPC 601 is a hunt objective.
    fn game_data_with_quests() -> GameData {
        let mut gd = GameData::empty();
        let q = &mut gd.systems.quests;
        add_quest_act(q, 100, kind::START, ActKind::ConAcceptNpc { npc_id: 500 });
        add_quest_act(
            q,
            110,
            kind::READY,
            ActKind::ConReportNpc {
                npc_id: 501,
                use_alias: false,
                alias_id: 0,
            },
        );
        add_quest_act(
            q,
            120,
            kind::START,
            ActKind::ConAcceptNpcEmotion {
                npc_id: 502,
                emotion: "wave".into(),
            },
        );
        add_quest_act(
            q,
            130,
            kind::START,
            ActKind::ConAcceptNpcKill { npc_id: 600 },
        );
        add_quest_act(
            q,
            140,
            kind::PROGRESS,
            ActKind::ObjMonsterHunt {
                npc_id: 601,
                use_alias: false,
                alias_id: 0,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
            },
        );
        q.finish();
        gd
    }

    /// One service-role flag: name and the setter of a `NpcTemplate`.
    type RoleSetter = (&'static str, fn(&mut NpcTemplate));

    /// The service-role flags of `is_resident` (name, setter).
    const SERVICE_ROLE_SETTERS: [RoleSetter; 14] = [
        ("merchant", |t| t.merchant = true),
        ("banker", |t| t.banker = true),
        ("skill_trainer", |t| t.skill_trainer = true),
        ("auctioneer", |t| t.auctioneer = true),
        ("priest", |t| t.priest = true),
        ("blacksmith", |t| t.blacksmith = true),
        ("teleporter", |t| t.teleporter = true),
        ("ability_changer", |t| t.ability_changer = true),
        ("stabler", |t| t.stabler = true),
        ("trader", |t| t.trader = true),
        ("repairman", |t| t.repairman = true),
        ("specialty", |t| t.specialty = true),
        ("expedition", |t| t.expedition = true),
        ("look_converter", |t| t.look_converter = true),
    ];

    /// A template with one service role set (id 1 is not a quest NPC of the fixture).
    fn role_template(set: fn(&mut NpcTemplate)) -> NpcTemplate {
        let mut t = NpcTemplate {
            id: 1,
            ..NpcTemplate::default()
        };
        set(&mut t);
        t
    }

    #[test]
    fn quest_givers_reporters_and_emotion_accepts_are_residents() {
        let residents = ResidentNpcTemplates::from_game_data(&game_data_with_quests());
        for id in [500, 501, 502] {
            let t = NpcTemplate {
                id,
                ..NpcTemplate::default()
            };
            assert!(residents.is_resident(&t), "npc {id}");
        }
    }

    #[test]
    fn service_roles_are_residents() {
        let residents = ResidentNpcTemplates::from_game_data(&GameData::empty());
        assert!(
            !residents.is_resident(&NpcTemplate::default()),
            "no role is not a resident"
        );
        for (name, set) in SERVICE_ROLE_SETTERS {
            assert!(residents.is_resident(&role_template(set)), "{name}");
        }
    }

    #[test]
    fn aggression_or_creator_only_rejects_every_role() {
        let residents = ResidentNpcTemplates::from_game_data(&game_data_with_quests());
        let mut cases: Vec<(&str, NpcTemplate)> = SERVICE_ROLE_SETTERS
            .iter()
            .map(|(name, set)| (*name, role_template(*set)))
            .collect();
        cases.push((
            "quest_npc",
            NpcTemplate {
                id: 500,
                ..NpcTemplate::default()
            },
        ));
        for (name, t) in &cases {
            let mut aggressive = t.clone();
            aggressive.aggression = true;
            assert!(!residents.is_resident(&aggressive), "aggressive {name}");
            let mut hidden = t.clone();
            hidden.visible_to_creator_only = true;
            assert!(!residents.is_resident(&hidden), "creator-only {name}");
        }
    }

    #[test]
    fn neutral_bystanders_and_quest_targets_are_not_residents() {
        let residents = ResidentNpcTemplates::from_game_data(&game_data_with_quests());
        let bystander = NpcTemplate {
            id: 700,
            ..NpcTemplate::default()
        };
        assert!(!residents.is_resident(&bystander), "neutral bystander");
        // `ConAcceptNpcKill` (600) and the `ObjMonsterHunt` target (601) are no residents.
        for id in [600, 601] {
            let t = NpcTemplate {
                id,
                ..NpcTemplate::default()
            };
            assert!(!residents.is_resident(&t), "npc {id}");
        }
    }
}
