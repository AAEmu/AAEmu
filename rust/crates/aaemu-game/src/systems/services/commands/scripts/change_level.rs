//! `Scripts/Commands/ChangeLevel.cs`.

use super::prelude::*;

/// `ChangeLevel`.
pub struct ChangeLevel;

impl ChangeLevel {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ChangeLevel {
    fn command_names(&self) -> Vec<String> {
        names(&[
            "level",
            "setlevel",
            "set_level",
            "change_level",
            "changelevel",
        ])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: ChangeLevel")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
