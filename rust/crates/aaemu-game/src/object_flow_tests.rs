//! Phase-3a flow: a player sees an NPC and a doodad appear (spawn / walking into range),
//! disappear (walking away / despawn) and reappear, with the exact C# packet sequences
//! (`Region.AddToCharacters` / `RemoveFromCharacters`, `Npc` / `Doodad` visibility overrides).

use aaemu_commons::PacketStream;

use crate::models::doodad::Doodad;
use crate::models::npc::Npc;
use crate::models::transform::PositionAndRotation;
use crate::opcodes::sc;
use crate::test_support::*;
use crate::world::{unit_state, Entity, ObjectKind, WorldObject};

const NPC: u32 = 0x200;
const DOODAD: u32 = 0x300;

fn npc() -> Npc {
    let mut n = Npc::new(1234);
    n.unit.name = "Wolf".into();
    n.unit.level = 3;
    n.unit.hp = 80;
    n.unit.mp = 20;
    n.unit.max_hp = 80;
    n.unit.transform.set_xyz(110.0, 110.0, 100.0);
    n.has_ai = true;
    n
}

fn doodad() -> Doodad {
    let mut d = Doodad::new(777);
    d.func_group_id = 5;
    d.transform.set_xyz(120.0, 130.0, 100.0);
    d
}

/// `Doodad.Write` of [`doodad`], spelled out.
fn doodad_bytes() -> Vec<u8> {
    let mut s = PacketStream::new();
    s.write_bc(DOODAD)
        .write_u32(777)
        .write_bc(0) // owner obj
        .write_bc(0) // parent obj
        .write_u8(255) // AttachPointKind.System
        .write_position(120.0, 130.0, 100.0)
        .write_i16(0)
        .write_i16(0)
        .write_i16(0)
        .write_f32(1.0)
        .write_bool(false) // hasLootItem
        .write_u32(5) // func group
        .write_u32(0) // owner id
        .write_u64(0) // ucc
        .write_u32(0) // item template
        .write_u32(0) // type2
        .write_u32(0) // time left
        .write_i64(0) // plant time
        .write_u32(0) // quest glow
        .write_i32(0) // family
        .write_i32(-1) // puzzle group
        .write_u8(255) // owner type System
        .write_u32(0) // owner db id
        .write_i32(0); // data
    s.into_bytes()
}

fn npc_state_bytes(n: &Npc) -> Vec<u8> {
    let mut n = n.clone();
    unit_state::npc_unit_state(NPC, &mut n, "", 0).body
}

fn points(obj_id: u32, hp: i32, mp: i32) -> (u16, Vec<u8>) {
    let mut s = PacketStream::new();
    s.write_bc(obj_id).write_i32(hp * 100).write_i32(mp * 100);
    (sc::SCUnitPointsPacket, s.into_bytes())
}

fn ids_packet(op: u16, ids: &[u32]) -> (u16, Vec<u8>) {
    let mut s = PacketStream::new();
    s.write_u16(ids.len() as u16);
    for &i in ids {
        s.write_bc(i);
    }
    (op, s.into_bytes())
}

#[test]
fn doodad_write_is_byte_exact() {
    let d = doodad();
    let mut s = PacketStream::new();
    d.write(DOODAD, &PositionAndRotation::default(), 0, &mut s);
    assert_eq!(s.as_bytes(), &doodad_bytes()[..]);
}

#[tokio::test]
async fn player_sees_npc_and_doodad_appear_and_disappear() {
    let server = test_server();
    let mut a = TestClient::new(&server, 1, 1001);
    let pa = spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
    assert!(a.drain().is_empty()); // alone: nothing to see (own state comes from CSSpawnCharacter)
    let world = server.world.world(0).unwrap();
    let ctx = server.view_ctx();

    // ---- an NPC spawns next to the player: SCUnitState + SCUnitPoints.
    let n = Entity::new(NPC, npc());
    world.spawn(&n, &ctx);
    let f = a.drain();
    assert_eq!(
        f,
        vec![
            (sc::SCUnitStatePacket, npc_state_bytes(&npc())),
            points(NPC, 80, 20),
        ]
    );
    // The NPC branch: bc objId, "Wolf", BaseUnitType.Npc, bc objId, templateId.
    assert_eq!(f[0].1[..3], [0x00, 0x02, 0x00]);
    assert_eq!(f[0].1[9], 1);
    assert_eq!(f[0].1[13..17], 1234u32.to_le_bytes());
    assert!(n.is_visible());
    assert_eq!(n.data().unit.transform.instance_id, 0);
    assert_eq!(n.data().unit.transform.zone_id, TEST_ZONE);

    // ---- a doodad spawns: SCDoodadCreatedPacket (Doodad.AddVisibleObject).
    let d = Entity::new(DOODAD, doodad());
    world.spawn(&d, &ctx);
    assert_eq!(a.drain(), vec![(sc::SCDoodadCreatedPacket, doodad_bytes())]);
    assert_eq!(world.region_object_ids((1, 2)), vec![DOODAD]);
    assert_eq!(world.region_object_ids((1, 1)), vec![0x100, NPC]);
    assert_eq!(world.all_npcs().len(), 1);
    assert_eq!(world.all_of_kind(ObjectKind::Doodad).len(), 1);
    assert_eq!(
        world.objects_around(&WorldObject::from(&pa)).len(),
        2,
        "GetAround<GameObject>"
    );

    // ---- a second player arrives: it gets the NPC (state + points), then the doodads as one
    // SCDoodadsCreatedPacket batch; the first player learns about it.
    let mut b = TestClient::new(&server, 2, 1002);
    let pb = spawn_player(&server, &b, test_character(2, "Beta", 105.0, 100.0), 0x101);
    let fb = b.drain();
    let mut batch = vec![1u8];
    batch.extend(doodad_bytes());
    assert_eq!(
        fb.iter().map(|f| f.0).collect::<Vec<_>>(),
        vec![
            sc::SCUnitStatePacket, // Alpha
            sc::SCUnitPointsPacket,
            sc::SCUnitStatePacket, // the NPC
            sc::SCUnitPointsPacket,
            sc::SCDoodadsCreatedPacket,
        ]
    );
    assert_eq!(fb[2].1, npc_state_bytes(&npc()));
    assert_eq!(fb[4].1, batch);
    assert_eq!(
        a.drain_ops(),
        vec![sc::SCUnitStatePacket, sc::SCUnitPointsPacket]
    );
    assert!(n.data().ai_should_tick);

    // ---- Alpha walks far away: the units and doodads of the regions it leaves are removed in
    // bulk (Region.RemoveFromCharacters for a character), and Beta loses Alpha.
    pa.data().unit.transform.set_xyz(1500.0, 1500.0, 100.0);
    let z = world.add_visible_object(&pa, &ctx);
    assert!(z.is_empty());
    let fa = a.drain();
    assert_eq!(
        fa,
        vec![
            // region (1,1): Beta and the NPC are units
            ids_packet(sc::SCUnitsRemovedPacket, &[NPC, 0x101]),
            // region (1,2): the doodad (count, last = true, (bc id, false)...)
            (
                sc::SCDoodadsRemovedPacket,
                vec![1, 0, 1, 0x00, 0x03, 0x00, 0]
            ),
        ]
    );
    assert_eq!(
        b.drain(),
        vec![ids_packet(sc::SCUnitsRemovedPacket, &[0x100])]
    );
    assert!(!n.data().ai_should_tick, "no player left near the NPC");

    // ---- ...and walks back: everything reappears. Removing Alpha swapped Beta to the front of
    // sector (1,1) (C# array swap-remove), so Beta comes before the NPC now.
    assert_eq!(world.region_object_ids((1, 1)), vec![0x101, NPC]);
    pa.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&pa, &ctx);
    let fa = a.drain();
    assert_eq!(
        fa.iter().map(|f| f.0).collect::<Vec<_>>(),
        vec![
            sc::SCUnitStatePacket, // Beta
            sc::SCUnitPointsPacket,
            sc::SCUnitStatePacket, // NPC
            sc::SCUnitPointsPacket,
            sc::SCDoodadsCreatedPacket,
        ]
    );
    assert_eq!(fa[2].1, npc_state_bytes(&npc()));
    assert_eq!(fa[4].1, batch);
    b.drain();

    // ---- the NPC walks out of Beta's and Alpha's neighbourhood: removal only for players that
    // no longer see it (the "IMPORTANT FIX" filter of RemoveFromCharacters).
    n.data().unit.transform.set_xyz(400.0, 100.0, 100.0); // sector (6,1): 5 sectors away
    world.add_visible_object(&n, &ctx);
    let gone = ids_packet(sc::SCUnitsRemovedPacket, &[NPC]);
    assert_eq!(a.drain(), vec![gone.clone()]);
    assert_eq!(b.drain(), vec![gone.clone()]);
    // ...and back into range: both see it again.
    n.data().unit.transform.set_xyz(110.0, 110.0, 100.0);
    world.add_visible_object(&n, &ctx);
    assert_eq!(
        a.drain_ops(),
        vec![sc::SCUnitStatePacket, sc::SCUnitPointsPacket]
    );
    assert_eq!(
        b.drain_ops(),
        vec![sc::SCUnitStatePacket, sc::SCUnitPointsPacket]
    );

    // ---- a player targeting the NPC loses the target when it despawns.
    pa.data().unit.current_target = NPC;
    // Despawn (GameObject.Delete): hidden first, so every player in the old neighbourhood gets
    // the removal — twice for the own sector (C# notifies the own region and then all
    // neighbours, which include it).
    world.delete(&n);
    let mut target_reset = PacketStream::new();
    target_reset.write_bc(0x100).write_bc(0);
    let target_reset = (sc::SCTargetChangedPacket, target_reset.into_bytes());
    assert_eq!(a.drain(), vec![target_reset, gone.clone(), gone.clone()]);
    assert_eq!(pa.data().unit.current_target, 0);
    assert_eq!(b.drain(), vec![gone.clone(), gone]);
    assert!(world.npc(NPC).is_none());
    assert!(!n.is_visible() && n.region().is_none());

    // The doodad sits in sector (1,2) and the players in (1,1): one removal each.
    world.delete(&d);
    let removed = (sc::SCDoodadRemovedPacket, vec![0x00, 0x03, 0x00, 0]);
    assert_eq!(a.drain(), vec![removed.clone()]);
    assert_eq!(b.drain(), vec![removed]);
    assert_eq!(world.object_count(), 2); // the two players
    drop(pb);
}

#[tokio::test]
async fn children_follow_their_parent() {
    let server = test_server();
    let mut a = TestClient::new(&server, 1, 1001);
    let _pa = spawn_player(&server, &a, test_character(1, "Alpha", 100.0, 100.0), 0x100);
    let world = server.world.world(0).unwrap();
    let ctx = server.view_ctx();

    // A doodad attached to an NPC at local (2, 0, 0): shown right after its parent, removed
    // right after it, and positioned in world space.
    let n = Entity::new(NPC, npc());
    let mut child = doodad();
    child.transform.set_xyz(2.0, 0.0, 0.0);
    child.parent_obj_id = NPC;
    let d = Entity::new(DOODAD, child);
    let (no, dobj) = (WorldObject::from(&n), WorldObject::from(&d));
    world.add_object(&n);
    world.add_object(&d);
    world.attach_child(&no, &dobj);
    let w = world.world_pos_rot(&dobj);
    assert_eq!((w.x, w.y, w.z), (112.0, 110.0, 100.0));
    assert_eq!(world.region_of(&dobj), Some((1, 1)));

    world.spawn(&n, &ctx);
    assert_eq!(
        a.drain_ops(),
        vec![
            sc::SCUnitStatePacket,
            sc::SCUnitPointsPacket,
            sc::SCDoodadCreatedPacket, // base.AddVisibleObject -> children
            // C#: "Ensure children are added on initial spawn" then places the (region-less)
            // child itself, whose own AddToCharacters shows it a second time. Mirrored.
            sc::SCDoodadCreatedPacket,
        ]
    );
    assert_eq!(dobj.region(), Some((0, 1, 1)));
    world.hide(&n);
    let ops = a.drain_ops();
    // NPC (own region + neighbours) and its child doodad, each removal starting with the
    // children (GameObject.RemoveVisibleObject runs before the kind's own packet).
    assert_eq!(
        ops.iter()
            .filter(|&&o| o == sc::SCUnitsRemovedPacket)
            .count(),
        2
    );
    assert!(ops.contains(&sc::SCDoodadRemovedPacket));
    assert_eq!(dobj.region(), None);
}
