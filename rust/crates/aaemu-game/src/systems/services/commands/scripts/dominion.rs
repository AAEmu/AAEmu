//! Explicit siege scheduling for the 1.2 world server. Historical static plans are not timers.

use super::prelude::*;
use crate::systems::housing::dominion::{runtime, SiegeSide};

pub struct Dominion;

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
