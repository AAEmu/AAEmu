//! `Scripts/Commands/TestTransfer.cs`.

use super::prelude::*;

/// `TestTransfer`.
pub struct TestTransfer;

impl TestTransfer {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestTransfer {
    fn command_names(&self) -> Vec<String> {
        names(&["testtransfer", "test_transfer"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // The rest of the C# body (spawning a test carriage) is commented out.
        CommandManager::send_error_text(self, out, "function disabled");
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Spawns a transportation vehicle".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn test_transfer_is_disabled() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (handled, out) = harness::run(&server, &gm.p, "test_transfer 1").await;
        assert!(handled);
        assert_eq!(out, [harness::error("testtransfer", "function disabled")]);
        assert_eq!(
            gm.chat(),
            [harness::error("testtransfer", "function disabled")]
        );
        assert!(server
            .world
            .world(0)
            .unwrap()
            .all_of_kind(crate::world::ObjectKind::Transfer)
            .is_empty());
    }
}
