//! `Scripts/Commands/Rotate.cs`.

use super::prelude::*;
use crate::models::movement::{
    GameStanceType, MoveKind, MoveType, MoveTypeActorFlags, MoveTypeAlertness, UnitMove,
};
use crate::packets::world::SCOneUnitMovementPacket;
use crate::utils::math::{
    calculate_angle_from, convert_degree_to_sbyte_direction, convert_radian_to_direction,
};
use crate::utils::numeric::deg_to_rad_f32;

/// `Rotate`.
pub struct Rotate;

impl Rotate {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Rotate {
    fn command_names(&self) -> Vec<String> {
        names(&["rotate", "lookatme"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let Some(target) = current_target(server, &character) else {
            CommandManager::send_error_text(self, out, "You need to target a unit first");
            return Ok(());
        };
        let name = target.with_base_unit(|b| b.name().to_string());
        ctx.character.send_message(&format!(
            "[Rotate] Unit: {name}, ObjId: {}",
            target.obj_id()
        ));

        let tw = world_pos(server, &target);
        let me = world_pos(server, &WorldObject::from(&character));
        let angle = calculate_angle_from(tw.x, tw.y, me.x, me.y) as f32 - 90f32;
        let rot_z = convert_degree_to_sbyte_direction(f64::from(angle));

        // Local.SetRotationDegree(0f, 0f, angle)
        let local = target.with_transform(|t| {
            t.rot_x = deg_to_rad_f32(0.0);
            t.rot_y = deg_to_rad_f32(0.0);
            t.rot_z = deg_to_rad_f32(angle);
            *t
        });

        let move_type = MoveType {
            time: 50, // has to change all the time for normal motion.
            ..MoveType::new(MoveKind::Unit(UnitMove {
                x: tw.x,
                y: tw.y,
                z: tw.z,
                rotation_x: convert_radian_to_direction(f64::from(local.rot_x)),
                rotation_y: convert_radian_to_direction(f64::from(local.rot_y)),
                rotation_z: convert_radian_to_direction(f64::from(local.rot_z)),
                actor_flags: MoveTypeActorFlags(5),
                delta_movement: [0, 0, 0],
                stance: GameStanceType::RELAXED, // combat=0, idle=1
                alertness: MoveTypeAlertness::IDLE, // idle=0, combat=2
                ..UnitMove::default()
            }))
        };

        if let Some(world) = parent_world(server, &character) {
            crate::world::broadcast(
                &world,
                &character,
                &SCOneUnitMovementPacket {
                    obj_id: target.obj_id(),
                    move_type,
                },
                true,
            );
        }
        CommandManager::send_normal_text(
            self,
            out,
            &format!(
                "New rotation {}° ({} rad, sbyte {rot_z}) for {}",
                f32s(angle),
                fixed(f64::from(local.rot_z), 2),
                target.obj_id()
            ),
        );
        CommandManager::send_normal_text(
            self,
            out,
            &format!("New position {}", pos_rot_string(&local.local())),
        );
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[angle]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Rotate target unit towards you, or set it's local rotation to a given angle".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn rotate_turns_the_target_towards_the_caller() {
        let server = ts::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "rotate").await;
        assert_eq!(
            out,
            [harness::error("rotate", "You need to target a unit first")]
        );

        let npc = ts::spawn_npc(&server, ts::NPC, 100.0, 110.0);
        gm.p.data().unit.current_target = npc.obj_id;
        gm.frames();
        let (_, out) = harness::run(&server, &gm.p, "lookatme").await;
        assert_eq!(
            out,
            [
                harness::normal(
                    "rotate",
                    &format!(
                        "New rotation -180° (-3.14 rad, sbyte 64) for {}",
                        npc.obj_id
                    )
                ),
                harness::normal(
                    "rotate",
                    "New position X:100 Y:110 Z:100  r:0° p:0° y:-180°"
                ),
            ]
        );
        let frames = gm.frames();
        assert!(frames.iter().any(|f| f.0 == sc::SCOneUnitMovementPacket));
        let rz = npc.data().unit.transform.rot_z;
        assert!((rz + std::f32::consts::PI).abs() < 1e-5);
    }
}
