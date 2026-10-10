//! `Scripts/Commands/AddBuff.cs`.

use super::prelude::*;
use crate::systems::skills::buffs;
use crate::systems::skills::types::SkillCaster;
use crate::world::now_ms;

/// `AddBuff`.
pub struct AddBuff;

impl AddBuff {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }

    /// The `view` listing.
    fn view(
        &self,
        server: &GameServer,
        ctx: &CommandContext<'_>,
        selected_unit: &WorldObject,
        user_friendly_name: &str,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // `GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs, true)`: (template id, passive).
        let lists = selected_unit.with_base_unit(|b| {
            b.buffs().all_buffs(true).map(|(good, bad, hidden)| {
                let ids = |v: Vec<&crate::models::buff::Buff>| {
                    v.iter()
                        .map(|b| (b.template.id, b.passive))
                        .collect::<Vec<_>>()
                };
                (ids(good), ids(bad), ids(hidden))
            })
        });
        let Ok((good_buffs, bad_buffs, hidden_buffs)) = lists else {
            // C#: `GetAllBuffs` throws for an unknown buff kind.
            anyhow::bail!("Specified method is not supported.");
        };
        let (obj_id, name) = (
            selected_unit.obj_id(),
            selected_unit.with_base_unit(|b| b.name().to_string()),
        );

        let skills = &server.game_data.systems.skills;
        let localization = &server.game_data.systems.services.localization;
        let mut tags: Vec<u32> = Vec::new();
        if good_buffs.len() + bad_buffs.len() + hidden_buffs.len() > 0 {
            ctx.character.send_message(&format!(
                "[AddBuff] Listing buffs for {obj_id} - {name} {user_friendly_name} !"
            ));
            for (color, list) in [
                ("FF00FF00", &good_buffs),
                ("FFFF0000", &bad_buffs),
                ("FF6666FF", &hidden_buffs),
            ] {
                for (id, passive) in list {
                    tags.extend_from_slice(skills.buff_tags(*id));
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "|c{color}{}{id} - {}|r",
                            if *passive { "Passive " } else { "" },
                            localization.get("buffs", "name", i64::from(*id), "")
                        ),
                    );
                }
            }
            if !tags.is_empty() {
                let tags: Vec<String> = tags.iter().map(u32::to_string).collect();
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("|cFF888888Tags: {}|r", tags.join(",")),
                );
            }
        } else {
            CommandManager::send_normal_text(self, out, &format!("No buffs on {obj_id} - {name}"));
        }
        Ok(())
    }
}

impl Command for AddBuff {
    fn command_names(&self) -> Vec<String> {
        names(&["buff", "addbuff", "add_buff", "buffs"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let mut first_arg = 0;
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut source_unit = WorldObject::from(&character);

        let Some(selected_unit) = current_target(server, &character).filter(WorldObject::is_unit)
        else {
            CommandManager::send_error_text(self, out, "No target unit selected");
            return Ok(());
        };

        let mut target_unit = selected_unit.clone();
        let user_friendly_name = match &selected_unit {
            WorldObject::Npc(n) => format!("@NPC_NAME({})", n.data().unit.template_id),
            _ => String::new(),
        };

        let a0 = args[0].to_lowercase();
        if a0 == "view" || a0 == "v" {
            return self.view(server, ctx, &selected_unit, &user_friendly_name, out);
        }

        if a0 == "astarget" || a0 == "t" || a0 == "at" {
            first_arg += 1;
            source_unit = selected_unit.clone();
            target_unit = WorldObject::from(&character);
        }

        if args.len() <= first_arg {
            CommandManager::send_error_text(self, out, "No buffId provided ?");
            return Ok(());
        }

        let Some(buff_id_int) = try_parse::<i32>(&args[first_arg]) else {
            CommandManager::send_error_text(self, out, "Parse error for buffId !");
            return Ok(());
        };

        let mut ab_level = 1u32;
        if args.len() > first_arg + 1 {
            ab_level = try_parse::<u32>(&args[first_arg + 1]).unwrap_or(1);
        }

        // `(uint)Math.Abs(buffIdInt)`.
        let Some(buff_id) = buff_id_int.checked_abs().map(|v| v as u32) else {
            anyhow::bail!("Negating the minimum value of a twos complement number is invalid.");
        };

        let Some(buff_template) = server.game_data.systems.skills.buff(buff_id).cloned() else {
            CommandManager::send_error_text(self, out, &format!("Unknown buffId {buff_id}"));
            return Ok(());
        };

        if buff_id_int > 0 {
            let mut new_buff = buffs::new_buff(
                server,
                &buff_template,
                Some(&source_unit),
                &SkillCaster::Unit(source_unit.obj_id()),
                None,
                now_ms(),
            );
            new_buff.ab_level = ab_level;
            buffs::add_buff(server, &target_unit, new_buff, 0, 0);
        }

        if buff_id_int < 0 {
            if selected_unit.with_base_unit(|b| b.buffs().check_buff(buff_id)) {
                buffs::remove_buff(server, &selected_unit, buff_id);
            } else {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!("Target didn't have buff to remove {buff_id}"),
                );
            }
        } else {
            // I think this might be zero
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[[View] || [AsTarget] <buffId>] <ab_level>".into()
    }

    fn get_command_help_text(&self) -> String {
        concat!(
            "Adds or removes a buff to selected target. Negative BuffId's will remove a buff. ",
            "If preceded by AsTarget (at) buff will be applied as if target was source. ",
            "If you provide view as a parameter, it will list the current buffs on target.",
            "You can also use 'v' instead of 'view' and 'at' or 't' instead of 'AsTarget'"
        )
        .into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::spawn_npc;
    use super::super::harness;
    use crate::data::GameData;
    use crate::systems::skills::data::BuffTemplate;

    #[tokio::test]
    async fn buff_adds_views_and_removes_buffs() {
        let mut data = GameData::empty();
        data.systems.skills.add_buff(BuffTemplate {
            id: 500,
            kind: 1,
            duration: 60_000,
            ..BuffTemplate::default()
        });
        data.systems.services.localization.add_translation(
            "buffs".into(),
            "name".into(),
            500,
            "Strength".into(),
        );
        let server = harness::server_with(data);
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, "buff 500").await;
        assert_eq!(out, [harness::error("buff", "No target unit selected")]);

        let npc = spawn_npc(&server, 0x500, 10, 105.0, 100.0, 1_000);
        gm.p.data().unit.current_target = npc.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "buff v").await;
        assert_eq!(out, ["[buff] No buffs on 1280 - Npc10"]);

        let (_, out) = harness::run(&server, &gm.p, "addbuff 500 3").await;
        assert!(out.is_empty());
        assert!(npc.data().unit.buffs.check_buff(500));
        assert_eq!(
            npc.data()
                .unit
                .buffs
                .effect_from_buff_id(500)
                .unwrap()
                .ab_level,
            3
        );

        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "buffs VIEW").await;
        assert_eq!(out, ["[buff] |cFF00FF00500 - Strength|r"]);
        assert_eq!(
            gm.chat()[0],
            "[AddBuff] Listing buffs for 1280 - Npc10 @NPC_NAME(10) !"
        );

        // As target: the buff goes on the character, cast by the target.
        harness::run(&server, &gm.p, "buff at 500").await;
        assert!(gm.p.data().unit.buffs.check_buff(500));

        harness::run(&server, &gm.p, "buff -500").await;
        assert!(!npc.data().unit.buffs.check_buff(500));
        let (_, out) = harness::run(&server, &gm.p, "buff -500").await;
        assert_eq!(
            out,
            [harness::error(
                "buff",
                "Target didn't have buff to remove 500"
            )]
        );

        let (_, out) = harness::run(&server, &gm.p, "buff 501").await;
        assert_eq!(out, [harness::error("buff", "Unknown buffId 501")]);
        let (_, out) = harness::run(&server, &gm.p, "buff x").await;
        assert_eq!(out, [harness::error("buff", "Parse error for buffId !")]);
        let (_, out) = harness::run(&server, &gm.p, "buff t").await;
        assert_eq!(out, [harness::error("buff", "No buffId provided ?")]);
    }
}
