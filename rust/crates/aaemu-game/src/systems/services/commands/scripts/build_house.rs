//! `Scripts/Commands/BuildHouse.cs`.

use super::prelude::*;
use super::test_house::target_house;
use crate::systems::housing::manager as housing;
use crate::systems::housing::packets::SCHouseBuildProgressPacket;

/// `BuildHouse`.
pub struct BuildHouse;

impl BuildHouse {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for BuildHouse {
    fn command_names(&self) -> Vec<String> {
        names(&["build", "build_house"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let player = ctx.player()?;
        let Some(target_house) = target_house(server, &player) else {
            CommandManager::send_error_text(self, out, "You must target a house");
            return Ok(());
        };
        let mut build_action_count = 1u32;
        if let Some(val) = args.first().and_then(|a| try_parse::<u32>(a)) {
            build_action_count = val;
        }
        let actions_left_for_step = {
            let h = target_house.data();
            h.all_action - h.current_action()
        };
        // `uint > int` compares as long.
        if i64::from(build_action_count) > i64::from(actions_left_for_step) {
            CommandManager::send_error_text(
                self,
                out,
                &format!(
                    "Cannot do {build_action_count} build actions when the maximum allowed for the current step is {actions_left_for_step}"
                ),
            );
            return Ok(());
        }
        let world = server.world.world_of(&WorldObject::from(&player));
        for _ in 0..build_action_count {
            housing::add_build_action(server, &target_house);
            let packet = {
                let h = target_house.data();
                SCHouseBuildProgressPacket {
                    tl: h.unit.tl_id,
                    model_id: h.unit.model_id,
                    all_step: h.all_action,
                    cur_step: if h.current_step == -1 {
                        h.all_action
                    } else {
                        h.current_action()
                    },
                }
            };
            // character.BroadcastPacket(packet, true)
            if let Some(w) = &world {
                crate::world::broadcast(w, &player, &packet, true);
            }
        }
        if target_house.data().current_step == -1 {
            if let Some(w) = housing::world_of(server, &target_house) {
                for doodad in server.systems.housing.manager.attached(target_house.obj_id) {
                    crate::systems::npcs::doodad::manager::spawn(server, &w, &doodad);
                }
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[stepcount]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Advances the target house further by [stepcount] or 1 if omitted".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::test_house::fixture;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn build_advances_the_target_house() {
        let server = harness::server_with(fixture::data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "build").await;
        assert_eq!(out, [harness::error("build", "You must target a house")]);

        let house = fixture::place_house(&server, &gm.p, 110.0, 110.0, 0);
        fixture::target(&gm.p, &house);
        gm.frames();
        // 3 actions in total (2 + 1).
        let (_, out) = harness::run(&server, &gm.p, "build 4").await;
        assert_eq!(
            out,
            [harness::error(
                "build",
                "Cannot do 4 build actions when the maximum allowed for the current step is 3"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "build_house").await;
        assert!(out.is_empty());
        assert_eq!(house.data().num_action, 1);
        let progress: Vec<_> = gm
            .packets()
            .into_iter()
            .filter(|(op, _)| *op == sc::SCHouseBuildProgressPacket)
            .collect();
        assert_eq!(progress.len(), 1);
        // Two more: the house is finished and its bound doodad spawned.
        let (_, out) = harness::run(&server, &gm.p, "build 2").await;
        assert!(out.is_empty());
        assert_eq!(house.data().current_step, -1);
        assert_eq!(house.data().unit.model_id, 700);
        let frames = gm.packets();
        assert_eq!(
            frames
                .iter()
                .filter(|(op, _)| *op == sc::SCHouseBuildProgressPacket)
                .count(),
            2
        );
        let attached = server.systems.housing.manager.attached(house.obj_id);
        assert_eq!(attached.len(), 1);
        let d = attached[0].clone();
        let ops: Vec<u16> = frames.iter().map(|f| f.0).collect();
        assert!(
            ops.contains(&sc::SCDoodadCreatedPacket) || ops.contains(&sc::SCDoodadsCreatedPacket),
            "doodad {} gm {} ops {ops:?}",
            d.obj_id,
            gm.p.obj_id
        );
    }
}
