//! `Scripts/Commands/Damage.cs`.

use super::prelude::*;
use crate::systems::skills::combat;
use crate::systems::skills::types::KillReason;

/// `Damage`.
pub struct Damage;

impl Damage {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Damage {
    fn command_names(&self) -> Vec<String> {
        names(&["damage"])
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
        let self_obj = WorldObject::from(&character);
        let target = current_target(server, &character)
            .filter(WorldObject::is_unit)
            .unwrap_or_else(|| self_obj.clone());

        let mut damage_str = args[0].as_str();
        let mut is_percent = false;

        // check if user added the % directly after the number, if so, trim it and set it as percent value
        if damage_str.ends_with('%') {
            damage_str = damage_str.trim_end_matches('%');
            is_percent = true;
        }

        // Check if the 2nd argument is a "%"
        if args.len() > 1 && args[1] == "%" {
            is_percent = true;
        }

        // Try to parse damage
        let mut damage = try_parse::<i32>(damage_str).unwrap_or(100);

        // If % based, calculate its damage
        if is_percent {
            let max_hp = target.with_unit(|u| u.unit().max_hp).unwrap_or(0);
            // C#: unchecked int arithmetic.
            damage = max_hp.wrapping_mul(damage) / 100;
        }

        // Only apply if damage > 0 and we have a valid target
        if damage > 0 {
            combat::reduce_current_hp(server, &target, Some(&self_obj), damage, KillReason::Damage);
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<damage> [%]".into()
    }

    fn get_command_help_text(&self) -> String {
        format!(
            "Damages self or your current target based on raw damage or Hp percentage, usage:\n\
             {COMMAND_PREFIX}damage 9999  -> Inflicts 9999 damage\n\
             {COMMAND_PREFIX}damage 80 %  -> Inflicts 80% of target's max hp damage\n\
             {COMMAND_PREFIX}damage 20%   -> Inflicts 20% of target's max hp damage"
        )
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::spawn_npc;
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn damage_hits_self_or_the_target() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "damage 300").await;
        assert!(out.is_empty());
        assert_eq!(gm.p.data().unit.hp, 4_700);
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCUnitPointsPacket));
        // 10% of 5000.
        harness::run(&server, &gm.p, "damage 10%").await;
        assert_eq!(gm.p.data().unit.hp, 4_200);
        harness::run(&server, &gm.p, "damage 2 %").await;
        assert_eq!(gm.p.data().unit.hp, 4_100);
        // Unparsable: 100.
        harness::run(&server, &gm.p, "damage lots").await;
        assert_eq!(gm.p.data().unit.hp, 4_000);

        let npc = spawn_npc(&server, 0x500, 10, 105.0, 100.0, 1_000);
        gm.p.data().unit.current_target = npc.obj_id;
        harness::run(&server, &gm.p, "damage 50 %").await;
        assert_eq!(npc.data().unit.hp, 500);
        assert_eq!(gm.p.data().unit.hp, 4_000);

        let (_, out) = harness::run(&server, &gm.p, "damage").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/damage|r |cFFEEEEAA<damage> [%]|r\n|cFF888888Damages self or your current target based on raw damage or Hp percentage, usage:\n/damage 9999  -> Inflicts 9999 damage\n/damage 80 %  -> Inflicts 80% of target's max hp damage\n/damage 20%   -> Inflicts 20% of target's max hp damage|r"]
        );
    }
}
