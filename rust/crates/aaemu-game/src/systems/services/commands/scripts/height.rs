//! `Scripts/Commands/Height.cs`.

use super::prelude::*;
use crate::systems::skills::controllers::Vec3;

/// `Height`.
pub struct Height;

impl Height {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Height {
    fn command_names(&self) -> Vec<String> {
        names(&["height"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut target_player = character.clone();
        if let Some(a) = args.first() {
            target_player = get_target_or_self(server, &character, Some(a)).0;
        }

        let target = WorldObject::from(&target_player);
        let p = world_pos(server, &target);
        let zone_id = target.transform().zone_id;
        // WorldManager.Instance.GetHeight(zoneId, x, y, z)
        let floor_height = server
            .systems
            .npcs
            .heightmaps
            .get()
            .map_or(0.0, |h| h.get_height(zone_id, p.x, p.y, p.z));
        // targetPlayer.ParentWorld.Template.GeoData.GetHeight(position)
        let nav_mesh_height =
            crate::systems::ai::geodata::geo_data_height(server, &target, Vec3::new(p.x, p.y, p.z));
        // C#: the Z shown is the caller's, not the target's.
        let my_z = world_pos(server, &WorldObject::from(&character)).z;
        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "{} Z-Pos: {} - Floor: {}, NavMeshHeight: {}",
                name_of(&target_player),
                f32s(my_z),
                f32s(floor_height),
                f32s(nav_mesh_height)
            ),
        );
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target)".into()
    }

    fn get_command_help_text(&self) -> String {
        "Gets your or target's current height and that of the supposed floor (using heightmap data)"
            .into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn height_reports_the_callers_z() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let bob = harness::pc(&server, 2, "Bob", 10.0, 20.0);
        bob.p.data().unit.transform.z = 55.5;
        let (_, out) = harness::run(&server, &gm.p, "height").await;
        assert_eq!(
            out,
            [harness::normal(
                "height",
                "Gm Z-Pos: 100 - Floor: 0, NavMeshHeight: 0"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "height Bob").await;
        assert_eq!(
            out,
            [harness::normal(
                "height",
                "Bob Z-Pos: 100 - Floor: 0, NavMeshHeight: 0"
            )]
        );
    }
}
