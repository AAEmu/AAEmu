//! `Scripts/Commands/HouseBindingMove.cs`.

use super::prelude::*;
use super::test_house::target_house;
use crate::packets::world_objects::SCTargetChangedPacket;
use crate::systems::housing::manager as housing;

/// `HouseBindingMove`.
pub struct HouseBindingMove;

impl HouseBindingMove {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for HouseBindingMove {
    fn command_names(&self) -> Vec<String> {
        names(&["house_binding_move", "housebindingmove"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let player = ctx.player()?;
        let Some(house) = target_house(server, &player) else {
            CommandManager::send_error_text(self, out, "No house selected");
            return Ok(());
        };
        if args.len() < 4 {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        let (Some(attach_point_id_val), Some(x), Some(y), Some(z)) = (
            try_parse::<u32>(&args[0]),
            parse_f32(&args[1]),
            parse_f32(&args[2]),
            parse_f32(&args[3]),
        ) else {
            CommandManager::send_error_text(self, out, "Float parse error on coordinates");
            return Ok(());
        };
        // `(AttachPointKind)attachPointIdVal` (a byte enum: unchecked truncation).
        let attach_point_id = attach_point_id_val as u8;
        let attach_point_obj = server
            .systems
            .housing
            .manager
            .attached(house.obj_id)
            .into_iter()
            .find(|o| o.data().attach_point == attach_point_id);
        let Some(attach_point_obj) = attach_point_obj else {
            CommandManager::send_error_text(self, out, "Not found this attach doodad");
            return Ok(());
        };
        housing::delete_house(server, &house);
        attach_point_obj.data().transform.set_xyz(x, y, z);
        housing::spawn_house(server, &house);
        player.data().unit.current_target = house.obj_id;
        let packet = SCTargetChangedPacket {
            obj_id: player.obj_id,
            target_obj_id: house.obj_id,
        };
        if let Some(w) = server.world.world_of(&WorldObject::from(&player)) {
            crate::world::broadcast(&w, &player, &packet, true);
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<AttachPointId> <X> <Y> <Z>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Command used for testing and moving house binding points".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::test_house::fixture;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn house_binding_move_moves_the_attached_doodad() {
        let server = harness::server_with(fixture::data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "house_binding_move 9 1 2 3").await;
        assert_eq!(
            out,
            [harness::error("house_binding_move", "No house selected")]
        );
        let house = fixture::place_house(&server, &gm.p, 110.0, 110.0, -1);
        fixture::target(&gm.p, &house);
        let (_, out) = harness::run(&server, &gm.p, "housebindingmove 9 1").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/house_binding_move|r |cFFEEEEAA<AttachPointId> <X> <Y> <Z>|r\n|cFF888888Command used for testing and moving house binding points|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "housebindingmove 9 a 2 3").await;
        assert_eq!(
            out,
            [harness::error(
                "house_binding_move",
                "Float parse error on coordinates"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "housebindingmove 3 1 2 3").await;
        assert_eq!(
            out,
            [harness::error(
                "house_binding_move",
                "Not found this attach doodad"
            )]
        );
        gm.frames();
        // 265 truncates to attach point 9.
        let (_, out) = harness::run(&server, &gm.p, "housebindingmove 265 4.5 -2 7").await;
        assert!(out.is_empty());
        let d = server.systems.housing.manager.attached(house.obj_id)[0].clone();
        let t = d.data().transform;
        assert_eq!((t.x, t.y, t.z), (4.5, -2.0, 7.0));
        assert_eq!(gm.p.data().unit.current_target, house.obj_id);
        let frames = gm.packets();
        let target_changed: Vec<_> = frames
            .iter()
            .filter(|(op, _)| *op == sc::SCTargetChangedPacket)
            .collect();
        // The hide clears the target (SCTargetChanged to 0), then the command re-targets.
        let mut expected = aaemu_commons::PacketStream::new();
        expected.write_bc(gm.p.obj_id).write_bc(house.obj_id);
        assert_eq!(target_changed.last().unwrap().1, expected.into_bytes());
        // The house was shown again.
        assert!(server.world.world(0).unwrap().house(house.obj_id).is_some());
    }
}
