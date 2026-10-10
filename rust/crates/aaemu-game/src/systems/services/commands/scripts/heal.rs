//! `Scripts/Commands/Heal.cs`.

use super::prelude::*;
use crate::packets::world::SCUnitPointsPacket;
use crate::systems::skills::combat;
use crate::systems::skills::types::KillReason;

/// `unit.Hp = unit.MaxHp; unit.Mp = unit.MaxMp;` + the `SCUnitPointsPacket` broadcast.
/// Returns the new (hp, max hp, mp, max mp).
fn refill(server: &GameServer, unit: &WorldObject) -> (i32, i32, i32, i32) {
    let (hp, max_hp, mp, max_mp) = unit
        .with_unit(|u| {
            let u = u.unit_mut();
            u.hp = u.max_hp;
            u.mp = u.max_mp;
            (u.hp, u.max_hp, u.mp, u.max_mp)
        })
        .unwrap_or_default();
    crate::systems::skills::broadcast(
        server,
        unit,
        &SCUnitPointsPacket {
            obj_id: unit.obj_id(),
            hp,
            mp,
        },
        true,
    );
    (hp, max_hp, mp, max_mp)
}

/// `Heal`.
pub struct Heal;

impl Heal {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Heal {
    fn command_names(&self) -> Vec<String> {
        names(&["heal"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let mut player_target = current_target(server, &character);

        let chat_target = args.first().map_or("", String::as_str);
        let target_player = server.world.character_by_name(chat_target);
        if !chat_target.is_empty() {
            if let Some(t) = &target_player {
                player_target = Some(WorldObject::from(t));
            }
        }

        if let (Some(target_player), Some(_)) = (&target_player, &player_target) {
            let obj = WorldObject::from(target_player);
            let old_hp = target_player.data().unit.hp;
            if old_hp == 0 {
                // This check is needed otherwise the player will be kicked
                CommandManager::send_error_text(
                    self,
                    out,
                    "Cannot heal a dead target, use the revive command instead",
                );
            } else {
                let (hp, ..) = refill(server, &obj);
                combat::post_update_current_hp(
                    server,
                    &obj,
                    Some(&obj),
                    old_hp,
                    hp,
                    KillReason::Unknown,
                );
            }
        } else if let Some(unit) = player_target.filter(WorldObject::is_unit) {
            // Player is trying to heal some other unit
            let old_hp = unit.with_unit(|u| u.unit().hp).unwrap_or(0);
            if old_hp == 0 {
                CommandManager::send_error_text(self, out, "Cannot heal a dead target");
            } else {
                let (hp, max_hp, mp, max_mp) = refill(server, &unit);
                let name = unit.with_base_unit(|b| b.name().to_string());
                send_player_message(
                    &character,
                    &format!("{name} => {hp}/{max_hp} HP, {mp}/{max_mp} MP"),
                );
                combat::post_update_current_hp(
                    server,
                    &unit,
                    Some(&unit),
                    old_hp,
                    hp,
                    KillReason::Unknown,
                );
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) or self".into()
    }

    fn get_command_help_text(&self) -> String {
        "Heals target or self if no target supplied".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::add_xp::tests::spawn_npc;
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn heal_refills_players_and_units() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        bob.p.data().unit.hp = 10;
        bob.p.data().unit.mp = 1;
        let (_, out) = harness::run(&server, &gm.p, "heal bob").await;
        assert!(out.is_empty());
        assert_eq!(
            {
                let c = bob.p.data();
                (c.unit.hp, c.unit.mp)
            },
            (5_000, 1_000)
        );
        // SCUnitPointsPacket(0x102, 5000, 1000) (values * 100)
        let mut body = vec![0x02, 0x01, 0x00];
        body.extend(500_000i32.to_le_bytes());
        body.extend(100_000i32.to_le_bytes());
        assert_eq!(bob.packets(), vec![(sc::SCUnitPointsPacket, body)]);

        bob.p.data().unit.hp = 0;
        let (_, out) = harness::run(&server, &gm.p, "heal Bob").await;
        assert_eq!(
            out,
            [harness::error(
                "heal",
                "Cannot heal a dead target, use the revive command instead"
            )]
        );

        // No name and no target: nothing happens (C#: no self heal).
        gm.p.data().unit.hp = 1;
        harness::run(&server, &gm.p, "heal").await;
        assert_eq!(gm.p.data().unit.hp, 1);

        // The current target unit.
        let npc = spawn_npc(&server, 0x500, 10, 105.0, 100.0, 1_000);
        npc.data().unit.hp = 5;
        gm.p.data().unit.current_target = npc.obj_id;
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "heal").await;
        assert!(out.is_empty());
        assert_eq!(npc.data().unit.hp, 1_000);
        assert_eq!(gm.chat(), ["Npc10 => 1000/1000 HP, 100/100 MP"]);
        npc.data().unit.hp = 0;
        let (_, out) = harness::run(&server, &gm.p, "heal nobody").await;
        assert_eq!(out, [harness::error("heal", "Cannot heal a dead target")]);
    }
}
