//! `Scripts/Commands/UseSkill.cs`.

use super::prelude::*;

/// `UseSkill`.
pub struct UseSkill;

impl UseSkill {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for UseSkill {
    fn command_names(&self) -> Vec<String> {
        names(&["useskill", "use_skill", "testskill", "test_skill"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: UseSkill")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
