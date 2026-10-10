//! `Scripts/Commands/ResetSkillCooldowns.cs`.

use super::prelude::*;
use crate::systems::instances::instant_game;

/// `ResetSkillCooldowns`.
pub struct ResetSkillCooldowns;

impl ResetSkillCooldowns {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ResetSkillCooldowns {
    fn command_names(&self) -> Vec<String> {
        names(&["resetcd", "resetskillcooldowns", "rcd"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let character = ctx.player()?;
        instant_game::reset_all_skill_cooldowns(ctx.server()?, &character, false);
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Resets skill cooldowns.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn resetcd_sends_the_cooldown_resets() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (handled, out) = harness::run(&server, &gm.p, "rcd").await;
        assert!(handled);
        assert!(out.is_empty());
        // One CompressedGamePackets batch (the 378-tagged skills; none in empty data).
        assert_eq!(gm.frames().len(), 1);
    }
}
