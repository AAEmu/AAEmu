//! `Scripts/Commands/Run.cs`.

use futures::future::BoxFuture;

use super::prelude::*;
use crate::systems::skills::buffs;
use crate::systems::skills::types::SkillCaster;
use crate::world::now_ms;

/// `Character.SendDebugMessage(message)`: a system chat line when `DebugInfo` is enabled and
/// the character's effective access level reaches `DebugInfoLevel`.
pub async fn send_debug_message(server: &GameServer, p: &PlayerRef, message: &str) {
    if server.config.debug_info
        && crate::systems::services::character::effective_access_level(server, p).await
            >= server.config.debug_info_level as i32
    {
        send_player_message(p, message);
    }
}

/// `Run`.
pub struct Run;

impl Run {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Run {
    fn command_names(&self) -> Vec<String> {
        names(&["run"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("Run runs through execute_async")
    }

    fn execute_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        args: &'a [String],
        out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(async move {
            let server = ctx.server()?;
            let character = ctx.player()?;
            let mut target = character.clone();
            if let Some(a) = args.first() {
                target = get_target_or_self(server, &character, Some(a)).0;
            }

            let buff_id = 3105u32; // Run buff id

            let Some(buff_template) = server.game_data.systems.skills.buff(buff_id).cloned() else {
                CommandManager::send_error_text(self, out, &format!("Unknown buffId {buff_id}"));
                return Ok(());
            };

            let target_obj = WorldObject::from(&target);
            let has_buff = target.data().unit.buffs.check_buff(buff_id);

            if has_buff {
                // Removing buff
                buffs::remove_buff(server, &target_obj, buff_id);
                send_debug_message(server, &target, "Run buff removed.").await;
            } else {
                // Applying buff with extended duration
                let caster = WorldObject::from(&character);
                let new_buff = buffs::new_buff(
                    server,
                    &buff_template,
                    Some(&caster),
                    &SkillCaster::Unit(character.obj_id),
                    None,
                    now_ms(),
                );
                buffs::add_buff(server, &target_obj, new_buff, 0, 60_000); // 1 minute
                send_debug_message(server, &target, "Run buff applied for 60 seconds.").await;
            }
            Ok(())
        })
    }

    fn get_command_line_help(&self) -> String {
        "(target)".into()
    }

    fn get_command_help_text(&self) -> String {
        "Toggles run buff (id=3105) on the target character. If the buff is active, it removes it; otherwise, it applies it.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::data::GameData;
    use crate::systems::skills::data::BuffTemplate;

    #[tokio::test]
    async fn run_toggles_the_run_buff() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "run").await;
        assert_eq!(out, [harness::error("run", "Unknown buffId 3105")]);

        let mut data = GameData::empty();
        data.systems.skills.add_buff(BuffTemplate {
            id: 3105,
            kind: 1,
            duration: 5_000,
            ..BuffTemplate::default()
        });
        let server = harness::server_with(data);
        server.config.update(|c| {
            c.debug_info = true;
            c.debug_info_level = 0;
        });
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        let (_, out) = harness::run(&server, &gm.p, "run").await;
        assert!(out.is_empty());
        assert!(gm.p.data().unit.buffs.check_buff(3105));
        assert_eq!(gm.chat(), ["Run buff applied for 60 seconds."]);
        harness::run(&server, &gm.p, "run").await;
        assert!(!gm.p.data().unit.buffs.check_buff(3105));
        assert_eq!(gm.chat(), ["Run buff removed."]);

        harness::run(&server, &gm.p, "run Bob").await;
        assert!(bob.p.data().unit.buffs.check_buff(3105));
        assert_eq!(bob.chat(), ["Run buff applied for 60 seconds."]);
    }
}
