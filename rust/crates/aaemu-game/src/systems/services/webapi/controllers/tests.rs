//! Controller behaviour through `WebApiSession.OnReceivedRequest` with a test server (no
//! MySQL: the DB-backed paths are covered where they fail before the first query).

use std::sync::Arc;

use super::super::http::HttpRequest;
use super::super::{on_received_request, WebApiServer};
use super::*;
use crate::test_support::{spawn_player, test_character, test_server, TestClient};

fn api(server: &Arc<crate::GameServer>) -> WebApiServer {
    WebApiServer::new(Arc::downgrade(server)).unwrap()
}

async fn get(api: &WebApiServer, url: &str) -> HttpResponse {
    on_received_request(api, HttpRequest::new("GET", url, "HTTP/1.1")).await
}

async fn post(api: &WebApiServer, url: &str, body: &str) -> HttpResponse {
    on_received_request(
        api,
        HttpRequest::new("POST", url, "HTTP/1.1").with_body(body),
    )
    .await
}

#[test]
fn timespan_format() {
    assert_eq!(format_timespan(0), "00:00:00");
    assert_eq!(format_timespan(3_725), "01:02:05");
    assert_eq!(format_timespan(90_061), "1.01:01:01");
}

#[tokio::test]
async fn status_world_and_expeditions() {
    let server = Arc::new(test_server());
    server.set_self();
    let client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Bob", 10.0, 10.0);
    c.created = 1_700_000_000;
    c.is_online = true;
    spawn_player(&server, &client, c, 0x100);
    let api = api(&server);

    let r = get(&api, "/status").await;
    assert_eq!(r.status, 200);
    assert_eq!(r.content_type(), Some("text/html"));
    assert!(r.body.starts_with("Server uptime: "));
    assert!(r
        .body
        .ends_with("<br/>Players online: 1<br/>Number of TaskManager Jobs: 0<br/>"));

    let r = get(&api, "/api/world/logged-characters").await;
    assert_eq!(r.content_type(), Some("application/json"));
    assert_eq!(
        r.body,
        r#"[{"Id":1,"Name":"Bob","Level":10,"CreatedAt":"2023-11-14T22:13:20","IsOnline":true}]"#
    );

    let r = get(&api, "/api/expedition/list").await;
    assert_eq!((r.status, r.body.as_str()), (200, "[]"));
}

#[tokio::test]
async fn command_controller_validation_and_dispatch() {
    let server = Arc::new(test_server());
    server.set_self();
    let api = api(&server);
    // Missing body -> System.Text.Json exception -> 500 page.
    let r = post(&api, "/api/commands/kick", "").await;
    assert_eq!(r.status, 500);
    assert!(r
        .body
        .contains("The input does not contain any JSON tokens"));
    // Missing property -> KeyNotFoundException -> 500.
    let r = post(&api, "/api/commands/kick", r#"{"character":"Bob"}"#).await;
    assert_eq!(r.status, 500);
    let r = post(
        &api,
        "/api/commands/kick",
        r#"{"character":" ","arguments":"x"}"#,
    )
    .await;
    assert_eq!(r.status, 400);
    assert_eq!(
        r.body,
        r#"{"Message":"Character name is required","StackTrace":null}"#
    );
    let r = post(
        &api,
        "/api/commands/kick",
        r#"{"character":"Bob","arguments":null}"#,
    )
    .await;
    assert_eq!(
        r.body,
        r#"{"Message":"Command line is required","StackTrace":null}"#
    );
    let r = post(
        &api,
        "/api/commands/kick",
        r#"{"character":"Bob","arguments":"x"}"#,
    )
    .await;
    assert_eq!(
        r.body,
        r#"{"Message":"Character \"Bob\" not found","StackTrace":null}"#
    );
    // GET is not a route.
    assert_eq!(get(&api, "/api/commands/kick").await.status, 404);
}

#[tokio::test]
async fn mail_controller_validation() {
    let server = Arc::new(test_server());
    server.set_self();
    let api = api(&server);
    let bad = |m: &str| format!(r#"{{"Message":"{m}","StackTrace":null}}"#);
    let r = post(&api, "/api/mail/send", "null").await;
    assert_eq!(
        (r.status, r.body.clone()),
        (400, bad("Invalid mail request"))
    );
    let r = post(&api, "/api/mail/send", r#"{"Body":"b"}"#).await;
    assert_eq!(r.body, bad("Email Title cannot be empty."));
    let r = post(&api, "/api/mail/send", r#"{"Title":"t"}"#).await;
    assert_eq!(r.body, bad("Email body cannot be empty."));
    let r = post(
        &api,
        "/api/mail/send",
        r#"{"Title":"t","Body":"b","Money":-1}"#,
    )
    .await;
    assert_eq!(r.body, bad("Money cannot be less than 0."));
    let r = post(
        &api,
        "/api/mail/send",
        r#"{"Title":"t","Body":"b","Billing":-1}"#,
    )
    .await;
    assert_eq!(r.body, bad("Billing cannot be less than 0."));
    let r = post(
        &api,
        "/api/mail/send",
        r#"{"Title":"t","Body":"b","RecipientType":9}"#,
    )
    .await;
    assert_eq!(r.body, bad("Invalid recipient type."));
    // Online recipients with nobody online.
    let r = post(
        &api,
        "/api/mail/send",
        r#"{"Title":"t","Body":"b","RecipientType":3}"#,
    )
    .await;
    assert_eq!(r.body, bad("Recipients is empty."));
    let r = post(
        &api,
        "/api/mail/send",
        r#"{"Title":"t","Body":"b","RecipientType":2,"Recipients":[5]}"#,
    )
    .await;
    assert_eq!(r.body, bad("Invalid FamilyId: 5 ."));
    let r = post(
        &api,
        "/api/mail/send",
        r#"{"Title":"t","Body":"b","RecipientType":1,"Recipients":[5]}"#,
    )
    .await;
    assert_eq!(r.body, bad("Invalid ExpeditionId: 5 ."));

    // List / delete: plain JSON strings.
    let r = post(&api, "/api/mail/list", "x").await;
    assert_eq!((r.status, r.body.as_str()), (400, "\"Invalid request.\""));
    let r = post(&api, "/api/mail/list", r#"{"CharacterId":0}"#).await;
    assert_eq!(r.body, "\"Unknown character.\"");
    let r = post(&api, "/api/mail/list", r#"{"CharacterId":7}"#).await;
    assert_eq!(r.body, "\"Unknown character.\"");
    let r = post(&api, "/api/mail/delete", r#"{"MailId":0}"#).await;
    assert_eq!(r.body, "\"Unknown mail.\"");
    let r = post(&api, "/api/mail/delete", r#"{"MailId":3}"#).await;
    assert_eq!(r.body, "\"Verification failed.\"");
}

#[tokio::test]
async fn mail_send_to_online_characters_goes_through_the_hook() {
    use std::sync::Mutex;

    use crate::systems::services::webapi::hooks::{WebApiEconomyHooks, WebApiMail};

    #[derive(Default)]
    struct Sent(Mutex<Vec<WebApiMail>>);
    impl WebApiEconomyHooks for Sent {
        fn send_mail(&self, _server: &crate::GameServer, mail: WebApiMail) -> bool {
            self.0.lock().unwrap().push(mail);
            true
        }
    }

    let server = Arc::new(test_server());
    server.set_self();
    let sent = Arc::new(Sent::default());
    server
        .systems
        .services
        .webapi_economy_hooks
        .install(sent.clone());
    let client = TestClient::new(&server, 1, 1);
    spawn_player(
        &server,
        &client,
        test_character(1, "Bob", 10.0, 10.0),
        0x100,
    );
    let api = api(&server);
    let r = post(
        &api,
        "/api/mail/send",
        r#"{"Title":"t","Body":"b","RecipientType":3,"Money":5,"SenderName":" "}"#,
    )
    .await;
    assert_eq!((r.status, r.body.as_str()), (200, r#"{"SendCount":1}"#));
    let mails = sent.0.lock().unwrap();
    assert_eq!(mails.len(), 1);
    assert_eq!(mails[0].receiver_name, "Bob");
    assert_eq!(mails[0].sender_name, "GM");
    assert_eq!(mails[0].copper_coins, 5);
    assert_eq!(mails[0].mail_type, 18);
}

#[tokio::test]
async fn auction_controller_without_economy() {
    let server = Arc::new(test_server());
    server.set_self();
    let api = api(&server);
    let r = post(&api, "/api/auction/add", r#"{"ItemId":1}"#).await;
    assert_eq!(
        (r.status, r.body.as_str()),
        (400, r#"{"error":"Invalid parameters"}"#)
    );
    let r = post(
        &api,
        "/api/auction/add",
        r#"{"ItemId":1,"Quantity":1,"Price":1,"Duration":0,"ClientId":1,"ClientName":"x"}"#,
    )
    .await;
    assert_eq!(
        r.body,
        r#"{"error":"Internal server error","details":"Item not found!"}"#
    );
    let r = get(&api, "/api/auction/list").await;
    assert_eq!(
        r.body,
        r#"{"error":"Internal server error","details":"AuctionManager is not available"}"#
    );
    // Wrong JSON types outside the try -> 500.
    let r = post(
        &api,
        "/api/auction/add",
        r#"{"ItemId":"1","Quantity":1,"Price":1,"Duration":0,"ClientId":1,"ClientName":"x"}"#,
    )
    .await;
    assert_eq!(r.status, 500);
}
