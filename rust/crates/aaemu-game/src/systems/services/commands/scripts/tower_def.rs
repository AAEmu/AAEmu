//! `Scripts/Commands/TowerDef.cs`.

use super::prelude::*;
use crate::systems::instances::packets::{
    SCTowerDefEndPacket, SCTowerDefStartPacket, SCTowerDefWaveStartPacket, TowerDefKey,
};

/// `args[i]` (an `IndexOutOfRangeException` past the end).
fn arg(args: &[String], i: usize) -> anyhow::Result<&str> {
    args.get(i)
        .map(String::as_str)
        .ok_or_else(|| anyhow::anyhow!("Index was outside the bounds of the array."))
}

/// `TowerDef`.
pub struct TowerDef;

impl TowerDef {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TowerDef {
    fn command_names(&self) -> Vec<String> {
        names(&["towerdef", "tower_def"])
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
        let key = |id: u32| TowerDefKey {
            tower_def_id: id,
            zone_group_id: 5,
        };
        match args[0].to_lowercase().as_str() {
            "list" => CommandManager::send_normal_text(self, out, "Not implemented"),
            "start" => {
                // C#: `args[1]` without a length check (IndexOutOfRangeException).
                let Some(start_id) = try_parse::<u32>(arg(args, 1)?) else {
                    return Ok(());
                };
                let player = ctx.player()?;
                let zone_id = player.data().unit.transform.zone_id;
                player.send(SCTowerDefStartPacket {
                    key: key(start_id),
                    event_zone_id: zone_id,
                });
            }
            "end" => {
                let Some(end_id) = try_parse::<u32>(arg(args, 1)?) else {
                    return Ok(());
                };
                let player = ctx.player()?;
                let zone_id = player.data().unit.transform.zone_id;
                player.send(SCTowerDefEndPacket {
                    key: key(end_id),
                    event_zone_id: zone_id,
                });
            }
            "next" => {
                let Some(next_id) = try_parse::<u32>(arg(args, 1)?) else {
                    return Ok(());
                };
                let Some(step) = try_parse::<u32>(arg(args, 2)?) else {
                    return Ok(());
                };
                let player = ctx.player()?;
                let zone_id = player.data().unit.transform.zone_id;
                player.send(SCTowerDefWaveStartPacket {
                    key: key(next_id),
                    event_zone_id: zone_id,
                    step,
                });
            }
            _ => CommandManager::send_error_text(
                self,
                out,
                &format!("Unknown tower defense action {}", args[0]),
            ),
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<action> <params>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Actions are: list, start, end, next".into()
    }
}

#[cfg(test)]
mod tests {
    use aaemu_commons::PacketStream;

    use super::super::harness;
    use crate::opcodes::sc;

    fn body(id: u32, zone: u32, step: Option<u32>) -> Vec<u8> {
        let mut s = PacketStream::new();
        s.write_u32(id).write_u16(5).write_u32(zone);
        if let Some(step) = step {
            s.write_u32(step);
        }
        s.into_bytes()
    }

    #[tokio::test]
    async fn tower_def_actions() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        gm.p.data().unit.transform.zone_id = 77;

        let (_, out) = harness::run(&server, &gm.p, "towerdef start 12").await;
        assert!(out.is_empty());
        assert_eq!(
            gm.packets(),
            vec![(sc::SCTowerDefStartPacket, body(12, 77, None))]
        );
        harness::run(&server, &gm.p, "tower_def END 3").await;
        assert_eq!(
            gm.packets(),
            vec![(sc::SCTowerDefEndPacket, body(3, 77, None))]
        );
        harness::run(&server, &gm.p, "towerdef next 4 2").await;
        assert_eq!(
            gm.packets(),
            vec![(sc::SCTowerDefWaveStartPacket, body(4, 77, Some(2)))]
        );
        // Unparsable id: nothing.
        let (_, out) = harness::run(&server, &gm.p, "towerdef start x").await;
        assert!(out.is_empty());
        assert!(gm.packets().is_empty());

        let (_, out) = harness::run(&server, &gm.p, "towerdef list").await;
        assert_eq!(out, [harness::normal("towerdef", "Not implemented")]);
        let (_, out) = harness::run(&server, &gm.p, "towerdef Foo").await;
        assert_eq!(
            out,
            [harness::error(
                "towerdef",
                "Unknown tower defense action Foo"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "towerdef").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/towerdef|r |cFFEEEEAA<action> <params>|r\n|cFF888888Actions are: list, start, end, next|r"]
        );
        // C#: `args[1]` without a length check throws.
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "towerdef start").await;
        assert!(out.is_empty());
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000Index was outside the bounds of the array.|r"]
        );
    }
}
