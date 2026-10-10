//! `Scripts/Commands/TestChatChannel.cs`.

use super::prelude::*;

/// `TestChatChannel`.
pub struct TestChatChannel;

impl TestChatChannel {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestChatChannel {
    fn command_names(&self) -> Vec<String> {
        names(&["testchatchannel", "test_chat_channel", "testchat"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: TestChatChannel")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
