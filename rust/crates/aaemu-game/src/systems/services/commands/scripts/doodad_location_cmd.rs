//! `Scripts/Commands/DoodadLocationCmd.cs`.

use super::prelude::*;

/// `DoodadLocationCmd`.
pub struct DoodadLocationCmd;

impl DoodadLocationCmd {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for DoodadLocationCmd {
    fn command_names(&self) -> Vec<String> {
        names(&["doodad_location", "doodadlocation", "dloc"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.len() < 4 {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        let Some(id) = try_parse::<u32>(&args[0]) else {
            CommandManager::send_error_text(self, out, "<doodad objId> parse error");
            return Ok(());
        };
        let server = ctx.server()?;
        let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
        let Some(doodad) = world.doodad(id) else {
            CommandManager::send_error_text(
                self,
                out,
                &format!("Doodad with objId {id} does not exist"),
            );
            return Ok(());
        };

        let t = doodad.data().transform;
        let (mut x, mut y, mut z) = (t.x, t.y, t.z);
        let value = |a: &str, axis: &str| {
            if a != "*" && a != axis {
                parse_f32(a)
            } else {
                None
            }
        };
        if let Some(v) = value(&args[1], "x") {
            x = v;
        }
        if let Some(v) = value(&args[2], "y") {
            y = v;
        }
        if let Some(v) = value(&args[3], "z") {
            z = v;
        }

        doodad.data().transform.set_xyz(x, y, z);

        hide_show(server, &world, &WorldObject::from(&doodad));
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<doodad objId> <x> <y> <z>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Change doodad location. You can use * instead of the x y or z values to keep their original position.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn dloc_moves_the_doodad() {
        let server = ts::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let d = ts::spawn_doodad(&server, ts::DOODAD, 102.0, 100.0);
        gm.packets();
        let (_, out) = harness::run(&server, &gm.p, &format!("dloc {} 105.5 * z", d.obj_id)).await;
        assert!(out.is_empty());
        let t = d.data().transform;
        assert_eq!((t.x, t.y, t.z), (105.5, 100.0, 100.0));
        let ops: Vec<u16> = gm.packets().into_iter().map(|p| p.0).collect();
        assert!(
            ops.contains(&sc::SCDoodadRemovedPacket) || ops.contains(&sc::SCDoodadsRemovedPacket)
        );
        assert!(
            ops.contains(&sc::SCDoodadCreatedPacket) || ops.contains(&sc::SCDoodadsCreatedPacket)
        );

        let (_, out) = harness::run(&server, &gm.p, "doodadlocation 9 1 2 3").await;
        assert_eq!(
            out,
            [harness::error(
                "doodad_location",
                "Doodad with objId 9 does not exist"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "dloc a 1 2 3").await;
        assert_eq!(
            out,
            [harness::error(
                "doodad_location",
                "<doodad objId> parse error"
            )]
        );
    }
}
