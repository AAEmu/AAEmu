//! `Scripts/Commands/Kill.cs`.

use super::prelude::*;
use crate::systems::skills::combat;
use crate::systems::skills::types::KillReason;

/// `Kill`.
pub struct Kill;

impl Kill {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Kill {
    fn command_names(&self) -> Vec<String> {
        names(&["kill"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        // C#: `GetTargetOrSelf(character, null, out _)`, the result is unused.
        let _ = get_target_or_self(server, &character, None);
        let self_obj = WorldObject::from(&character);
        if let Some(a_unit) = current_target(server, &character).filter(WorldObject::is_unit) {
            // Player is trying to kill an NPC/Monster
            let (hp, max_hp) = a_unit
                .with_unit(|u| (u.unit().hp, u.unit().max_hp))
                .unwrap_or_default();
            if hp == 0 {
                CommandManager::send_error_text(self, out, "Target is already dead");
            } else {
                // We must broadcast this package because if character had initially attacked the mob and then executed kill
                // the mob's "ghost" will still be attacking you and draining HP even though he doesn't exist in the world anymore
                a_unit.with_unit(|u| u.unit_mut().current_target = 0);

                // Don't directly do the DoDie(), but trigger a damage with enough damage to kill target
                // no matter what (even if it somehow still had hp after settings it to 0)
                combat::reduce_current_hp(
                    server,
                    &a_unit,
                    Some(&self_obj),
                    max_hp.wrapping_add(1).wrapping_mul(10),
                    KillReason::Gm,
                );
            }
        } else {
            CommandManager::send_normal_text(self, out, "Cannot kill this target");
        }

        // In case the character gets stuck in battle mode after engaging a mob
        combat::set_in_battle(server, &self_obj, false);
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target)".into()
    }

    fn get_command_help_text(&self) -> String {
        "Kills target".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::spawn_npc;
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn kill_kills_the_current_target() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        gm.p.data().unit.is_in_battle = true;
        let (_, out) = harness::run(&server, &gm.p, "kill").await;
        assert_eq!(out, ["[kill] Cannot kill this target"]);
        assert!(!gm.p.data().unit.is_in_battle);
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCCombatClearedPacket));

        let npc = spawn_npc(&server, 0x500, 10, 105.0, 100.0, 1_000);
        npc.data().unit.current_target = gm.p.obj_id;
        gm.p.data().unit.current_target = npc.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "kill").await;
        assert!(out.is_empty());
        assert_eq!(npc.data().unit.hp, 0);
        assert_eq!(npc.data().unit.current_target, 0);
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCUnitDeathPacket));
        let (_, out) = harness::run(&server, &gm.p, "kill").await;
        assert_eq!(out, [harness::error("kill", "Target is already dead")]);
    }
}
