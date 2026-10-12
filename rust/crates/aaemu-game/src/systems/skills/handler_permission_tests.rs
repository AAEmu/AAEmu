//! Client buff cancellation may only change the player's own companions and vehicles.

use std::sync::Arc;

use aaemu_commons::PacketStream;

use crate::data::GameData;
use crate::models::slave::Slave;
use crate::net::registry::ClientPacket;
use crate::test_support::*;
use crate::world::{Entity, WorldObject};

use super::data::BuffTemplate;
use super::handlers::CSRemoveBuffPacket;

#[tokio::test]
async fn remove_buff_packet_preserves_foreign_slave_buff_and_removes_owned_slave_buff() {
    let mut data = GameData::empty();
    data.systems.skills.add_buff(BuffTemplate {
        id: 50001,
        kind: 1,
        duration: 0,
        stack_rule: 1,
        ..Default::default()
    });
    let server = Arc::new(test_server_with(data));
    super::bind(&server);
    let mut client = TestClient::new(&server, 1, 1);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "Owner", 100.0, 100.0),
        0x100,
    );
    client.conn.active_char = Some(player.clone());
    let world = server.world.world(0).expect("world");
    let caster = WorldObject::from(&player);
    for (obj_id, summoner_id, removable) in [(0x200, 2, false), (0x201, 1, true)] {
        let mut data = Slave::new(obj_id, 1);
        data.summoner_id = summoner_id;
        data.unit.transform.set_xyz(103.0, 100.0, 100.0);
        let slave = Entity::new(obj_id, data);
        world.spawn(&slave, &server.view_ctx());
        let owner = WorldObject::from(&slave);
        super::buffs::add_buff_by_id(&server, &owner, 50001, &caster, 0);
        let index = slave.data().unit.buffs.effects[0].index;
        let mut body = PacketStream::new();
        body.write_bc(obj_id).write_u32(index).write_u8(0);
        let packet = CSRemoveBuffPacket::read(&mut body).expect("remove buff body");
        packet
            .handle(&server, &mut client.conn)
            .await
            .expect("remove buff");
        assert_eq!(slave.data().unit.buffs.check_buff(50001), !removable);
    }
}

#[test]
fn start_skill_rejects_unknown_caster_type() {
    // Unknown wire discriminants already fail before the skill-use permission branches.
    let mut body = PacketStream::from_bytes(vec![1, 0, 0, 0, 255]);
    assert!(super::handlers::CSStartSkillPacket::read(&mut body).is_err());
}
