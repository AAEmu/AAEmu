//! `Scripts/Commands/AddXP.cs`.

use super::prelude::*;
use crate::systems::skills::progress;

/// `AddXP`.
pub struct AddXP;

impl AddXP {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddXP {
    fn command_names(&self) -> Vec<String> {
        names(&["xp", "add_xp", "addxp", "givexp"])
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

        let xp_to_add = try_parse::<i32>(arg_at(args, first_arg)?).unwrap_or(0);
        if xp_to_add > 0 {
            progress::add_exp(server, &target, xp_to_add, true);
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) <xp>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Adds experience points (to target player)".into()
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use crate::data::experience::{ExperienceTable, LevelTemplate};
    use crate::data::GameData;
    use crate::models::mate::Mate;
    use crate::models::npc::Npc;
    use crate::opcodes::sc;
    use crate::server::GameServer;
    use crate::world::{Entity, MateRef, NpcRef};

    use super::super::harness;

    /// Game data with a 55 level experience table (level n starts at `(n - 1) * 10 000` exp,
    /// `(n - 1) * 1 000` for mates).
    pub fn chars_data() -> GameData {
        let mut gd = GameData::empty();
        gd.experience = ExperienceTable::from_templates(
            (1u8..=55)
                .map(|level| LevelTemplate {
                    level,
                    total_exp: (i32::from(level) - 1) * 10_000,
                    total_mate_exp: (i32::from(level) - 1) * 1_000,
                    skill_points: 0,
                })
                .collect(),
        )
        .expect("experience table");
        gd
    }

    /// An NPC (template `template`, `hp` / `max_hp`) spawned at (x, y, 100) in instance 0.
    pub fn spawn_npc(
        server: &GameServer,
        obj_id: u32,
        template: u32,
        x: f32,
        y: f32,
        hp: i32,
    ) -> NpcRef {
        let mut n = Npc::new(template);
        n.unit.name = format!("Npc{template}");
        n.unit.hp = hp;
        n.unit.max_hp = hp;
        n.unit.mp = 100;
        n.unit.max_mp = 100;
        n.unit.level = 1;
        n.unit.transform.set_xyz(x, y, 100.0);
        let n = Entity::new(obj_id, n);
        let world = server.world.world(0).expect("instance 0");
        world.spawn(&n, &server.view_ctx());
        n
    }

    /// A level-1 mate spawned at (x, y, 100) in instance 0.
    pub fn spawn_mate(server: &GameServer, obj_id: u32, x: f32, y: f32) -> MateRef {
        let mut m = Mate::new(1, 100);
        m.unit.name = "Pet".into();
        m.unit.level = 1;
        m.unit.hp = 100;
        m.unit.max_hp = 100;
        m.unit.transform.set_xyz(x, y, 100.0);
        let m = Entity::new(obj_id, m);
        let world = server.world.world(0).expect("instance 0");
        world.spawn(&m, &server.view_ctx());
        m
    }

    #[tokio::test]
    async fn xp_adds_experience_to_self_and_target() {
        let server = harness::server_with(chars_data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        gm.p.data().experience = 90_000; // level 10
        bob.p.data().experience = 90_000;

        let (handled, out) = harness::run(&server, &gm.p, "xp 15000").await;
        assert!(handled);
        assert!(out.is_empty());
        assert_eq!(gm.p.data().experience, 105_000);
        assert_eq!(gm.p.data().unit.level, 11);
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCExpChangedPacket));

        harness::run(&server, &gm.p, "givexp Bob 500").await;
        assert_eq!(bob.p.data().experience, 90_500);
        // Negative / invalid values are ignored.
        harness::run(&server, &gm.p, "xp -5").await;
        harness::run(&server, &gm.p, "xp abc").await;
        assert_eq!(gm.p.data().experience, 105_000);
        // A name without a value: `args[firstArg]` is out of range.
        harness::run(&server, &gm.p, "xp Bob").await;
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000Index was outside the bounds of the array.|r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "xp").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/xp|r |cFFEEEEAA(target) <xp>|r\n|cFF888888Adds experience points (to target player)|r"]
        );
    }
}
