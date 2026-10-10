//! `Scripts/Commands/SoloParty.cs`.

use super::prelude::*;

/// `SoloParty`.
pub struct SoloParty;

impl SoloParty {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for SoloParty {
    fn command_names(&self) -> Vec<String> {
        names(&["soloparty", "solo_party"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: SoloParty")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
