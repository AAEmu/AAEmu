//! Tower defense operator controls (extends the old packet-only `TowerDef.cs` command).

use super::prelude::*;
use crate::systems::instances::tower_def::{self, EventKey};

pub struct TowerDef;

impl TowerDef {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TowerDef {
    fn command_names(&self) -> Vec<String> {
        names(&["towerdef", "tower_def"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let Some(action) = args.first() else {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        };
        let server = ctx.server()?;
        let player = ctx.player()?;
        let zone_id = player.data().unit.transform.zone_id;
        let result = (|| -> anyhow::Result<()> {
            match action.to_lowercase().as_str() {
                "list" | "status" => {
                    if args.len() != 1 {
                        anyhow::bail!("Usage: /towerdef list");
                    }
                    let mut ids: Vec<_> = server
                        .game_data
                        .systems
                        .instances
                        .tower_defs
                        .tower_defs
                        .keys()
                        .copied()
                        .collect();
                    ids.sort_unstable();
                    let world_id = player.data().unit.transform.instance_id;
                    let location = EventKey::at_player(server, &player, 0)?;
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "World {world_id}, zone {zone_id}, zone group {}",
                            location.zone_group_id
                        ),
                    );
                    let active = tower_def::infos(server, world_id);
                    tower_def::send_list(server, &player);
                    for id in ids {
                        let key = EventKey::at_player(server, &player, id)?;
                        let status = if let Some(info) = active.iter().find(|i| {
                            i.key.tower_def_id == id && i.key.zone_group_id == key.zone_group_id
                        }) {
                            format!("running, wave {}", info.current_step)
                        } else {
                            match tower_def::validate(server, key, zone_id) {
                                Ok(()) => "ready".to_string(),
                                Err(error) => format!("unavailable: {error}"),
                            }
                        };
                        CommandManager::send_normal_text(self, out, &format!("{id}: {status}"));
                    }
                }
                "start" | "end" | "next" => {
                    let expected = if action.eq_ignore_ascii_case("next") {
                        3
                    } else {
                        2
                    };
                    if args.len() != expected {
                        anyhow::bail!("Usage: /towerdef start <id>, end <id>, next <id> <wave>");
                    }
                    let id = try_parse::<u32>(&args[1])
                        .ok_or_else(|| anyhow::anyhow!("Invalid tower defense id {}", args[1]))?;
                    let key = EventKey::at_player(server, &player, id)?;
                    match action.to_lowercase().as_str() {
                        "start" => tower_def::start(server, key, zone_id)?,
                        "end" => tower_def::end(server, key)?,
                        _ => {
                            let step = try_parse::<u32>(&args[2])
                                .ok_or_else(|| anyhow::anyhow!("Invalid wave {}", args[2]))?;
                            tower_def::next_wave(server, key, step)?;
                        }
                    }
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!("Tower defense {id}: {}", action.to_lowercase()),
                    );
                }
                _ => anyhow::bail!("Unknown tower defense action {action}"),
            }
            Ok(())
        })();
        if let Err(error) = result {
            CommandManager::send_error_text(self, out, &error.to_string());
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "list | start <id> | end <id> | next <id> <wave>".into()
    }
    fn get_command_help_text(&self) -> String {
        "Control events in your world and zone group; waves start at 1. Automatic schedules use Dungeons.TowerDefense.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn tower_def_rejects_bad_arguments_without_sending_event_packets() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        for command in [
            "towerdef start",
            "towerdef next 12",
            "towerdef start x",
            "towerdef next 12 x",
            "towerdef Foo",
        ] {
            let (_, out) = harness::run(&server, &gm.p, command).await;
            assert_eq!(out.len(), 1, "{command}");
            assert!(out[0].contains("|cFFFF0000"), "{out:?}");
            assert!(gm.packets().is_empty());
        }
    }
}
