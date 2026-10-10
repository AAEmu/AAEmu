//! Port of `AAEmu.UnitTests/Services/WebApi/WebApiSessionTests.cs` (+ the 500 page and the
//! HTTP transport).

use std::sync::Weak;

use super::http::{ok_html, HttpRequest};
use super::route_mapper::{handler, Controller, Method, RouteMapper, RouteSpec};
use super::*;

struct MyRegexController;

impl Controller for MyRegexController {
    fn routes() -> Vec<RouteSpec> {
        vec![
            RouteSpec {
                method: Method::Get,
                path: "/world/(.+)",
                name: "GetCharacter",
                handler: handler(|_| async { Ok(ok_html("world")) }),
            },
            RouteSpec {
                method: Method::Post,
                path: "/world/(.+)",
                name: "GetCharacterPost",
                handler: handler(|_| async { Ok(ok_html("world-post")) }),
            },
            RouteSpec {
                method: Method::Get,
                path: "/test/(.+)",
                name: "GetCharacter",
                handler: handler(|_| async { Ok(ok_html("test")) }),
            },
            RouteSpec {
                method: Method::Post,
                path: "/test/(.+)",
                name: "GetCharacterPost",
                handler: handler(|_| async { Ok(ok_html("test-post")) }),
            },
            RouteSpec {
                method: Method::Post,
                path: "/multipleMatches/([^/]+)/([^/]+)",
                name: "GetComplexRegexPost",
                handler: handler(|ctx| async move {
                    let mut html = "test-post".to_string();
                    for m in &ctx.matches {
                        for g in &m.groups {
                            html += &format!("\ngroup: name({}) value({})", g.name, g.value);
                        }
                    }
                    Ok(ok_html(&html))
                }),
            },
            RouteSpec {
                method: Method::Get,
                path: "/fail",
                name: "Fail",
                handler: handler(|_| async { Err(anyhow::anyhow!("boom")) }),
            },
        ]
    }
}

fn server() -> WebApiServer {
    let mut mapper = RouteMapper::new();
    mapper
        .discover_routes_from_type::<MyRegexController>()
        .unwrap();
    WebApiServer::with_routes(mapper, Weak::new())
}

#[tokio::test]
async fn on_received_request_when_route_not_found_should_return_404() {
    let r = on_received_request(&server(), HttpRequest::new("GET", "/not-found", "HTTP/1.1")).await;
    assert_eq!(r.status, 404);
    assert_eq!(r.status_phrase, "Not Found");
    assert_eq!(r.body, "Not found");
    assert_eq!(r.content_type(), None);
}

#[tokio::test]
async fn on_received_request_when_route_found_should_return_html() {
    for (method, path, expected) in [
        ("GET", "/world/1", "world"),
        ("GET", "/world/fdsf", "world"),
        ("GET", "/world/fdsf/any/193", "world"),
        ("POST", "/world/1", "world-post"),
        ("POST", "/world/e1", "world-post"),
        ("POST", "/world/rr/1", "world-post"),
        ("GET", "/test/1", "test"),
        ("POST", "/test/1", "test-post"),
    ] {
        let r = on_received_request(&server(), HttpRequest::new(method, path, "HTTP/1.1")).await;
        assert_eq!(r.status, 200, "{method} {path}");
        assert_eq!(r.status_phrase, "OK");
        assert_eq!(r.content_type(), Some("text/html"));
        assert_eq!(r.body, expected);
    }
}

#[tokio::test]
async fn on_received_request_when_route_found_with_regex_should_return_html_with_matches() {
    for (method, path, expected_matches) in [
        (
            "POST",
            "/multipleMatches/resource/subresource",
            ["resource", "subresource"],
        ),
        (
            "POST",
            "/multipleMatches/players/search",
            ["players", "search"],
        ),
    ] {
        let r = on_received_request(&server(), HttpRequest::new(method, path, "HTTP/1.1")).await;
        assert_eq!(r.status, 200);
        assert_eq!(r.status_phrase, "OK");
        assert_eq!(r.content_type(), Some("text/html"));
        let mut expected = "test-post".to_string();
        expected += &format!("\ngroup: name(0) value({path})");
        for (i, m) in expected_matches.iter().enumerate() {
            expected += &format!("\ngroup: name({}) value({m})", i + 1);
        }
        assert_eq!(r.body, expected);
    }
}

#[tokio::test]
async fn controller_errors_produce_the_500_page() {
    let r = on_received_request(&server(), HttpRequest::new("GET", "/fail", "HTTP/1.1")).await;
    assert_eq!(r.status, 500);
    assert_eq!(r.status_phrase, "Internal Server Error");
    assert_eq!(r.content_type(), Some("text/html"));
    assert_eq!(
        r.body,
        "<h1>Internal server error</h1>\n                   <h3>Error:boom</h3>\n                   <h2>Stack trace:</h3>\n                   <pre></pre>"
    );
}

#[tokio::test]
async fn ambiguous_routes_produce_the_500_page() {
    let mut mapper = RouteMapper::new();
    mapper
        .discover_routes_from_type::<MyRegexController>()
        .unwrap();
    mapper
        .register_route(
            "/world/1",
            Method::Get,
            "Other",
            handler(|_| async { Ok(ok_html("x")) }),
        )
        .unwrap();
    let api = WebApiServer::with_routes(mapper, Weak::new());
    let r = on_received_request(&api, HttpRequest::new("GET", "/world/1", "HTTP/1.1")).await;
    assert_eq!(r.status, 500);
    assert!(r.body.contains("Multiple routes found for GET /world/1"));
}

#[tokio::test]
async fn served_over_http() {
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    let api = Arc::new(server());
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let addr = listener.local_addr().unwrap();
    let app = axum::Router::new().fallback(dispatch).with_state(api);
    let task = tokio::spawn(async move {
        axum::serve(listener, app).await.unwrap();
    });
    let mut s = tokio::net::TcpStream::connect(addr).await.unwrap();
    s.write_all(
        b"POST /multipleMatches/a/b?x=1 HTTP/1.1\r\nHost: x\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
    )
    .await
    .unwrap();
    let mut buf = String::new();
    s.read_to_string(&mut buf).await.unwrap();
    assert!(buf.starts_with("HTTP/1.1 200 OK"), "{buf}");
    assert!(buf.to_ascii_lowercase().contains("content-type: text/html"));
    assert!(buf.ends_with("group: name(2) value(b?x=1)"), "{buf}");
    task.abort();
}
