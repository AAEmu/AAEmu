//! `AuctionController`: list / search / add / generate auction lots.

use serde_json::Value;

use crate::systems::services::webapi::hooks::{AuctionLotView, NewAuctionLot};
use crate::systems::services::webapi::http::{
    bad_request_json, ok_json, HttpResponse, QueryString,
};
use crate::systems::services::webapi::json::{JValue, Stj};
use crate::systems::services::webapi::route_mapper::{
    handler, Controller, ControllerCtx, Method, RouteSpec,
};

/// `new { error = "Internal server error", details = message }`.
fn internal_error(details: &str) -> HttpResponse {
    bad_request_json(&JValue::object([
        ("error", JValue::from("Internal server error")),
        ("details", JValue::from(details)),
    ]))
}

fn invalid_parameters() -> HttpResponse {
    bad_request_json(&JValue::object([(
        "error",
        JValue::from("Invalid parameters"),
    )]))
}

/// `TryGetProperty` for each name; `None` as soon as one is missing.
fn properties<'a, const N: usize>(v: &'a Value, names: [&str; N]) -> Option<[&'a Value; N]> {
    let mut out = [&Value::Null; N];
    for (i, n) in names.iter().enumerate() {
        out[i] = Stj::try_property(v, n)?;
    }
    Some(out)
}

/// `AddAuctionItem` (`/api/auction/add`).
async fn add_auction_item(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let json = Stj::parse(&ctx.request.body)?;
    let Some([item_id, quantity, price, duration, client_id, client_name]) = properties(
        &json,
        [
            "ItemId",
            "Quantity",
            "Price",
            "Duration",
            "ClientId",
            "ClientName",
        ],
    ) else {
        return Ok(invalid_parameters());
    };
    let item_id = Stj::get_u32(item_id)?;
    let quantity = Stj::get_i32(quantity)?;
    let price = Stj::get_i32(price)?;
    let duration = Stj::get_i32(duration)? as u8;
    let client_id = Stj::get_u32(client_id)?;
    let _client_name = Stj::get_string(client_name)?;

    let server = ctx.server()?;
    let player = server.world.character_by_id(client_id);
    let item = server.items.item_by_id(u64::from(item_id));
    let (Some(player), Some(item)) = (player, item) else {
        return Ok(internal_error("Item not found!"));
    };
    let (id, name) = {
        let c = player.data();
        (c.id, c.unit.name.clone())
    };
    let lot = NewAuctionLot {
        client_id: id,
        client_name: name,
        item,
        start_price: price,
        buyout_price: price,
        duration,
        min_stack: 1,
        max_stack: quantity,
    };
    match server
        .systems
        .services
        .webapi_economy()
        .create_auction_lot(server, lot)
    {
        Ok(lot) => {
            tracing::info!("Added auction item: {}", lot.to_json());
            Ok(ok_json(&JValue::object([
                ("message", JValue::from("Auction item added successfully")),
                ("item", lot),
            ])))
        }
        Err(e) => {
            tracing::error!("Error adding auction item: {e}");
            Ok(internal_error(&e.to_string()))
        }
    }
}

/// `GetAllAuctionItems` (`/api/auction/list`).
async fn get_all_auction_items(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let server = ctx.server()?;
    match server
        .systems
        .services
        .webapi_economy()
        .auction_lots(server)
    {
        Ok(lots) => Ok(ok_json(&JValue::object([(
            "items",
            JValue::Array(lots.into_iter().map(|l| l.json).collect()),
        )]))),
        Err(e) => {
            tracing::error!("Error retrieving auction items: {e}");
            Ok(internal_error(&e.to_string()))
        }
    }
}

/// `uint.Parse` / `int.Parse` failures (`FormatException` / `OverflowException`).
fn parse_num<T: std::str::FromStr>(s: &str) -> anyhow::Result<T> {
    s.trim()
        .parse()
        .map_err(|_| anyhow::anyhow!("The input string '{s}' was not in a correct format."))
}

/// The `SearchAuctionItems` filters.
pub fn search_lots(lots: Vec<AuctionLotView>, url: &str) -> anyhow::Result<Vec<AuctionLotView>> {
    // `request.Url.Split('?')[1]`: only the part up to a second '?'.
    let query = url.split('?').nth(1).unwrap_or("");
    let q = QueryString::parse(query);
    let mut lots = lots;
    if let Some(v) = q.get("ItemId") {
        let id: u32 = parse_num(&v)?;
        lots.retain(|l| l.template_id == id);
    }
    if let Some(name) = q.get("ClientName") {
        lots.retain(|l| l.client_name.to_lowercase() == name.to_lowercase());
    }
    if let Some(v) = q.get("StackSize") {
        let n: u32 = parse_num(&v)?;
        lots.retain(|l| l.stack_size == n);
    }
    if let Some(v) = q.get("DirectMoney") {
        let n: i32 = parse_num(&v)?;
        lots.retain(|l| l.direct_money == n);
    }
    if let Some(v) = q.get("BidMoney") {
        let n: i32 = parse_num(&v)?;
        lots.retain(|l| l.bid_money == n);
    }
    if let Some(name) = q.get("BidderName") {
        // `item.BidderName.Equals(...)` throws on a null bidder name.
        let mut out = Vec::with_capacity(lots.len());
        for l in lots {
            let Some(bidder) = &l.bidder_name else {
                return Err(anyhow::anyhow!(
                    "Object reference not set to an instance of an object."
                ));
            };
            if bidder.to_lowercase() == name.to_lowercase() {
                out.push(l);
            }
        }
        lots = out;
    }
    Ok(lots)
}

/// `SearchAuctionItems` (`/api/auction/search`).
async fn search_auction_items(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let server = ctx.server()?;
    let result = server
        .systems
        .services
        .webapi_economy()
        .auction_lots(server)
        .and_then(|lots| search_lots(lots, &ctx.request.url));
    match result {
        Ok(lots) => Ok(ok_json(&JValue::object([(
            "items",
            JValue::Array(lots.into_iter().map(|l| l.json).collect()),
        )]))),
        Err(e) => {
            tracing::error!("Error retrieving auction items: {e}");
            Ok(internal_error(&e.to_string()))
        }
    }
}

/// `GenerateAuctionItem` (`/api/auction/generate`).
async fn generate_auction_item(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let json = Stj::parse(&ctx.request.body)?;
    let Some([template, quantity, grade, buy_now, start, duration, client_id, client_name]) =
        properties(
            &json,
            [
                "ItemTemplateId",
                "Quantity",
                "GradeId",
                "BuyNowPrice",
                "StartPrice",
                "Duration",
                "ClientId",
                "ClientName",
            ],
        )
    else {
        return Ok(invalid_parameters());
    };
    let item_template_id = Stj::get_u32(template)?;
    let quantity = Stj::get_i32(quantity)?;
    let grade_id = Stj::get_u8(grade)?;
    let buy_now_price = Stj::get_i32(buy_now)?;
    let start_price = Stj::get_i32(start)?;
    let duration = Stj::get_i32(duration)? as u8;
    let client_id = Stj::get_u32(client_id)?;
    let client_name = Stj::get_string(client_name)?.unwrap_or_default();

    let server = ctx.server()?;
    let hooks = server.systems.services.webapi_economy();
    let result: anyhow::Result<HttpResponse> = (|| {
        let Some(new_items) =
            hooks.acquire_auction_items(server, client_id, item_template_id, quantity, grade_id)?
        else {
            let err = format!(
                "Unable to create new item {item_template_id} for character {client_name} ({client_id})"
            );
            tracing::warn!("{err}");
            return Ok(bad_request_json(&JValue::from(err)));
        };
        if new_items.len() != 1 {
            tracing::warn!(
                "Generate auction item request generated more than one entry! ({})",
                new_items.len()
            );
        }
        let Some(new_item) = new_items.into_iter().next() else {
            let err = format!(
                "No item was generated with template {item_template_id} for character {client_name} ({client_id})"
            );
            tracing::warn!("{err}");
            return Ok(bad_request_json(&JValue::from(err)));
        };
        let player = server.names.get_character_name(client_id);
        let template = server.game_data.item_template(item_template_id);
        if player.is_none() || template.is_none() {
            return Ok(internal_error("Item not found!"));
        }
        let lot = hooks.create_auction_lot(
            server,
            NewAuctionLot {
                client_id,
                client_name: client_name.clone(),
                item: new_item,
                start_price,
                buyout_price: buy_now_price,
                duration,
                min_stack: 1,
                max_stack: quantity,
            },
        )?;
        tracing::info!("Added auction item: {}", lot.to_json());
        Ok(ok_json(&JValue::object([
            (
                "message",
                JValue::from("Auction item generated successfully"),
            ),
            ("item", lot),
        ])))
    })();
    match result {
        Ok(r) => Ok(r),
        Err(e) => {
            tracing::error!("Error adding auction item: {e}");
            Ok(internal_error(&e.to_string()))
        }
    }
}

/// `AuctionController`.
pub struct AuctionController;

impl Controller for AuctionController {
    fn routes() -> Vec<RouteSpec> {
        vec![
            RouteSpec {
                method: Method::Post,
                path: "/api/auction/add",
                name: "AddAuctionItem",
                handler: handler(|ctx| async move { add_auction_item(&ctx).await }),
            },
            RouteSpec {
                method: Method::Get,
                path: "/api/auction/list",
                name: "GetAllAuctionItems",
                handler: handler(|ctx| async move { get_all_auction_items(&ctx).await }),
            },
            RouteSpec {
                method: Method::Get,
                path: "/api/auction/search",
                name: "SearchAuctionItems",
                handler: handler(|ctx| async move { search_auction_items(&ctx).await }),
            },
            RouteSpec {
                method: Method::Post,
                path: "/api/auction/generate",
                name: "GenerateAuctionItem",
                handler: handler(|ctx| async move { generate_auction_item(&ctx).await }),
            },
        ]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn lot(template_id: u32, client: &str, bidder: Option<&str>) -> AuctionLotView {
        AuctionLotView {
            json: JValue::from(template_id),
            template_id,
            client_name: client.into(),
            stack_size: 1,
            direct_money: 10,
            bid_money: 5,
            bidder_name: bidder.map(Into::into),
        }
    }

    #[test]
    fn search_filters() {
        let lots = vec![
            lot(1, "Bob", Some("x")),
            lot(2, "bob", None),
            lot(1, "Al", Some("y")),
        ];
        let r = search_lots(lots.clone(), "/api/auction/search?ItemId=1&ClientName=BOB").unwrap();
        assert_eq!(r.len(), 1);
        let r = search_lots(lots.clone(), "/api/auction/search?DirectMoney=10").unwrap();
        assert_eq!(r.len(), 3);
        assert!(search_lots(lots.clone(), "/api/auction/search?ItemId=x").is_err());
        // A lot without bidder makes the C# `Equals` throw.
        assert!(search_lots(lots.clone(), "/api/auction/search?BidderName=x").is_err());
        let r = search_lots(lots, "/api/auction/search").unwrap();
        assert_eq!(r.len(), 3);
    }
}
