use super::*;

#[tokio::test]
async fn rotate_ends_before_moving_a_despawned_owner_whose_id_has_been_recycled() {
    let (server, mut client, _player, npc) = super::tests::setup();
    let owner = WorldObject::from(&npc);
    let template = SkillControllerTemplate {
        id: 1,
        kind_id: 7,
        value: [100, 0, 1000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ..Default::default()
    };
    let sc = create(
        &server,
        &template,
        &owner,
        &Target::Obj(owner.clone()),
        0.0,
        0.0,
        0.0,
        0.0,
    )
    .unwrap();
    activate(&server, &sc);
    let world = server.world.world(0).unwrap();
    world.delete(&owner);
    let mut replacement = crate::models::npc::Npc::new(2);
    replacement.unit.hp = 1000;
    replacement.unit.transform.rot_z = 0.5;
    let replacement = crate::world::Entity::new(npc.obj_id, replacement);
    world.spawn(&replacement, &server.view_ctx());
    client.drain();
    sc.tick(&server, Duration::from_millis(500));
    assert_eq!(sc.state(), ScState::Ended);
    assert_eq!(npc.data().unit.transform.rot_z, 0.0);
    assert_eq!(replacement.data().unit.transform.rot_z, 0.5);
    assert!(!client
        .drain()
        .iter()
        .any(|(op, _)| *op == crate::opcodes::sc::SCOneUnitMovementPacket));
}
#[tokio::test]
async fn real_7678_rotate_waits_interpolates_and_stops_at_the_native_deadline() {
    let (server, mut client, _player, npc) = super::tests::setup();
    let owner = WorldObject::from(&npc);
    npc.data().unit.transform.rot_z = 0.25;
    let template = SkillControllerTemplate {
        id: 7678,
        kind_id: 7,
        value: [100, 730, 1900, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0],
        ..Default::default()
    };
    let sc = create(
        &server,
        &template,
        &owner,
        &Target::Obj(owner.clone()),
        0.0,
        0.0,
        0.0,
        0.0,
    )
    .unwrap();
    activate(&server, &sc);
    sc.tick(&server, Duration::from_millis(700));
    assert_eq!(npc.data().unit.transform.rot_z, 0.25);
    sc.tick(&server, Duration::from_millis(615));
    assert!((npc.data().unit.transform.rot_z - (0.25 + 50_f32.to_radians())).abs() < 0.001);
    sc.tick(&server, Duration::from_secs(1));
    assert!((npc.data().unit.transform.rot_z - (0.25 + 100_f32.to_radians())).abs() < 0.001);
    assert_eq!(sc.state(), ScState::Ended);
    assert!(active(&owner).is_none());
    assert!(client
        .drain()
        .iter()
        .any(|(op, _)| *op == crate::opcodes::sc::SCOneUnitMovementPacket));
    let pos = npc.data().unit.transform;
    assert_eq!((pos.x, pos.y, pos.z), (105.0, 100.0, 100.0));
}

#[tokio::test]
async fn rotate_ends_when_the_owner_dies_and_negative_angles_turn_clockwise() {
    let (server, _client, _player, npc) = super::tests::setup();
    let owner = WorldObject::from(&npc);
    let template = SkillControllerTemplate {
        id: 1,
        kind_id: 7,
        value: [-100, 0, 1000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ..Default::default()
    };
    let sc = create(
        &server,
        &template,
        &owner,
        &Target::Obj(owner.clone()),
        0.0,
        0.0,
        0.0,
        0.0,
    )
    .unwrap();
    activate(&server, &sc);
    sc.tick(&server, Duration::from_millis(500));
    assert!((npc.data().unit.transform.rot_z + 50_f32.to_radians()).abs() < 0.001);
    npc.data().unit.hp = 0;
    sc.tick(&server, Duration::from_millis(500));
    assert_eq!(sc.state(), ScState::Ended);
    assert!((npc.data().unit.transform.rot_z + 50_f32.to_radians()).abs() < 0.001);
}
