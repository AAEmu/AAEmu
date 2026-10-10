//! Port of AAEmu.UnitTests/Game/Utils/Scripts/SubCommands/DoodadSetAgeCommandTests.cs
//!
//! The C# runs `new DoodadCmd().PreExecute(character, "doodad", [alias, objId, age])`; the
//! `Scripts/Commands` `DoodadCmd` root is not ported yet, so the test root below registers
//! [`DoodadSetAgeCommand`] under the same aliases (`"age", "setage"`). The C# `RecordingDoodad`
//! (`Hide` / `Show` overrides) is observed through the packets a player next to the doodad
//! receives: `SCDoodadRemoved` (Hide), then `SCDoodadCreated` (Show) carrying the new
//! `PlantTime` (so it was set before the doodad was hidden / shown again).

use std::sync::Arc;

use super::doodads::DoodadSetAgeCommand;
use super::output::{CharacterMessageOutput, Color, PlayerCharacter};
use super::subcommand::{pre_execute, SubCommand, SubCommandBase};
use super::*;
use crate::models::doodad::Doodad;
use crate::opcodes::sc;
use crate::test_support::*;
use crate::world::{DoodadRef, Entity};

/// `DoodadCmd` (only the ported `age` / `setage` sub command).
struct DoodadCmd {
    base: SubCommandBase,
}

impl DoodadCmd {
    fn new() -> Self {
        let mut base = SubCommandBase::new(
            "[Doodad]",
            "Root command to manage Doodads",
            &format!("{COMMAND_PREFIX}doodad"),
        );
        base.register(Arc::new(DoodadSetAgeCommand::default()), &["age", "setage"]);
        Self { base }
    }
}

impl SubCommand for DoodadCmd {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }
}

struct Fx {
    server: GameServer,
    client: TestClient,
    player: PlayerRef,
}

/// `CreateCharacter()`: a character in a world (instance 0).
fn fx() -> Fx {
    let server = test_server();
    let mut client = TestClient::new(&server, 1, 1001);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "Gm", 100.0, 100.0),
        0x100,
    );
    client.drain();
    Fx {
        server,
        client,
        player,
    }
}

impl Fx {
    /// `world.AddObject(doodad)` (placed next to the character and shown).
    fn add_doodad(&mut self, plant_time: i64, growth_time_ms: i64) -> DoodadRef {
        let mut d = Doodad::new(777);
        d.plant_time = plant_time;
        d.growth_time_ms = growth_time_ms;
        d.transform.set_xyz(110.0, 110.0, 100.0);
        let doodad = Entity::new(123, d);
        let world = self.server.world.world(0).unwrap();
        world.spawn(&doodad, &self.server.view_ctx());
        self.client.drain();
        doodad
    }

    /// `command.PreExecute(character, "doodad", args, output)`; returns the output messages.
    fn run(&self, args: &[&str]) -> Vec<String> {
        let ctx = CommandContext {
            server: Some(&self.server),
            character: PlayerCharacter::arc(&self.player),
        };
        let mut out = CharacterMessageOutput::new(ctx.character.clone());
        let args: Vec<String> = args.iter().map(|s| s.to_string()).collect();
        pre_execute(&DoodadCmd::new(), &ctx, "doodad", &args, &mut out);
        out.messages().to_vec()
    }
}

fn is_red(m: &str) -> bool {
    m.starts_with("|cFFFF0000") && m.ends_with("|r")
}

#[tokio::test]
async fn pre_execute_valid_age_updates_plant_time_in_seconds_and_refreshes_visibility() {
    for (alias, age_seconds) in [("age", 3600i64), ("setage", 86_400), ("setage", 0)] {
        let mut f = fx();
        let now = chrono::Utc::now().timestamp();
        let growth_time = crate::world::now_ms() + 3_600_000;
        let doodad = f.add_doodad(now - 7 * 86_400, growth_time);
        let before = chrono::Utc::now().timestamp();

        let messages = f.run(&[alias, "123", &age_seconds.to_string()]);

        let after = chrono::Utc::now().timestamp();
        let plant_time = doodad.data().plant_time;
        assert!(plant_time >= before - age_seconds, "{alias} {age_seconds}");
        assert!(plant_time <= after - age_seconds, "{alias} {age_seconds}");
        // VisibilityChanges == "Hide, Show"
        let frames: Vec<(u16, Vec<u8>)> = f
            .client
            .drain()
            .into_iter()
            .filter(|(op, _)| *op == sc::SCDoodadRemovedPacket || *op == sc::SCDoodadCreatedPacket)
            .collect();
        let ops: Vec<u16> = frames.iter().map(|(op, _)| *op).collect();
        let first_created = ops
            .iter()
            .position(|op| *op == sc::SCDoodadCreatedPacket)
            .expect("Show");
        assert!(first_created > 0, "Hide before Show: {ops:?}");
        assert!(ops[..first_created]
            .iter()
            .all(|op| *op == sc::SCDoodadRemovedPacket));
        assert!(ops[first_created..]
            .iter()
            .all(|op| *op == sc::SCDoodadCreatedPacket));
        // PlantTimeWhenHidden == PlantTime: the shown doodad already carries the new time.
        let body = &frames[first_created].1;
        assert!(body.windows(8).any(|w| w == plant_time.to_le_bytes()));
        assert_eq!(doodad.data().growth_time_ms, growth_time);
        assert_eq!(
            messages
                .iter()
                .filter(|m| m.contains("123") && !is_red(m))
                .count(),
            1,
            "{messages:?}"
        );
    }
}

#[tokio::test]
async fn pre_execute_unknown_object_reports_missing_doodad() {
    let f = fx();

    let messages = f.run(&["setage", "999", "3600"]);

    assert_eq!(
        messages
            .iter()
            .filter(|m| is_red(m) && m.contains("999"))
            .count(),
        1,
        "{messages:?}"
    );
    assert_eq!(
        messages[0],
        Color::RED.wrap("[Doodad SetAge] Doodad with objId 999 Does not exist")
    );
}

#[tokio::test]
async fn pre_execute_negative_age_does_not_change_doodad() {
    let mut f = fx();
    let plant_time = 1_767_225_600; // 2026-01-01 00:00:00 UTC
    let doodad = f.add_doodad(plant_time, 0);

    let messages = f.run(&["setage", "123", "-1"]);

    assert_eq!(doodad.data().plant_time, plant_time);
    // VisibilityChanges is empty (the client only gets the error chat message).
    assert!(
        !f.client
            .drain_ops()
            .iter()
            .any(|op| *op == sc::SCDoodadRemovedPacket || *op == sc::SCDoodadCreatedPacket),
        "no Hide / Show"
    );
    assert_eq!(
        messages.iter().filter(|m| is_red(m)).count(),
        1,
        "{messages:?}"
    );
}
