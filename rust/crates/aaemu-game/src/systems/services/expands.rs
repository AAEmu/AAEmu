//! The bag / bank and expert expansions: `CharacterManager` `bag_expands` /
//! `expand_expert_limits` (+ `GetExpands`, `GetExpandExpertLimit`), `Inventory.ExpandSlot`,
//! `CharacterActability.Regrade` / `ExpandExpert` and their packets (`CSExpandSlotsPacket`,
//! `CSExpandExpertPacket`, `CSUpgradeExpertLimitPacket`, `CSDowngradeExpertLimitPacket`,
//! `SCInvenExpandedPacket`, `SCExpertLimitModifiedPacket`, `SCExpertExpandedPacket`).

use std::collections::BTreeMap;

use aaemu_commons::{Marshal, PacketStream};
use futures::future::BoxFuture;
use rusqlite::Connection;

use super::item_ops::{item_task, CHARACTER_ITEM_OPS};
use crate::data::sqlite::for_each_row;
use crate::error::Result;
use crate::models::container::slot_type;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::world::PlayerRef;

/// `ErrorMessageType.NotEnoughExpandItem`.
const NOT_ENOUGH_EXPAND_ITEM: i16 = 686;
/// `ErrorMessageType.NotEnoughExpandItemAndMoney`.
const NOT_ENOUGH_EXPAND_ITEM_AND_MONEY: i16 = 687;
/// `GamePointKind.Vocation`.
const GAME_POINT_VOCATION: u8 = 1;

/// `Expand` (`bag_expands`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Expand {
    pub is_bank: bool,
    pub step: i32,
    pub price: i32,
    pub item_id: u32,
    pub item_count: i32,
    pub currency_id: i32,
}

/// `ExpandExpertLimit`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct ExpandExpertLimit {
    pub id: u32,
    pub expand_count: u8,
    pub life_point: i32,
    pub item_id: u32,
    pub item_count: i32,
}

/// The expansion tables of `CharacterManager.Load`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct ExpandData {
    /// `_expands` (step -> rows in table order).
    pub expands: BTreeMap<i32, Vec<Expand>>,
    /// `_expandExpertLimits` (index in `expand_count` order).
    pub expand_expert_limits: Vec<ExpandExpertLimit>,
}

impl ExpandData {
    pub fn load(conn: &Connection) -> rusqlite::Result<Self> {
        let mut d = Self::default();
        for_each_row(conn, "SELECT * FROM bag_expands", [], |r| {
            let e = Expand {
                // `GetBoolean("is_bank", fromString: true)`.
                is_bank: r.bool_str("is_bank")?,
                step: r.i32("step")?,
                price: r.i32("price")?,
                item_id: r.u32_or("item_id", 0)?,
                item_count: r.i32("item_count")?,
                currency_id: r.i32("currency_id")?,
            };
            d.expands.entry(e.step).or_default().push(e);
            Ok(())
        })?;
        for_each_row(
            conn,
            "SELECT * FROM expand_expert_limits ORDER BY expand_count ASC",
            [],
            |r| {
                d.expand_expert_limits.push(ExpandExpertLimit {
                    id: r.u32("id")?,
                    expand_count: r.u8("expand_count")?,
                    life_point: r.i32("life_point")?,
                    item_id: r.u32_or("item_id", 0)?,
                    item_count: r.i32("item_count")?,
                });
                Ok(())
            },
        )?;
        Ok(d)
    }

    /// `GetExpands(step)` (`_expands[step]`: a missing step throws in C#).
    pub fn get_expands(&self, step: i32) -> Option<&Vec<Expand>> {
        self.expands.get(&step)
    }

    /// `GetExpandExpertLimit(step)`.
    pub fn get_expand_expert_limit(&self, step: i32) -> Option<&ExpandExpertLimit> {
        usize::try_from(step)
            .ok()
            .and_then(|i| self.expand_expert_limits.get(i))
    }
}

/// `SCInvenExpandedPacket(slotType, numSlots)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCInvenExpandedPacket {
    pub slot_type: u8,
    pub num_slots: u8,
}
packet_def!(
    SCInvenExpandedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCInvenExpandedPacket
);
impl Marshal for SCInvenExpandedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.slot_type).write_u8(self.num_slots);
    }
}

/// `SCExpertLimitModifiedPacket(isUpgrade, id, step)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCExpertLimitModifiedPacket {
    pub is_upgrade: bool,
    pub id: u32,
    pub step: u8,
}
packet_def!(
    SCExpertLimitModifiedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCExpertLimitModifiedPacket
);
impl Marshal for SCExpertLimitModifiedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bool(self.is_upgrade)
            .write_u32(self.id)
            .write_u8(self.step);
    }
}

/// `SCExpertExpandedPacket(next)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCExpertExpandedPacket {
    pub next: u8,
}
packet_def!(
    SCExpertExpandedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCExpertExpandedPacket
);
impl Marshal for SCExpertExpandedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.next);
    }
}

fn data(server: &GameServer) -> &ExpandData {
    &server.game_data.systems.services.expands
}

/// `Inventory.ExpandSlot(slotType)`.
pub fn expand_slot(server: &GameServer, p: &PlayerRef, slot: u8) {
    let is_bank = slot == slot_type::BANK;
    let (num_bank, num_bag, money) = {
        let c = p.data();
        (c.num_bank_slots, c.num_inventory_slots, c.money)
    };
    let step = ((if is_bank {
        i32::from(num_bank)
    } else {
        i32::from(num_bag)
    }) - 50)
        / 10;
    let Some(expands) = data(server).get_expands(step) else {
        // `_expands[step]` throws KeyNotFoundException.
        tracing::error!("The given key '{step}' was not present in the dictionary.");
        return;
    };
    let Some(expand) = expands.iter().find(|e| e.is_bank == is_bank).copied() else {
        return;
    };
    if expand.price != 0 && money < i64::from(expand.price) {
        tracing::warn!("No Money for expand!");
        return;
    }
    let ops = CHARACTER_ITEM_OPS.get();
    if expand.item_id != 0
        && expand.item_count != 0
        && !ops
            .as_ref()
            .is_some_and(|o| o.check_items(server, p, expand.item_id, expand.item_count))
    {
        tracing::warn!("Item or Count not fount.");
        return;
    }
    if expand.price != 0 {
        // The C# builds a `MoneyChange` task but never sends it.
        p.data().money -= i64::from(expand.price);
    }
    if expand.item_id != 0 && expand.item_count != 0 {
        if let Some(o) = &ops {
            let task = if is_bank {
                item_task::EXPAND_BANK
            } else {
                item_task::EXPAND_BAG
            };
            o.consume_item(server, p, task, expand.item_id, expand.item_count);
        }
    }
    let packet = {
        let mut c = p.data();
        if is_bank {
            c.num_bank_slots = (50 + 10 * (1 + step)) as i16;
            let size = i32::from(c.num_bank_slots);
            c.inventory
                .warehouse()
                .lock()
                .expect("container poisoned")
                .set_container_size(size);
            SCInvenExpandedPacket {
                slot_type: slot_type::BANK,
                num_slots: c.num_bank_slots as u8,
            }
        } else {
            c.num_inventory_slots = (50 + 10 * (1 + step)) as u8;
            let size = i32::from(c.num_inventory_slots);
            c.inventory
                .bag()
                .lock()
                .expect("container poisoned")
                .set_container_size(size);
            SCInvenExpandedPacket {
                slot_type: slot_type::INVENTORY,
                num_slots: c.num_inventory_slots,
            }
        }
    };
    p.send(packet);
}

/// `CharacterActability.Regrade(id, isUpgrade)`.
pub fn regrade(server: &GameServer, p: &PlayerRef, id: u32, is_upgrade: bool) {
    let limits = &server.game_data.characters;
    let step = {
        let mut c = p.data();
        let Some(a) = c.actability.actabilities.iter_mut().find(|a| a.id == id) else {
            // `Actabilities[id]` throws KeyNotFoundException.
            tracing::error!("The given key '{id}' was not present in the dictionary.");
            return;
        };
        if is_upgrade {
            let Some(t) = limits.expert_limit(usize::from(a.step)) else {
                return;
            };
            if a.point < t.up_limit {
                return;
            }
            a.step = a.step.wrapping_add(1);
        } else {
            let Some(t) = usize::from(a.step)
                .checked_sub(1)
                .and_then(|s| limits.expert_limit(s))
            else {
                return;
            };
            a.step = a.step.wrapping_sub(1);
            a.point = t.up_limit;
        }
        a.step
    };
    p.send(SCExpertLimitModifiedPacket {
        is_upgrade,
        id,
        step,
    });
}

/// `CharacterActability.ExpandExpert()`.
pub fn expand_expert(server: &GameServer, p: &PlayerRef) {
    let (expanded, vocation) = {
        let c = p.data();
        (c.expanded_expert, c.vocation_point)
    };
    let Some(expand) = data(server)
        .get_expand_expert_limit(i32::from(expanded))
        .copied()
    else {
        return;
    };
    if expand.life_point > vocation {
        p.send(SCErrorMsgPacket::new(
            NOT_ENOUGH_EXPAND_ITEM_AND_MONEY,
            0,
            true,
        ));
        return;
    }
    let ops = CHARACTER_ITEM_OPS.get();
    if expand.item_id != 0
        && expand.item_count != 0
        && !ops
            .as_ref()
            .is_some_and(|o| o.check_items(server, p, expand.item_id, expand.item_count))
    {
        p.send(SCErrorMsgPacket::new(NOT_ENOUGH_EXPAND_ITEM, 0, true));
        return;
    }
    if expand.life_point > 0 {
        // C#: `ChangeGamePoints(Vocation, +LifePoint)` (adds the cost instead of paying it).
        crate::systems::skills::progress::change_game_points(
            server,
            p,
            GAME_POINT_VOCATION,
            expand.life_point,
        );
    }
    if expand.item_id != 0 && expand.item_count != 0 {
        if let Some(o) = &ops {
            o.consume_item(
                server,
                p,
                item_task::EXPAND_EXPERT,
                expand.item_id,
                expand.item_count,
            );
        }
    }
    let next = {
        let mut c = p.data();
        c.expanded_expert = expand.expand_count;
        c.expanded_expert
    };
    p.send(SCExpertExpandedPacket { next });
}

/// `CSExpandSlotsPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSExpandSlotsPacket {
    pub slot_type: i32,
    pub auto_use_aa_point: bool,
}

impl ClientPacket for CSExpandSlotsPacket {
    const NAME: &'static str = "CSExpandSlotsPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            slot_type: s.read_i32()?,
            auto_use_aa_point: s.read_bool()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!(
                "ExpandSlots, slotType -> {}, autoUseAAPoint -> {}",
                self.slot_type,
                self.auto_use_aa_point
            );
            if let Some(p) = conn.active_char.clone() {
                expand_slot(server, &p, self.slot_type as u8);
            }
            Ok(())
        })
    }
}

/// `CSExpandExpertPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSExpandExpertPacket;

impl ClientPacket for CSExpandExpertPacket {
    const NAME: &'static str = "CSExpandExpertPacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!("ExpandExpert");
            if let Some(p) = conn.active_char.clone() {
                expand_expert(server, &p);
            }
            Ok(())
        })
    }
}

/// `CSUpgradeExpertLimitPacket` / `CSDowngradeExpertLimitPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRegradeExpertLimitPacket<const UPGRADE: bool> {
    pub id: u32,
    pub auto_use_aa_point: bool,
}

/// `CSUpgradeExpertLimitPacket`.
pub type CSUpgradeExpertLimitPacket = CSRegradeExpertLimitPacket<true>;
/// `CSDowngradeExpertLimitPacket`.
pub type CSDowngradeExpertLimitPacket = CSRegradeExpertLimitPacket<false>;

impl<const UPGRADE: bool> ClientPacket for CSRegradeExpertLimitPacket<UPGRADE> {
    const NAME: &'static str = if UPGRADE {
        "CSUpgradeExpertLimitPacket"
    } else {
        "CSDowngradeExpertLimitPacket"
    };

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            id: s.read_u32()?,
            auto_use_aa_point: s.read_bool()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!(
                "{}ExpertLimit, id -> {}, autoUseAAPoint -> {}",
                if UPGRADE { "Upgrade" } else { "Downgrade" },
                self.id,
                self.auto_use_aa_point
            );
            if let Some(p) = conn.active_char.clone() {
                regrade(server, &p, self.id, UPGRADE);
            }
            Ok(())
        })
    }
}

/// Registers the packets of this module.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSExpandSlotsPacket>(LEVEL_PLAIN, cs::CSExpandSlotsPacket);
    reg.register::<CSExpandExpertPacket>(LEVEL_PLAIN, cs::CSExpandExpertPacket);
    reg.register::<CSUpgradeExpertLimitPacket>(LEVEL_PLAIN, cs::CSUpgradeExpertLimitPacket);
    reg.register::<CSDowngradeExpertLimitPacket>(LEVEL_PLAIN, cs::CSDowngradeExpertLimitPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::data::character_templates::ExpertLimit;
    use crate::data::GameData;
    use crate::models::char_data::Actability;
    use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};

    #[test]
    fn packets() {
        let enc =
            |p: &dyn crate::net::packet::ServerPacket| crate::net::packet::encode(p)[6..].to_vec();
        assert_eq!(
            enc(&SCInvenExpandedPacket {
                slot_type: 2,
                num_slots: 60
            }),
            vec![2, 60]
        );
        assert_eq!(
            enc(&SCExpertLimitModifiedPacket {
                is_upgrade: true,
                id: 3,
                step: 2
            }),
            vec![1, 3, 0, 0, 0, 2]
        );
        assert_eq!(enc(&SCExpertExpandedPacket { next: 4 }), vec![4]);
    }

    #[tokio::test]
    async fn regrade_and_expand_slot() {
        let mut gd = GameData::empty();
        gd.characters.expert_limits = vec![
            ExpertLimit {
                id: 1,
                up_limit: 100,
                expert_limit_count: 0,
            },
            ExpertLimit {
                id: 2,
                up_limit: 200,
                expert_limit_count: 0,
            },
        ];
        gd.systems.services.expands.expands.insert(
            0,
            vec![Expand {
                is_bank: false,
                step: 0,
                price: 10,
                ..Expand::default()
            }],
        );
        let server = test_server_with(gd);
        let mut client = TestClient::new(&server, 1, 1);
        let mut c = test_character(1, "Al", 10.0, 10.0);
        c.actability.actabilities.push(Actability {
            id: 7,
            point: 150,
            step: 0,
        });
        c.money = 50;
        let p = spawn_player(&server, &client, c, 0x100);
        client.drain();
        regrade(&server, &p, 7, true);
        assert_eq!(p.data().actability.actabilities[0].step, 1);
        // Not enough points for the next step.
        regrade(&server, &p, 7, true);
        assert_eq!(p.data().actability.actabilities[0].step, 1);
        regrade(&server, &p, 7, false);
        let a = p.data().actability.actabilities[0];
        assert_eq!((a.step, a.point), (0, 100));
        assert_eq!(
            client.drain_ops(),
            vec![
                sc::SCExpertLimitModifiedPacket,
                sc::SCExpertLimitModifiedPacket
            ]
        );
        expand_slot(&server, &p, slot_type::INVENTORY);
        assert_eq!(p.data().num_inventory_slots, 60);
        assert_eq!(p.data().money, 40);
        assert_eq!(client.drain_ops(), vec![sc::SCInvenExpandedPacket]);
    }

    #[test]
    fn real_compact_db_expands() {
        let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
            return;
        };
        let c =
            Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY).unwrap();
        let d = ExpandData::load(&c).unwrap();
        assert!(!d.expands.is_empty());
        assert!(!d.expand_expert_limits.is_empty());
    }
}
