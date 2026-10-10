//! `Services/WebApi/Controllers` and `Services/WebApi/Models`.

mod auction;
mod mail;

pub use auction::AuctionController;
pub use mail::MailController;

use chrono::NaiveDateTime;
use sqlx::Row;

use super::http::{bad_request_json, error_model, ok_html, ok_json, HttpResponse, QueryString};
use super::json::{DateKind, JValue, Stj};
use super::route_mapper::{handler, Controller, ControllerCtx, Method, RouteSpec};
use crate::systems::services::commands::output::MessageOutput;
use crate::systems::services::game_service;

/// `CharacterModel(Id, Name, Level, CreatedAt, IsOnline)`.
pub fn character_model(
    id: u32,
    name: &str,
    level: u32,
    created_at: JValue,
    online: bool,
) -> JValue {
    JValue::object([
        ("Id", JValue::from(id)),
        ("Name", JValue::from(name)),
        ("Level", JValue::from(level)),
        ("CreatedAt", created_at),
        ("IsOnline", JValue::from(online)),
    ])
}

/// A `DateTime` read from MySQL / kept as unix seconds (`DateTimeKind.Unspecified`;
/// 0 = `DateTime.MinValue`).
pub fn db_date(unix: i64) -> JValue {
    JValue::Date(crate::db::from_unix(unix), DateKind::Unspecified)
}

/// `TimeSpan.ToString()` (`c` format): `[-][d.]hh:mm:ss`.
pub fn format_timespan(total_secs: i64) -> String {
    let neg = total_secs < 0;
    let s = total_secs.unsigned_abs();
    let (d, h, m, sec) = (s / 86_400, (s / 3600) % 24, (s / 60) % 60, s % 60);
    let mut out = String::new();
    if neg {
        out.push('-');
    }
    if d > 0 {
        out.push_str(&format!("{d}."));
    }
    out.push_str(&format!("{h:02}:{m:02}:{sec:02}"));
    out
}

/// `StatusController`.
pub struct StatusController;

impl Controller for StatusController {
    fn routes() -> Vec<RouteSpec> {
        vec![RouteSpec {
            method: Method::Get,
            path: "/status",
            name: "GetStatus",
            handler: handler(|ctx| async move { get_status(&ctx) }),
        }]
    }
}

/// `GetStatus`.
fn get_status(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let server = ctx.server()?;
    let player_count = server.world.all_characters().len();
    let uptime = format_timespan(i64::from(game_service::up_time()));
    let jobs = server.systems.services.tasks.get_queue_count();
    Ok(ok_html(&format!(
        "Server uptime: {uptime}<br/>Players online: {player_count}<br/>Number of TaskManager Jobs: {jobs}<br/>"
    )))
}

/// `WorldController`.
pub struct WorldController;

impl Controller for WorldController {
    fn routes() -> Vec<RouteSpec> {
        vec![RouteSpec {
            method: Method::Get,
            path: "/api/world/logged-characters",
            name: "GetCharacter",
            handler: handler(|ctx| async move {
                let server = ctx.server()?;
                let list = server
                    .world
                    .all_characters()
                    .iter()
                    .map(|p| {
                        let c = p.data();
                        character_model(
                            c.id,
                            &c.unit.name,
                            u32::from(c.unit.level),
                            db_date(c.created),
                            c.is_online,
                        )
                    })
                    .collect();
                Ok(ok_json(&JValue::Array(list)))
            }),
        }]
    }
}

/// `ExpeditionController` (list of `ExpeditionModel`).
pub struct ExpeditionController;

impl Controller for ExpeditionController {
    fn routes() -> Vec<RouteSpec> {
        vec![RouteSpec {
            method: Method::Get,
            path: "/api/expedition/list",
            name: "List",
            handler: handler(|ctx| async move {
                let server = ctx.server()?;
                let list = server
                    .expeditions
                    .all()
                    .iter()
                    .map(|e| {
                        let f = &e.faction;
                        JValue::object([
                            ("Id", JValue::from(f.id)),
                            ("Name", JValue::from(f.name.as_str())),
                            ("Owner", JValue::from(f.owner_id)),
                            ("OwnerName", JValue::from(f.owner_name.as_str())),
                            ("Mother", JValue::from(f.mother_id)),
                            ("CreatedAt", db_date(f.created)),
                            ("MemberCount", JValue::from(e.members.len() as u32)),
                            (
                                "OnlineCount",
                                JValue::from(
                                    e.members.iter().filter(|m| m.is_online).count() as u32
                                ),
                            ),
                        ])
                    })
                    .collect();
                Ok(ok_json(&JValue::Array(list)))
            }),
        }]
    }
}

/// `CharacterController`.
pub struct CharacterController;

impl Controller for CharacterController {
    fn routes() -> Vec<RouteSpec> {
        vec![RouteSpec {
            method: Method::Get,
            path: "/api/character/list",
            name: "List",
            handler: handler(|ctx| async move { character_list(&ctx).await }),
        }]
    }
}

/// `CharacterController.List`: characters from MySQL (optional `AccountId` / `Name` query
/// filters), online level / status from the world.
async fn character_list(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let server = ctx.server()?;
    let query = QueryString::from_url(&ctx.request.url);
    let mut sql = String::from(
        "SELECT CAST(`id` AS SIGNED) AS id, `name`, CAST(`level` AS SIGNED) AS level, `created_at`, `account_id` FROM `characters` WHERE `deleted` = 0",
    );
    let account_id = query.get("AccountId");
    if account_id.is_some() {
        sql.push_str(" AND `account_id` = ?");
    }
    let name = query.get("Name");
    if name.is_some() {
        sql.push_str(" AND `name` = ?");
    }
    let mut q = sqlx::query(&sql);
    if let Some(a) = &account_id {
        q = q.bind(a);
    }
    if let Some(n) = &name {
        q = q.bind(n);
    }
    let rows = q.fetch_all(&server.db).await?;
    let mut list = Vec::with_capacity(rows.len());
    for r in rows {
        let id = r.try_get::<i64, _>("id")? as u32;
        let char_name: String = r.try_get("name")?;
        let created_at: NaiveDateTime = r
            .try_get::<Option<NaiveDateTime>, _>("created_at")
            .ok()
            .flatten()
            .unwrap_or_else(crate::db::min_value);
        let character = server.world.character_by_id(id);
        let mut level = r.try_get::<i64, _>("level")? as u32;
        if let Some(c) = &character {
            level = u32::from(c.data().unit.level);
        }
        list.push(character_model(
            id,
            &char_name,
            level,
            JValue::Date(created_at, DateKind::Unspecified),
            character.is_some(),
        ));
    }
    Ok(ok_json(&JValue::Array(list)))
}

/// `CommandController`.
pub struct CommandController;

impl Controller for CommandController {
    fn routes() -> Vec<RouteSpec> {
        vec![RouteSpec {
            method: Method::Post,
            path: "/api/commands/([^/]+)",
            name: "ExecuteCommand",
            handler: handler(|ctx| async move { execute_command(&ctx).await }),
        }]
    }
}

fn is_null_or_white_space(s: Option<&str>) -> bool {
    s.is_none_or(|s| s.trim().is_empty())
}

/// `CommandController.ExecuteCommand`.
async fn execute_command(ctx: &ControllerCtx) -> anyhow::Result<HttpResponse> {
    let command_name = ctx.matches.first().map_or("", |m| m.group(1)).to_string();
    if command_name.trim().is_empty() {
        return Ok(bad_request_json(&error_model("Command name is required")));
    }
    let json_body = Stj::parse(&ctx.request.body)?;
    let command_character = Stj::get_string(Stj::property(&json_body, "character")?)?;
    if is_null_or_white_space(command_character.as_deref()) {
        return Ok(bad_request_json(&error_model("Character name is required")));
    }
    let command_arguments = Stj::get_string(Stj::property(&json_body, "arguments")?)?;
    if is_null_or_white_space(command_arguments.as_deref()) {
        return Ok(bad_request_json(&error_model("Command line is required")));
    }
    let command_character = command_character.unwrap_or_default();
    let command_line = format!("{command_name} {}", command_arguments.unwrap_or_default());
    let server = ctx.server()?;
    let Some(character) = server.world.character_by_name(&command_character) else {
        return Ok(bad_request_json(&error_model(format!(
            "Character \"{command_character}\" not found"
        ))));
    };
    let (_, output) = server
        .systems
        .services
        .commands
        .handle(server, &character, &command_line)
        .await;
    let strings =
        |v: &[String]| JValue::Array(v.iter().map(|s| JValue::from(s.as_str())).collect());
    Ok(ok_json(&JValue::object([
        ("commandLine", JValue::from(command_line.as_str())),
        ("commandCharacter", JValue::from(command_character.as_str())),
        ("Messages", strings(output.messages())),
        ("ErrorMessages", strings(output.error_messages())),
    ])))
}

#[cfg(test)]
mod tests;
