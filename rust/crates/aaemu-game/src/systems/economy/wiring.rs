//! Connects the economy's internal hook slots (installed in `System::start`): the auction,
//! cash shop and trade-pack mails go through the mail port (`MailForAuction`,
//! `CommercialMail`, `MailForSpeciality`), the store's NPC queries go to
//! [`super::hooks::NpcEconomyHooks`].

use std::sync::Arc;

use crate::server::GameServer;
use crate::world::{DoodadRef, NpcRef, PlayerRef};

use super::auction::{AuctionMail, AuctionMailHooks};
use super::cash_shop::{CashShopMailHooks, CommercialMailRequest};
use super::inventory::ItemTxn;
use super::mail_kinds::{CommercialMail, MailForAuction, MailForSpeciality};
use super::store::{SpecialtyMail, StoreHooks};

/// The economy-internal hook implementations.
#[derive(Debug, Default)]
pub struct Internal;

impl AuctionMailHooks for Internal {
    fn send_auction_mail(&self, server: &GameServer, mail: AuctionMail) -> bool {
        let mut txn = ItemTxn::begin(server);
        match mail {
            AuctionMail::SaleSeller {
                item,
                seller_id,
                buyout_price,
                listing_fee,
                seller_share,
                trade_tax_fee,
            } => {
                let mut m =
                    MailForAuction::new(server, &item, seller_id, buyout_price, listing_fee);
                m.finalize_for_sale_seller(server, seller_share, trade_tax_fee);
                m.send(&mut txn)
            }
            AuctionMail::SaleBuyer {
                item,
                seller_id,
                buyout_price,
                listing_fee,
                buyer_id,
            } => {
                let mut m =
                    MailForAuction::new(server, &item, seller_id, buyout_price, listing_fee);
                m.finalize_for_sale_buyer(server, buyer_id);
                m.send(&mut txn)
            }
            AuctionMail::Cancel {
                item,
                seller_id,
                buyout_price,
                listing_fee,
            } => {
                let mut m =
                    MailForAuction::new(server, &item, seller_id, buyout_price, listing_fee);
                m.finalize_for_cancel(server);
                m.send(&mut txn)
            }
            AuctionMail::Fail {
                item,
                seller_id,
                buyout_price,
                listing_fee,
            } => {
                let mut m =
                    MailForAuction::new(server, &item, seller_id, buyout_price, listing_fee);
                m.finalize_for_fail(server);
                m.send(&mut txn)
            }
            AuctionMail::BidFail {
                item_template_id,
                seller_id,
                buyout_price,
                listing_fee,
                previous_buyer_id,
                previous_bid,
            } => {
                let mut m = MailForAuction::new_from_template(
                    server,
                    item_template_id,
                    seller_id,
                    buyout_price,
                    listing_fee,
                );
                m.finalize_for_bid_fail(server, previous_buyer_id, previous_bid);
                m.send(&mut txn)
            }
        }
    }
}

impl CashShopMailHooks for Internal {
    fn send_commercial_mail(&self, server: &GameServer, r: CommercialMailRequest) -> bool {
        let mut txn = ItemTxn::begin(server);
        let mut m = CommercialMail::new(
            r.receiver_id,
            &r.receiver_name,
            &r.sender_name,
            r.items,
            r.is_gift,
            r.is_refund,
            &r.purchased_item_title,
        );
        m.finalize_mail(&mut txn);
        m.send(&mut txn)
    }
}

impl StoreHooks for Internal {
    fn npc_specialty_coin_id(&self, server: &GameServer, npc: &NpcRef) -> u32 {
        server
            .systems
            .economy
            .hooks
            .npcs
            .get()
            .map_or(0, |h| h.specialty_coin_id(server, npc))
    }

    fn send_specialty_mail(
        &self,
        server: &GameServer,
        seller: &PlayerRef,
        mail: &SpecialtyMail,
    ) -> bool {
        let mut txn = ItemTxn::begin(server);
        let mut m = MailForSpeciality::new(
            server,
            seller,
            mail.crafter_id,
            mail.trade_pack_template_id,
            mail.trade_rate,
            mail.item_reward_template_id,
            mail.item_count_base,
            mail.item_count_bonus,
            mail.item_count_for_seller,
            mail.item_count_for_crafter,
            mail.interest_rate,
        );
        // The C# ignores the Finalize* result and only checks `Send()`.
        if mail.for_crafter {
            m.finalize_for_crafter(&mut txn);
        } else {
            m.finalize_for_seller(&mut txn);
        }
        m.send(&mut txn)
    }

    fn buy_coin_item(&self, server: &GameServer, player: &PlayerRef, doodad: &DoodadRef, id: u32) {
        match server.systems.economy.hooks.npcs.get() {
            Some(h) => h.buy_coin_item(server, player, doodad, id),
            None => tracing::warn!(
                "BuyCoinItem, no doodadFunc for objId: {}, id: {id}",
                doodad.obj_id
            ),
        }
    }
}

/// Installs the internal hooks (called from `System::start`; tests call it too).
pub fn install(server: &GameServer) {
    let e = &server.systems.economy;
    let h = Arc::new(Internal);
    e.auction.mail.install(h.clone());
    e.cash_shop.mail.install(h.clone());
    e.store.hooks.install(h);
}
