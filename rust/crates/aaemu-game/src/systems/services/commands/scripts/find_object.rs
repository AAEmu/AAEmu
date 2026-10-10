//! `Scripts/Commands/FindObject.cs`.

use super::around::show_object_data;
use super::prelude::*;
use crate::systems::skills::geometry::{distance_to, Target};

/// `FindObject`.
pub struct FindObject;

impl FindObject {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for FindObject {
    fn command_names(&self) -> Vec<String> {
        names(&["findobject", "findobj"])
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

        let mut template_id = 0u32;
        if args.len() > 1 {
            match try_parse::<u32>(&args[1]) {
                Some(v) => template_id = v,
                None => {
                    CommandManager::send_error_text(self, out, "Error parsing template ID !");
                    return Ok(());
                }
            }
        }

        if template_id == 0 {
            CommandManager::send_error_text(self, out, "No template ID provided !");
            return Ok(());
        }

        let mut max_count = 5u32;
        if args.len() > 2 {
            match try_parse::<u32>(&args[2]) {
                Some(v) => max_count = v,
                None => {
                    CommandManager::send_error_text(self, out, "Error parsing max count !");
                    return Ok(());
                }
            }
        }
        max_count = max_count.clamp(1, 100);

        let server = ctx.server()?;
        let character = ctx.player()?;
        let me = Target::Obj(WorldObject::from(&character));
        let cmd = &self.command_names()[0];
        let world = parent_world(server, &character).ok_or_else(nre)?;
        let mut results: Vec<(f32, WorldObject)> = Vec::new();
        let mut add = |o: WorldObject| {
            let d = distance_to(server, &me, &Target::Obj(o.clone()), false);
            results.push((d, o));
        };
        match args[0].to_lowercase().as_str() {
            "doodads" | "doodad" => {
                for d in world.all_doodads() {
                    if d.data().template_id == template_id {
                        add(WorldObject::from(&d));
                    }
                }
            }
            // C#: the gimmick case searches the NPCs as well.
            "mob" | "mobs" | "npc" | "npcs" | "gimmick" | "gimmicks" => {
                for n in world.all_npcs() {
                    if n.data().unit.template_id == template_id {
                        add(WorldObject::from(&n));
                    }
                }
            }
            _ => {
                out.send_message(&format!("[{cmd}] unsupported type selected {}", args[0]));
            }
        }

        // OrderBy (stable).
        results.sort_by(|a, b| a.0.partial_cmp(&b.0).unwrap_or(std::cmp::Ordering::Equal));
        let mut c = 0u32;

        if !results.is_empty() {
            if results.len() > max_count as usize {
                out.send_message(&format!(
                    "[{cmd}] Showing {max_count}/{} nearest object(s) of {}",
                    results.len(),
                    args[0]
                ));
            } else {
                out.send_message(&format!(
                    "[{cmd}] Showing {} nearest object(s) of {}",
                    results.len(),
                    args[0]
                ));
            }

            for (_, o) in &results {
                show_object_data(server, o, c as usize, "", true, out);
                c += 1;
                if c >= max_count {
                    break;
                }
            }
        } else {
            out.send_message(&format!("[{cmd}] No results."));
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<doodad || npc || gimmick> <template id> [max results]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Finds the closest object of the giver type and template id.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn findobject_lists_nearest_first() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let far = ts::spawn_doodad(&server, ts::DOODAD, 140.0, 100.0);
        let near = ts::spawn_doodad(&server, ts::DOODAD, 102.0, 100.0);
        let (_, out) =
            harness::run(&server, &gm.p, &format!("findobj doodad {} 1", ts::DOODAD)).await;
        assert_eq!(
            out,
            [
                "[findobject] Showing 1/2 nearest object(s) of doodad".to_string(),
                format!(
                    "#1 -> BcId: {} DoodadTemplateId: {} - @DOODAD_NAME({}) FuncGroupId 0",
                    near.obj_id,
                    ts::DOODAD,
                    ts::DOODAD
                ),
                "#1 -> [i0] X:102 Y:100 Z:100  r:0° p:0° y:0°".to_string(),
            ]
        );
        let _ = far;
        let (_, out) = harness::run(&server, &gm.p, "findobject npc 1").await;
        assert_eq!(out, ["[findobject] No results."]);
        let (_, out) = harness::run(&server, &gm.p, "findobject house 1").await;
        assert_eq!(
            out,
            [
                "[findobject] unsupported type selected house",
                "[findobject] No results."
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, "findobject npc").await;
        assert_eq!(
            out,
            [harness::error("findobject", "No template ID provided !")]
        );
        let (_, out) = harness::run(&server, &gm.p, "findobject npc 1 x").await;
        assert_eq!(
            out,
            [harness::error("findobject", "Error parsing max count !")]
        );
    }
}
