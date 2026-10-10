//! The auction packets: `CSAuctionPostPacket`, `CSAuctionSearchPacket`, `CSBidAuctionPacket`,
//! `CSCancelAuctionPacket`, `CSAuctionMyBidListPacket`, `CSAuctionLowestPricePacket`,
//! `SCAuctionPostedPacket`, `SCAuctionSearchedPacket`, `SCAuctionBidPacket`,
//! `SCAuctionCanceledPacket`, `SCAuctionLowestPricePacket`.

use aaemu_commons::{Marshal, PacketStream};
use chrono::{DateTime, Utc};

use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::server::GameServer;

use super::auction::{AuctionBid, AuctionLot, AuctionSearch, ClientAuctionLot};
use super::item_handlers::{active, sync_client_packet};

pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSAuctionPostPacket>(LEVEL_PLAIN, cs::CSAuctionPostPacket);
    reg.register::<CSAuctionSearchPacket>(LEVEL_PLAIN, cs::CSAuctionSearchPacket);
    reg.register::<CSBidAuctionPacket>(LEVEL_PLAIN, cs::CSBidAuctionPacket);
    reg.register::<CSCancelAuctionPacket>(LEVEL_PLAIN, cs::CSCancelAuctionPacket);
    reg.register::<CSAuctionMyBidListPacket>(LEVEL_PLAIN, cs::CSAuctionMyBidListPacket);
    reg.register::<CSAuctionLowestPricePacket>(LEVEL_PLAIN, cs::CSAuctionLowestPricePacket);
}

// ---------------------------------------------------------------------------------------------
// C->S
// ---------------------------------------------------------------------------------------------

/// `CSAuctionPostPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSAuctionPostPacket {
    pub auctioneer_id: u32,
    pub auctioneer_id2: u32,
    pub item_id: u64,
    pub start_price: i32,
    pub buyout_price: i32,
    pub duration: u8,
}

impl CSAuctionPostPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            auctioneer_id: s.read_bc()?,
            auctioneer_id2: s.read_bc()?,
            item_id: s.read_u64()?,
            start_price: s.read_i32()?,
            buyout_price: s.read_i32()?,
            duration: s.read_u8()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!(
            "AuctionMyBidList, auctioneerId: {}, auctioneerId2: {}, itemId: {}, startPrice: {}, buyoutPrice: {}, duration: {}",
            self.auctioneer_id,
            self.auctioneer_id2,
            self.item_id,
            self.start_price,
            self.buyout_price,
            self.duration
        );
        let Some(p) = active(conn) else { return };
        server.systems.economy.auction.post_lot_on_auction(
            server,
            &p,
            self.item_id,
            self.start_price,
            self.buyout_price,
            self.duration,
        );
    }
}
sync_client_packet!(CSAuctionPostPacket);

/// `CSAuctionSearchPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSAuctionSearchPacket {
    pub auctioneer_id: u32,
    pub auctioneer_id2: u32,
    pub search: AuctionSearch,
}

impl CSAuctionSearchPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            auctioneer_id: s.read_bc()?,
            auctioneer_id2: s.read_bc()?,
            search: AuctionSearch::read(s)?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!(
            "AuctionSearch, auctioneerId: {}, auctioneerId: {}, Keyword: {}",
            self.auctioneer_id,
            self.auctioneer_id2,
            self.search.keyword
        );
        let Some(p) = active(conn) else { return };
        server
            .systems
            .economy
            .auction
            .search_auction_lots(server, &p, &self.search);
    }
}
sync_client_packet!(CSAuctionSearchPacket);

/// `CSBidAuctionPacket` (`AuctionDisplay` + `AuctionBid`).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSBidAuctionPacket {
    pub auctioneer_id: u32,
    pub auctioneer_id2: u32,
    pub lot: ClientAuctionLot,
    pub bid: AuctionBid,
}

impl CSBidAuctionPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            auctioneer_id: s.read_bc()?,
            auctioneer_id2: s.read_bc()?,
            lot: ClientAuctionLot::read(s)?,
            bid: AuctionBid::read(s)?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!(
            "AuctionBid, auctioneerId: {}, auctioneerId2: {}, BidderName: {}, LotId: {}:{}, Money: {}",
            self.auctioneer_id,
            self.auctioneer_id2,
            self.bid.bidder_name,
            self.lot.id,
            self.bid.lot_id,
            self.bid.money
        );
        let Some(p) = active(conn) else {
            tracing::warn!("Invalid arguments passed to BidOnAuctionLot.");
            return;
        };
        server
            .systems
            .economy
            .auction
            .bid_on_auction_lot(server, &p, self.lot.id, self.bid);
    }
}
sync_client_packet!(CSBidAuctionPacket);

/// `CSCancelAuctionPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSCancelAuctionPacket {
    pub auctioneer_id: u32,
    pub auctioneer_id2: u32,
    pub lot: ClientAuctionLot,
}

impl CSCancelAuctionPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            auctioneer_id: s.read_bc()?,
            auctioneer_id2: s.read_bc()?,
            lot: ClientAuctionLot::read(s)?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!(
            "AuctionCancel, auctioneerId: {}, auctioneerId2: {}, ClientName: {}, LotId: {}",
            self.auctioneer_id,
            self.auctioneer_id2,
            self.lot.client_name,
            self.lot.id
        );
        let Some(p) = active(conn) else { return };
        server
            .systems
            .economy
            .auction
            .cancel_auction_lot(server, &p, self.lot.id);
    }
}
sync_client_packet!(CSCancelAuctionPacket);

/// `CSAuctionMyBidListPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSAuctionMyBidListPacket {
    pub auctioneer_id: u32,
    pub auctioneer_id2: u32,
    pub page: i32,
}

impl CSAuctionMyBidListPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            auctioneer_id: s.read_bc()?,
            auctioneer_id2: s.read_bc()?,
            page: s.read_i32()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!(
            "AuctionMyBidList, auctioneerId: {}, auctioneerId2: {}, Page: {}",
            self.auctioneer_id,
            self.auctioneer_id2,
            self.page
        );
        let Some(p) = active(conn) else { return };
        server
            .systems
            .economy
            .auction
            .get_bid_auction_lots(server, &p, self.page);
    }
}
sync_client_packet!(CSAuctionMyBidListPacket);

/// `CSAuctionLowestPricePacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSAuctionLowestPricePacket {
    pub auctioneer_id: u32,
    pub auctioneer_id2: u32,
    pub item_template_id: u32,
    pub item_grade: u8,
}

impl CSAuctionLowestPricePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            auctioneer_id: s.read_bc()?,
            auctioneer_id2: s.read_bc()?,
            item_template_id: s.read_u32()?,
            item_grade: s.read_u8()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        tracing::warn!(
            "AuctionLowestPrice, auctioneerId: {}, auctioneerId2: {}, TemplateId: {}, Grade: {}",
            self.auctioneer_id,
            self.auctioneer_id2,
            self.item_template_id,
            self.item_grade
        );
        let Some(p) = active(conn) else { return };
        server.systems.economy.auction.cheapest_auction_lot(
            &p,
            self.item_template_id,
            self.item_grade,
        );
    }
}
sync_client_packet!(CSAuctionLowestPricePacket);

// ---------------------------------------------------------------------------------------------
// S->C
// ---------------------------------------------------------------------------------------------

/// `SCAuctionPostedPacket(auctionLot)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCAuctionPostedPacket {
    pub lot: AuctionLot,
}
packet_def!(
    SCAuctionPostedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAuctionPostedPacket
);
impl Marshal for SCAuctionPostedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.lot.write(s);
    }
}

/// `SCAuctionSearchedPacket(page, count, lots, errorMsg, serverTime)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCAuctionSearchedPacket {
    pub page: i32,
    pub count: i32,
    pub lots: Vec<AuctionLot>,
    pub error_msg: i16,
    pub server_time: DateTime<Utc>,
}
packet_def!(
    SCAuctionSearchedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAuctionSearchedPacket
);
impl Marshal for SCAuctionSearchedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_i32(self.page).write_i32(self.count);
        for lot in &self.lots {
            lot.write(s);
        }
        s.write_i16(self.error_msg)
            .write_date_time(self.server_time);
    }
}

/// `SCAuctionBidPacket(bid, isBuyout, itemId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCAuctionBidPacket {
    pub bid: AuctionBid,
    pub is_buyout: bool,
    /// The item template id.
    pub item_id: u32,
}
packet_def!(
    SCAuctionBidPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAuctionBidPacket
);
impl Marshal for SCAuctionBidPacket {
    fn write(&self, s: &mut PacketStream) {
        self.bid.write(s);
        s.write_bool(self.is_buyout).write_u32(self.item_id);
    }
}

/// `SCAuctionCanceledPacket(auctionLot)`.
#[derive(Debug, Clone, PartialEq)]
pub struct SCAuctionCanceledPacket {
    pub lot: AuctionLot,
}
packet_def!(
    SCAuctionCanceledPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAuctionCanceledPacket
);
impl Marshal for SCAuctionCanceledPacket {
    fn write(&self, s: &mut PacketStream) {
        self.lot.write(s);
    }
}

/// `SCAuctionLowestPricePacket(itemTemplateId, itemGrade, moneyAmount)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCAuctionLowestPricePacket {
    pub item_template_id: u32,
    pub item_grade: u8,
    pub money_amount: i32,
}
packet_def!(
    SCAuctionLowestPricePacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAuctionLowestPricePacket
);
impl Marshal for SCAuctionLowestPricePacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.item_template_id)
            .write_u8(self.item_grade)
            .write_i32(self.money_amount);
    }
}
