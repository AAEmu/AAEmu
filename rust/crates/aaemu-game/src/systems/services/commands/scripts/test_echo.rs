//! `Scripts/Commands/TestEcho.cs`.

use super::prelude::*;

/// `TestEcho`.
pub struct TestEcho;

impl TestEcho {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestEcho {
    fn command_names(&self) -> Vec<String> {
        names(&["echo"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let mut s = String::new();
        for a in args {
            s = s + a + " ";
        }
        ctx.character
            .send_message(&format!("|cFFFFFFFF[Echo]|r {s}"));
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<text>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Repeats the provided arguments in chat as raw text".into()
    }
}
