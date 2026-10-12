//! The housing packets: `CSConstructHouseTax`, `CSCreateHouse`, `CSDecorateHouse`,
//! `CSChangeHouseName`, `CSChangeHousePermission`, `CSChangeHousePay` (no 1.2 opcode),
//! `CSRequestHouseTax`, `CSAllowHousingRecover`, `CSSellHouse`, `CSSellHouseCancel`,
//! `CSBuyHouse`; and the `SCHouse*` / `SCMyHouse*` / `SCLoginCharInfoHouse` /
//! `SCConstructHouseTax` / `SCHousingRecoverToggle` packets.

use aaemu_commons::helpers::convert_long_to_coord;
use aaemu_commons::{Marshal, PacketStream};

use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, sc};
use crate::packets::{packet_def, raw_body_packet};

use super::ops;

// ---------------------------------------------------------------------------------------------
// S->C
// ---------------------------------------------------------------------------------------------

/// Defines a level-1 S->C packet with its `Write`.
macro_rules! sc_packet {
    ($(#[$m:meta])* $name:ident { $($f:ident : $t:ty),* $(,)? } |$self:ident, $s:ident| $body:block) => {
        $(#[$m])*
        #[derive(Debug, Clone, PartialEq)]
        pub struct $name { $(pub $f: $t),* }
        packet_def!($name, level = LEVEL_PLAIN, opcode = sc::$name);
        impl Marshal for $name {
            fn write(&$self, $s: &mut PacketStream) $body
        }
    };
}

sc_packet!(
    /// `SCConstructHouseTaxPacket(designId, heavyTaxHouseCount, normalTaxHouseCount,
    /// isHeavyTaxHouse, baseTaxMoneyAmount, depositTaxMoneyAmount, totalTaxMoneyAmount)`.
    SCConstructHouseTaxPacket {
        design_id: u32,
        heavy_tax_house_count: i32,
        normal_tax_house_count: i32,
        is_heavy_tax_house: bool,
        base_tax_money_amount: i32,
        deposit_tax_money_amount: i32,
        total_tax_money_amount: i32,
    } |self, s| {
        s.write_u32(self.design_id)
            .write_i32(self.heavy_tax_house_count)
            .write_i32(self.normal_tax_house_count)
            .write_bool(self.is_heavy_tax_house)
            .write_i32(self.base_tax_money_amount)
            .write_i32(self.deposit_tax_money_amount)
            .write_i32(self.total_tax_money_amount);
    }
);

sc_packet!(
    /// `SCHouseBuildPayChangedPacket(tl, moneyAmount)` (not sent by the 1.2 C#).
    SCHouseBuildPayChangedPacket { tl: u16, money_amount: i32 } |self, s| {
        s.write_u16(self.tl).write_i32(self.money_amount);
    }
);

sc_packet!(
    /// `SCHouseBuildProgressPacket(tl, modelId, allStep, curStep)`.
    SCHouseBuildProgressPacket { tl: u16, model_id: u32, all_step: i32, cur_step: i32 } |self, s| {
        s.write_u16(self.tl)
            .write_u32(self.model_id)
            .write_i32(self.all_step)
            .write_i32(self.cur_step);
    }
);

sc_packet!(
    /// `SCHouseDemolishedPacket(tl)`.
    SCHouseDemolishedPacket { tl: u16 } |self, s| {
        s.write_u16(self.tl);
    }
);

sc_packet!(
    /// `SCHouseFarmPacket(name, total, harvestable)` (not sent by the 1.2 C#).
    SCHouseFarmPacket { name: String, total: i32, harvestable: i32 } |self, s| {
        s.write_str(&self.name).write_i32(self.total).write_i32(self.harvestable);
    }
);

sc_packet!(
    /// `SCHousePermissionChangedPacket(tl, permission)`.
    SCHousePermissionChangedPacket { tl: u16, permission: u8 } |self, s| {
        s.write_u16(self.tl).write_u8(self.permission);
    }
);

sc_packet!(
    /// `SCHouseResetForSalePacket(tl, houseName)`.
    SCHouseResetForSalePacket { tl: u16, house_name: String } |self, s| {
        s.write_u16(self.tl).write_str(&self.house_name);
    }
);

sc_packet!(
    /// `SCHouseSetForSalePacket(tl, moneyAmount, sellToPlayerId, sellToName, houseName)`.
    SCHouseSetForSalePacket {
        tl: u16,
        money_amount: u32,
        sell_to_player_id: u32,
        sell_to_name: String,
        house_name: String,
    } |self, s| {
        s.write_u16(self.tl)
            .write_u32(self.money_amount)
            .write_u32(self.sell_to_player_id)
            .write_str(&self.sell_to_name)
            .write_str(&self.house_name);
    }
);

sc_packet!(
    /// `SCHouseSoldPacket(tl, unkId (previous owner), unk2Id (new owner), newOwnerAcc,
    /// ownerName, houseName)`.
    SCHouseSoldPacket {
        tl: u16,
        unk_id: u32,
        unk2_id: u32,
        new_owner_acc: u32,
        owner_name: String,
        house_name: String,
    } |self, s| {
        s.write_u16(self.tl)
            .write_u32(self.unk_id)
            .write_u32(self.unk2_id)
            .write_u32(self.new_owner_acc)
            .write_str(&self.owner_name)
            .write_str(&self.house_name);
    }
);

sc_packet!(
    /// `SCHouseTaxInfoPacket(tl, dominionTaxRate, moneyAmount, moneyAmount2, due,
    /// isAlreadyPaid, weeksWithoutPay, isHeavyTaxHouse)`; `due` is a unix time in seconds
    /// (`PacketStream.Write(DateTime)`).
    SCHouseTaxInfoPacket {
        tl: u16,
        dominion_tax_rate: i32,
        money_amount: i32,
        money_amount2: i32,
        due: i64,
        is_already_paid: bool,
        weeks_without_pay: i32,
        is_heavy_tax_house: bool,
    } |self, s| {
        s.write_u16(self.tl)
            .write_i32(self.dominion_tax_rate)
            .write_i32(self.money_amount)
            .write_i32(self.money_amount2)
            .write_i64(self.due.max(0))
            .write_bool(self.is_already_paid)
            .write_i32(self.weeks_without_pay)
            .write_bool(self.is_heavy_tax_house);
    }
);

sc_packet!(
    /// `SCHousingRecoverTogglePacket(tl, allowRecover)`.
    SCHousingRecoverTogglePacket { tl: u16, allow_recover: bool } |self, s| {
        s.write_u16(self.tl).write_bool(self.allow_recover);
    }
);

sc_packet!(
    /// `SCMyHouseRemovedPacket(tl)`.
    SCMyHouseRemovedPacket { tl: u16 } |self, s| {
        s.write_u16(self.tl);
    }
);

// `SCMyHousePacket(house)`: `house.Write(stream)` (built under the house lock).
raw_body_packet!(SCMyHousePacket, sc::SCMyHousePacket);
// `SCLoginCharInfoHouse(id, house)`: `stream.Write(id)` + `house.Write(stream)`.
raw_body_packet!(SCLoginCharInfoHousePacket, sc::SCLoginCharInfoHousePacket);

// ---------------------------------------------------------------------------------------------
// C->S
// ---------------------------------------------------------------------------------------------

/// Defines a C->S packet: read + async handle.
macro_rules! cs_packet {
    ($(#[$m:meta])* $name:ident { $($f:ident : $t:ty),* $(,)? }
        read |$rs:ident| $read:block
        handle |$self:ident, $server:ident, $conn:ident| $handle:block) => {
        $(#[$m])*
        #[derive(Debug, Clone, PartialEq)]
        pub struct $name { $(pub $f: $t),* }
        impl crate::net::registry::ClientPacket for $name {
            const NAME: &'static str = stringify!($name);
            fn read($rs: &mut PacketStream) -> crate::error::Result<Self> $read
            #[allow(unused_variables)]
            fn handle<'a>(
                $self,
                $server: &'a crate::server::GameServer,
                $conn: &'a mut crate::net::connection::GameConnection,
            ) -> futures::future::BoxFuture<'a, crate::error::Result<()>> {
                Box::pin(async move $handle)
            }
        }
    };
}

/// `Connection.ActiveChar` (returns from the handler when there is none; the C# throws).
macro_rules! active_char {
    ($conn:ident) => {
        match $conn.active_char.clone() {
            Some(p) => p,
            None => return Ok(()),
        }
    };
}

cs_packet!(
    /// `CSConstructHouseTaxPacket` (designId, x, y as fixed-point longs, z).
    CSConstructHouseTaxPacket { design_id: u32, x: f32, y: f32, z: f32 }
    read |s| {
        Ok(Self {
            design_id: s.read_u32()?,
            x: convert_long_to_coord(s.read_i64()?),
            y: convert_long_to_coord(s.read_i64()?),
            z: s.read_f32()?,
        })
    }
    handle |self, server, conn| {
        tracing::debug!("ConstructHouseTax");
        let player = active_char!(conn);
        ops::construct_house_tax(server, &player, self.design_id, self.x, self.y, self.z);
        Ok(())
    }
);

cs_packet!(
    /// `CSCreateHousePacket`.
    CSCreateHousePacket {
        design_id: u32,
        x: f32,
        y: f32,
        z: f32,
        z_rot: f32,
        item_id: u64,
        money_amount: i32,
        ht: i32,
        auto_use_aa_point: bool,
    }
    read |s| {
        Ok(Self {
            design_id: s.read_u32()?,
            x: convert_long_to_coord(s.read_i64()?),
            y: convert_long_to_coord(s.read_i64()?),
            z: s.read_f32()?,
            z_rot: s.read_f32()?,
            item_id: s.read_u64()?,
            money_amount: s.read_i32()?,
            ht: s.read_i32()?,
            auto_use_aa_point: s.read_bool()?,
        })
    }
    handle |self, server, conn| {
        tracing::debug!(
            "CreateHouse, Id: {}, X: {}, Y: {}, Z: {}, ZRot: {}",
            self.design_id, self.x, self.y, self.z, self.z_rot
        );
        let player = active_char!(conn);
        ops::build(server, &player, conn.account_id(), &self);
        Ok(())
    }
);

cs_packet!(
    /// `CSDecorateHousePacket` (position relative to the house, rotation quaternion).
    CSDecorateHousePacket {
        house_tl: u16,
        design_id: u32,
        x: f32,
        y: f32,
        z: f32,
        quat: [f32; 4],
        parent_obj_id: u32,
        item_id: u64,
    }
    read |s| {
        Ok(Self {
            house_tl: s.read_u16()?,
            design_id: s.read_u32()?,
            x: s.read_f32()?,
            y: s.read_f32()?,
            z: s.read_f32()?,
            quat: [s.read_f32()?, s.read_f32()?, s.read_f32()?, s.read_f32()?],
            parent_obj_id: s.read_bc()?,
            item_id: s.read_u64()?,
        })
    }
    handle |self, server, conn| {
        tracing::debug!(
            "DecorateHouse, houseId: {}, designId: {}, x: {}, y: {}, z: {}, rot {:?}, objId: {}, itemId: {}",
            self.house_tl, self.design_id, self.x, self.y, self.z, self.quat, self.parent_obj_id, self.item_id
        );
        let player = active_char!(conn);
        match ops::decorate_house(server, &player, &self) {
            Ok(true) => {}
            Ok(false) => {
                super::manager::send_error(
                    &player,
                    super::manager::error_message::HOUSE_CANNOT_DECORATE,
                );
                tracing::warn!(
                    "DecorateHouse, FAILED with houseId: {}, designId: {}, x: {}, y: {}, z: {}, rot {:?}, objId: {}, itemId: {}",
                    self.house_tl, self.design_id, self.x, self.y, self.z, self.quat, self.parent_obj_id, self.item_id
                );
            }
            // C#: the exception escapes the packet handler (logged, no error message).
            Err(e) => tracing::error!("DecorateHouse: {e}"),
        }
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeHouseNamePacket`.
    CSChangeHouseNamePacket { tl: u16, name: String }
    read |s| { Ok(Self { tl: s.read_u16()?, name: s.read_string()? }) }
    handle |self, server, conn| {
        tracing::debug!("ChangeHouseName, Tl: {}, Name: {}", self.tl, self.name);
        let player = active_char!(conn);
        ops::change_house_name(server, conn, &player, self.tl, &self.name);
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeHousePermissionPacket`.
    CSChangeHousePermissionPacket { tl: u16, permission: u8 }
    read |s| { Ok(Self { tl: s.read_u16()?, permission: s.read_u8()? }) }
    handle |self, server, conn| {
        tracing::debug!("ChangeHousePermission, Tl: {}, Permission: {}", self.tl, self.permission);
        let player = active_char!(conn);
        ops::change_house_permission(server, &player, self.tl, self.permission);
        Ok(())
    }
);

cs_packet!(
    /// `CSChangeHousePayPacket` (logged only; no 1.2 opcode, not registered).
    CSChangeHousePayPacket { tl: u16, money_amount: i32 }
    read |s| { Ok(Self { tl: s.read_u16()?, money_amount: s.read_i32()? }) }
    handle |self, server, conn| {
        tracing::debug!("ChangeHousePay, Tl: {}, MoneyAmount: {}", self.tl, self.money_amount);
        Ok(())
    }
);

cs_packet!(
    /// `CSRequestHouseTaxPacket`.
    CSRequestHouseTaxPacket { tl: u16 }
    read |s| { Ok(Self { tl: s.read_u16()? }) }
    handle |self, server, conn| {
        tracing::debug!("RequestHouseTax, Tl: {}", self.tl);
        let player = active_char!(conn);
        ops::house_tax_info(server, &player, self.tl);
        Ok(())
    }
);

cs_packet!(
    /// `CSAllowHousingRecoverPacket`.
    CSAllowHousingRecoverPacket { tl: u16 }
    read |s| { Ok(Self { tl: s.read_u16()? }) }
    handle |self, server, conn| {
        tracing::debug!("AllowHousingRecover, Tl: {}", self.tl);
        let player = active_char!(conn);
        ops::toggle_allow_recover(server, &player, self.tl);
        Ok(())
    }
);

cs_packet!(
    /// `CSSellHousePacket` (the listing can only be changed by its owner).
    CSSellHousePacket { tl: u16, money_amount: u32, sell_to: String }
    read |s| {
        Ok(Self { tl: s.read_u16()?, money_amount: s.read_u32()?, sell_to: s.read_string()? })
    }
    handle |self, server, conn| {
        tracing::debug!(
            "SellHouse, Tl: {}, MoneyAmount: {}, SellTo: {}",
            self.tl, self.money_amount, self.sell_to
        );
        let player = active_char!(conn);
        if !server.systems.housing.manager.by_tl(self.tl)
            .is_some_and(|house| house.data().unit.owner_id == player.character_id)
        {
            return Ok(());
        }
        let mut sell_to_id = 0;
        if !self.sell_to.is_empty() {
            // Note (C#): the typed name is not normalized before the lookup.
            sell_to_id = server.names.get_character_id(&self.sell_to);
            if sell_to_id == 0 {
                super::manager::send_error(
                    &player,
                    super::manager::error_message::HOUSE_CANNOT_SELL_AS_DESIGNATED_BUYER_NOT_FOUND,
                );
                return Ok(());
            }
        }
        if self.money_amount > 0 {
            ops::set_for_sale_by_tl(server, self.tl, self.money_amount, sell_to_id, Some(&player));
        } else {
            ops::cancel_for_sale_by_tl(server, self.tl, true);
        }
        Ok(())
    }
);

cs_packet!(
    /// `CSSellHouseCancelPacket`.
    CSSellHouseCancelPacket { tl: u16 }
    read |s| { Ok(Self { tl: s.read_u16()? }) }
    handle |self, server, conn| {
        tracing::debug!("SellHouseCancel, Tl: {}", self.tl);
        let player = active_char!(conn);
        if !server.systems.housing.manager.by_tl(self.tl)
            .is_some_and(|house| house.data().unit.owner_id == player.character_id)
        {
            return Ok(());
        }
        ops::cancel_for_sale_by_tl(server, self.tl, true);
        Ok(())
    }
);

cs_packet!(
    /// `CSBuyHousePacket`.
    CSBuyHousePacket { tl: u16, money_amount: u32 }
    read |s| { Ok(Self { tl: s.read_u16()?, money_amount: s.read_u32()? }) }
    handle |self, server, conn| {
        let player = active_char!(conn);
        ops::buy_house(server, self.tl, self.money_amount, &player);
        Ok(())
    }
);

/// Registers the housing C->S packets (`CSChangeHousePayPacket` has opcode 0xfff in 1.2).
pub fn register(reg: &mut PacketRegistry) {
    use crate::net::packet::LEVEL_PLAIN as L;
    reg.register::<CSConstructHouseTaxPacket>(L, cs::CSConstructHouseTaxPacket);
    reg.register::<CSCreateHousePacket>(L, cs::CSCreateHousePacket);
    reg.register::<CSDecorateHousePacket>(L, cs::CSDecorateHousePacket);
    reg.register::<CSChangeHouseNamePacket>(L, cs::CSChangeHouseNamePacket);
    reg.register::<CSChangeHousePermissionPacket>(L, cs::CSChangeHousePermissionPacket);
    reg.register::<CSRequestHouseTaxPacket>(L, cs::CSRequestHouseTaxPacket);
    reg.register::<CSAllowHousingRecoverPacket>(L, cs::CSAllowHousingRecoverPacket);
    reg.register::<CSSellHousePacket>(L, cs::CSSellHousePacket);
    reg.register::<CSSellHouseCancelPacket>(L, cs::CSSellHouseCancelPacket);
    reg.register::<CSBuyHousePacket>(L, cs::CSBuyHousePacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::registry::ClientPacket;
    use aaemu_commons::helpers::convert_coord_to_long;

    fn body(p: &impl Marshal) -> Vec<u8> {
        let mut s = PacketStream::new();
        p.write(&mut s);
        s.into_bytes()
    }

    fn bytes(f: impl FnOnce(&mut PacketStream)) -> Vec<u8> {
        let mut s = PacketStream::new();
        f(&mut s);
        s.into_bytes()
    }

    #[test]
    fn sc_packets_are_byte_exact() {
        let p = SCConstructHouseTaxPacket {
            design_id: 0x11223344,
            heavy_tax_house_count: 2,
            normal_tax_house_count: 1,
            is_heavy_tax_house: true,
            base_tax_money_amount: 100000,
            deposit_tax_money_amount: 200000,
            total_tax_money_amount: 300000,
        };
        assert_eq!(
            body(&p),
            vec![
                0x44, 0x33, 0x22, 0x11, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0xA0, 0x86, 1, 0, 0x40, 0x0D, 3,
                0, 0xE0, 0x93, 4, 0
            ]
        );
        assert_eq!(
            body(&SCHouseBuildPayChangedPacket {
                tl: 0x0102,
                money_amount: -1
            }),
            vec![2, 1, 0xFF, 0xFF, 0xFF, 0xFF]
        );
        assert_eq!(
            body(&SCHouseBuildProgressPacket {
                tl: 7,
                model_id: 0x0A0B,
                all_step: 40,
                cur_step: 7
            }),
            vec![7, 0, 0x0B, 0x0A, 0, 0, 40, 0, 0, 0, 7, 0, 0, 0]
        );
        assert_eq!(
            body(&SCHouseDemolishedPacket { tl: 0x1234 }),
            vec![0x34, 0x12]
        );
        assert_eq!(
            body(&SCMyHouseRemovedPacket { tl: 0x1234 }),
            vec![0x34, 0x12]
        );
        assert_eq!(
            body(&SCHouseFarmPacket {
                name: "ab".into(),
                total: 3,
                harvestable: 1
            }),
            vec![2, 0, b'a', b'b', 3, 0, 0, 0, 1, 0, 0, 0]
        );
        assert_eq!(
            body(&SCHousePermissionChangedPacket {
                tl: 5,
                permission: 2
            }),
            vec![5, 0, 2]
        );
        assert_eq!(
            body(&SCHouseResetForSalePacket {
                tl: 5,
                house_name: "H".into()
            }),
            vec![5, 0, 1, 0, b'H']
        );
        assert_eq!(
            body(&SCHouseSetForSalePacket {
                tl: 5,
                money_amount: 1000,
                sell_to_player_id: 9,
                sell_to_name: "Bo".into(),
                house_name: "H".into(),
            }),
            vec![5, 0, 0xE8, 3, 0, 0, 9, 0, 0, 0, 2, 0, b'B', b'o', 1, 0, b'H']
        );
        assert_eq!(
            body(&SCHouseSoldPacket {
                tl: 5,
                unk_id: 1,
                unk2_id: 2,
                new_owner_acc: 3,
                owner_name: "B".into(),
                house_name: "H".into(),
            }),
            vec![5, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 1, 0, b'B', 1, 0, b'H']
        );
        assert_eq!(
            body(&SCHouseTaxInfoPacket {
                tl: 5,
                dominion_tax_rate: 0,
                money_amount: 200000,
                money_amount2: 100000,
                due: 0x5F5E1000,
                is_already_paid: true,
                weeks_without_pay: -1,
                is_heavy_tax_house: false,
            }),
            bytes(|s| {
                s.write_u16(5)
                    .write_i32(0)
                    .write_i32(200000)
                    .write_i32(100000)
                    .write_i64(0x5F5E1000)
                    .write_bool(true)
                    .write_i32(-1)
                    .write_bool(false);
            })
        );
        assert_eq!(
            body(&SCHousingRecoverTogglePacket {
                tl: 5,
                allow_recover: true
            }),
            vec![5, 0, 1]
        );
    }

    #[test]
    fn cs_packets_decode() {
        let b = bytes(|s| {
            s.write_u32(300)
                .write_i64(convert_coord_to_long(1000.5))
                .write_i64(convert_coord_to_long(2000.25))
                .write_f32(55.5);
        });
        let p = CSConstructHouseTaxPacket::read(&mut PacketStream::from(b)).unwrap();
        assert_eq!(
            p,
            CSConstructHouseTaxPacket {
                design_id: 300,
                x: 1000.5,
                y: 2000.25,
                z: 55.5
            }
        );

        let b = bytes(|s| {
            s.write_u32(300)
                .write_i64(convert_coord_to_long(10.0))
                .write_i64(convert_coord_to_long(20.0))
                .write_f32(30.0)
                .write_f32(1.5)
                .write_u64(0xAABBCCDD)
                .write_i32(500)
                .write_i32(7)
                .write_bool(true);
        });
        let p = CSCreateHousePacket::read(&mut PacketStream::from(b)).unwrap();
        assert_eq!(
            p,
            CSCreateHousePacket {
                design_id: 300,
                x: 10.0,
                y: 20.0,
                z: 30.0,
                z_rot: 1.5,
                item_id: 0xAABBCCDD,
                money_amount: 500,
                ht: 7,
                auto_use_aa_point: true,
            }
        );

        let b = bytes(|s| {
            s.write_u16(7)
                .write_u32(50)
                .write_f32(1.0)
                .write_f32(2.0)
                .write_f32(3.0)
                .write_f32(0.0)
                .write_f32(0.0)
                .write_f32(0.5)
                .write_f32(0.75)
                .write_bc(0x123456)
                .write_u64(99);
        });
        let p = CSDecorateHousePacket::read(&mut PacketStream::from(b)).unwrap();
        assert_eq!(p.house_tl, 7);
        assert_eq!(p.quat, [0.0, 0.0, 0.5, 0.75]);
        assert_eq!(p.parent_obj_id, 0x123456);
        assert_eq!(p.item_id, 99);

        let b = bytes(|s| {
            s.write_u16(3).write_u32(1000).write_str("Bob");
        });
        let p = CSSellHousePacket::read(&mut PacketStream::from(b)).unwrap();
        assert_eq!(
            p,
            CSSellHousePacket {
                tl: 3,
                money_amount: 1000,
                sell_to: "Bob".into()
            }
        );
        let b = bytes(|s| {
            s.write_u16(3).write_str("villa");
        });
        assert_eq!(
            CSChangeHouseNamePacket::read(&mut PacketStream::from(b)).unwrap(),
            CSChangeHouseNamePacket {
                tl: 3,
                name: "villa".into()
            }
        );
        let b = bytes(|s| {
            s.write_u16(3).write_u8(1);
        });
        assert_eq!(
            CSChangeHousePermissionPacket::read(&mut PacketStream::from(b)).unwrap(),
            CSChangeHousePermissionPacket {
                tl: 3,
                permission: 1
            }
        );
        let b = bytes(|s| {
            s.write_u16(3).write_u32(77);
        });
        assert_eq!(
            CSBuyHousePacket::read(&mut PacketStream::from(b)).unwrap(),
            CSBuyHousePacket {
                tl: 3,
                money_amount: 77
            }
        );
        for p in [
            CSRequestHouseTaxPacket::read(&mut PacketStream::from(vec![9, 0]))
                .unwrap()
                .tl,
            CSAllowHousingRecoverPacket::read(&mut PacketStream::from(vec![9, 0]))
                .unwrap()
                .tl,
            CSSellHouseCancelPacket::read(&mut PacketStream::from(vec![9, 0]))
                .unwrap()
                .tl,
        ] {
            assert_eq!(p, 9);
        }
        let b = bytes(|s| {
            s.write_u16(3).write_i32(-5);
        });
        assert_eq!(
            CSChangeHousePayPacket::read(&mut PacketStream::from(b)).unwrap(),
            CSChangeHousePayPacket {
                tl: 3,
                money_amount: -5
            }
        );
    }
}
