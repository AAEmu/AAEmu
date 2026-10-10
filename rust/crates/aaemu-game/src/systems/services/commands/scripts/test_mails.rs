//! `Scripts/Commands/TestMails.cs`.

use super::prelude::*;

/// `TestMails`.
pub struct TestMails;

impl TestMails {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestMails {
    fn command_names(&self) -> Vec<String> {
        names(&["testmail", "test_mail"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestMails")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
