//! The social subsystem's implementations of the other subsystems' hooks, and the
//! [`SocialHooks`] the social code itself calls:
//!
//! * [`SocialForSkills`]: `skills::ext::SocialSkillHooks` (team queries, crime evidence from
//!   damage, bot reports, `DeclareDominion`, appellations, `ReportCrimeEffect`);
//! * [`GameSocialHooks`]: the social code's view of the world. The npcs parts (duel flag and
//!   crime evidence doodads, terrain height, evidence funcs, doodad phases, the court judge) and
//!   the skills parts (`PostUpdateCurrentHp`, buff tags) call those subsystems directly; the
//!   housing (`UpdateOwnedHousingFaction`) and mates (`ForceDismount`) parts keep the trait
//!   defaults until their owners are ported.
//!
//! Installed by [`install`] from `System::start`.

use std::sync::Arc;

use crate::data::skills::BuffKind;
use crate::models::character::DEFAULT_INSTANCE_ID;
use crate::server::GameServer;
use crate::systems::npcs::doodad::manager as doodads;
use crate::systems::skills::data::{EffectKind, EffectTemplate};
use crate::systems::skills::effects::EffectArgs;
use crate::systems::skills::ext::SocialSkillHooks;
use crate::systems::skills::relation::owner_character;
use crate::world::{DoodadRef, PlayerRef, WorldObject};

use super::hooks::{EvidenceFunc, SocialHooks};

/// `FarmGroupKind.Invalid`.
const FARM_GROUP_INVALID: u8 = 0;

/// Installs this subsystem's hook implementations.
pub fn install(server: &GameServer) {
    install_economy(server);
    server
        .systems
        .skills
        .ext
        .social
        .install(Arc::new(SocialForSkills));
    server
        .systems
        .social
        .hooks
        .install(Arc::new(GameSocialHooks));
}

/// `skills::ext::SocialSkillHooks` of the social subsystem.
pub struct SocialForSkills;

impl SocialSkillHooks for SocialForSkills {
    fn are_team_members(&self, server: &GameServer, char1: u32, char2: u32) -> bool {
        server.systems.social.teams.are_team_members(char1, char2)
    }

    fn team_of(&self, server: &GameServer, obj_id: u32) -> u32 {
        server
            .systems
            .social
            .teams
            .team_by_obj_id(obj_id)
            .map_or(0, |t| t.id)
    }

    fn is_team_obj_member(&self, server: &GameServer, team_id: u32, obj_id: u32) -> bool {
        server.systems.social.teams.is_obj_member(team_id, obj_id)
    }

    fn team_members(&self, server: &GameServer, team_id: u32) -> (bool, Vec<u32>) {
        match server.systems.social.teams.active_team(team_id) {
            Some(t) => (
                t.is_party,
                t.members
                    .iter()
                    .flatten()
                    .map(|m| m.character.obj_id)
                    .collect(),
            ),
            None => (true, Vec::new()),
        }
    }

    fn evidence_from_damage(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        target: &WorldObject,
    ) {
        let criminal_owner = owner_character(server, caster);
        let victim_owner = owner_character(server, target);
        server.systems.social.crimes.generate_evidence_from_damage(
            server,
            caster,
            criminal_owner.as_ref(),
            victim_owner.as_ref(),
        );
    }

    fn apply_effect(
        &self,
        _server: &GameServer,
        _kind: EffectKind,
        template: &EffectTemplate,
        _args: &EffectArgs<'_>,
    ) -> bool {
        match template {
            EffectTemplate::ReportCrime { .. } => {
                // `ReportCrimeEffect.Apply` only logs (crimes are reported through the
                // evidence doodad funcs).
                tracing::warn!("ReportCrimeEffect");
                true
            }
            _ => false,
        }
    }

    fn report_bot(
        &self,
        server: &GameServer,
        suspect: &PlayerRef,
        reporter: &PlayerRef,
        msg: &str,
    ) -> bool {
        server.systems.social.crimes.report_bot(
            server,
            suspect,
            reporter,
            msg,
            chrono::Utc::now().timestamp_millis(),
        )
    }

    fn report_bot_expired(&self, server: &GameServer, player: &PlayerRef) -> bool {
        server
            .systems
            .social
            .crimes
            .report_bot_expired(server, player)
    }

    fn declare_dominion(
        &self,
        server: &GameServer,
        caster: &WorldObject,
        expedition_id: u32,
        lodestone: &WorldObject,
    ) {
        declare_dominion(server, caster, expedition_id, lodestone);
    }

    fn give_appellation(&self, _server: &GameServer, player: &PlayerRef, appellation_id: u32) {
        // CharacterAppellations.Add
        let added = {
            let mut c = player.data();
            if c.appellations.appellations.contains(&appellation_id) {
                tracing::warn!("Duplicate add {appellation_id}, ownerId {}", c.id);
                false
            } else {
                c.appellations.appellations.push(appellation_id);
                true
            }
        };
        if added {
            player.send(crate::systems::quests::packets::SCAppellationGainedPacket {
                appellation_id,
            });
        }
    }
}

/// `DeclareDominion` after the expedition / lodestone checks: the new `DominionData`, the
/// server-wide `SCDominionDataPacket(dominion, true, true)` and the caster's backpack consumed
/// (`Equipment.ConsumeItem(SkillReagents, backpack.TemplateId, 1, backpack)`, items hook).
fn declare_dominion(
    server: &GameServer,
    caster: &WorldObject,
    expedition_id: u32,
    lodestone: &WorldObject,
) {
    use super::dominion::{
        DominionData, DominionSiegeTimers, DominionTerritoryData, DominionUnkData,
    };
    let Some(house) = lodestone.as_house() else {
        return;
    };
    let now = chrono::Utc::now();
    let min = chrono::DateTime::<chrono::Utc>::MIN_UTC;
    let pos = match server.world.world_of(lodestone) {
        Some(w) => w.world_pos_rot(lodestone),
        None => lodestone.transform().local(),
    };
    let zone_key = lodestone.transform().zone_id;
    let zone_group = server
        .game_data
        .zones
        .zone_by_key(zone_key)
        .map_or(0, |z| z.group_id);
    let dominion = DominionData {
        house: house.data().id,
        x: pos.x,
        y: pos.y,
        z: pos.z,
        tax_rate: 50,
        reign_start_time: now,
        expedition_id,
        cur_house_tax_money: 500_000,
        cur_hunt_tax_money: 9000,
        peace_tax_money: 300_000,
        cur_house_tax_aa_point: 0,
        peace_tax_aa_point: 0,
        last_paid_time: now,
        last_siege_end_time: now,
        last_tax_rate_changed_time: now,
        last_national_tax_rate_changed_time: now,
        national_tax_rate: 500,
        national_monument_db_id: 0,
        national_monument_x: 0.0,
        national_monument_y: 0.0,
        national_monument_z: 0.0,
        territory_data: DominionTerritoryData {
            id: 6,
            id2: 4771,
            max_gates: 1,
            max_walls: 50,
            radius_declare: 250,
            radius_dominion: 110,
            radius_siege: 250,
            radius_offense_hq: 100,
        },
        siege_timers: DominionSiegeTimers {
            bdm: 0,
            durations: [0; 5],
            fixed: min,
            started: min,
            siege_period: 1,
            unk_data: DominionUnkData {
                obj_id: 4,
                ..DominionUnkData::default()
            },
            unk2_data: DominionUnkData::default(),
        },
        non_pvp_duration: 0,
        non_pvp_start: now,
        zone_id: zone_group as u16,
        obj_id: 0,
    };
    let packet = super::packets::misc::SCDominionDataPacket {
        dominion,
        newly_declared: true,
        final_data_by_request: true,
    };
    server.world.broadcast_to_server(&packet);
    if let Some(p) = caster.as_player() {
        let backpack = p
            .data()
            .inventory
            .equipment()
            .lock()
            .expect("container poisoned")
            .get_item_by_slot(crate::models::container::equip_slot::BACKPACK)
            .map(|i| i.id);
        match backpack {
            Some(item_id) => server
                .systems
                .skills
                .ext
                .items()
                .consume_item_by_id(server, p, item_id, 1),
            None => {
                tracing::error!("DeclareDominion: no backpack equipped (NullReferenceException)")
            }
        }
    }
}

/// The social code's [`SocialHooks`].
pub struct GameSocialHooks;

impl SocialHooks for GameSocialHooks {
    fn spawn_doodad(
        &self,
        server: &GameServer,
        instance_id: u32,
        template_id: u32,
        pos: (f32, f32, f32),
        rot_z: f32,
    ) -> Option<DoodadRef> {
        use crate::systems::npcs::doodad::spawner::{DoodadSpawner, DoodadSpawnerDef, SpawnerBase};
        let world = server.world.world(instance_id)?;
        let zone_id = server
            .game_data
            .worlds
            .get_zone_id(Some(&world.template), pos.0, pos.1);
        let mut base = SpawnerBase {
            unit_id: template_id,
            ..SpawnerBase::default()
        };
        base.position.world_id = world.template.id;
        base.position.zone_id = zone_id;
        base.position.x = pos.0;
        base.position.y = pos.1;
        base.position.z = pos.2;
        base.position.yaw = rot_z;
        let spawner = Arc::new(DoodadSpawner::new(
            DoodadSpawnerDef {
                base,
                scale: 1.0,
                count: 1,
                related_ids: None,
                respawn_doodad_template_id: 0,
            },
            Some(instance_id),
        ));
        spawner.spawn(server, 0)
    }

    fn terrain_height(&self, server: &GameServer, instance_id: u32, x: f32, y: f32) -> Option<f32> {
        // `GeoData.GetHeight(position)` (heightmap fallback of the navmesh); 0 = no height
        // known (the duel then keeps the challenger's Z).
        let world = server.world.world(instance_id)?;
        let z = crate::systems::npcs::npc::spawner::geo_data_height(server, &world, x, y);
        (z > 0.0).then_some(z)
    }

    fn create_player_doodad(
        &self,
        server: &GameServer,
        owner: &PlayerRef,
        template_id: u32,
        pos: (f32, f32, f32),
        rot_z: f32,
        item_template_id: u32,
        custom_data: i32,
    ) -> Option<DoodadRef> {
        doodads::create_player_doodad(
            server,
            owner,
            template_id,
            pos,
            rot_z,
            1.0,
            0,
            FARM_GROUP_INVALID,
            item_template_id,
            custom_data,
            true,
        )
    }

    fn evidence_func(&self, server: &GameServer, doodad: &DoodadRef) -> Option<EvidenceFunc> {
        use crate::systems::npcs::doodad::templates::FuncTemplate;
        let data = &server.game_data.systems.npcs.doodad;
        let group = doodad.data().func_group_id;
        let mut found = None;
        for f in data.doodad_funcs(group) {
            if f.func_type != "DoodadFuncEvidenceItemLoot" {
                continue;
            }
            if let Some(FuncTemplate::EvidenceItemLoot(e)) =
                data.func_template(f.func_id, &f.func_type)
            {
                found = Some(EvidenceFunc {
                    crime_kind: e.crime_kind_id as u8,
                    crime_value: e.crime_value,
                    next_phase: f.next_phase,
                });
            }
        }
        found
    }

    fn doodad_change_phase(
        &self,
        server: &GameServer,
        doodad: &DoodadRef,
        caster: &PlayerRef,
        next_phase: i32,
    ) {
        let obj = WorldObject::from(doodad);
        let Some(world) = server.world.world_of(&obj) else {
            return;
        };
        doodads::do_change_phase(
            server,
            &world,
            doodad,
            Some(&WorldObject::from(caster)),
            next_phase,
        );
    }

    // update_owned_housing_faction: HousingManager (housing subsystem).
    fn force_dismount(&self, server: &GameServer, player: &PlayerRef) {
        // `Character.ForceDismount()` (systems::vehicles)
        crate::systems::vehicles::force_dismount(
            server,
            player,
            crate::systems::vehicles::slaves::attach_reason::PREFAB_CHANGED,
        );
    }

    fn post_update_current_hp(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        old_hp: i32,
        new_hp: i32,
    ) {
        // `defendant.PostUpdateCurrentHp(defendant, oldHp, newHp, KillReason.PvpEnemy)`.
        let obj = WorldObject::from(player);
        crate::systems::skills::combat::post_update_current_hp(
            server,
            &obj,
            Some(&obj),
            old_hp,
            new_hp,
            crate::systems::skills::types::KillReason::PvpEnemy,
        );
    }

    fn judge_obj_id(&self, server: &GameServer, court_room_id: u32) -> u32 {
        let Some(spawns) = server.systems.npcs.spawns.get(DEFAULT_INSTANCE_ID) else {
            return 0;
        };
        let link = format!("justice.{court_room_id}.judge");
        spawns
            .get_special_npc_spawners("justice.")
            .into_iter()
            .find(|s| s.special_link == link)
            .and_then(|s| {
                s.spawned_npcs()
                    .into_values()
                    .next()
                    .and_then(|l| l.first().map(|n| n.obj_id))
            })
            .unwrap_or(0)
    }

    fn check_buff_tag(&self, server: &GameServer, player: &PlayerRef, tag: u32) -> bool {
        crate::systems::skills::buffs::check_buff_tag(server, &WorldObject::from(player), tag)
    }

    fn remove_buffs_by_tag(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        kind: BuffKind,
        count: i32,
        tag: u32,
    ) {
        crate::systems::skills::buffs::remove_buffs_by_kind(
            server,
            &WorldObject::from(player),
            match kind {
                BuffKind::Good => 1,
                BuffKind::Bad => 2,
                BuffKind::Hidden => 3,
                BuffKind::Other(o) => o,
            },
            count,
            tag,
        );
    }
}

// ---------------------------------------------------------------------------------------------
// Economy hooks (team loot rules)
// ---------------------------------------------------------------------------------------------

/// The social subsystem's `LootTeamHooks` (`TeamManager.GetActiveTeam`,
/// `Team.GetNextLootWinner`, `TeamManager.ChangeLootingRule`).
pub struct SocialForEconomy;

/// Installs the social subsystem's implementations of the economy hooks.
pub fn install_economy(server: &GameServer) {
    server
        .systems
        .economy
        .loot
        .hooks
        .teams
        .install(Arc::new(SocialForEconomy));
}

fn economy_rule(
    r: &super::team::LootingRule,
) -> crate::systems::economy::loot_packets::LootingRule {
    crate::systems::economy::loot_packets::LootingRule {
        loot_method: r.loot_method,
        minimum_grade: r.minimum_grade,
        loot_master: r.loot_master,
        roll_for_bind_on_pickup: r.roll_for_bind_on_pickup,
    }
}

impl crate::systems::economy::looting::LootTeamHooks for SocialForEconomy {
    fn active_team(
        &self,
        server: &GameServer,
        team_id: u32,
    ) -> Option<crate::systems::economy::looting::TeamLootInfo> {
        let team = server.systems.social.teams.active_team(team_id)?;
        Some(crate::systems::economy::looting::TeamLootInfo {
            team_id: team.id,
            members: team.online_members(),
            looting_rule: economy_rule(&team.looting_rule),
        })
    }

    fn next_loot_winner(
        &self,
        server: &GameServer,
        team_id: u32,
        eligible: &[PlayerRef],
        _loot_owner_obj_id: u32,
    ) -> Option<PlayerRef> {
        let ids: Vec<u32> = eligible.iter().map(|p| p.character_id).collect();
        server.systems.social.teams.next_loot_winner(team_id, &ids)
    }

    fn change_looting_rule(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        team_id: u32,
        change_flags: u8,
        rule: crate::systems::economy::loot_packets::LootingRule,
    ) {
        server.systems.social.teams.change_looting_rule(
            player,
            team_id,
            change_flags,
            super::team::LootingRule {
                loot_method: rule.loot_method,
                minimum_grade: rule.minimum_grade,
                loot_master: rule.loot_master,
                roll_for_bind_on_pickup: rule.roll_for_bind_on_pickup,
            },
        );
    }
}
