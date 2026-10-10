//! `Scripts/Commands/ResetSkillCooldowns.cs`.

use super::prelude::*;

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
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: ResetSkillCooldowns")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
