//! Explicit siege scheduling for the 1.2 world server. Historical static plans are not timers.

use super::prelude::*;
use crate::systems::housing::dominion::{runtime, SiegeSide};

pub struct Dominion;

/// Player commands only change the requesting character's scheduled siege participation.
pub struct JoinSiege;
pub struct LeaveSiege;

impl JoinSiege {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl LeaveSiege {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for JoinSiege {
    fn command_names(&self) -> Vec<String> {
        names(&["joinsiege"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        participation(self, ctx, args, out, true)
    }

    fn get_command_line_help(&self) -> String {
        "[zoneGroupId]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Join your expedition's scheduled siege. Defaults to your current territory; your guild rank must permit siege participation.".into()
    }
}

impl Command for LeaveSiege {
    fn command_names(&self) -> Vec<String> {
        names(&["leavesiege"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        participation(self, ctx, args, out, false)
    }

    fn get_command_line_help(&self) -> String {
        "[zoneGroupId]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Leave a scheduled siege as your own character. Defaults to your current territory.".into()
    }
}

fn participation(
    command: &dyn Command,
    ctx: &CommandContext<'_>,
    args: &[String],
    out: &mut dyn MessageOutput,
    join: bool,
) -> anyhow::Result<()> {
    if args.len() > 1
        || args
            .first()
            .is_some_and(|value| try_parse::<u32>(value).is_none())
    {
        CommandManager::send_default_help_text(command, out);
        return Ok(());
    }
    let server = ctx.server()?;
    let player = ctx.player()?;
    let object = WorldObject::from(&player);
    let (online, expedition_id, transform) = {
        let player = player.data();
        (
            player.is_online,
            player.unit.expedition_id,
            player.unit.transform,
        )
    };
    if !online || !runtime::is_live_object(server, &object) {
        CommandManager::send_error_text(
            command,
            out,
            "You must be in the world to change siege participation",
        );
        return Ok(());
    }
    let current_zone = server
        .game_data
        .zones
        .zone_by_key(transform.zone_id)
        .or_else(|| {
            let world = server.world.world_of(&object)?;
            let position = world.world_pos_rot(&object);
            server
                .game_data
                .zones
                .zone_by_key(world.template.zone_id_at(position.x, position.y))
        })
        .map(|zone| zone.group_id);
    let Some(zone) = args
        .first()
        .and_then(|value| try_parse::<u32>(value))
        .or(current_zone)
    else {
        CommandManager::send_error_text(
            command,
            out,
            "Your current location has no siege territory",
        );
        return Ok(());
    };
    let _operation = server
        .systems
        .housing
        .operation_lock
        .lock()
        .expect("housing poisoned");
    let manager = &server.systems.housing.dominions;
    let Some(previous) = manager.get(zone) else {
        CommandManager::send_error_text(command, out, "This territory has no scheduled siege");
        return Ok(());
    };
    let now = chrono::Utc::now().timestamp();
    let Some(siege) = previous.siege.as_ref().filter(|siege| {
        siege.status != crate::systems::housing::dominion::SiegeStatus::Finished
            && now < siege.ends_at
    }) else {
        CommandManager::send_error_text(
            command,
            out,
            "This territory has no unfinished scheduled siege",
        );
        return Ok(());
    };
    let result = if join {
        let current_expedition = server
            .expeditions
            .expedition_of_character(player.character_id);
        let Some(expedition) = server
            .expeditions
            .get(current_expedition)
            .filter(|expedition| {
                !expedition.is_disbanded
                    && current_expedition != 0
                    && expedition_id == current_expedition
            })
        else {
            CommandManager::send_error_text(
                command,
                out,
                "You must be a current expedition member to join its siege",
            );
            return Ok(());
        };
        let side = if current_expedition == siege.attacker_expedition_id {
            SiegeSide::Attacker
        } else if current_expedition == siege.defender_expedition_id {
            SiegeSide::Defender
        } else {
            CommandManager::send_error_text(
                command,
                out,
                "Your expedition is not attacking or defending this territory",
            );
            return Ok(());
        };
        let allowed = expedition
            .member(player.character_id)
            .and_then(|member| expedition.policy_by_role(member.role))
            .is_some_and(|policy| policy.join_siege);
        if !allowed {
            CommandManager::send_error_text(
                command,
                out,
                "Your expedition rank does not permit siege participation",
            );
            return Ok(());
        }
        manager.register_participant(zone, player.character_id, current_expedition, side, now)
    } else {
        match manager.unregister_participant(zone, player.character_id) {
            Ok(removed) => {
                if removed {
                    runtime::abort_character(server, player.character_id);
                }
                manager
                    .get(zone)
                    .ok_or(crate::systems::housing::dominion::DominionError::Missing)
            }
            Err(error) => Err(error),
        }
    };
    match result {
        Ok(state) => {
            crate::systems::social::dominion::broadcast_participant_changes(
                server, &previous, &state,
            );
            crate::systems::social::dominion::broadcast_snapshot(server, &state, false);
            crate::systems::housing::dominion::participants::sync_player(server, &player);
            CommandManager::send_normal_text(
                command,
                out,
                &format!(
                    "{} siege for territory {zone}",
                    if join { "Joined" } else { "Left" }
                ),
            );
        }
        Err(error) => CommandManager::send_error_text(command, out, &error.to_string()),
    }
    Ok(())
}

/// Player entry point; administrative siege operations remain on the GM-only command.
pub struct CollectDominion;

impl CollectDominion {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for CollectDominion {
    fn command_names(&self) -> Vec<String> {
        names(&["collectdominion"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.len() != 1 || try_parse::<u32>(&args[0]).is_none() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        Dominion.execute(ctx, &["collect".into(), args[0].clone()], out)
    }

    fn get_command_line_help(&self) -> String {
        "<zoneGroupId>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Collect property tax revenue by mail as the current owning expedition's leader.".into()
    }
}

impl Dominion {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Dominion {
    fn command_names(&self) -> Vec<String> {
        names(&["dominion"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let manager = &server.systems.housing.dominions;
        if args.is_empty() || args[0] == "info" {
            for state in manager.all() {
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "Zone {}: expedition {}, tower {}, tax {}%, siege {:?}",
                        state.zone_group_id,
                        state.owner_expedition_id,
                        state.tower.house_id,
                        state.tax_rate,
                        state.siege.as_ref().map(|s| (
                            s.siege_id,
                            s.status,
                            s.starts_at,
                            s.ends_at
                        ))
                    ),
                );
            }
            return Ok(());
        }
        let Some(zone) = args.get(1).and_then(|v| try_parse::<u32>(v)) else {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        };
        let _operation = server
            .systems
            .housing
            .operation_lock
            .lock()
            .expect("housing poisoned");
        let previous = manager.get(zone);
        let now = chrono::Utc::now().timestamp();
        let result = match args[0].as_str() {
            "collect" => {
                crate::systems::housing::dominion::tax::collect_locked(server, &ctx.player()?, zone)
            }
            "headquarters" => {
                let side = match args.get(2).map(String::as_str) {
                    Some("attacker") => SiegeSide::Attacker,
                    Some("defender") => SiegeSide::Defender,
                    _ => {
                        CommandManager::send_default_help_text(self, out);
                        return Ok(());
                    }
                };
                let Some(id) = args.get(3).and_then(|value| try_parse::<u32>(value)) else {
                    CommandManager::send_default_help_text(self, out);
                    return Ok(());
                };
                let player = ctx.player()?;
                let Some(doodad) = server
                    .world
                    .world_of(&crate::world::WorldObject::from(&player))
                    .and_then(|world| world.doodad(id))
                else {
                    CommandManager::send_error_text(
                        self,
                        out,
                        "Headquarters doodad is not in your world",
                    );
                    return Ok(());
                };
                if let Err(error) = server
                    .systems
                    .housing
                    .headquarters
                    .bind(server, zone, side, &doodad)
                {
                    CommandManager::send_error_text(self, out, &error.to_string());
                    return Ok(());
                }
                manager
                    .get(zone)
                    .ok_or(crate::systems::housing::dominion::DominionError::Missing)
            }
            "schedule" => {
                let Some(attacker) = args.get(2).and_then(|v| try_parse::<u32>(v)) else {
                    CommandManager::send_default_help_text(self, out);
                    return Ok(());
                };
                if server.expeditions.get(attacker).is_none() {
                    CommandManager::send_error_text(
                        self,
                        out,
                        "Attacking expedition does not exist",
                    );
                    return Ok(());
                }
                let Some(delay) = args.get(3).and_then(|v| try_parse::<u32>(v)) else {
                    CommandManager::send_default_help_text(self, out);
                    return Ok(());
                };
                let Some(template) = server
                    .game_data
                    .systems
                    .housing
                    .castles
                    .siege_zones
                    .get(&zone)
                else {
                    CommandManager::send_error_text(
                        self,
                        out,
                        "Zone has no original siege timing data",
                    );
                    return Ok(());
                };
                let duration = i64::from(template.siege_days) * 86400
                    + i64::from(template.siege_hours) * 3600
                    + i64::from(template.siege_mins) * 60;
                let Some(start) = now.checked_add(i64::from(delay)) else {
                    return Ok(());
                };
                let Some(end) = start.checked_add(duration) else {
                    return Ok(());
                };
                manager.schedule_siege(zone, attacker, start, end, now)
            }
            "register" => {
                let Some(name) = args.get(2) else {
                    CommandManager::send_default_help_text(self, out);
                    return Ok(());
                };
                let character = server
                    .names
                    .get_character_id(&crate::managers::name::normalize_name(name));
                let expedition = server.expeditions.expedition_of_character(character);
                let side = match args.get(3).map(String::as_str) {
                    Some("attacker") => SiegeSide::Attacker,
                    Some("defender") => SiegeSide::Defender,
                    _ => {
                        CommandManager::send_default_help_text(self, out);
                        return Ok(());
                    }
                };
                manager.register_participant(zone, character, expedition, side, now)
            }
            "unregister" => {
                let Some(name) = args.get(2) else {
                    CommandManager::send_default_help_text(self, out);
                    return Ok(());
                };
                let character = server
                    .names
                    .get_character_id(&crate::managers::name::normalize_name(name));
                runtime::abort_character(server, character);
                match manager.unregister_participant(zone, character) {
                    Ok(_) => manager
                        .get(zone)
                        .ok_or(crate::systems::housing::dominion::DominionError::Missing),
                    Err(e) => Err(e),
                }
            }
            "cancel" => manager.cancel_siege(zone, now),
            _ => {
                CommandManager::send_default_help_text(self, out);
                return Ok(());
            }
        };
        match result {
            Ok(state) => {
                if args[0] == "schedule" {
                    crate::systems::social::dominion::broadcast_siege_declared(server, &state);
                }
                if let Some(previous) = &previous {
                    crate::systems::social::dominion::broadcast_participant_changes(
                        server, previous, &state,
                    );
                }
                runtime::apply_tower_state(server, &state);
                if matches!(args[0].as_str(), "schedule" | "cancel") {
                    crate::systems::social::dominion::broadcast_phase_change(server, &state);
                } else {
                    crate::systems::social::dominion::broadcast_snapshot(server, &state, false);
                }
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("Updated dominion zone {}", state.zone_group_id),
                );
            }
            Err(e) => CommandManager::send_error_text(self, out, &e.to_string()),
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "info | collect <zoneGroupId> | schedule <zoneGroupId> <attackerExpeditionId> <startDelaySeconds> | register <zoneGroupId> <character> attacker|defender | unregister <zoneGroupId> <character> | headquarters <zoneGroupId> attacker|defender <doodadObjectId> | cancel <zoneGroupId>".into()
    }
    fn get_command_help_text(&self) -> String {
        "Manage explicit siege schedules and participants using the original zone duration. Collect actual property tax revenue as the current guild leader. Bind an authored headquarters in its current siege phase; bindings must be restored after server restart.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness::{self, Pc};
    use super::*;
    use crate::data::world_data::{SystemFaction, Zone};
    use crate::data::GameData;
    use crate::models::house::House;
    use crate::opcodes::sc;
    use crate::systems::housing::data::{GuardTowerSetting, HousingTemplate};
    use crate::systems::housing::dominion::{DominionError, DominionState};
    use crate::systems::skills::data::BuffTemplate;
    use crate::systems::social::expedition::{
        member_from_character, Expedition, ExpeditionRolePolicy,
    };
    use crate::test_support::TEST_ZONE;
    use crate::world::{Entity, Player};
    use aaemu_commons::PacketStream;

    const ZONE: u32 = 25;

    struct Fixture {
        server: Arc<GameServer>,
        attacker: Pc,
        defender: Pc,
        outsider: Pc,
    }

    fn fixture() -> Fixture {
        let mut data = GameData::empty();
        data.zones.zones.insert(
            TEST_ZONE,
            Zone {
                zone_key: TEST_ZONE,
                group_id: ZONE,
                ..Default::default()
            },
        );
        data.zones.zones.insert(
            TEST_ZONE + 1,
            Zone {
                zone_key: TEST_ZONE + 1,
                group_id: ZONE + 1,
                ..Default::default()
            },
        );
        data.systems.housing.add_template(HousingTemplate {
            id: 139,
            guard_tower_setting_id: 6,
            ..Default::default()
        });
        data.systems.housing.castles.guard_tower_settings.insert(
            6,
            GuardTowerSetting {
                id: 6,
                radius_dominion: 200,
                ..Default::default()
            },
        );
        for id in [3733, 3734] {
            data.systems.skills.add_buff(BuffTemplate {
                id,
                max_stack: 1,
                system: true,
                dead_applicable: true,
                ..Default::default()
            });
        }
        let server = harness::server_with(data);
        server.systems.services.commands.access_levels.load(&[
            ("joinsiege".into(), 0),
            ("leavesiege".into(), 0),
            ("dominion".into(), 100),
        ]);
        let mut attacker = harness::pc(&server, 1, "Attacker", 100.0, 100.0);
        let mut defender = harness::pc(&server, 2, "Defender", 100.0, 100.0);
        let mut outsider = harness::pc(&server, 3, "Outsider", 100.0, 100.0);
        for (id, pc) in [(20, &attacker), (10, &defender), (30, &outsider)] {
            pc.p.data().access_level = 0;
            pc.p.data().unit.expedition_id = id;
            server.expeditions.add(Expedition {
                faction: SystemFaction {
                    id,
                    owner_id: 90 + id,
                    ..Default::default()
                },
                members: vec![member_from_character(&server, id, &pc.p, false)],
                policies: vec![ExpeditionRolePolicy {
                    expedition_id: id,
                    role: 0,
                    name: "Member".into(),
                    dominion_declare: false,
                    invite: false,
                    expel: false,
                    promote: false,
                    dismiss: false,
                    chat: true,
                    manager_chat: false,
                    siege_master: false,
                    join_siege: true,
                }],
                removed_members: Vec::new(),
                is_disbanded: false,
            });
        }
        let mut house = House::new(61, 139);
        house.current_step = -1;
        house.unit.transform.zone_id = TEST_ZONE;
        house.unit.transform.set_xyz(100.0, 100.0, 100.0);
        let tower = Entity::new(0x200, house);
        server.systems.housing.manager.insert(&tower);
        server
            .world
            .world(0)
            .unwrap()
            .spawn(&tower, &server.view_ctx());
        let (_, identity) = runtime::tower_identity(&server, &tower).unwrap();
        let now = chrono::Utc::now().timestamp();
        server
            .systems
            .housing
            .dominions
            .declare_unowned_with(ZONE, identity, 10, now - 1000, || true)
            .unwrap();
        server
            .systems
            .housing
            .dominions
            .schedule_siege(ZONE, 20, now - 10, now + 3600, now - 20)
            .unwrap();
        attacker.frames();
        defender.frames();
        outsider.frames();
        Fixture {
            server,
            attacker,
            defender,
            outsider,
        }
    }

    fn state(f: &Fixture) -> DominionState {
        f.server.systems.housing.dominions.get(ZONE).unwrap()
    }

    fn member_body(expedition: u32, character: u32, added: bool) -> Vec<u8> {
        let mut stream = PacketStream::new();
        stream
            .write_u16(ZONE as u16)
            .write_u32(expedition)
            .write_u32(character)
            .write_bool(added);
        stream.into_bytes()
    }

    #[tokio::test]
    async fn ordinary_members_join_their_own_sides_and_replay_has_no_member_delta() {
        let mut f = fixture();
        let (_, out) = harness::run_at(&f.server, &f.attacker.p, "joinsiege", 0).await;
        assert_eq!(
            out,
            [harness::normal(
                "joinsiege",
                "Joined siege for territory 25"
            )]
        );
        let packets = f.attacker.packets();
        let member = packets
            .iter()
            .position(|packet| packet.0 == sc::SCSiegeMemberPacket)
            .unwrap();
        let snapshot = packets
            .iter()
            .position(|packet| packet.0 == sc::SCDominionDataPacket)
            .unwrap();
        assert!(member < snapshot);
        assert_eq!(packets[member].1, member_body(20, 1, true));
        assert!(!packets
            .iter()
            .any(|packet| packet.0 == sc::SCSiegeStatePacket));
        assert!(f.attacker.p.data().unit.buffs.check_buff(3733));
        f.defender.frames();
        harness::run_at(&f.server, &f.defender.p, "joinsiege 25", 0).await;
        assert!(f.defender.p.data().unit.buffs.check_buff(3734));
        let before = state(&f);
        assert_eq!(
            before.siege.as_ref().unwrap().participants[&1].side,
            SiegeSide::Attacker
        );
        assert_eq!(
            before.siege.as_ref().unwrap().participants[&2].side,
            SiegeSide::Defender
        );
        f.attacker.frames();
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert_eq!(state(&f), before);
        let replay = f.attacker.packets();
        assert!(!replay
            .iter()
            .any(|packet| packet.0 == sc::SCSiegeMemberPacket
                || packet.0 == sc::SCSiegeStatePacket
                || packet.0 == sc::SCBuffCreatedPacket));
        assert_eq!(
            replay
                .iter()
                .filter(|packet| packet.0 == sc::SCDominionDataPacket)
                .count(),
            1
        );
        let (_, denied) = harness::run_at(&f.server, &f.attacker.p, "dominion cancel 25", 0).await;
        assert_eq!(denied, ["|cFFFF0000Insufficient privileges.|r"]);
        assert_eq!(state(&f), before);
    }

    #[tokio::test]
    async fn names_or_side_arguments_never_delegate_registration_or_removal() {
        let f = fixture();
        for command in [
            "joinsiege 25 Defender",
            "joinsiege Defender",
            "joinsiege 25 defender",
            "leavesiege 25 Attacker",
        ] {
            harness::run_at(&f.server, &f.attacker.p, command, 0).await;
            assert!(state(&f).siege.as_ref().unwrap().participants.is_empty());
        }
        harness::run_at(&f.server, &f.defender.p, "joinsiege 25", 0).await;
        harness::run_at(&f.server, &f.attacker.p, "leavesiege 25", 0).await;
        assert!(state(&f)
            .siege
            .as_ref()
            .unwrap()
            .participants
            .contains_key(&2));
        assert!(!state(&f)
            .siege
            .as_ref()
            .unwrap()
            .participants
            .contains_key(&1));
    }

    #[tokio::test]
    async fn unrelated_stale_or_rank_ineligible_members_cannot_join() {
        let f = fixture();
        let empty = state(&f);
        let (_, out) = harness::run_at(&f.server, &f.outsider.p, "joinsiege 25", 0).await;
        assert!(out[0].contains("not attacking or defending"));
        assert_eq!(state(&f), empty);
        f.attacker.p.data().unit.expedition_id = 10;
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert_eq!(state(&f), empty);
        f.attacker.p.data().unit.expedition_id = 20;
        f.server
            .expeditions
            .with_mut(20, |guild| guild.policies[0].join_siege = false)
            .unwrap();
        let (_, out) = harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert!(out[0].contains("rank does not permit"));
        assert_eq!(state(&f), empty);
        f.server
            .expeditions
            .with_mut(20, |guild| guild.policies.clear())
            .unwrap();
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert_eq!(state(&f), empty);
        f.server
            .expeditions
            .with_mut(20, |guild| guild.members.clear())
            .unwrap();
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert_eq!(state(&f), empty);
    }

    #[tokio::test]
    async fn leave_removes_only_self_and_cancels_channel_even_after_policy_or_guild_changes() {
        let mut f = fixture();
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        harness::run_at(&f.server, &f.defender.p, "joinsiege 25", 0).await;
        let authority = &f.server.systems.housing.dominions;
        let now = chrono::Utc::now().timestamp();
        let tower = state(&f).tower;
        authority.mark_tower_destroyed(ZONE, tower, now).unwrap();
        let handle = authority
            .begin_engraving(ZONE, tower, 1, 20, now * 1000)
            .unwrap();
        f.server
            .expeditions
            .with_mut(20, |guild| {
                guild.policies[0].join_siege = false;
                guild.members.clear();
            })
            .unwrap();
        f.attacker.p.data().unit.expedition_id = 0;
        f.attacker.frames();
        let (_, out) = harness::run_at(&f.server, &f.attacker.p, "leavesiege", 0).await;
        assert_eq!(
            out,
            [harness::normal("leavesiege", "Left siege for territory 25")]
        );
        assert!(!f.attacker.p.data().unit.buffs.check_buff(3733));
        assert!(f.defender.p.data().unit.buffs.check_buff(3734));
        let packets = f.attacker.packets();
        let member = packets
            .iter()
            .position(|packet| packet.0 == sc::SCSiegeMemberPacket)
            .unwrap();
        let snapshot = packets
            .iter()
            .position(|packet| packet.0 == sc::SCDominionDataPacket)
            .unwrap();
        assert!(member < snapshot);
        assert_eq!(packets[member].1, member_body(20, 1, false));
        assert_eq!(
            authority.complete_engraving(&handle, 20, handle.ready_at_ms),
            Err(DominionError::StaleHandle)
        );
        let before = state(&f);
        assert_eq!(before.siege.as_ref().unwrap().participants.len(), 1);
        assert!(before.siege.as_ref().unwrap().participants.contains_key(&2));
        harness::run_at(&f.server, &f.attacker.p, "leavesiege 25", 0).await;
        assert_eq!(state(&f), before);
        assert!(!f
            .attacker
            .packets()
            .iter()
            .any(|packet| packet.0 == sc::SCSiegeMemberPacket));
    }

    #[tokio::test]
    async fn per_side_capacity_rejects_new_members_but_allows_existing_replay_and_other_side() {
        let f = fixture();
        let authority = &f.server.systems.housing.dominions;
        let now = chrono::Utc::now().timestamp();
        for character in 1000..1100 {
            authority
                .register_participant(ZONE, character, 20, SiegeSide::Attacker, now)
                .unwrap();
        }
        let full = state(&f);
        let (_, out) = harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert!(out[0].contains("100-character capacity"));
        assert_eq!(state(&f), full);
        harness::run_at(&f.server, &f.defender.p, "joinsiege 25", 0).await;
        assert!(state(&f)
            .siege
            .as_ref()
            .unwrap()
            .participants
            .contains_key(&2));
        authority.unregister_participant(ZONE, 1000).unwrap();
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        let rejoined = state(&f);
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert_eq!(state(&f), rejoined);
        assert_eq!(
            rejoined
                .siege
                .as_ref()
                .unwrap()
                .participants
                .values()
                .filter(|participant| participant.side == SiegeSide::Attacker)
                .count(),
            100
        );
    }

    #[tokio::test]
    async fn remote_explicit_registration_and_warmup_do_not_grant_active_zone_buffs() {
        let f = fixture();
        f.attacker.p.data().unit.transform.zone_id = TEST_ZONE + 1;
        let (_, out) = harness::run_at(&f.server, &f.attacker.p, "joinsiege", 0).await;
        assert!(out[0].contains("no scheduled siege"));
        assert!(state(&f).siege.as_ref().unwrap().participants.is_empty());
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert!(state(&f)
            .siege
            .as_ref()
            .unwrap()
            .participants
            .contains_key(&1));
        assert!(!f.attacker.p.data().unit.buffs.check_buff(3733));
        harness::run_at(&f.server, &f.attacker.p, "leavesiege 25", 0).await;
        assert!(state(&f).siege.as_ref().unwrap().participants.is_empty());
        f.attacker.p.data().unit.transform.zone_id = TEST_ZONE;
        let mut warmup = state(&f);
        warmup.siege.as_mut().unwrap().starts_at = chrono::Utc::now().timestamp() + 100;
        f.server
            .systems
            .housing
            .dominions
            .restore(vec![warmup])
            .unwrap();
        harness::run_at(&f.server, &f.attacker.p, "joinsiege", 0).await;
        assert!(state(&f)
            .siege
            .as_ref()
            .unwrap()
            .participants
            .contains_key(&1));
        assert!(!f.attacker.p.data().unit.buffs.check_buff(3733));
    }

    #[tokio::test]
    async fn no_schedule_expired_or_finished_sieges_reject_join_and_leave() {
        let f = fixture();
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 99", 0).await;
        assert!(state(&f).siege.as_ref().unwrap().participants.is_empty());
        let mut expired = state(&f);
        let siege = expired.siege.as_mut().unwrap();
        siege.scheduled_at = chrono::Utc::now().timestamp() - 100;
        siege.starts_at = siege.scheduled_at + 10;
        siege.ends_at = siege.starts_at + 10;
        f.server
            .systems
            .housing
            .dominions
            .restore(vec![expired.clone()])
            .unwrap();
        for command in ["joinsiege 25", "leavesiege 25"] {
            let (_, out) = harness::run_at(&f.server, &f.attacker.p, command, 0).await;
            assert!(out[0].contains("no unfinished scheduled siege"));
            assert_eq!(state(&f), expired);
        }
        let f = fixture();
        f.server
            .systems
            .housing
            .dominions
            .cancel_siege(ZONE, chrono::Utc::now().timestamp())
            .unwrap();
        let finished = state(&f);
        for command in ["joinsiege", "leavesiege"] {
            harness::run_at(&f.server, &f.attacker.p, command, 0).await;
            assert_eq!(state(&f), finished);
        }
    }

    #[tokio::test]
    async fn offline_replaced_or_out_of_world_player_handles_never_enroll() {
        let f = fixture();
        let empty = state(&f);
        f.attacker.p.data().is_online = false;
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert_eq!(state(&f), empty);
        f.attacker.p.data().is_online = true;
        let replacement = Player::new(f.attacker.p.data().clone(), f.attacker.client.conn.handle());
        let world = f.server.world.world(0).unwrap();
        world.remove_object(f.attacker.p.obj_id);
        world.add_object(&replacement);
        harness::run_at(&f.server, &f.attacker.p, "joinsiege 25", 0).await;
        assert_eq!(state(&f), empty);
        world.remove_object(replacement.obj_id);
        harness::run_at(&f.server, &replacement, "joinsiege 25", 0).await;
        assert_eq!(state(&f), empty);
    }
}
