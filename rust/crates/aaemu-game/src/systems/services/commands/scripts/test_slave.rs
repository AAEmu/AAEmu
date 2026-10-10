//! `Scripts/Commands/TestSlave.cs`.

use super::prelude::*;
use crate::models::slave::Slave;
use crate::world::Entity;

/// `TestSlave`.
pub struct TestSlave;

impl TestSlave {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestSlave {
    fn command_names(&self) -> Vec<String> {
        names(&["testslave", "test_slave"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let me = WorldObject::from(&character);
        let obj_id = server.ids.object.get_next_id()?;
        let tl_id = server.ids.tl.get_next_id()? as u16;
        let (char_id, char_name, faction_id) = {
            let c = character.data();
            (c.id, c.unit.name.clone(), c.unit.faction_id)
        };
        let mut slave = Slave::new(0, 73);
        slave.summoner_id = char_id;
        slave.summoner_obj_id = character.obj_id;
        slave.summoner_name = char_name;
        slave.unit.model_id = 1008;
        slave.unit.tl_id = tl_id;
        // `Faction = FactionManager.GetFaction(143)`, replaced by the character's faction below.
        slave.unit.level = 50;
        slave.unit.transform = clone_detached(server, &me);
        add_distance_to_front(&mut slave.unit.transform, 5.0);
        slave.unit.hp = 190_000;
        slave.unit.max_hp = 190_000;
        slave.unit.faction_id = faction_id;
        // `slave.Template = SlaveGameData.GetSlaveTemplate(73)` (read from the game data by
        // template id).

        let slave = Entity::new(obj_id, slave);
        let world = parent_world(server, &character).ok_or_else(nre)?;
        let changes = world.spawn(&slave, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Spawns a test slave".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn testslave_spawns_a_slave_in_front() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "test_slave").await;
        assert!(out.is_empty());
        let world = server.world.world(0).unwrap();
        let slaves = crate::systems::vehicles::slaves::all_slaves(&world);
        assert_eq!(slaves.len(), 1);
        let s = slaves[0].data();
        assert_eq!(s.unit.template_id, 73);
        assert_eq!(s.unit.max_hp, 190_000);
        assert_eq!(s.unit.faction_id, 101);
        assert_eq!(s.summoner_obj_id, gm.p.obj_id);
        assert!((s.unit.transform.y - 105.0).abs() < 1e-4);
    }
}
