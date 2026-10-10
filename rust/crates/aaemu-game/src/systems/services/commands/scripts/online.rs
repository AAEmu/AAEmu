//! `Scripts/Commands/Online.cs`.

use super::prelude::*;
use crate::systems::social::faction::{relation_state, relation_state as rs};

/// `Online`.
pub struct Online;

impl Online {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Online {
    fn command_names(&self) -> Vec<String> {
        names(&["online", "list_online"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let characters = server.world.all_characters();
        let Some(filter) = args.first() else {
            CommandManager::send_normal_text(
                self,
                out,
                &format!("{} players online.", characters.len()),
            );
            return Ok(());
        };
        let me = ctx.player()?;
        let (my_id, my_faction) = {
            let c = me.data();
            (c.id, c.unit.faction_id)
        };
        let filter_lower = filter.to_lowercase();
        let mut final_message = String::new();
        let mut show_count = 0usize;
        for online in &characters {
            let (id, name, faction) = {
                let c = online.data();
                (c.id, c.unit.name.clone(), c.unit.faction_id)
            };
            if !name.to_lowercase().contains(&filter_lower) && filter != "*" {
                continue;
            }
            if !final_message.trim().is_empty() {
                final_message += ", ";
            }
            let mut faction_color = match relation_state(
                &server.game_data.factions,
                &server.game_data.systems.social.relations,
                my_faction,
                faction,
            ) {
                rs::HOSTILE => "FFFF4444",
                rs::NEUTRAL => "FF888888",
                rs::FRIENDLY => "FF4444FF",
                _ => "FFFFFFFF",
            };
            if id == my_id {
                faction_color = "FFFFFFFF";
            }
            final_message += &format!("|c{faction_color}{name}|r");
            show_count += 1;
            if show_count > 100 {
                break;
            }
        }
        out.send_message(&final_message);
        CommandManager::send_normal_text(
            self,
            out,
            &if show_count != characters.len() {
                format!("Showing {show_count}/{} online players.", characters.len())
            } else {
                format!("{} players online.", characters.len())
            },
        );
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[filter]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Lists the number of online players. If a filter or * is provided, it will be used as a filter to show up to 100 names as well.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn online_counts_and_filters() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let _a = harness::pc(&server, 2, "Alice", 120.0, 100.0);
        let _b = harness::pc(&server, 3, "Bob", 140.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "online").await;
        assert_eq!(out, ["[online] 3 players online."]);
        let (_, out) = harness::run(&server, &gm.p, "list_online AL").await;
        // Unknown factions (empty game data) are Neutral.
        assert_eq!(
            out,
            ["|cFF888888Alice|r", "[online] Showing 1/3 online players."]
        );
        let (_, out) = harness::run(&server, &gm.p, "online *").await;
        assert_eq!(out.len(), 2);
        assert!(out[0].contains("|cFFFFFFFFGm|r"));
        assert_eq!(out[1], "[online] 3 players online.");
    }
}
