//! `Scripts/Commands/FailSkill.cs`.

use super::prelude::*;

/// `FailSkill`.
pub struct FailSkill;

impl FailSkill {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for FailSkill {
    fn command_names(&self) -> Vec<String> {
        names(&["failskill", "fail_skill", "skillfail", "skill_fail"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: FailSkill")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
