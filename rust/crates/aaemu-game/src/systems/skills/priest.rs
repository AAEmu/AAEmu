//! The 1.2 priest blessing purchase: `priest_buffs` supplies the buff and copper cost,
//! `NpcTemplate.priest` exposes the Blessing interaction, and item task 22 reports payment.
//! The descriptor layout is also confirmed by 10.x `sub_39B07970`; no 10.x wire fields
//! are used. The former C# purchase handler only logged the request.

use std::collections::HashMap;

use rusqlite::Connection;

use crate::data::sqlite::for_each_row;
use crate::models::container::slot_type;
use crate::models::item_task::task_type;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::systems::economy::{error_message, inventory};
use crate::world::{PlayerRef, WorldObject};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct PriestBuff {
    pub buff_id: u32,
    pub cost: i32,
}

pub fn load(conn: &Connection) -> rusqlite::Result<HashMap<u32, PriestBuff>> {
    let mut blessings = HashMap::new();
    for_each_row(conn, "SELECT * FROM priest_buffs", [], |r| {
        let id = r.u32("id")?;
        let blessing = PriestBuff {
            buff_id: r.u32("buff_id")?,
            cost: r.i32("cost")?,
        };
        if blessings.insert(id, blessing).is_some() {
            return Err(rusqlite::Error::InvalidParameterName(format!(
                "duplicate priest blessing {id}"
            )));
        }
        Ok(())
    })?;
    Ok(blessings)
}

pub fn buy(server: &GameServer, player: &PlayerRef, blessing_id: u32, npc_id: u32) {
    let fail = |error| player.send(SCErrorMsgPacket::new(error, 0, true));
    let owner = WorldObject::from(player);
    let (dead, interaction) = {
        let c = player.data();
        (c.unit.is_dead(), c.unit.current_interaction_object)
    };
    if dead || npc_id == 0 || interaction != npc_id {
        fail(error_message::PRIEST_CANNOT_CREATE_BUFF);
        return;
    }
    let Some(world) = server.world.world_of(&owner) else {
        return;
    };
    let Some(npc) = world.npc(npc_id) else {
        fail(error_message::PRIEST_CANNOT_CREATE_BUFF);
        return;
    };
    let (template_id, npc_dead) = {
        let n = npc.data();
        (n.unit.template_id, n.unit.is_dead())
    };
    if npc_dead
        || !server
            .game_data
            .systems
            .npcs
            .npc
            .template(template_id)
            .is_some_and(|t| t.priest)
    {
        fail(error_message::PRIEST_CANNOT_CREATE_BUFF);
        return;
    }
    let caster = WorldObject::from(&npc);
    if super::geometry::distance_to(
        server,
        &super::geometry::Target::Obj(owner.clone()),
        &super::geometry::Target::Obj(caster.clone()),
        false,
    ) > 5.0
    {
        fail(error_message::TOO_FAR_AWAY);
        return;
    }
    let data = &server.game_data.systems.skills;
    let Some(blessing) = data.priest_buffs.get(&blessing_id).filter(|b| b.cost >= 0) else {
        fail(error_message::PRIEST_CANNOT_CREATE_BUFF);
        return;
    };
    let Some(template) = data.buff(blessing.buff_id) else {
        fail(error_message::PRIEST_CANNOT_CREATE_BUFF);
        return;
    };
    if player.data().unit.buffs.check_buff(template.id) {
        fail(error_message::PRIEST_ALREADY_OWNED);
        return;
    }
    if super::buffs::check_buff_immune(server, &owner, template.id)
        || (template.require_buff_id != 0
            && !player
                .data()
                .unit
                .buffs
                .check_buff(template.require_buff_id))
    {
        fail(error_message::PRIEST_CANNOT_CREATE_BUFF);
        return;
    }
    // The inventory helper checks the balance and subtracts under the same character lock.
    if !inventory::change_money(
        &mut inventory::Out::Direct,
        player,
        slot_type::INVENTORY,
        slot_type::NONE,
        blessing.cost,
        task_type::BUY_PRIEST_BUFF,
    ) {
        return;
    }
    super::buffs::apply_template(
        server,
        template,
        Some(&caster),
        &super::types::SkillCaster::Unit(npc_id),
        &owner,
        None,
    );
}

#[cfg(test)]
mod tests {
    use std::sync::Arc;

    use aaemu_commons::PacketStream;

    use super::*;
    use crate::data::GameData;
    use crate::models::npc::Npc;
    use crate::net::registry::ClientPacket;
    use crate::opcodes::sc;
    use crate::systems::economy::store::CSBuyPriestBuffPacket;
    use crate::systems::npcs::npc::data::NpcTemplate;
    use crate::systems::skills::data::BuffTemplate;
    use crate::test_support::*;
    use crate::world::Entity;

    fn fixture() -> (Arc<GameServer>, TestClient, PlayerRef) {
        let mut data = GameData::empty();
        data.systems.skills.add_buff(BuffTemplate {
            id: 239,
            kind: 1,
            duration: 0,
            stack_rule: 1,
            ..Default::default()
        });
        data.systems.skills.priest_buffs.insert(
            1,
            PriestBuff {
                buff_id: 239,
                cost: 100,
            },
        );
        data.systems.npcs.npc.templates.insert(
            777,
            NpcTemplate {
                id: 777,
                priest: true,
                ..Default::default()
            },
        );
        let server = Arc::new(test_server_with(data));
        super::super::bind(&server);
        let mut client = TestClient::new(&server, 1, 1);
        let mut c = test_character(1, "Buyer", 100.0, 100.0);
        c.unit.hp = 100;
        c.unit.max_hp = 100;
        c.money = 200;
        c.unit.current_interaction_object = 0x200;
        let player = spawn_player(&server, &client, c, 0x100);
        client.conn.active_char = Some(player.clone());
        let mut n = Npc::new(777);
        n.unit.hp = 100;
        n.unit.transform.set_xyz(103.0, 100.0, 100.0);
        let npc = Entity::new(0x200, n);
        server
            .world
            .world(0)
            .expect("world")
            .spawn(&npc, &server.view_ctx());
        client.drain();
        (server, client, player)
    }

    async fn packet(server: &GameServer, client: &mut TestClient) {
        // 1.2: u32 blessing id followed by BC npc id.
        let mut body = PacketStream::from_bytes(vec![1, 0, 0, 0, 0, 2, 0]);
        let request = CSBuyPriestBuffPacket::read(&mut body).expect("purchase body");
        assert_eq!(request.npc_unit_id, 0x200);
        request
            .handle(server, &mut client.conn)
            .await
            .expect("purchase");
    }

    fn error(client: &mut TestClient) -> i16 {
        let frames = client.drain();
        let (_, bytes) = frames
            .iter()
            .find(|(op, _)| *op == sc::SCErrorMsgPacket)
            .expect("error response");
        i16::from_le_bytes([bytes[0], bytes[1]])
    }

    #[tokio::test]
    async fn priest_purchase_charges_once_and_rejects_duplicate() {
        let (server, mut client, player) = fixture();
        packet(&server, &mut client).await;
        assert_eq!(player.data().money, 100);
        assert!(player.data().unit.buffs.check_buff(239));
        let frames = client.drain();
        let (_, body) = frames
            .iter()
            .find(|(op, _)| *op == sc::SCItemTaskSuccessPacket)
            .expect("payment task");
        assert_eq!(body[0], task_type::BUY_PRIEST_BUFF);
        assert!(frames.iter().any(|(op, _)| *op == sc::SCBuffCreatedPacket));
        packet(&server, &mut client).await;
        assert_eq!(error(&mut client), error_message::PRIEST_ALREADY_OWNED);
        assert_eq!(player.data().money, 100);
    }

    #[tokio::test]
    async fn priest_purchase_rejects_insufficient_funds_and_remote_or_nonpriest_npcs() {
        let (server, mut client, player) = fixture();
        player.data().money = 99;
        packet(&server, &mut client).await;
        assert_eq!(error(&mut client), error_message::NOT_ENOUGH_MONEY);
        assert_eq!(player.data().money, 99);
        assert!(!player.data().unit.buffs.check_buff(239));
        player.data().money = 200;
        let npc = server
            .world
            .world(0)
            .expect("world")
            .npc(0x200)
            .expect("npc");
        npc.data().unit.transform.set_xyz(120.0, 100.0, 100.0);
        packet(&server, &mut client).await;
        assert_eq!(error(&mut client), error_message::TOO_FAR_AWAY);
        npc.data().unit.transform.set_xyz(103.0, 100.0, 100.0);
        npc.data().unit.template_id = 778;
        packet(&server, &mut client).await;
        assert_eq!(error(&mut client), error_message::PRIEST_CANNOT_CREATE_BUFF);
        assert_eq!(player.data().money, 200);
        assert!(!player.data().unit.buffs.check_buff(239));
    }

    #[test]
    fn priest_loader_preserves_price_and_rejects_duplicate_ids() {
        let conn = Connection::open_in_memory().expect("sqlite");
        conn.execute_batch("CREATE TABLE priest_buffs(id INT, buff_id INT, cost INT, position INT); INSERT INTO priest_buffs VALUES(1,239,100,1);").expect("fixture");
        assert_eq!(
            load(&conn).expect("blessings")[&1],
            PriestBuff {
                buff_id: 239,
                cost: 100
            }
        );
        conn.execute_batch("INSERT INTO priest_buffs VALUES(1,240,200,2);")
            .expect("duplicate");
        assert!(load(&conn).is_err());
    }

    #[test]
    #[ignore = "requires AAEMU_COMPACT_DB"]
    fn priest_real_compact_db() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let conn = Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .expect("compact");
        let blessings = load(&conn).expect("blessings");
        assert_eq!(blessings.len(), 1);
        assert_eq!(
            blessings[&1],
            PriestBuff {
                buff_id: 239,
                cost: 100
            }
        );
    }
}
