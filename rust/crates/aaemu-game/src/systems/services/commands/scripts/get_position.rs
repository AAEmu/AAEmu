//! `Scripts/Commands/GetPosition.cs`.

use super::prelude::*;

/// `GetPosition`.
pub struct GetPosition;

impl GetPosition {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `WorldInstance.ToString()`: `{Id}-{Template.Name}({Template.Id})`.
fn world_string(w: &WorldInstance) -> String {
    format!("{}-{}({})", w.id, w.template.name, w.template.id)
}

use crate::world::WorldInstance;

impl Command for GetPosition {
    fn command_names(&self) -> Vec<String> {
        names(&["position", "pos"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let target = current_target(server, &character);
        if let Some(target) = target.filter(|t| t.obj_id() != character.obj_id) {
            let pos = spawn_position(&clone_detached(server, &target));

            if let WorldObject::Npc(npc) = &target {
                // C#: `npc.Spawner.Id` (NullReferenceException without a spawner).
                if npc_spawner_of(server, npc.obj_id).is_none() {
                    return Err(nre());
                }
                let (spawner_id, template_id) = {
                    let n = npc.data();
                    (n.spawner_id, n.unit.template_id)
                };
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "Id: {spawner_id}, ObjId: {}, TemplateId: {template_id} X: |cFFFFFFFF{}|r  Y: |cFFFFFFFF{}|r  Z: |cFFFFFFFF{}|r",
                        npc.obj_id,
                        f32s(pos.x),
                        f32s(pos.y),
                        f32s(pos.z)
                    ),
                );
            }
        } else {
            let mut target_player = character.clone();
            if let Some(a) = args.first() {
                target_player = get_target_or_self(server, &character, Some(a)).0;
            }

            let pos = spawn_position(&clone_detached(server, &WorldObject::from(&target_player)));

            let mut zone_name = "???".to_string();
            if let Some(zone) = server.game_data.zones.zone_by_key(pos.zone_id) {
                zone_name = format!("@ZONE_NAME({})", zone.id);
            }

            let world = parent_world(server, &target_player)
                .map(|w| world_string(&w))
                .unwrap_or_default();
            // C#: `SubZoneId` is never assigned (always 0).
            let sub_zone_id = 0u32;
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "|cFFFFFFFF{}|r X: |cFFFFFFFF{}|r  Y: |cFFFFFFFF{}|r  Z: |cFFFFFFFF{}|r  RotZ: |cFFFFFFFF{}|r  ZoneId: |cFFFFFFFF{}|r {zone_name}  SubZoneId: |cFFFFFFFF{sub_zone_id}|r  Instance: |cFFFFFFFF{world}|r",
                    name_of(&target_player),
                    fixed(f64::from(pos.x), 1),
                    fixed(f64::from(pos.y), 1),
                    fixed(f64::from(pos.z), 1),
                    fixed(f64::from(pos.yaw), 0),
                    pos.zone_id
                ),
            );
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(player)".into()
    }

    fn get_command_help_text(&self) -> String {
        "Displays information about the position of you, or your target if a target is selected or provided as a argument.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn position_of_self_target_and_npc() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.25, 200.0);
        let _bob = harness::pc(&server, 2, "Bob", 10.0, 20.0);
        let world = server.world.world(0).unwrap();
        let w = format!(
            "{}-{}({})",
            world.id, world.template.name, world.template.id
        );
        let zone = gm.p.data().unit.transform.zone_id;
        let (_, out) = harness::run(&server, &gm.p, "pos").await;
        assert_eq!(
            out,
            [harness::normal(
                "position",
                &format!("|cFFFFFFFFGm|r X: |cFFFFFFFF100.3|r  Y: |cFFFFFFFF200.0|r  Z: |cFFFFFFFF100.0|r  RotZ: |cFFFFFFFF0|r  ZoneId: |cFFFFFFFF{zone}|r ???  SubZoneId: |cFFFFFFFF0|r  Instance: |cFFFFFFFF{w}|r")
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "position bob").await;
        assert!(out[0].starts_with("[position] |cFFFFFFFFBob|r X: |cFFFFFFFF10.0|r"));

        // An NPC target without a spawner: NullReferenceException.
        let npc = ts::spawn_npc(&server, ts::NPC, 110.0, 100.0);
        gm.p.data().unit.current_target = npc.obj_id;
        let (_, out) = harness::run(&server, &gm.p, "pos").await;
        assert!(out.is_empty());
    }
}
