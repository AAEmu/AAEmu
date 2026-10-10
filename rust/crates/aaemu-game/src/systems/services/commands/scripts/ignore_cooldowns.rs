//! `Scripts/Commands/IgnoreCooldowns.cs`.

use super::prelude::*;

/// `IgnoreCooldowns`.
pub struct IgnoreCooldowns;

impl IgnoreCooldowns {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for IgnoreCooldowns {
    fn command_names(&self) -> Vec<String> {
        names(&[
            "ignoreskillcds",
            "disablecooldowns",
            "ignorecooldowns",
            "ignorecd",
        ])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        match parse_bool(&args[0]) {
            Some(ignore_cooldowns) => {
                ctx.player()?.data().skill_char.ignore_skill_cooldowns = ignore_cooldowns;
            }
            None => CommandManager::send_error_text(self, out, "Bool parse error!"),
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<true||false>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Enables or disables skill cooldowns.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn ignorecd_sets_the_character_flag() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "ignorecd true").await;
        assert!(out.is_empty());
        assert!(gm.p.data().skill_char.ignore_skill_cooldowns);
        harness::run(&server, &gm.p, "disablecooldowns FALSE").await;
        assert!(!gm.p.data().skill_char.ignore_skill_cooldowns);
        let (_, out) = harness::run(&server, &gm.p, "ignoreskillcds 1").await;
        assert_eq!(out, [harness::error("ignoreskillcds", "Bool parse error!")]);
    }
}
