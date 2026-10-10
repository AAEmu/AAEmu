//! `Scripts/Commands/AddPortals.cs`.

use super::prelude::*;
use crate::systems::npcs::portal;

/// `AddPortals`.
pub struct AddPortals;

impl AddPortals {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddPortals {
    fn command_names(&self) -> Vec<String> {
        names(&["register_portal", "add_portal", "addportal"])
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
        let server = ctx.server()?;
        let character = ctx.player()?;
        let (target, first_arg) = get_target_or_self(server, &character, Some(&args[0]));

        let portal_name = arg_at(args, first_arg)?.to_string();
        // `character.Transform.CloneAsSpawnPosition()` (the caller's position, not the
        // target's).
        let obj = WorldObject::from(&character);
        let world_pos = match parent_world(server, &character) {
            Some(w) => w.world_pos_rot(&obj),
            None => obj.transform().local(),
        };
        let mut x = world_pos.x;
        let mut y = world_pos.y;
        let mut z = world_pos.z;
        // C#: `zRot = position.Roll` (the roll, not the yaw, of the caller).
        let mut z_rot = world_pos.rot_x;
        let mut zone_id = character.data().unit.transform.zone_id;

        let full = args.len() == first_arg + 5;
        if full {
            if let Some(v) = parse_f32(&args[first_arg + 1]) {
                x = v;
            }
            if let Some(v) = parse_f32(&args[first_arg + 2]) {
                y = v;
            }
            if let Some(v) = parse_f32(&args[first_arg + 3]) {
                z = v;
            }
            if let Some(v) = try_parse::<u32>(&args[first_arg + 4]) {
                zone_id = v;
            }
            // If not using the current location, set the rotation to zero
            z_rot = 0.0;
        }

        portal::add_private_portal(server, &target, x, y, z, z_rot, zone_id, &portal_name);
        if is_other(&character, &target) {
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "added {portal_name} entry to {}'s portal book",
                    name_of(&target)
                ),
            );
            send_player_message(
                &target,
                &format!(
                    "[GM] {} has added the entry \"{portal_name}\" to your portal book",
                    name_of(&character)
                ),
            );
        } else {
            CommandManager::send_normal_text(
                self,
                out,
                &format!("Registered \"{portal_name}\" in your portal book"),
            );
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) <name> [<x> <y> <z> <zoneid>]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Adds a portal with <name> to your teleport book.\nIf [<x> <y> <z> <zoneid>] is omitted or incomplete, your current position will be used.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn register_portal_adds_private_portals() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        gm.p.data().unit.transform.zone_id = 7;
        gm.p.data().unit.transform.rot_x = 0.5;

        let (_, out) = harness::run(&server, &gm.p, "register_portal Home").await;
        assert_eq!(
            out,
            ["[register_portal] Registered \"Home\" in your portal book"]
        );
        {
            let c = gm.p.data();
            let p = &c.portals.private_portals[0];
            assert_eq!(
                (p.name.as_str(), p.x, p.y, p.z, p.zone_id, p.z_rot),
                ("Home", 100.0, 200.0, 100.0, 7, 0.5)
            );
        }
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCCharacterPortalsPacket));

        let (_, out) = harness::run(&server, &gm.p, "add_portal Bob Far 1.5 2.5 3.5 9").await;
        assert_eq!(
            out,
            ["[register_portal] added Far entry to Bob's portal book"]
        );
        assert_eq!(
            bob.chat(),
            ["[GM] Gm has added the entry \"Far\" to your portal book"]
        );
        {
            let c = bob.p.data();
            let p = &c.portals.private_portals[0];
            assert_eq!(
                (p.name.as_str(), p.x, p.y, p.z, p.zone_id, p.z_rot),
                ("Far", 1.5, 2.5, 3.5, 9, 0.0)
            );
        }
        // Only a name for a target: `args[firstArg]` is out of range.
        harness::run(&server, &gm.p, "addportal Bob").await;
        assert_eq!(
            gm.chat(),
            [
                "[register_portal] added Far entry to Bob's portal book",
                "|cFFFF0000Index was outside the bounds of the array.|r"
            ]
        );
    }
}
