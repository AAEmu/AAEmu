//! `Scripts/Commands/FailSkill.cs`.

use super::prelude::*;
use crate::systems::skills::packets::SCSkillStartedPacket;
use crate::systems::skills::types::{SkillCastTarget, SkillCaster, SkillObject};

/// `FailSkill`.
pub struct FailSkill;

impl FailSkill {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for FailSkill {
    fn command_names(&self) -> Vec<String> {
        names(&["failskill", "fail_skill", "skillfail", "skill_fail"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let mut args_idx = 0;
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        let fail_id = match try_parse::<u8>(&args[args_idx]) {
            Some(v) if v != 0 => v,
            _ => {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!(
                        "<skill result> parse error (byte), must be larger than 0, your value: {}",
                        args[args_idx]
                    ),
                );
                return Ok(());
            }
        };
        args_idx += 1;

        let mut ushort_val = 0u16;
        if args.len() > args_idx {
            match try_parse::<u16>(&args[args_idx]) {
                Some(v) => ushort_val = v,
                None => {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("[ushort] parse error, your value: {}", args[args_idx]),
                    );
                    return Ok(());
                }
            }
            args_idx += 1;
        }

        let mut uint_val = 0u32;
        if args.len() > args_idx {
            match try_parse::<u32>(&args[args_idx]) {
                Some(v) => uint_val = v,
                None => {
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!("not a number (uint), your value: {}", args[args_idx]),
                    );
                    return Ok(());
                }
            }
        }

        let server = ctx.server()?;
        let character = ctx.player()?;
        // Source and target: the character itself.
        let caster_obj = SkillCaster::Unit(character.obj_id);
        let target_obj = SkillCastTarget::Unit(character.obj_id);
        let skill_id = 2u32; // using basic melee attack
        if server.game_data.systems.skills.skill(skill_id).is_none() {
            return Ok(());
        }
        let mut packet =
            SCSkillStartedPacket::new(skill_id, 0, caster_obj, target_obj, SkillObject::None);
        packet
            .set_skill_result(fail_id)
            .set_result_ushort(ushort_val)
            .set_result_uint(uint_val);
        character.send(packet);
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<skill result> [ushort] [uint]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Force yourself to use a default skill that fails with a given error".into()
    }
}

#[cfg(test)]
mod tests {
    use aaemu_commons::Marshal;

    use super::super::harness;
    use super::*;
    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::systems::skills::data::SkillTemplate;

    #[tokio::test]
    async fn failskill_sends_a_failed_skill_start() {
        let mut data = GameData::empty();
        data.systems.skills.add_skill(SkillTemplate {
            id: 2,
            ..SkillTemplate::default()
        });
        let server = harness::server_with(data);
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, "failskill 5 7 9").await;
        assert!(out.is_empty());
        let mut expected = SCSkillStartedPacket::new(
            2,
            0,
            SkillCaster::Unit(0x101),
            SkillCastTarget::Unit(0x101),
            SkillObject::None,
        );
        expected
            .set_skill_result(5)
            .set_result_ushort(7)
            .set_result_uint(9);
        let mut body = aaemu_commons::PacketStream::new();
        expected.write(&mut body);
        assert_eq!(
            gm.packets(),
            vec![(sc::SCSkillStartedPacket, body.into_bytes())]
        );

        let (_, out) = harness::run(&server, &gm.p, "failskill 0").await;
        assert_eq!(
            out,
            [harness::error(
                "failskill",
                "<skill result> parse error (byte), must be larger than 0, your value: 0"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "skill_fail 1 x").await;
        assert_eq!(
            out,
            [harness::error(
                "failskill",
                "[ushort] parse error, your value: x"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "skillfail 1 2 -3").await;
        assert_eq!(out, ["[failskill] not a number (uint), your value: -3"]);
        assert!(gm.packets().is_empty());
    }
}
