//! `Scripts/Commands/DeSpawnAll.cs`.

use super::prelude::*;

/// `DeSpawnAll`.
pub struct DeSpawnAll;

impl DeSpawnAll {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for DeSpawnAll {
    fn command_names(&self) -> Vec<String> {
        names(&["despawnall", "despawn_all"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
        let removed_count = server
            .systems
            .npcs
            .spawns
            .get_or_create(&world)
            .despawn_all(server);
        CommandManager::send_normal_text(self, out, &format!("Removed {removed_count} objects"));
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "De-spawns all world objects".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn despawn_all_removes_npcs_and_doodads() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let npc = ts::spawn_npc(&server, ts::NPC, 110.0, 100.0);
        let doodad = ts::spawn_doodad(&server, ts::DOODAD, 102.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "despawn_all").await;
        assert_eq!(out, [harness::normal("despawnall", "Removed 2 objects")]);
        let world = server.world.world(0).unwrap();
        assert!(world.npc(npc.obj_id).is_none());
        assert!(world.doodad(doodad.obj_id).is_none());
    }
}
