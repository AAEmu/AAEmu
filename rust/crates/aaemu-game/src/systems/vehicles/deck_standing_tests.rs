//! Serialized standing-on movement through the ordinary authenticated session path.

use super::*;
use crate::models::movement::{MoveTypeActorFlags, MoveTypeFlags, UnitMove};
use crate::models::slave::Slave;
use crate::net::registry::ClientPacket;
use crate::packets::c2g_world::CSMoveUnitPacket;
use crate::world::{Entity, SlaveRef};

fn fixture() -> (Arc<GameServer>, TestClient, PlayerRef, SlaveRef) {
    let server = server_with(GameData::empty());
    let mut client = TestClient::new(&server, 1, 1);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "DeckSailor", 1002.0, 1001.0),
        PA,
    );
    client.conn.active_char = Some(player.clone());
    let world = server.world.world(0).unwrap();
    let mut hull = Slave::new(1, 1);
    hull.unit
        .transform
        .set_position(1000.0, 1000.0, 99.0, 0.0, 0.0, std::f32::consts::FRAC_PI_2);
    let boat = Entity::new(0x300, hull);
    world.spawn(&boat, &server.view_ctx());
    (server, client, player, boat)
}

fn packet(flags: MoveTypeFlags, actor_flags: MoveTypeActorFlags, gc_id: u32) -> CSMoveUnitPacket {
    let mut movement = MoveType::new(MoveKind::Unit(UnitMove {
        x: 1.0,
        y: -2.0,
        z: 1.0,
        rotation_z: 32,
        actor_flags,
        gc_flags: 1,
        gc_id,
        x2: 1002.0,
        y2: 1001.0,
        z2: 100.0,
        rotation_z2: 64,
        ..Default::default()
    }));
    movement.flags = flags;
    movement.sc_type = 4;
    movement.phase = 1;
    serialized(CSMoveUnitPacket {
        obj_id: PA,
        move_type: movement,
    })
}

fn serialized(packet: CSMoveUnitPacket) -> CSMoveUnitPacket {
    let mut bytes = PacketStream::new();
    bytes
        .write_bc(packet.obj_id)
        .write_u8(packet.move_type.type_raw);
    packet.move_type.write(&mut bytes);
    CSMoveUnitPacket::read(&mut PacketStream::from_bytes(bytes.as_bytes().to_vec())).unwrap()
}

fn close(actual: f32, expected: f32) {
    // The position payload quantizes Z; attachment and coordinate frame are exact.
    assert!((actual - expected).abs() < 0.002, "{actual} != {expected}");
}

#[tokio::test]
async fn actor_standing_without_header_attaches_and_keeps_deck_frame_during_controller_updates() {
    let (server, mut client, player, boat) = fixture();
    let world = server.world.world(0).unwrap();
    let me = WorldObject::from(&player);
    // The GC block is present even with the entire movement header flag byte clear.
    let initial = packet(
        MoveTypeFlags::NONE,
        MoveTypeActorFlags::STANDING_ON_OBJECT,
        boat.obj_id,
    );
    let MoveKind::Unit(u) = &initial.move_type.kind else {
        unreachable!()
    };
    assert_eq!(initial.move_type.flags, MoveTypeFlags::NONE);
    assert_eq!((u.gc_flags, u.gc_id), (1, boat.obj_id));
    initial.handle(&server, &mut client.conn).await.unwrap();
    let t = me.transform();
    assert_eq!((t.parent_obj_id, t.x, t.y), (boat.obj_id, 1.0, -2.0));
    close(t.z, 1.0);
    assert!(boat.core().children().iter().any(|child| child.same(&me)));
    let before = world.world_pos_rot(&me);
    close(before.x, 1002.0);
    close(before.y, 1001.0);

    // Carrying the deck forward must carry the player: detaching instead would use
    // the old auxiliary world position and leave the sailor behind.
    boat.data().unit.transform.x += 20.0;
    let carried = world.world_pos_rot(&me);
    close(carried.x, before.x + 20.0);
    close(carried.y, before.y);
    let mut controller = packet(
        MoveTypeFlags::IN_COMBAT | MoveTypeFlags::HAS_SC_TYPE_AND_PHASE,
        MoveTypeActorFlags::STANDING_ON_OBJECT,
        1,
    );
    assert_eq!(
        (controller.move_type.sc_type, controller.move_type.phase),
        (4, 1)
    );
    assert!(!controller
        .move_type
        .flags
        .contains(MoveTypeFlags::STANDING_ON_OBJECT));
    let MoveKind::Unit(u) = &mut controller.move_type.kind else {
        unreachable!()
    };
    u.x = 2.0;
    u.y = -3.0;
    // Deliberately stale backup; a valid current parent must retain local coordinates.
    u.x2 = 1002.0;
    u.y2 = 1001.0;
    serialized(controller)
        .handle(&server, &mut client.conn)
        .await
        .unwrap();
    let t = me.transform();
    assert_eq!((t.parent_obj_id, t.x, t.y), (boat.obj_id, 2.0, -3.0));
    close(t.rot_z, std::f32::consts::FRAC_PI_2);
    let moved = world.world_pos_rot(&me);
    close(moved.x, 1023.0);
    close(moved.y, 1002.0);

    // Both standing flags clear is an ordinary disembark, not a sticky attachment.
    let mut off = packet(MoveTypeFlags::NONE, MoveTypeActorFlags::NONE, 0);
    let MoveKind::Unit(u) = &mut off.move_type.kind else {
        unreachable!()
    };
    u.x = 1031.0;
    u.y = 1010.0;
    u.z = 100.0;
    serialized(off)
        .handle(&server, &mut client.conn)
        .await
        .unwrap();
    let t = me.transform();
    assert_eq!((t.parent_obj_id, t.x, t.y), (0, 1031.0, 1010.0));
    assert!(!boat.core().children().iter().any(|child| child.same(&me)));
    boat.data().unit.transform.x += 20.0;
    let after = world.world_pos_rot(&me);
    close(after.x, 1031.0);
    close(after.y, 1010.0);
}

#[tokio::test]
async fn actor_standing_without_header_rejects_missing_self_and_unattached_sentinel_parents() {
    for gc_id in [0x999, PA, 1] {
        let (server, mut client, player, boat) = fixture();
        let world = server.world.world(0).unwrap();
        let me = WorldObject::from(&player);
        packet(
            MoveTypeFlags::NONE,
            MoveTypeActorFlags::STANDING_ON_OBJECT,
            gc_id,
        )
        .handle(&server, &mut client.conn)
        .await
        .unwrap();
        let t = me.transform();
        assert_eq!((t.parent_obj_id, t.x, t.y), (0, 1002.0, 1001.0));
        close(t.z, 100.0);
        close(t.rot_z, std::f32::consts::PI);
        assert!(boat.core().children().is_empty());
        assert!(me.core().children().is_empty());
        let position = world.world_pos_rot(&me);
        close(position.x, 1002.0);
        close(position.y, 1001.0);
    }
}
