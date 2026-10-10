//! `Scripts/Commands/ObjectPosition.cs`.

use super::prelude::*;

/// `ObjectPosition`.
pub struct ObjectPosition;

impl ObjectPosition {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ObjectPosition {
    fn command_names(&self) -> Vec<String> {
        names(&["setpos", "npcloc", "npcpos", "doodadloc", "doodadpos"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: ObjectPosition")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
