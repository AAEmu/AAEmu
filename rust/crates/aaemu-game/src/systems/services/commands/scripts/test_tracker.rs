//! `Scripts/Commands/TestTracker.cs`.

use super::prelude::*;

/// `TestTracker`.
pub struct TestTracker;

impl TestTracker {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestTracker {
    fn command_names(&self) -> Vec<String> {
        names(&["testtracker", "track", "tt"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut target_object = current_target(server, &character);
        if let Some(target_obj_id_val) = args.first().and_then(|a| try_parse::<u32>(a)) {
            target_object = parent_world(server, &character)
                .ok_or_else(|| {
                    anyhow::anyhow!("Object reference not set to an instance of an object.")
                })?
                .object(target_obj_id_val);
        }

        match target_object {
            Some(target_object) => {
                let toggle_result = if target_object
                    .core()
                    .toggle_debug_tracker(character.character_id)
                {
                    "Now"
                } else {
                    "No longer"
                };
                // Every world object is a `BaseUnit` here ("<gameobject>" otherwise).
                let unit_name = target_object.with_base_unit(|b| b.name().to_string());
                ctx.character.send_message(&format!(
                    "[TestTracking] {toggle_result} tracking {} - {unit_name}",
                    target_object.obj_id()
                ));
            }
            None => ctx.character.send_message("[TestTracking] Invalid object"),
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) [objId]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Toggle movement debug information for target".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::spawn_npc;
    use super::super::harness;

    #[tokio::test]
    async fn track_toggles_the_debug_tracker() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        harness::run(&server, &gm.p, "tt").await;
        assert_eq!(gm.chat(), ["[TestTracking] Invalid object"]);

        let npc = spawn_npc(&server, 0x500, 10, 105.0, 100.0, 1_000);
        gm.p.data().unit.current_target = npc.obj_id;
        harness::run(&server, &gm.p, "track").await;
        assert_eq!(gm.chat(), ["[TestTracking] Now tracking 1280 - Npc10"]);
        harness::run(&server, &gm.p, "testtracker 1280").await;
        assert_eq!(
            gm.chat(),
            ["[TestTracking] No longer tracking 1280 - Npc10"]
        );
        harness::run(&server, &gm.p, "tt 257").await;
        assert_eq!(gm.chat(), ["[TestTracking] Now tracking 257 - Gm"]);
        harness::run(&server, &gm.p, "tt 4242").await;
        assert_eq!(gm.chat(), ["[TestTracking] Invalid object"]);
    }
}
