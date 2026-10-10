//! `Scripts/Commands/SpawnGrid.cs`.

use super::prelude::*;
use crate::utils::math::calculate_angle_from;
use crate::utils::numeric::deg_to_rad_f32;

/// `SpawnGrid`.
pub struct SpawnGrid;

impl SpawnGrid {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// The spawn position of `SpawnDoodad` / `SpawnNPC`: the character's spawn position moved to
/// (newX, newY), facing `CalculateAngleFrom(new, character)` (degrees converted to radians).
fn grid_position(
    server: &GameServer,
    character: &PlayerRef,
    new_x: f32,
    new_y: f32,
) -> crate::data::world_templates::WorldSpawnPosition {
    let me = clone_detached(server, &WorldObject::from(character));
    let mut position = spawn_position(&me);
    position.y = new_y;
    position.x = new_x;
    let angle = calculate_angle_from(position.x, position.y, me.x, me.y) as f32;
    // TODO (C#): this seems wrong for now, will need to replace with a LookAt() at some later point
    position.yaw = deg_to_rad_f32(angle);
    position.pitch = 0.0;
    position.roll = 0.0;
    position
}

/// `SpawnDoodad(unitId, character, newX, newY)`.
pub fn spawn_doodad(
    server: &GameServer,
    world: &Arc<crate::world::WorldInstance>,
    unit_id: u32,
    character: &PlayerRef,
    new_x: f32,
    new_y: f32,
) {
    let position = grid_position(server, character, new_x, new_y);
    new_doodad_spawner(world.id, unit_id, position).spawn(server, 0);
}

/// `SpawnNPC(unitId, character, newX, newY)`.
pub fn spawn_npc(
    server: &GameServer,
    world: &Arc<crate::world::WorldInstance>,
    unit_id: u32,
    character: &PlayerRef,
    new_x: f32,
    new_y: f32,
) -> anyhow::Result<()> {
    let position = grid_position(server, character, new_x, new_y);
    let npc_spawner = add_runtime_npc_spawner(server, world, unit_id, position)?;
    npc_spawner.spawn_all(server, false);
    Ok(())
}

impl Command for SpawnGrid {
    fn command_names(&self) -> Vec<String> {
        names(&["spawngrid", "spawngroup"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.len() < 5 {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        let action = args[0].to_lowercase();
        let parsed = (|| {
            Some((
                try_parse::<u32>(&args[1])?,
                try_parse::<u32>(&args[2])?,
                try_parse::<u32>(&args[3])?,
                parse_f32(&args[4])?,
            ))
        })();
        let Some((template_id, mut columns, mut rows, mut spacing)) = parsed else {
            CommandManager::send_error_text(self, out, "Arguments parse error");
            return Ok(());
        };

        if columns < 1 {
            columns = 1;
        }
        if rows < 1 {
            rows = 1;
        }
        if spacing < 0.1 {
            spacing = 0.1;
        }

        let server = ctx.server()?;
        match action.as_str() {
            "npc" => {
                if !server.game_data.systems.npcs.npc.exist(template_id) {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("NPC {template_id} don't exist"),
                    );
                    return Ok(());
                }
            }
            "doodad" => {
                if !server.game_data.systems.npcs.doodad.exist(template_id) {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("Doodad {template_id} don't exist"),
                    );
                    return Ok(());
                }
            }
            _ => {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!("Unknown object type {action}"),
                );
                return Ok(());
            }
        }

        let character = ctx.player()?;
        let world = parent_world(server, &character).ok_or_else(nre)?;
        let me = WorldObject::from(&character);
        // Origin point for spawns (computed and unused in C#)
        for y in 0..rows {
            let pos_y = (y + 1) as f32 * spacing;
            for x in 0..columns {
                let size_x = columns as f32 * spacing;
                let pos_x = x as f32 * spacing - size_x / 2.0;
                let mut new_pos = clone_detached(server, &me);
                add_distance_to_front(&mut new_pos, pos_y);
                add_distance_to_right(&mut new_pos, pos_x);
                match action.as_str() {
                    "npc" => spawn_npc(
                        server,
                        &world,
                        template_id,
                        &character,
                        new_pos.x,
                        new_pos.y,
                    )?,
                    "doodad" => spawn_doodad(
                        server,
                        &world,
                        template_id,
                        &character,
                        new_pos.x,
                        new_pos.y,
                    ),
                    _ => {}
                }
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<npc||doodad> <templateID> <columns> <rows> <spacing>".into()
    }

    fn get_command_help_text(&self) -> String {
        format!(
            "Spawns a large amount of NPCs or doodads using <templateID> as a template in a grid in front of you using specified number of columns, rows and spacing.\nExample: {COMMAND_PREFIX}{} doodad 320 5 5 2",
            self.command_names()[0]
        )
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn spawngrid_spawns_a_grid_of_doodads() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let world = server.world.world(0).unwrap();
        let (_, out) = harness::run(
            &server,
            &gm.p,
            &format!("spawngroup doodad {} 2 3 2", ts::DOODAD),
        )
        .await;
        assert!(out.is_empty());
        let mut pos: Vec<(i32, i32)> = world
            .all_doodads()
            .iter()
            .map(|d| {
                let t = d.data().transform;
                (t.x.round() as i32, t.y.round() as i32)
            })
            .collect();
        pos.sort();
        assert_eq!(
            pos,
            [
                (98, 102),
                (98, 104),
                (98, 106),
                (100, 102),
                (100, 104),
                (100, 106)
            ]
        );

        let (_, out) = harness::run(&server, &gm.p, "spawngrid tree 1 1 1 1").await;
        assert_eq!(
            out,
            [harness::error("spawngrid", "Unknown object type tree")]
        );
        let (_, out) = harness::run(&server, &gm.p, "spawngrid doodad 1 1 1 x").await;
        assert_eq!(out, [harness::error("spawngrid", "Arguments parse error")]);
        let (_, out) = harness::run(&server, &gm.p, "spawngrid npc 99999 1 1 1").await;
        assert_eq!(out, [harness::error("spawngrid", "NPC 99999 don't exist")]);
        let (_, out) = harness::run(&server, &gm.p, "spawngrid").await;
        assert!(out[0].contains("Example: /spawngrid doodad 320 5 5 2"));
    }
}
