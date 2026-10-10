//! `Scripts/Commands/UseSkill.cs`.

use super::prelude::*;
use crate::systems::skills::data::SkillTemplate;
use crate::systems::skills::state::SkillRun;
use crate::systems::skills::types::{SkillCastTarget, SkillCaster, SkillObject};
use crate::systems::skills::{skill, tasks};

/// `TaskManager.Instance.Schedule(new UseSkillTask(new Skill(template), source, casterObj,
/// target, targetObj, skillObject), TimeSpan.FromMilliseconds(0))`: `Skill.Use(..., bypassGcd:
/// true)` on the task thread.
fn schedule_use_skill(
    server: &GameServer,
    template: Arc<SkillTemplate>,
    source: WorldObject,
    caster_obj: SkillCaster,
    target_obj: SkillCastTarget,
) {
    let use_skill = SkillRun::new(template, 1);
    tasks::schedule(server, 0, move |srv| {
        let mut target_obj = target_obj;
        skill::use_skill(
            &srv,
            &use_skill,
            &source,
            caster_obj,
            &mut target_obj,
            Some(SkillObject::None),
            true,
        );
    });
}

/// `UseSkill`.
pub struct UseSkill;

impl UseSkill {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }

    /// `DoAoe(character, skill)`: every unit within 20 m uses the skill on the character.
    fn do_aoe(server: &GameServer, character: &PlayerRef, template: &Arc<SkillTemplate>) {
        let obj = WorldObject::from(character);
        let Some(world) = parent_world(server, character) else {
            return;
        };
        for target in around_radius(&world, &obj, 20.0)
            .into_iter()
            .filter(WorldObject::is_unit)
        {
            let caster_obj = SkillCaster::Unit(target.obj_id());
            let target_obj = SkillCastTarget::Unit(character.obj_id);
            schedule_use_skill(server, template.clone(), target, caster_obj, target_obj);
        }
    }
}

impl Command for UseSkill {
    fn command_names(&self) -> Vec<String> {
        names(&["useskill", "use_skill", "testskill", "test_skill"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut args_idx = 0;
        let mut source = WorldObject::from(&character);
        let mut target = match current_target(server, &character) {
            None => source.clone(),
            Some(t) if t.is_unit() => t,
            // C#: `(Unit)character.CurrentTarget` on a doodad.
            Some(_) => anyhow::bail!(
                "Unable to cast object of type 'AAEmu.Game.Models.Game.DoodadObj.Doodad' to type 'AAEmu.Game.Models.Game.Units.Unit'."
            ),
        };

        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        if args[0] == "target" {
            std::mem::swap(&mut source, &mut target);
            args_idx += 1;
        }

        if args[0] == "area" {
            if let Some(skill_id_aoe) = try_parse::<u32>(arg_at(args, 1)?) {
                if let Some(t) = server.game_data.systems.skills.skill(skill_id_aoe).cloned() {
                    Self::do_aoe(server, &character, &t);
                }
            }
            return Ok(());
        }

        let caster_obj = SkillCaster::Unit(source.obj_id());
        let target_obj = SkillCastTarget::Unit(target.obj_id());

        let Some(skill_id) = try_parse::<u32>(arg_at(args, args_idx)?) else {
            return Ok(());
        };
        let Some(template) = server.game_data.systems.skills.skill(skill_id).cloned() else {
            return Ok(());
        };
        schedule_use_skill(server, template, source, caster_obj, target_obj);
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[target||area] <skillId>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Forces unit(target optional) to use a skill".into()
    }
}

#[cfg(test)]
mod tests {
    use std::time::Duration;

    use super::super::harness;
    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::systems::skills::data::SkillTemplate;

    #[tokio::test]
    async fn useskill_schedules_the_skill() {
        let mut data = GameData::empty();
        data.systems.skills.add_skill(SkillTemplate {
            id: 77,
            target_type: 4,
            ..SkillTemplate::default()
        });
        let server = harness::server_with(data);
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, "useskill 77").await;
        assert!(out.is_empty());
        // The task runs on the next scheduler turn.
        tokio::time::sleep(Duration::from_millis(50)).await;
        let ops: Vec<u16> = gm.packets().into_iter().map(|(op, _)| op).collect();
        assert!(
            ops.contains(&sc::SCSkillStartedPacket) || ops.contains(&sc::SCSkillFiredPacket),
            "{ops:?}"
        );

        // Unknown skills / bad ids: nothing.
        harness::run(&server, &gm.p, "useskill 78").await;
        harness::run(&server, &gm.p, "useskill x").await;
        harness::run(&server, &gm.p, "useskill area 78").await;
        tokio::time::sleep(Duration::from_millis(50)).await;
        assert!(gm.packets().is_empty());

        // `area` without an id / `target` without an id: index out of range.
        harness::run(&server, &gm.p, "useskill area").await;
        harness::run(&server, &gm.p, "useskill target").await;
        assert_eq!(
            gm.chat(),
            [
                "|cFFFF0000Index was outside the bounds of the array.|r",
                "|cFFFF0000Index was outside the bounds of the array.|r"
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, "useskill").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/useskill|r |cFFEEEEAA[target||area] <skillId>|r\n|cFF888888Forces unit(target optional) to use a skill|r"]
        );
    }
}
