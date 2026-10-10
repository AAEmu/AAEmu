//! `MailController` (+ `SendMailRequest`, `ListMailRequest`, `DeleteMailRequest`,
//! `ListMailResponseItem`, `RecipientType`, `AttachmentItem`).

use chrono::Utc;
use serde_json::Value;
use sqlx::Row;

use crate::models::container::slot_type;
use crate::server::GameServer;
use crate::systems::services::webapi::hooks::{WebApiMail, WebApiMailInfo};
use crate::systems::services::webapi::http::{
    bad_request_json, error_model, ok_json, HttpResponse,
};
use crate::systems::services::webapi::json::{JValue, Stj, StjError};
use crate::systems::services::webapi::route_mapper::{
    handler, Controller, ControllerCtx, Method, RouteSpec,
};

/// `MailType.SysExpress` (the `SendMailRequest.Type` default).
pub const MAIL_TYPE_SYS_EXPRESS: u8 = 18;

/// `RecipientType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RecipientType {
    Character = 0,
    Expedition = 1,
    Family = 2,
    /// Online characters.
    Online = 3,
    /// All characters, excluding deleted characters.
    All = 4,
}

impl RecipientType {
    /// `Enum.IsDefined`.
    fn from_i32(v: i32) -> Option<Self> {
        Some(match v {
            0 => Self::Character,
            1 => Self::Expedition,
            2 => Self::Family,
            3 => Self::Online,
            4 => Self::All,
            _ => return None,
        })
    }
}

/// `AttachmentItem`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AttachmentItem {
    pub id: u32,
    pub count: i32,
}

/// `SendMailRequest` with its defaults.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SendMailRequest {
    /// `MailType` (a byte enum).
    pub mail_type: u8,
    pub sender_name: Option<String>,
    /// `Recipients` (`None` = JSON `null`).
    pub recipients: Option<Vec<u32>>,
    pub recipient_type: i32,
    pub title: Option<String>,
    pub body: Option<String>,
    pub money: i32,
    pub billing: i32,
    pub attachment_items: Option<Vec<AttachmentItem>>,
}

impl Default for SendMailRequest {
    fn default() -> Self {
        Self {
            mail_type: MAIL_TYPE_SYS_EXPRESS,
            sender_name: Some("GM".into()),
            recipients: Some(Vec::new()),
            recipient_type: 0,
            title: None,
            body: None,
            money: 0,
            billing: 0,
            attachment_items: Some(Vec::new()),
        }
    }
}

/// A `List<T>` member: `null` or an array.
fn list<T>(
    v: &Value,
    item: impl Fn(&Value) -> Result<T, StjError>,
) -> Result<Option<Vec<T>>, StjError> {
    match v {
        Value::Null => Ok(None),
        Value::Array(a) => a.iter().map(item).collect::<Result<Vec<_>, _>>().map(Some),
        _ => Err(StjError(
            "The JSON value could not be converted to System.Collections.Generic.List.".into(),
        )),
    }
}

impl SendMailRequest {
    /// `JsonSerializer.Deserialize<SendMailRequest>(body)`: `Ok(None)` for a JSON `null`.
    pub fn parse(body: &str) -> Result<Option<Self>, StjError> {
        let v = Stj::parse(body)?;
        if v.is_null() {
            return Ok(None);
        }
        if !v.is_object() {
            return Err(StjError(
                "The JSON value could not be converted to SendMailRequest.".into(),
            ));
        }
        let mut r = Self::default();
        if let Some(x) = Stj::try_property(&v, "Type") {
            r.mail_type = Stj::get_u8(x)?;
        }
        if let Some(x) = Stj::try_property(&v, "SenderName") {
            r.sender_name = Stj::get_string(x)?;
        }
        if let Some(x) = Stj::try_property(&v, "Recipients") {
            r.recipients = list(x, Stj::get_u32)?;
        }
        if let Some(x) = Stj::try_property(&v, "RecipientType") {
            r.recipient_type = Stj::get_i32(x)?;
        }
        if let Some(x) = Stj::try_property(&v, "Title") {
            r.title = Stj::get_string(x)?;
        }
        if let Some(x) = Stj::try_property(&v, "Body") {
            r.body = Stj::get_string(x)?;
        }
        if let Some(x) = Stj::try_property(&v, "Money") {
            r.money = Stj::get_i32(x)?;
        }
        if let Some(x) = Stj::try_property(&v, "Billing") {
            r.billing = Stj::get_i32(x)?;
        }
        if let Some(x) = Stj::try_property(&v, "AttachmentItems") {
            r.attachment_items = list(x, |a| {
                if a.is_null() {
                    return Err(StjError("null attachment".into()));
                }
                let mut item = AttachmentItem { id: 0, count: 1 };
                if let Some(id) = Stj::try_property(a, "Id") {
                    item.id = Stj::get_u32(id)?;
                }
                if let Some(c) = Stj::try_property(a, "Count") {
                    item.count = Stj::get_i32(c)?;
                }
                Ok(item)
            })?;
        }
        Ok(Some(r))
    }
}

/// The recipient members `MailController.Send` reads (`Character` / `Character.Load`).
#[derive(Debug, Clone, PartialEq, Eq)]
struct Recipient {
    id: u32,
    name: String,
    deleting: bool,
}

/// `WorldManager.GetCharacterById(id) ?? Character.Load(id)`: `None` when neither exists.
async fn load_recipient(server: &GameServer, id: u32) -> anyhow::Result<Option<Recipient>> {
    if let Some(p) = server.world.character_by_id(id) {
        let c = p.data();
        return Ok(Some(Recipient {
            id: c.id,
            name: c.unit.name.clone(),
            deleting: c.delete_time > 0 || c.delete_request_time > 0,
        }));
    }
    let row = sqlx::query(
        "SELECT CAST(`id` AS SIGNED) AS id, `name`, `delete_request_time`, `delete_time` FROM characters WHERE `id` = ? and `deleted`=0",
    )
    .bind(id)
    .fetch_optional(&server.db)
    .await?;
    Ok(row.map(|r| Recipient {
        id: r.try_get::<i64, _>("id").unwrap_or_default() as u32,
        name: r.try_get("name").unwrap_or_default(),
        deleting: crate::db::is_set(crate::db::dt(&r, "delete_time"))
            || crate::db::is_set(crate::db::dt(&r, "delete_request_time")),
    }))
}

fn null_ref() -> anyhow::Error {
    anyhow::anyhow!("Object reference not set to an instance of an object.")
}

/// `MailController.Send`.
pub async fn send(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let Some(mut req) = SendMailRequest::parse(&ctx.request.body)? else {
        return Ok(bad_request_json(&error_model("Invalid mail request")));
    };
    if req
        .sender_name
        .as_deref()
        .is_none_or(|s| s.trim().is_empty())
    {
        req.sender_name = Some("GM".into());
    }
    if req.title.as_deref().is_none_or(|s| s.trim().is_empty()) {
        return Ok(bad_request_json(&error_model(
            "Email Title cannot be empty.",
        )));
    }
    if req.body.as_deref().is_none_or(|s| s.trim().is_empty()) {
        return Ok(bad_request_json(&error_model(
            "Email body cannot be empty.",
        )));
    }
    if req.money < 0 {
        return Ok(bad_request_json(&error_model(
            "Money cannot be less than 0.",
        )));
    }
    if req.billing < 0 {
        return Ok(bad_request_json(&error_model(
            "Billing cannot be less than 0.",
        )));
    }
    let Some(recipient_type) = RecipientType::from_i32(req.recipient_type) else {
        return Ok(bad_request_json(&error_model("Invalid recipient type.")));
    };
    let server = ctx.server()?;

    let mut characters: Vec<Recipient> = Vec::new();
    match recipient_type {
        RecipientType::Expedition => {
            for &recipient in req.recipients.as_ref().ok_or_else(null_ref)? {
                let expedition = server.expeditions.get(recipient);
                let Some(e) = expedition.filter(|e| !e.is_disbanded) else {
                    return Ok(bad_request_json(&error_model(format!(
                        "Invalid ExpeditionId: {recipient} ."
                    ))));
                };
                for m in &e.members {
                    match load_recipient(server, m.character_id).await? {
                        Some(c) if !c.deleting => characters.push(c),
                        _ => {}
                    }
                }
            }
        }
        RecipientType::Family => {
            for &recipient in req.recipients.as_ref().ok_or_else(null_ref)? {
                let Some(family) = server.systems.social.families.family(recipient) else {
                    return Ok(bad_request_json(&error_model(format!(
                        "Invalid FamilyId: {recipient} ."
                    ))));
                };
                // `member.Character`: only online members have one.
                for m in &family.members {
                    let Some(p) = &m.character else { continue };
                    let c = p.data();
                    if c.delete_time > 0 || c.delete_request_time > 0 {
                        continue;
                    }
                    characters.push(Recipient {
                        id: c.id,
                        name: c.unit.name.clone(),
                        deleting: false,
                    });
                }
            }
        }
        RecipientType::Online => {
            for p in server.world.all_characters() {
                let c = p.data();
                characters.push(Recipient {
                    id: c.id,
                    name: c.unit.name.clone(),
                    deleting: false,
                });
            }
        }
        RecipientType::All => {
            let rows = sqlx::query(
                "SELECT CAST(`id` AS SIGNED) AS id, `name`, `account_id` FROM `characters` where `deleted` = 0",
            )
            .fetch_all(&server.db)
            .await?;
            for r in rows {
                characters.push(Recipient {
                    id: r.try_get::<i64, _>("id")? as u32,
                    name: r.try_get("name")?,
                    deleting: false,
                });
            }
        }
        RecipientType::Character => {
            for &recipient in req.recipients.as_ref().ok_or_else(null_ref)? {
                match load_recipient(server, recipient).await? {
                    Some(c) if !c.deleting => characters.push(c),
                    _ => {
                        return Ok(bad_request_json(&error_model(format!(
                            "Invalid characterId: {recipient} ."
                        ))))
                    }
                }
            }
        }
    }

    let attachments = req.attachment_items.clone().ok_or_else(null_ref)?;
    for a in &attachments {
        if server.game_data.item_template(a.id).is_none() {
            return Ok(bad_request_json(&error_model(format!(
                "Template does not exist for {} !",
                a.id
            ))));
        }
    }

    if characters.is_empty() {
        return Ok(bad_request_json(&error_model("Recipients is empty.")));
    }

    let hooks = server.systems.services.webapi_economy();
    let mut sended: i32 = 0;
    let mut attachments = attachments;
    for character in &characters {
        let now = Utc::now().naive_utc();
        let mut mail = WebApiMail {
            mail_type: req.mail_type,
            title: req.title.clone().unwrap_or_default(),
            receiver_name: character.name.clone(),
            sender_id: 0,
            sender_name: req.sender_name.clone().unwrap_or_default(),
            receiver_id: character.id,
            extra: 0,
            text: req.body.clone().unwrap_or_default(),
            send_date: now,
            recv_date: now,
            copper_coins: req.money,
            billing_amount: req.billing,
            attachments: Vec::new(),
        };
        // The C# clamps the request's attachment counts in place (they stay clamped for the
        // following recipients).
        for a in attachments.iter_mut() {
            let template = server.game_data.item_template(a.id).ok_or_else(null_ref)?;
            if a.count < 1 {
                a.count = 1;
            }
            if a.count > template.max_count {
                a.count = template.max_count;
            }
            let grade = template.fixed_grade.max(0);
            let mut item = server
                .items
                .create(
                    template.id,
                    a.count,
                    grade as u8,
                    server.config.id,
                    &server.game_data.items,
                    &server.ids.item,
                    Utc::now().timestamp(),
                )
                .ok_or_else(null_ref)?;
            item.owner_id = u64::from(character.id);
            item.slot_type = slot_type::MAIL;
            mail.attachments.push(item);
        }
        if hooks.send_mail(server, mail) {
            sended += 1;
        }
    }
    Ok(ok_json(&JValue::object([(
        "SendCount",
        JValue::from(sended),
    )])))
}

/// `ListMailResponseItem`.
fn list_item(m: &WebApiMailInfo) -> JValue {
    let date = |(d, k): (chrono::NaiveDateTime, _)| JValue::Date(d, k);
    JValue::object([
        ("Id", JValue::from(m.id)),
        ("SenderId", JValue::from(m.sender_id)),
        ("SenderName", JValue::from(m.sender_name.as_str())),
        ("ReceiverId", JValue::from(m.receiver_id)),
        ("ReceiverName", JValue::from(m.receiver_name.as_str())),
        ("IsDelivered", JValue::from(m.is_delivered)),
        ("Title", JValue::from(m.title.as_str())),
        ("MailType", JValue::from(m.mail_type)),
        ("SendDate", date(m.send_date)),
        ("ReceiveDate", date(m.receive_date)),
        ("OpenDate", date(m.open_date)),
        ("AttachmentCount", JValue::from(m.attachment_count)),
        ("CopperCoins", JValue::from(m.copper_coins)),
    ])
}

/// `MailController.List`.
pub async fn list_mails(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let character_id = match Stj::parse(&ctx.request.body).and_then(|v| match &v {
        Value::Null => Ok(None),
        Value::Object(_) => Stj::try_property(&v, "CharacterId")
            .map_or(Ok(0), Stj::get_u32)
            .map(Some),
        _ => Err(StjError("invalid".into())),
    }) {
        Ok(v) => v,
        Err(_) => return Ok(bad_request_json(&JValue::from("Invalid request."))),
    };
    let Some(character_id) = character_id.filter(|&id| id > 0) else {
        return Ok(bad_request_json(&JValue::from("Unknown character.")));
    };
    let server = ctx.server()?;
    let name = server.names.get_character_name(character_id);
    if name.as_deref().is_none_or(|n| n.trim().is_empty()) {
        return Ok(bad_request_json(&JValue::from("Unknown character.")));
    }
    let mails = server
        .systems
        .services
        .webapi_economy()
        .current_mail_list(server, character_id);
    let items = mails.iter().map(list_item).collect();
    Ok(ok_json(&JValue::object([(
        "mailItems",
        JValue::Array(items),
    )])))
}

/// `MailController.Delete`.
pub async fn delete_mail(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let parsed = Stj::parse(&ctx.request.body).and_then(|v| match &v {
        Value::Null => Ok(None),
        Value::Object(_) => {
            let get = |name: &str| Stj::try_property(&v, name);
            let mail_id = get("MailId").map_or(Ok(0), Stj::get_i64)?;
            let sender_id = get("SenderId").map_or(Ok(0), Stj::get_u32)?;
            let receiver_id = get("ReceiverId").map_or(Ok(0), Stj::get_u32)?;
            let trash = get("TrashItems").map_or(Ok(false), Stj::get_bool)?;
            Ok(Some((mail_id, sender_id, receiver_id, trash)))
        }
        _ => Err(StjError("invalid".into())),
    });
    let request = match parsed {
        Ok(r) => r,
        Err(_) => return Ok(bad_request_json(&JValue::from("Invalid request."))),
    };
    let Some((mail_id, sender_id, receiver_id, trash_items)) = request.filter(|r| r.0 > 0) else {
        return Ok(bad_request_json(&JValue::from("Unknown mail.")));
    };
    let server = ctx.server()?;
    let hooks = server.systems.services.webapi_economy();
    let mail = hooks.mail_by_id(server, mail_id);
    let Some(mail) = mail.filter(|m| m.sender_id == sender_id && m.receiver_id == receiver_id)
    else {
        return Ok(bad_request_json(&JValue::from("Verification failed.")));
    };
    if !hooks.delete_mail(server, mail.id, trash_items) {
        return Ok(bad_request_json(&JValue::object([
            ("message", JValue::from("Mail could not be deleted.")),
            ("mailId", JValue::from(mail.id)),
        ])));
    }
    Ok(ok_json(&JValue::object([
        ("message", JValue::from("Mail deleted.")),
        ("mailId", JValue::from(mail.id)),
    ])))
}

/// `MailController`.
pub struct MailController;

impl Controller for MailController {
    fn routes() -> Vec<RouteSpec> {
        vec![
            RouteSpec {
                method: Method::Post,
                path: "/api/mail/send",
                name: "Send",
                handler: handler(|ctx| async move { send(&ctx).await }),
            },
            RouteSpec {
                method: Method::Post,
                path: "/api/mail/list",
                name: "List",
                handler: handler(|ctx| async move { list_mails(&ctx).await }),
            },
            RouteSpec {
                method: Method::Post,
                path: "/api/mail/delete",
                name: "Delete",
                handler: handler(|ctx| async move { delete_mail(&ctx).await }),
            },
        ]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn send_mail_request_binding() {
        let r = SendMailRequest::parse(r#"{"Title":"t","Body":"b","Recipients":[1,2]}"#)
            .unwrap()
            .unwrap();
        assert_eq!(r.mail_type, MAIL_TYPE_SYS_EXPRESS);
        assert_eq!(r.sender_name.as_deref(), Some("GM"));
        assert_eq!(r.recipients, Some(vec![1, 2]));
        assert_eq!(r.attachment_items, Some(vec![]));
        // Case-sensitive member names: `title` is ignored.
        let r = SendMailRequest::parse(r#"{"title":"t"}"#).unwrap().unwrap();
        assert_eq!(r.title, None);
        assert_eq!(SendMailRequest::parse("null").unwrap(), None);
        assert!(SendMailRequest::parse(r#"{"Money":"5"}"#).is_err());
        let r = SendMailRequest::parse(r#"{"AttachmentItems":[{"Id":5},{"Id":6,"Count":3}]}"#)
            .unwrap()
            .unwrap();
        assert_eq!(
            r.attachment_items,
            Some(vec![
                AttachmentItem { id: 5, count: 1 },
                AttachmentItem { id: 6, count: 3 }
            ])
        );
    }
}
