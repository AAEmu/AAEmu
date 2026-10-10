//! `Scripts/Commands/ObjectPosition.cs`.

use super::prelude::*;

/// `ObjectPosition`.
pub struct ObjectPosition;

impl ObjectPosition {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `args[i] != axis && args[i] != "*" && float.TryParse(args[i], out v)`.
pub(super) fn axis_value(arg: &str, axis: &str) -> Option<f32> {
    if arg != axis && arg != "*" {
        parse_f32(arg)
    } else {
        None
    }
}

impl Command for ObjectPosition {
    fn command_names(&self) -> Vec<String> {
        names(&["setpos", "npcloc", "npcpos", "doodadloc", "doodadpos"])
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
            CommandManager::send_error_text(self, out, "ObjId parse error");
            return Ok(());
        };
        let server = ctx.server()?;
        let world = parent_world(server, &ctx.player()?).ok_or_else(nre)?;
        let Some(game_object) = world.object(id) else {
            CommandManager::send_error_text(self, out, &format!("ObjId: {id} not found"));
            return Ok(());
        };

        let t = game_object.transform();
        let (old_x, old_y, old_z) = (t.x, t.y, t.z);
        let (mut x, mut y, mut z) = (t.x, t.y, t.z);
        if let Some(v) = axis_value(&args[1], "x") {
            x = v;
        }
        if let Some(v) = axis_value(&args[2], "y") {
            y = v;
        }
        if let Some(v) = axis_value(&args[3], "z") {
            z = v;
        }

        game_object.with_transform(|t| t.set_xyz(x, y, z));

        hide_show(server, &world, &game_object);
        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "ObjId: Position {}, {}, {} => {}, {}, {}",
                f32s(old_x),
                f32s(old_y),
                f32s(old_z),
                f32s(x),
                f32s(y),
                f32s(z)
            ),
        );
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(ObjID) <x> <y> <z> (can use x y z or * instead of a value to keep current position)"
            .into()
    }

    fn get_command_help_text(&self) -> String {
        "Change a object's local position".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn setpos_moves_any_object() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let npc = ts::spawn_npc(&server, ts::NPC, 110.0, 100.0);
        let (_, out) =
            harness::run(&server, &gm.p, &format!("npcpos {} 111.5 y 99", npc.obj_id)).await;
        assert_eq!(
            out,
            [harness::normal(
                "setpos",
                "ObjId: Position 110, 100, 100 => 111.5, 100, 99"
            )]
        );
        let t = npc.data().unit.transform;
        assert_eq!((t.x, t.y, t.z), (111.5, 100.0, 99.0));
        let (_, out) = harness::run(&server, &gm.p, "setpos 77 1 2 3").await;
        assert_eq!(out, [harness::error("setpos", "ObjId: 77 not found")]);
        let (_, out) = harness::run(&server, &gm.p, "setpos x 1 2 3").await;
        assert_eq!(out, [harness::error("setpos", "ObjId parse error")]);
    }
}
