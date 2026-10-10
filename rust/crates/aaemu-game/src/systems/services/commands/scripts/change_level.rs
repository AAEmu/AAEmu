//! `Scripts/Commands/ChangeLevel.cs`.

use std::sync::Mutex;

use super::prelude::*;
use crate::data::experience::{ExperienceTable, MATE_LEVEL_CAP, PLAYER_LEVEL_CAP};
use crate::models::char_data::ability;
use crate::systems::skills::progress::{self, SCLevelChangedPacket};

/// `ExperienceManager.GetExpNeededToGivenLevel(currentExp, targetLevel, mate)`.
fn exp_needed_to_given_level(
    table: &ExperienceTable,
    current_exp: i32,
    target_level: u8,
    mate: bool,
) -> anyhow::Result<i32> {
    if current_exp < 0 {
        // `ArgumentOutOfRangeException.ThrowIfNegative(currentExp)`.
        anyhow::bail!(
            "currentExp ('{current_exp}') must be a non-negative value. (Parameter 'currentExp')\nActual value was {current_exp}."
        );
    }
    let target_exp = table.exp_for_level(target_level, mate);
    Ok(0.max(target_exp.wrapping_sub(current_exp)))
}

/// `ChangeLevel`.
pub struct ChangeLevel {
    /// `ExperienceManager.Instance.MaxPlayerLevel` / `MaxMateLevel` for the help text (which
    /// has no server access): the level caps until a command ran, then the loaded values.
    max_levels: Mutex<(u8, u8)>,
}

impl ChangeLevel {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self {
            max_levels: Mutex::new((PLAYER_LEVEL_CAP, MATE_LEVEL_CAP)),
        }
    }
}

impl Command for ChangeLevel {
    fn command_names(&self) -> Vec<String> {
        names(&[
            "level",
            "setlevel",
            "set_level",
            "change_level",
            "changelevel",
        ])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let exp = &server.game_data.experience;
        *self.max_levels.lock().expect("levels poisoned") =
            (exp.max_player_level(), exp.max_mate_level());
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            //character.SendMessage("[Level] level: 1-100");
            return Ok(());
        }
        let character = ctx.player()?;
        let (target, first_arg) = get_target_or_self(server, &character, Some(&args[0]));

        let level = try_parse::<u8>(arg_at(args, first_arg)?).unwrap_or(0);

        if let Some(mate) = current_target(server, &character).and_then(|t| t.as_mate().cloned()) {
            if level <= 1 || level > exp.max_mate_level() {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!("Allowed level range: 2-{}|r", exp.max_mate_level()),
                );
                return Ok(());
            }
            let mate_exp = mate.data().experience;
            let exp_for_target_level = exp.exp_for_level(level, true).wrapping_sub(mate_exp);
            if exp_for_target_level > 0 {
                crate::systems::vehicles::mates::add_exp(server, &mate, exp_for_target_level);
            }
            let mate_level = mate.data().unit.level;
            if level > mate_level {
                crate::systems::skills::broadcast(
                    server,
                    &WorldObject::from(&mate),
                    &SCLevelChangedPacket {
                        obj_id: mate.obj_id,
                        level,
                    },
                    true,
                );
            }
        } else {
            if level <= 1 || level > exp.max_player_level() {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!("Allowed level range: 2-{}|r", exp.max_player_level()),
                );
                return Ok(());
            }

            let (abilities, experience) = {
                let c = target.data();
                let ability_exp = |a: u8| {
                    c.abilities
                        .abilities
                        .iter()
                        .find(|x| x.id == a)
                        .map(|x| x.exp)
                };
                (
                    [c.ability1, c.ability2, c.ability3].map(|a| (a, ability_exp(a))),
                    c.experience,
                )
            };
            let mut max_exp_to_add = 0;
            for (a, a_exp) in abilities {
                if a == ability::NONE {
                    continue;
                }
                // C#: `Abilities.Abilities[ability]` throws for a missing ability.
                let a_exp = a_exp.ok_or_else(|| {
                    anyhow::anyhow!("The given key '{a}' was not present in the dictionary.")
                })?;
                let exp_for_a = exp_needed_to_given_level(exp, a_exp, level, false)?;
                if exp_for_a > max_exp_to_add {
                    max_exp_to_add = exp_for_a;
                }
            }
            let exp_for_level = exp_needed_to_given_level(exp, experience, level, false)?;
            if exp_for_level > max_exp_to_add {
                max_exp_to_add = exp_for_level;
            }

            // Add maximum required xp to get to target levels
            if max_exp_to_add > 0 {
                progress::add_exp(server, &target, max_exp_to_add, true);
            }

            // If the target level is bigger than player's current level, refill HP/MP
            let mut c = target.data();
            if level > c.unit.level {
                c.unit.hp = c.unit.max_hp;
                c.unit.mp = c.unit.max_mp;
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) <level>".into()
    }

    fn get_command_help_text(&self) -> String {
        let (max_player, max_mate) = *self.max_levels.lock().expect("levels poisoned");
        format!(
            "Adds experience points needed to reach target <level>\nAllowed range is 2-{max_player} and {max_mate} for pets\n"
        )
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::{chars_data, spawn_mate};
    use super::super::harness;
    use crate::models::char_data::ability;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn level_adds_the_missing_experience() {
        let server = harness::server_with(chars_data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        {
            let mut c = gm.p.data();
            c.experience = 90_000; // level 10
            c.ability1 = 1;
            c.ability2 = ability::NONE;
            c.ability3 = ability::NONE;
            c.abilities.get_mut(1).unwrap().exp = 100_000;
        }
        let (_, out) = harness::run(&server, &gm.p, "level 12").await;
        assert!(out.is_empty());
        {
            let c = gm.p.data();
            assert_eq!(c.experience, 110_000);
            assert_eq!(c.unit.level, 12);
            // AddExp(…, true) also adds the experience to the active ability.
            assert_eq!(c.abilities.abilities[0].exp, 120_000);
        }
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCLevelChangedPacket));

        // The ability needs more experience than the character.
        gm.p.data().abilities.get_mut(1).unwrap().exp = 0;
        harness::run(&server, &gm.p, "level 12").await;
        assert_eq!(gm.p.data().experience, 220_000);

        {
            let mut c = bob.p.data();
            c.experience = 0;
            c.ability1 = ability::NONE;
            c.ability2 = ability::NONE;
            c.ability3 = ability::NONE;
        }
        harness::run(&server, &gm.p, "setlevel Bob 3").await;
        assert_eq!(bob.p.data().experience, 20_000);

        let (_, out) = harness::run(&server, &gm.p, "level 1").await;
        assert_eq!(
            out,
            [harness::error("level", "Allowed level range: 2-55|r")]
        );
        let (_, out) = harness::run(&server, &gm.p, "level").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/level|r |cFFEEEEAA(target) <level>|r\n|cFF888888Adds experience points needed to reach target <level>\nAllowed range is 2-55 and 50 for pets\n|r"]
        );
    }

    #[tokio::test]
    async fn level_on_a_targeted_mate() {
        let server = harness::server_with(chars_data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mate = spawn_mate(&server, 0x500, 105.0, 100.0);
        gm.p.data().unit.current_target = mate.obj_id;
        gm.packets();
        let (_, out) = harness::run(&server, &gm.p, "level 5").await;
        assert!(out.is_empty());
        assert_eq!(mate.data().experience, 4_000);
        assert_eq!(mate.data().unit.level, 5);
        let (_, out) = harness::run(&server, &gm.p, "level 51").await;
        assert_eq!(
            out,
            [harness::error("level", "Allowed level range: 2-50|r")]
        );
    }
}
