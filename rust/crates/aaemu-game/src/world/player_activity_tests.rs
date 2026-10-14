//! `WorldInstance::player_activity_revision`: the monotonic invalidation token for player
//! presence / proximity-relevant movement. It must change on player spawn / show, on a player's
//! `add_visible_object` position update (including the same-region early-return case), on player
//! hide / removal / delete and on `transfer_out` (leaving the instance); NPC movement must not
//! change it, and revisions are isolated per instance.

use super::{Entity, Player, WorldObject};
use crate::models::npc::Npc;
use crate::test_support::{spawn_player, test_character, test_server, TestClient};

const NPC: u32 = 0x200;

fn npc(x: f32, y: f32) -> Npc {
    let mut n = Npc::new(1234);
    n.unit.name = "Wolf".into();
    n.unit.transform.set_xyz(x, y, 100.0);
    n
}

#[tokio::test]
async fn player_arrival_bumps_revision() {
    let server = test_server();
    let world = server.world.world(0).expect("instance 0");
    assert_eq!(world.player_activity_revision(), 0);

    let a = TestClient::new(&server, 1, 1001);
    let pa = spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);

    assert!(world.player_activity_revision() > 0);
    assert!(pa.is_visible());
    assert_eq!(pa.region(), Some((0, 1, 1)));
}

#[tokio::test]
async fn player_movement_within_and_across_regions_bumps_revision() {
    let server = test_server();
    let world = server.world.world(0).expect("instance 0");
    let a = TestClient::new(&server, 1, 1001);
    let pa = spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
    let ctx = server.view_ctx();

    // Within the same region: `add_visible_locked` returns early, the revision must still move.
    pa.data().unit.transform.set_xyz(110.0, 110.0, 100.0);
    let before = world.player_activity_revision();
    let changes = world.add_visible_object(&pa, &ctx);
    assert!(changes.is_empty());
    assert!(world.player_activity_revision() > before);
    assert_eq!(pa.region(), Some((0, 1, 1)));

    // Across regions: the object moves between sectors.
    pa.data().unit.transform.set_xyz(400.0, 400.0, 100.0);
    let before = world.player_activity_revision();
    world.add_visible_object(&pa, &ctx);
    assert!(world.player_activity_revision() > before);
    assert_eq!(pa.region(), Some((0, 6, 6)));
}

#[tokio::test]
async fn player_departure_and_transfer_bump_revision() {
    let server = test_server();
    let world = server.world.world(0).expect("instance 0");
    let a = TestClient::new(&server, 1, 1001);
    let pa = spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
    let spawned = world.player_activity_revision();

    // Hide (`GameObject.Hide`): leaves the region grid.
    world.hide(&pa);
    let hidden = world.player_activity_revision();
    assert!(hidden > spawned);
    assert!(pa.region().is_none());

    // Show again: back in the grid.
    world.show(&pa, &server.view_ctx());
    let shown = world.player_activity_revision();
    assert!(shown > hidden);

    // Transfer out (the old instance's half of a cross-instance move).
    world.transfer_out(&WorldObject::from(&pa));
    let transferred = world.player_activity_revision();
    assert!(transferred > shown);
    assert!(pa.region().is_none());

    // Delete (`GameObject.Delete`): departure from the instance entirely.
    world.show(&pa, &server.view_ctx());
    let before_delete = world.player_activity_revision();
    world.delete(&pa);
    assert!(world.player_activity_revision() > before_delete);
    assert!(world.object(pa.obj_id).is_none());
    assert!(world.character_by_obj_id(pa.obj_id).is_none());
}

#[tokio::test]
async fn direct_player_removal_bumps_revision_once() {
    let server = test_server();
    let world = server.world.world(0).expect("instance 0");
    let a = TestClient::new(&server, 1, 1001);
    let pa = spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
    let spawned = world.player_activity_revision();

    // A direct `RemoveObject` of a player still in the grid (dungeon `RemovePlayer`).
    assert!(world.remove_object(pa.obj_id));
    let removed = world.player_activity_revision();
    assert!(removed > spawned);

    // Idempotent: re-removing the same id does not bump again.
    assert!(!world.remove_object(pa.obj_id));
    assert_eq!(world.player_activity_revision(), removed);
}

#[tokio::test]
async fn npc_movement_does_not_bump_revision() {
    let server = test_server();
    let world = server.world.world(0).expect("instance 0");
    let a = TestClient::new(&server, 1, 1001);
    spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
    let ctx = server.view_ctx();
    let player_rev = world.player_activity_revision();

    let n = Entity::new(NPC, npc(110.0, 110.0));
    world.spawn(&n, &ctx);
    assert_eq!(world.player_activity_revision(), player_rev);

    // Within the same region.
    n.data().unit.transform.set_xyz(120.0, 120.0, 100.0);
    world.add_visible_object(&n, &ctx);
    assert_eq!(world.player_activity_revision(), player_rev);

    // Across regions.
    n.data().unit.transform.set_xyz(400.0, 400.0, 100.0);
    world.add_visible_object(&n, &ctx);
    assert_eq!(world.player_activity_revision(), player_rev);

    world.delete(&n);
    assert_eq!(world.player_activity_revision(), player_rev);
}

#[tokio::test]
async fn revisions_are_isolated_between_instances() {
    let server = test_server();
    let w0 = server.world.world(0).expect("instance 0");
    let w1 = server.world.create_world_instance(w0.template.clone(), 7);
    assert_eq!(w0.player_activity_revision(), 0);
    assert_eq!(w1.player_activity_revision(), 0);

    // Player activity in instance 1 does not touch instance 0's revision.
    let b = TestClient::new(&server, 2, 1002);
    let p1 = Player::new(test_character(2, "Beta", 100.0, 100.0), b.conn.handle());
    server.world.try_add_character(&p1);
    w1.spawn(&p1, &server.view_ctx());
    assert!(w1.player_activity_revision() > 0);
    assert_eq!(w0.player_activity_revision(), 0);

    // ...and activity in instance 0 does not touch instance 1's revision.
    let a = TestClient::new(&server, 1, 1001);
    spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
    let w1_rev = w1.player_activity_revision();
    assert!(w0.player_activity_revision() > 0);
    assert_eq!(w1.player_activity_revision(), w1_rev);
}
