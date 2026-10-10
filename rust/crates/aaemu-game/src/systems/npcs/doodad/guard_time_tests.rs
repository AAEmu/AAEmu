//! Port of AAEmu.UnitTests/Game/Models/Game/DoodadObj/DoodadGuardTimeTests.cs
//!
//! `IsPublicPropertyByAge` and the theft-evidence decision of `Doodad.Use`. The C#
//! `RecordingDoodad.GenerateTheftEvidence` override is the `SkillsBridge::generate_theft_evidence`
//! hook here (the Rust `Use` calls `CrimeManager.GenerateEvidenceFromTheft` through it).
//!
//! Time resolution: the C# boundary rows move `now` by one 100 ns tick; the Rust `PlantTime` is
//! unix seconds and `now` unix milliseconds, so one tick maps to one millisecond.

use std::sync::{Arc, Mutex};

use super::*;
use crate::data::GameData;
use crate::models::doodad::Doodad;
use crate::systems::npcs::doodad::runtime::DoodadCtx;
use crate::systems::npcs::doodad::templates::{DoodadGroups, DoodadTemplate};
use crate::systems::npcs::hooks::SkillsBridge;
use crate::test_support::*;
use crate::world::Entity;

/// `s_plantTime` = 2026-01-01 00:00:00 UTC (unix seconds).
const PLANT_TIME: i64 = 1_767_225_600;
const TEMPLATE: u32 = 4243;

/// The C# `RecordingDoodad.EvidenceRequests` (criminal character ids).
#[derive(Default)]
struct RecordingSkills {
    evidence_requests: Mutex<Vec<u32>>,
}

impl SkillsBridge for RecordingSkills {
    fn generate_theft_evidence(&self, _server: &GameServer, criminal: &PlayerRef, _d: &DoodadRef) {
        self.evidence_requests
            .lock()
            .unwrap()
            .push(criminal.data().id);
    }
}

/// A server with the test template (`Group.GuardOnFieldTime` = `guard_time` s) and, as the C#
/// `_funcsByGroups`, func group 0 = one non-loot func without skill (`TestNoOp`).
fn server(guard_time: u32) -> GameServer {
    let mut gd = GameData::empty();
    let d = &mut gd.systems.npcs.doodad;
    d.templates.insert(
        TEMPLATE,
        DoodadTemplate {
            id: TEMPLATE,
            group: DoodadGroups {
                guard_on_field_time: guard_time,
                ..DoodadGroups::default()
            },
            ..DoodadTemplate::default()
        },
    );
    d.template_order.push(TEMPLATE);
    d.funcs_by_groups.insert(
        0,
        vec![DoodadFunc {
            skill_id: 0,
            func_type: "TestNoOp".into(),
            ..DoodadFunc::default()
        }],
    );
    test_server_with(gd)
}

/// `CreateDoodad(guardTime, plantTime)`: owned by character 42.
fn create_doodad(plant_time: i64) -> DoodadRef {
    let mut d = Doodad::new(TEMPLATE);
    d.plant_time = plant_time;
    d.owner_id = 42;
    d.owner_type = doodad_owner_type::CHARACTER;
    Entity::new(0x500, d)
}

#[tokio::test]
async fn is_public_property_by_age_at_expiry_boundary_becomes_public_after_deadline() {
    // (ticks from expiry -> ms from expiry, expected)
    for (ms_from_expiry, expected) in [(-1i64, false), (0, false), (1, true)] {
        let server = server(3600);
        let world = server.world.world(0).unwrap();
        let doodad = create_doodad(PLANT_TIME);
        let ctx = DoodadCtx::new(&server, world, doodad);
        let now = (PLANT_TIME + 3600) * 1000 + ms_from_expiry;

        assert_eq!(
            is_public_property_by_age(&ctx, now),
            expected,
            "{ms_from_expiry}"
        );
    }
}

#[tokio::test]
async fn use_other_players_doodad_generates_evidence_only_while_protected() {
    for (guard_time, age_seconds, expect_evidence) in [
        (3600u32, 1800i64, true),
        (3600, 7200, false),
        (172_800, 129_600, true),
        (0, 172_800, true),
    ] {
        let server = server(guard_time);
        let skills = Arc::new(RecordingSkills::default());
        server
            .systems
            .npcs
            .hooks
            .skills
            .install(skills.clone() as Arc<dyn SkillsBridge>);
        let client = TestClient::new(&server, 1, 1001);
        // `new CharacterMock { Id = 99 }`
        let criminal = spawn_player(
            &server,
            &client,
            test_character(99, "Criminal", 100.0, 100.0),
            0x100,
        );
        let world = server.world.world(0).unwrap();
        let doodad = create_doodad(chrono::Utc::now().timestamp() - age_seconds);

        // doodad.Use(criminal)
        super::super::manager::use_doodad(
            &server,
            &world,
            &doodad,
            Some(&WorldObject::from(&criminal)),
            0,
            0,
        );

        let requests = skills.evidence_requests.lock().unwrap().clone();
        assert_eq!(
            requests.len(),
            usize::from(expect_evidence),
            "{guard_time} {age_seconds}"
        );
        if expect_evidence {
            assert_eq!(requests[0], 99);
        }
    }
}
