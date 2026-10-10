//! `Scripts/Commands/QuestCmd.cs`.

use super::prelude::*;

/// `QuestCmd`.
pub struct QuestCmd;

impl QuestCmd {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for QuestCmd {
    fn command_names(&self) -> Vec<String> {
        names(&["quest"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: QuestCmd")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
