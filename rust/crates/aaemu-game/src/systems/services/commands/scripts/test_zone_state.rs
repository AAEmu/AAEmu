//! `Scripts/Commands/TestZoneState.cs`.

use super::prelude::*;
use crate::world::zone_conflicts::{self, ZONE_CONFLICT_TYPE_NAMES};

/// `TestZoneState`.
pub struct TestZoneState;

impl TestZoneState {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestZoneState {
    fn command_names(&self) -> Vec<String> {
        names(&["zonestate", "zone_state"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let zones = &server.game_data.zones;
        if args.is_empty() {
            // List all when no args given
            for conflict in server.world.zone_conflicts.snapshot(server) {
                let zonegroup = zones
                    .group(u32::from(conflict.zone_group_id))
                    .ok_or_else(|| anyhow::anyhow!(NULL_REFERENCE))?;
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "ZoneGroup: {} ({}) State: {}",
                        zonegroup.name,
                        conflict.zone_group_id,
                        zone_conflicts::state_name(conflict.current_state)
                    ),
                );
            }
            return Ok(());
        }

        // Enum.TryParse(args[0], out ZoneConflictType) (byte enum)
        let mut zonestate = 0u8; // Tension
        if let Some(v) = enum_try_parse(&args[0], &ZONE_CONFLICT_TYPE_NAMES, 0, 255) {
            zonestate = v as u8;
        }

        let mut zonegroupid = 0u16;
        if args.len() > 1 {
            if let Some(v) = try_parse::<u16>(&args[1]) {
                zonegroupid = v;
            }
        }

        if zonegroupid == 0 {
            let zone_key = character.data().unit.transform.zone_id;
            if let Some(thiszone) = zones.zone_by_key(zone_key) {
                zonegroupid = thiszone.group_id as u16;
            }
        }

        if zonegroupid > 0 {
            let zonegroup = zones
                .group(u32::from(zonegroupid))
                .ok_or_else(|| anyhow::anyhow!(NULL_REFERENCE))?;

            if !zone_conflicts::set_state(server, zonegroupid, zonestate) {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!(
                        "ZoneGroup: {} ({}) does not have a conflict state defined in the game database and thus cannot be changed",
                        zonegroup.name, zonegroup.id
                    ),
                );
                return Ok(());
            }

            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "Changed ZoneGroup: {zonegroupid} to State: {}",
                    zone_conflicts::state_name(zonestate)
                ),
            );
        } else {
            CommandManager::send_error_text(
                self,
                out,
                &format!("Invalid zone group id {zonegroupid}|r"),
            );
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<StateId> [ZoneId]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Changes a zone's state (0=Tension, 1=Danger, 2=Dispute, 3=Unrest, 4=Crisis, 5=Conflict, 6=War, 7=Peace). If ZoneId is ommited, your current zone is used.".into()
    }
}

#[cfg(test)]
mod tests {
    use aaemu_commons::PacketStream;

    use super::super::harness;
    use crate::data::world_data::{Zone, ZoneConflict, ZoneGroup};
    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::test_support::TEST_ZONE;

    fn data() -> GameData {
        let mut data = GameData::empty();
        let z = &mut data.zones;
        z.zones.insert(
            TEST_ZONE,
            Zone {
                id: 1,
                name: "test".into(),
                zone_key: TEST_ZONE,
                group_id: 7,
                ..Zone::default()
            },
        );
        for (id, name) in [(7, "Solzreed"), (8, "Arcadian Sea")] {
            z.groups.insert(
                id,
                ZoneGroup {
                    id,
                    name: name.into(),
                    ..ZoneGroup::default()
                },
            );
        }
        z.conflicts.push(ZoneConflict {
            zone_group_id: 7,
            conflict_min: 10,
            war_min: 20,
            peace_min: 30,
            current_state: 0,
            ..ZoneConflict::default()
        });
        data
    }

    fn state_body(gid: u16, state: u8, end: i64) -> Vec<u8> {
        let mut s = PacketStream::new();
        s.write_u16(gid).write_u8(state).write_i64(end);
        s.into_bytes()
    }

    #[tokio::test]
    async fn zone_state_lists_and_changes_conflicts() {
        let server = harness::server_with(data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.0);
        let (handled, out) = harness::run(&server, &gm.p, "zonestate").await;
        assert!(handled);
        assert_eq!(
            out,
            [harness::normal(
                "zonestate",
                "ZoneGroup: Solzreed (7) State: Tension"
            )]
        );

        // By name, current zone's group; broadcast to everyone
        let (_, out) = harness::run(&server, &gm.p, "zonestate Crisis").await;
        assert_eq!(
            out,
            [harness::normal(
                "zonestate",
                "Changed ZoneGroup: 7 to State: Crisis"
            )]
        );
        assert_eq!(
            gm.packets(),
            vec![(sc::SCConflictZoneStatePacket, state_body(7, 4, 0))]
        );
        assert_eq!(
            server
                .world
                .zone_conflicts
                .get(&server, 7)
                .unwrap()
                .current_state,
            4
        );

        // By number and explicit group: War has a timer
        let before = chrono::Utc::now().timestamp();
        let (_, out) = harness::run(&server, &gm.p, "zone_state 6 7").await;
        assert_eq!(
            out,
            [harness::normal(
                "zonestate",
                "Changed ZoneGroup: 7 to State: War"
            )]
        );
        let c = server.world.zone_conflicts.get(&server, 7).unwrap();
        assert_eq!(c.current_state, 6);
        assert!(c.next_state_time >= before + 20 * 60 && c.next_state_time <= before + 20 * 60 + 5);
        let frames = gm.packets();
        assert_eq!(frames.len(), 1);
        assert_eq!(frames[0].1, state_body(7, 6, c.next_state_time));

        // Unknown name: Tension; group without a conflict
        let (_, out) = harness::run(&server, &gm.p, "zonestate nope 8").await;
        assert_eq!(
            out,
            [harness::error(
                "zonestate",
                "ZoneGroup: Arcadian Sea (8) does not have a conflict state defined in the game database and thus cannot be changed"
            )]
        );
        // Unknown group: NullReferenceException
        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "zonestate 1 99").await;
        assert!(out.is_empty());
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000Object reference not set to an instance of an object.|r"]
        );
        // No zone and no group
        gm.p.data().unit.transform.zone_id = 0;
        let (_, out) = harness::run(&server, &gm.p, "zonestate 1").await;
        assert_eq!(
            out,
            [harness::error("zonestate", "Invalid zone group id 0|r")]
        );
    }
}
