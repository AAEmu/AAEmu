//! `Scripts/Commands/TestCombat.cs`.

use super::prelude::*;
use crate::systems::skills::combat;
use crate::systems::skills::packets::{
    SCCombatEngagedPacket, SCCombatFirstHitPacket, SCCombatTextPacket,
};

/// `TestCombat`.
pub struct TestCombat;

impl TestCombat {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestCombat {
    fn command_names(&self) -> Vec<String> {
        names(&["testcombat", "test_combat"])
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
        let target = current_target(server, &character);

        match args[0].as_str() {
            // TODO Battle Start
            "engaged" => match target {
                Some(t) => {
                    character.send(SCCombatEngagedPacket {
                        obj_id: character.obj_id,
                    });
                    character.send(SCCombatEngagedPacket { obj_id: t.obj_id() });
                }
                None => CommandManager::send_error_text(self, out, "No target selected"),
            },
            // TODO Battle End
            "cleared" => match target.filter(WorldObject::is_unit) {
                Some(t) => {
                    combat::set_in_battle(server, &WorldObject::from(&character), false);
                    combat::set_in_battle(server, &t, false);
                }
                None => CommandManager::send_error_text(self, out, "No target selected"),
            },
            "first_hit" => match target {
                Some(t) => character.send(SCCombatFirstHitPacket {
                    victim: character.obj_id,
                    hitter: t.obj_id(),
                    hit_type: 0,
                }),
                None => CommandManager::send_error_text(self, out, "No target selected"),
            },
            // TODO Combat Effect
            "text" => character.send(SCCombatTextPacket {
                source: 0,
                target: character.obj_id,
                text_type: 0,
            }),
            _ => {}
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<engaged||cleared||first_hit||text>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Command to test combat related packets. You can try to use cleared if you are stuck in combat for example.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::spawn_npc;
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn testcombat_sends_combat_packets() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        for sub in ["engaged", "cleared", "first_hit"] {
            let (_, out) = harness::run(&server, &gm.p, &format!("testcombat {sub}")).await;
            assert_eq!(out, [harness::error("testcombat", "No target selected")]);
        }
        harness::run(&server, &gm.p, "testcombat text").await;
        assert_eq!(
            gm.packets(),
            vec![(sc::SCCombatTextPacket, vec![0, 0, 0, 0x01, 0x01, 0x00, 0])]
        );

        let npc = spawn_npc(&server, 0x500, 10, 105.0, 100.0, 1_000);
        gm.p.data().unit.current_target = npc.obj_id;
        gm.packets();
        harness::run(&server, &gm.p, "testcombat engaged").await;
        assert_eq!(
            gm.packets(),
            vec![
                (sc::SCCombatEngagedPacket, vec![0x01, 0x01, 0x00]),
                (sc::SCCombatEngagedPacket, vec![0x00, 0x05, 0x00]),
            ]
        );
        harness::run(&server, &gm.p, "testcombat first_hit").await;
        assert_eq!(
            gm.packets(),
            vec![(
                sc::SCCombatFirstHitPacket,
                vec![0x01, 0x01, 0x00, 0x00, 0x05, 0x00, 0, 0, 0, 0]
            )]
        );
        gm.p.data().unit.is_in_battle = true;
        npc.data().unit.is_in_battle = true;
        harness::run(&server, &gm.p, "test_combat cleared").await;
        assert!(!gm.p.data().unit.is_in_battle);
        assert!(!npc.data().unit.is_in_battle);
        // Unknown sub command: nothing.
        let (_, out) = harness::run(&server, &gm.p, "testcombat Engaged").await;
        assert!(out.is_empty());
    }
}
