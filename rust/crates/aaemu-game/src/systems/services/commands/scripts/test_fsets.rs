//! `Scripts/Commands/TestFSets.cs`.

use super::super::sub::FEATURE_NAMES;
use super::prelude::*;

/// `TestFSets`.
pub struct TestFSets;

impl TestFSets {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestFSets {
    fn command_names(&self) -> Vec<String> {
        names(&["testfsets", "test_fsets"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let fsets = server.systems.services.features(server);
        for (bit, name) in FEATURE_NAMES {
            let text = if fsets.check_bit(*bit) {
                format!("|cFF00FF00ON  |cFF80FF80{name}")
            } else {
                format!("|cFFFF0000OFF |cFF802020{name}")
            };
            CommandManager::send_normal_text(self, out, &text);
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Shows currently active fsets of the server".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn lists_every_feature() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        server.systems.services.set_feature(&server, 0, true);
        server.systems.services.set_feature(&server, 1, false);
        let (_, out) = harness::run(&server, &gm.p, "test_fsets").await;
        assert_eq!(out.len(), super::FEATURE_NAMES.len());
        assert_eq!(out[0], "[testfsets] |cFF00FF00ON  |cFF80FF80siege");
        assert_eq!(
            out[1],
            "[testfsets] |cFFFF0000OFF |cFF802020allowFamilyChanges"
        );
    }
}
