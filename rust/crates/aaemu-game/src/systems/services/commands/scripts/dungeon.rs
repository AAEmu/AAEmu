//! Player controls for inspecting and resetting their own dungeon bindings.

use super::prelude::*;
use crate::systems::instances::{dungeon, indun, worlds};

pub struct DungeonCommand;

impl Command for DungeonCommand {
    fn command_names(&self) -> Vec<String> {
        names(&["dungeon"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let player = ctx.player()?;
        let result = (|| -> anyhow::Result<()> {
            match args.first().map(String::as_str) {
                Some("status") if args.len() == 1 => {
                    let runs: Vec<_> = server
                        .systems
                        .instances
                        .all_dungeons()
                        .into_iter()
                        .filter(|d| {
                            !d.is_system
                                && (d.has_access(player.character_id)
                                    || dungeon::player_in_same_team(server, d, &player))
                        })
                        .collect();
                    if runs.is_empty() {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            "You have no dungeon bindings.",
                        );
                    }
                    for d in runs {
                        let zone = server
                            .game_data
                            .zones
                            .zone_by_key(d.zone_instance_id.zone_id)
                            .map_or(0, |z| z.id);
                        let players = d.world(server).map_or(0, |w| worlds::character_count(&w));
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "{}: zone {zone}, instance {}, {players}/{} players",
                                d.indun_zone,
                                d.zone_instance_id.instance_id,
                                d.indun_zone.max_players
                            ),
                        );
                    }
                }
                Some("reset") if args.len() == 2 => {
                    let zone = try_parse::<u32>(&args[1])
                        .ok_or_else(|| anyhow::anyhow!("Invalid dungeon zone id"))?;
                    indun::reset_dungeon_instance(server, &player, zone)?;
                    CommandManager::send_normal_text(
                        self,
                        out,
                        "Dungeon reset. Use its entrance to start a fresh run.",
                    );
                }
                _ => anyhow::bail!("Usage: /dungeon status | /dungeon reset <zone id>"),
            }
            Ok(())
        })();
        if let Err(error) = result {
            CommandManager::send_error_text(self, out, &error.to_string());
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "status | reset <zone id>".into()
    }
    fn get_command_help_text(&self) -> String {
        "Inspect your runs or reset an empty dungeon as its owner or party leader. Enter through the dungeon entrance.".into()
    }
}
