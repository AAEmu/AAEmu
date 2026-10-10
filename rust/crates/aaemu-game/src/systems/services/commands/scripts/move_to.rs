//! `Scripts/Commands/MoveTo.cs`.

use super::prelude::*;
use crate::systems::ai::simulation;

/// `MoveTo`.
pub struct MoveTo;

impl MoveTo {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for MoveTo {
    fn command_names(&self) -> Vec<String> {
        names(&["moveto"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let mut name_file = "movefile".to_string();
        let cmd: String;

        if args.is_empty() {
            ctx.character.send_message(
                "[MoveTo] /moveto <rec||save filename||go filename||back filename||stop||run||walk>",
            );
            return Ok(());
        }

        if args[0] == "rec" || args[0] == "stop" || args[0] == "run" || args[0] == "walk" {
            cmd = args[0].clone();
        } else if args.len() == 2 {
            cmd = args[0].clone();
            name_file = args[1].clone();
        } else {
            CommandManager::send_error_text(
                self,
                out,
                "there should be two parameters, a command and a file_name...",
            );
            return Ok(());
        }

        CommandManager::send_normal_text(self, out, &format!("cmd: {cmd}, nameFile: {name_file}"));
        let server = ctx.server()?;
        let character = ctx.player()?;
        let move_to = simulation::character_simulation(server, &character); // take the AI movement
                                                                            // moveTo.Npc = (Npc)character.CurrentTarget
        let npc = match current_target(server, &character) {
            None => None,
            Some(WorldObject::Npc(n)) => Some(n),
            Some(other) => return Err(invalid_cast_to_npc(&other)),
        };
        let Some(npc) = npc else {
            CommandManager::send_normal_text(self, out, "You need a target NPC to manage it!");
            return Ok(());
        };
        match cmd.as_str() {
            "rec" => {
                CommandManager::send_normal_text(self, out, "start recording...");
                simulation::start_record(server, &move_to, &character);
            }
            "save" => {
                CommandManager::send_normal_text(self, out, "have finished recording...");
                move_to.lock().expect("simulation poisoned").move_file_name = name_file;
                simulation::stop_record(&move_to).map_err(anyhow::Error::msg)?;
            }
            "go" => {
                CommandManager::send_normal_text(self, out, "walk go...");
                {
                    let mut s = move_to.lock().expect("simulation poisoned");
                    s.running_mode = false;
                    s.move_file_name = name_file;
                }
                simulation::go_to_path(server, &move_to, &npc, true, 0, 0);
            }
            "back" => {
                CommandManager::send_normal_text(self, out, "walk back...");
                {
                    let mut s = move_to.lock().expect("simulation poisoned");
                    s.running_mode = false;
                    s.move_file_name = name_file;
                }
                simulation::go_to_path(server, &move_to, &npc, false, 0, 0);
            }
            "run" => {
                CommandManager::send_normal_text(self, out, "turned on running mode...");
                move_to.lock().expect("simulation poisoned").running_mode = true;
            }
            //case "walk":
            //    character.SendMessage("[MoveTo] turned off running mode...");
            //    moveTo.RunningMode = walk;
            //    break;
            "stop" => {
                CommandManager::send_normal_text(self, out, "standing still...");
                simulation::stop_move(server, &move_to, &npc);
            }
            _ => {}
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<rec||save filename||go filename||back filename||stop||run||walk>".into()
    }

    fn get_command_help_text(&self) -> String {
        "what is he doing:\n\
         - automatically writes the route to the file;\n\
         - you can load path data from a file;\n- moves along the route.\n\n\
         To start, you need to create the route (s), recording occurs as follows:\n\
         1. Start recording;\n\
         2. Take a route;\n\
         3. Stop recording.\n\
         === here is an example file structure (x, y, z) ===\n\
         |15629.0|14989.02|141.2055|\n\
         |15628.0|14987.24|141.3826|\n\
         |15626.0|14983.88|141.3446|\n\
         ===================================================;\n"
            .into()
    }
}

#[cfg(test)]
mod tests {
    use std::time::Duration;

    use super::super::harness;
    use crate::models::npc::Npc;
    use crate::systems::ai::simulation;
    use crate::world::Entity;

    #[tokio::test]
    async fn move_to_records_and_drives_the_target() {
        let server = harness::server();
        let dir = std::env::temp_dir().join(format!("aaemu-moveto-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        server.systems.ai.set_paths_folder(dir.clone());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);

        let (handled, out) = harness::run(&server, &gm.p, "moveto").await;
        assert!(handled);
        assert!(out.is_empty());
        assert_eq!(
            gm.chat(),
            ["[MoveTo] /moveto <rec||save filename||go filename||back filename||stop||run||walk>"]
        );
        let (_, out) = harness::run(&server, &gm.p, "moveto go").await;
        assert_eq!(
            out,
            [harness::error(
                "moveto",
                "there should be two parameters, a command and a file_name..."
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "moveto rec").await;
        assert_eq!(
            out,
            [
                harness::normal("moveto", "cmd: rec, nameFile: movefile"),
                harness::normal("moveto", "You need a target NPC to manage it!"),
            ]
        );

        // A player target: (Npc) cast exception
        let other = harness::pc(&server, 2, "Bob", 120.0, 200.0);
        gm.p.data().unit.current_target = other.p.obj_id;
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "moveto run").await;
        assert_eq!(out.len(), 1);
        assert_eq!(
            gm.chat().last().unwrap(),
            "|cFFFF0000Unable to cast object of type 'AAEmu.Game.Models.Game.Char.Character' to type 'AAEmu.Game.Models.Game.NPChar.Npc'.|r"
        );

        // An NPC target
        let mut n = Npc::new(100);
        n.unit.transform.set_xyz(110.0, 200.0, 100.0);
        let npc = Entity::new(0x900, n);
        server
            .world
            .world(0)
            .unwrap()
            .spawn(&npc, &server.view_ctx());
        gm.p.data().unit.current_target = npc.obj_id;

        let (_, out) = harness::run(&server, &gm.p, "moveto run").await;
        assert_eq!(
            out[1],
            harness::normal("moveto", "turned on running mode...")
        );
        let sim = simulation::character_simulation(&server, &gm.p);
        assert!(sim.lock().unwrap().running_mode);

        let (_, out) = harness::run(&server, &gm.p, "moveto rec").await;
        assert_eq!(out[1], harness::normal("moveto", "start recording..."));
        assert!(sim.lock().unwrap().save_path_enabled);
        tokio::time::sleep(Duration::from_millis(1150)).await;
        let (_, out) = harness::run(&server, &gm.p, "moveto save route1").await;
        assert_eq!(
            out,
            [
                harness::normal("moveto", "cmd: save, nameFile: route1"),
                harness::normal("moveto", "have finished recording..."),
            ]
        );
        assert!(!sim.lock().unwrap().save_path_enabled);
        let text = std::fs::read_to_string(dir.join("route1.path")).unwrap();
        assert!(text.starts_with("|100|200|100|"), "{text}");

        // Follow the recorded file (go), then stop
        let (_, out) = harness::run(&server, &gm.p, "moveto go route1").await;
        assert_eq!(out[1], harness::normal("moveto", "walk go..."));
        {
            let s = sim.lock().unwrap();
            assert_eq!(s.move_file_name, "route1");
            assert!(!s.running_mode);
        }
        let (_, out) = harness::run(&server, &gm.p, "moveto stop").await;
        assert_eq!(out[1], harness::normal("moveto", "standing still..."));
        assert!(!sim.lock().unwrap().move_to_path_enabled);
        let _ = std::fs::remove_dir_all(&dir);
    }
}
