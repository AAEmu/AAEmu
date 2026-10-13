//! The four 1.2 mentor/mentee daily quests are sealed-chest objectives, not kill objectives.
//! Bind a chest to the qualifying party at boss death, then let the authored FakeUse/LootItem
//! phase chain grant seals. Quest item events supply the authored rewards and daily reset.

use std::collections::HashMap;
use std::sync::Arc;

use crate::systems::npcs::doodad::{manager, runtime::DoodadCtx, templates::FuncTemplate};
use crate::systems::quests::model::kind;
use crate::systems::skills::combat::MAX_LOOTING_RANGE;
use crate::world::{PlayerRef, WorldObject};
use crate::GameServer;

/// IDs and phase order verified against the target compact.sqlite3, including the NPC
/// variants used by app/Data/Worlds. Keep these bindings specific to the normal 1.2 dungeons.
#[derive(Debug, Clone, Copy)]
pub struct MentorDungeon {
    pub zone_group_id: u32,
    pub bosses: &'static [u32],
    pub chest_id: u32,
    pub chest_count: usize,
    pub mentor_quest: u32,
    pub mentee_quest: u32,
    pub mentee_levels: (u8, u8),
    pub mentor_levels: (u8, u8),
    /// Mentor first seal, mentee second seal, mentee loot, mentor seal, mentor loot.
    pub phases: [u32; 5],
}

pub const DUNGEONS: &[MentorDungeon] = &[
    MentorDungeon {
        zone_group_id: 50,
        bosses: &[11364, 12188],
        chest_id: 7522,
        chest_count: 1,
        mentor_quest: 6084,
        mentee_quest: 6083,
        mentee_levels: (18, 29),
        mentor_levels: (30, 55),
        phases: [21043, 20540, 20541, 20542, 20543],
    },
    MentorDungeon {
        zone_group_id: 45,
        bosses: &[8962, 10518],
        chest_id: 7659,
        chest_count: 2,
        mentor_quest: 6088,
        mentee_quest: 6087,
        mentee_levels: (31, 39),
        mentor_levels: (40, 55),
        phases: [21051, 20854, 20855, 20856, 20857],
    },
    MentorDungeon {
        zone_group_id: 47,
        bosses: &[9797, 12189],
        chest_id: 7660,
        chest_count: 1,
        mentor_quest: 6167,
        mentee_quest: 6166,
        mentee_levels: (18, 29),
        mentor_levels: (30, 55),
        phases: [21050, 20859, 20860, 20861, 20862],
    },
    MentorDungeon {
        zone_group_id: 46,
        bosses: &[9798, 10098],
        chest_id: 7661,
        chest_count: 2,
        mentor_quest: 6168,
        mentee_quest: 6169,
        mentee_levels: (31, 39),
        mentor_levels: (40, 55),
        phases: [21056, 20877, 20887, 20888, 20889],
    },
];

#[derive(Debug, Clone, Default)]
struct ChestAccess {
    /// Character id -> party id, captured at the boss kill.
    mentors: HashMap<u32, u32>,
    mentees: HashMap<u32, u32>,
    mentor: Option<u32>,
    mentee: Option<u32>,
}

#[derive(Debug, Default)]
pub struct MentorState {
    boss_defeated: bool,
    chests: HashMap<u32, ChestAccess>,
}

fn definition(chest_id: u32) -> Option<&'static MentorDungeon> {
    DUNGEONS.iter().find(|d| d.chest_id == chest_id)
}

fn eligible(p: &PlayerRef, quest: u32, levels: (u8, u8)) -> bool {
    let c = p.data();
    c.is_online
        && (levels.0..=levels.1).contains(&c.unit.level)
        && c.quest_state
            .active_quest(quest)
            .is_some_and(|q| q.step == kind::PROGRESS)
}

/// Only DoDie calls this, once per run; early HP-reduction notifications cannot open chests.
pub fn on_death(server: &GameServer, victim: &WorldObject) {
    let Some(npc) = victim.as_npc() else {
        return;
    };
    let (instance_id, template_id) = {
        let n = npc.data();
        (n.unit.transform.instance_id, n.unit.template_id)
    };
    let Some(dungeon) = server.systems.instances.dungeon_of_world(instance_id) else {
        return;
    };
    let Some(def) = DUNGEONS
        .iter()
        .find(|d| d.zone_group_id == dungeon.zone_group_id() && d.bosses.contains(&template_id))
    else {
        return;
    };
    let Some(world) = dungeon.world(server) else {
        return;
    };
    let _operation = dungeon
        .operations
        .lock()
        .expect("dungeon operations poisoned");
    if dungeon.world_id() != Some(world.id) {
        return;
    }
    // Dynamic world ids are recycled. A delayed death from a removed world must not
    // qualify a fresh run that now uses that id, even if an NPC object id was reused too.
    if !world
        .npc(npc.obj_id)
        .is_some_and(|current| Arc::ptr_eq(&current, npc))
    {
        return;
    }
    {
        let mut st = dungeon.st();
        if st.mentoring.boss_defeated {
            return;
        }
        st.mentoring.boss_defeated = true;
    }
    let pos = world.world_pos_rot(victim);
    let mut access = ChestAccess::default();
    for p in world.all_characters() {
        let (alive, online, instance) = {
            let c = p.data();
            (c.unit.hp > 0, c.is_online, c.unit.transform.instance_id)
        };
        if !alive || !online || instance != world.id {
            continue;
        }
        let pp = world.world_pos_rot(&WorldObject::from(&p));
        let distance =
            ((pos.x - pp.x).powi(2) + (pos.y - pp.y).powi(2) + (pos.z - pp.z).powi(2)).sqrt();
        if distance > MAX_LOOTING_RANGE {
            continue;
        }
        let Some(team) = server.systems.social.teams.team_by_obj_id(p.obj_id) else {
            continue;
        };
        if eligible(&p, def.mentor_quest, def.mentor_levels) {
            access.mentors.insert(p.character_id, team.id);
        }
        if eligible(&p, def.mentee_quest, def.mentee_levels) {
            access.mentees.insert(p.character_id, team.id);
        }
    }
    if !access
        .mentors
        .values()
        .any(|team| access.mentees.values().any(|other| other == team))
    {
        return;
    }
    let mut chests = world
        .all_doodads()
        .into_iter()
        .filter(|d| d.data().template_id == def.chest_id)
        .collect::<Vec<_>>();
    chests.sort_by_key(|d| d.obj_id);
    // Some of the ported spawn files lack the reward chests. Reuse the actual 1.2 template
    // at the defeated boss instead of granting Gilda or bypassing the seal objectives.
    while chests.len() < def.chest_count {
        let Some(chest) = manager::create(server, &world, 0, def.chest_id, None, true) else {
            tracing::warn!("Mentor chest {} has no template", def.chest_id);
            break;
        };
        chest
            .data()
            .transform
            .set_xyz(pos.x + chests.len() as f32 * 2.0, pos.y, pos.z);
        manager::spawn(server, &world, &chest);
        chests.push(chest);
    }
    for chest in chests.into_iter().take(def.chest_count) {
        dungeon
            .st()
            .mentoring
            .chests
            .insert(chest.obj_id, access.clone());
        manager::do_change_phase(server, &world, &chest, None, def.phases[0] as i32);
    }
}

/// Client requests may name the current phase, but cannot skip a seal or reveal a dormant chest.
pub fn allows_phase_request(ctx: &DoodadCtx<'_>, requested: i32) -> bool {
    let d = ctx.doodad.data();
    definition(d.template_id).is_none() || requested <= 0 || requested as u32 == d.func_group_id
}

/// Called under the doodad phase operation reservation, without holding object data locks.
pub fn authorize(
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
    func: &FuncTemplate,
) -> bool {
    let (template, phase) = {
        let d = ctx.doodad.data();
        (d.template_id, d.func_group_id)
    };
    let Some(def) = definition(template) else {
        return true;
    };
    let Some(player) = caster.and_then(WorldObject::as_player) else {
        return false;
    };
    let Some(stage) = def.phases.iter().position(|p| *p == phase) else {
        return false;
    };
    match func {
        FuncTemplate::FakeUse(f)
            if matches!(stage, 0 | 1 | 3) && f.fake_skill_id == skill_id && skill_id != 0 => {}
        FuncTemplate::LootItem(_) if matches!(stage, 2 | 4) => {}
        _ => return false,
    }
    let Some(dungeon) = ctx.server.systems.instances.dungeon_of_world(ctx.world.id) else {
        return false;
    };
    let Some(team) = ctx
        .server
        .systems
        .social
        .teams
        .team_by_obj_id(player.obj_id)
    else {
        return false;
    };
    if player.data().unit.transform.instance_id != ctx.world.id {
        return false;
    }
    let mentor_role = matches!(stage, 0 | 3 | 4);
    if !eligible(
        player,
        if mentor_role {
            def.mentor_quest
        } else {
            def.mentee_quest
        },
        if mentor_role {
            def.mentor_levels
        } else {
            def.mentee_levels
        },
    ) {
        return false;
    }
    let access = dungeon
        .st()
        .mentoring
        .chests
        .get(&ctx.doodad.obj_id)
        .cloned();
    let Some(access) = access else {
        return false;
    };
    match stage {
        0 => {
            access.mentors.get(&player.character_id) == Some(&team.id)
                && access.mentees.values().any(|t| *t == team.id)
        }
        1 => {
            let Some(mentor) = access
                .mentor
                .and_then(|id| ctx.server.world.character_by_id(id))
            else {
                return false;
            };
            access.mentees.get(&player.character_id) == Some(&team.id)
                && eligible(&mentor, def.mentor_quest, def.mentor_levels)
                && mentor.data().unit.transform.instance_id == ctx.world.id
                && ctx
                    .server
                    .systems
                    .social
                    .teams
                    .team_by_obj_id(mentor.obj_id)
                    .is_some_and(|t| t.id == team.id)
        }
        2 => access.mentee == Some(player.character_id),
        3 | 4 => {
            access.mentor == Some(player.character_id)
                && access.mentors.get(&player.character_id) == Some(&team.id)
        }
        _ => false,
    }
}

/// Record participants only after the function succeeded; failed inventory grants keep the
/// chest available for retry. The phase reservation serializes simultaneous interactions.
pub fn succeeded(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>) {
    let (template, phase) = {
        let d = ctx.doodad.data();
        (d.template_id, d.func_group_id)
    };
    let Some(def) = definition(template) else {
        return;
    };
    let Some(player) = caster.and_then(WorldObject::as_player) else {
        return;
    };
    let Some(dungeon) = ctx.server.systems.instances.dungeon_of_world(ctx.world.id) else {
        return;
    };
    let mut st = dungeon.st();
    let Some(access) = st.mentoring.chests.get_mut(&ctx.doodad.obj_id) else {
        return;
    };
    if phase == def.phases[0] {
        access.mentor = Some(player.character_id);
    }
    if phase == def.phases[1] {
        access.mentee = Some(player.character_id);
    }
}
