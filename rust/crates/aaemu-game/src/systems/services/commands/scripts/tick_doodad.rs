//! `Scripts/Commands/TickDoodad.cs`.

use super::prelude::*;
use crate::systems::npcs::doodad::tasks::execute_func_task_now;

/// `TickDoodad`.
pub struct TickDoodad;

impl TickDoodad {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TickDoodad {
    fn command_names(&self) -> Vec<String> {
        names(&["tickdoodad", "tick_doodad"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        let radius = 30f32;
        let Some(unit_id) = try_parse::<u32>(&args[0]) else {
            CommandManager::send_error_text(self, out, "Parse error unitId|r");
            return Ok(());
        };

        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut ticked_count = 0;
        // Use radius
        let my_doodads = around_of(server, &character, radius, |o| o.as_doodad().cloned());
        for doodad in &my_doodads {
            if doodad.data().template_id == unit_id {
                // doodad.FuncTask.Cancel(); Task.Run(doodad.FuncTask.ExecuteAsync);
                if execute_func_task_now(server, doodad) {
                    ticked_count += 1;
                }
            }
        }

        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "Phased {ticked_count} Doodad(s) with TemplateID {unit_id} - @DOODAD_NAME({unit_id})"
            ),
        );
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<objId>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Moves a doodad onto it's next Phase using <objId> inside a <radius> range.".into()
    }
}

#[cfg(test)]
mod tests {
    use std::time::Duration;

    use super::super::harness;
    use super::super::util::objects_test_support as ts;
    use crate::systems::npcs::doodad::tasks::{start_func_task, FuncTaskKind};
    use crate::world::WorldObject;

    #[tokio::test]
    async fn tickdoodad_runs_the_pending_func_task() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let d = ts::spawn_doodad(&server, ts::DOODAD, 102.0, 100.0);
        let tasks = &server.systems.npcs.doodads.tasks;
        tasks.set_manual(true);
        // A pending delete task (one hour away).
        start_func_task(
            &server,
            &d,
            None,
            FuncTaskKind::Delete,
            Duration::from_secs(3600),
        );
        let _ = WorldObject::from(&d);
        let (_, out) = harness::run(&server, &gm.p, &format!("tickdoodad {}", ts::DOODAD)).await;
        assert_eq!(
            out,
            [harness::normal(
                "tickdoodad",
                &format!(
                    "Phased 1 Doodad(s) with TemplateID {} - @DOODAD_NAME({})",
                    ts::DOODAD,
                    ts::DOODAD
                )
            )]
        );
        // The task now runs immediately; the hour-long one is cancelled.
        assert_eq!(tasks.advance(&server, 0), 1);
        assert!(server.world.world(0).unwrap().doodad(d.obj_id).is_none());
        tasks.advance(&server, 3_600_000);

        let (_, out) = harness::run(&server, &gm.p, "tick_doodad x").await;
        assert_eq!(out, [harness::error("tickdoodad", "Parse error unitId|r")]);
    }
}
