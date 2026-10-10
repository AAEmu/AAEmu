//! `Scripts/Commands/ObjectRotation.cs`.

use super::prelude::*;

/// `ObjectRotation`.
pub struct ObjectRotation;

impl ObjectRotation {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ObjectRotation {
    fn command_names(&self) -> Vec<String> {
        names(&["setrot", "npcrot", "doodadrot"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: ObjectRotation")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
