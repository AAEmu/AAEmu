//! `Services/WebApi`: the HTTP API of the game server (`WebApiService`, `WebApiServer`,
//! `WebApiSession`, `RouteMapper`, the controllers and their JSON models).
//!
//! The transport is axum (HTTP/1.1); every request goes through [`WebApiSession`]
//! (`OnReceivedRequest`), which keeps the C# routing (regex routes on the raw URL), status
//! codes and bodies.

pub mod controllers;
pub mod hooks;
pub mod http;
pub mod json;
pub mod route_mapper;

use std::net::{IpAddr, Ipv4Addr, SocketAddr};
use std::sync::{Arc, Mutex, Weak};

use axum::body::{Body, Bytes};
use axum::extract::State;
use axum::response::Response;
use tokio_util::sync::CancellationToken;

use self::http::{HttpRequest, HttpResponse};
use self::route_mapper::{ControllerCtx, RouteMapper};
use crate::config::NetworkConfig;
use crate::server::GameServer;

/// `WebApiServer`: the routes plus the server the controllers reach.
pub struct WebApiServer {
    pub route_mapper: RouteMapper,
    server: Weak<GameServer>,
}

impl WebApiServer {
    /// `new WebApiServer(address, port)`: discovers every controller route.
    pub fn new(server: Weak<GameServer>) -> Result<Self, route_mapper::RouteError> {
        let mut route_mapper = RouteMapper::new();
        route_mapper.discover_routes()?;
        Ok(Self {
            route_mapper,
            server,
        })
    }

    /// `new WebApiServer(address, port, routeMapper)`.
    pub fn with_routes(route_mapper: RouteMapper, server: Weak<GameServer>) -> Self {
        Self {
            route_mapper,
            server,
        }
    }
}

/// `WebApiSession.OnReceivedRequest(request)`: routes the request and returns the response
/// (`InternalSendResponseAsync` sends it).
pub async fn on_received_request(server: &WebApiServer, request: HttpRequest) -> HttpResponse {
    let result = async {
        let found = server
            .route_mapper
            .get_route(&request.url, &request.method)?;
        let Some((route, matches)) = found else {
            let mut response = HttpResponse::new(404);
            response.set_body("Not found");
            return Ok(response);
        };
        let target = route.target.clone();
        let ctx = ControllerCtx {
            server: server.server.upgrade(),
            request: request.clone(),
            matches,
        };
        target(ctx).await
    }
    .await;
    match result {
        Ok(r) => r,
        Err(e) => {
            tracing::error!("{e:?}");
            internal_server_error(&e)
        }
    }
}

/// The 500 page of `OnReceivedRequest`'s `catch` (the stack trace is the error chain).
pub fn internal_server_error(e: &anyhow::Error) -> HttpResponse {
    let mut response = HttpResponse::new(500);
    response.set_content_type("text/html");
    let trace: Vec<String> = e.chain().skip(1).map(|c| format!("   at {c}")).collect();
    let html = format!(
        "<h1>Internal server error</h1>\n                   <h3>Error:{}</h3>\n                   <h2>Stack trace:</h3>\n                   <pre>{}</pre>",
        e,
        trace.join("\n")
    );
    response.set_body(html);
    response
}

/// Converts a response for axum (status, headers, body; `Content-Length` is added by hyper).
fn to_axum(r: HttpResponse) -> Response {
    let mut builder = Response::builder().status(r.status);
    for (k, v) in &r.headers {
        builder = builder.header(k.as_str(), v.as_str());
    }
    builder
        .body(Body::from(r.body))
        .unwrap_or_else(|_| Response::new(Body::empty()))
}

async fn dispatch(
    State(server): State<Arc<WebApiServer>>,
    req: axum::extract::Request,
) -> Response {
    let (parts, body) = req.into_parts();
    let url = parts
        .uri
        .path_and_query()
        .map_or_else(|| parts.uri.path().to_string(), |p| p.as_str().to_string());
    let bytes: Bytes = axum::body::to_bytes(body, 64 * 1024 * 1024)
        .await
        .unwrap_or_default();
    let request = HttpRequest {
        method: parts.method.as_str().to_string(),
        url,
        protocol: format!("{:?}", parts.version),
        body: String::from_utf8_lossy(&bytes).into_owned(),
    };
    to_axum(on_received_request(&server, request).await)
}

/// `WebApiService` (`IHostedService`): starts / stops the HTTP listener.
#[derive(Default)]
pub struct WebApiService {
    running: Mutex<Option<(CancellationToken, tokio::task::JoinHandle<()>)>>,
}

impl std::fmt::Debug for WebApiService {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("WebApiService")
            .field("started", &self.is_started())
            .finish()
    }
}

/// `config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host)`.
pub fn bind_address(config: &NetworkConfig) -> anyhow::Result<SocketAddr> {
    let ip: IpAddr = if config.host == "*" {
        IpAddr::V4(Ipv4Addr::UNSPECIFIED)
    } else {
        config
            .host
            .parse()
            .map_err(|_| anyhow::anyhow!("An invalid IP address was specified."))?
    };
    Ok(SocketAddr::new(ip, config.port))
}

impl WebApiService {
    /// `StartAsync`: nothing without a `WebApiNetwork` section.
    pub async fn start(&self, server: &Arc<GameServer>) -> anyhow::Result<()> {
        let Some(config) = server.config.web_api_network.clone() else {
            tracing::warn!("WebApi server configuration not found. WebApi will not start");
            return Ok(());
        };
        let addr = bind_address(&config)?;
        let api = Arc::new(WebApiServer::new(Arc::downgrade(server))?);
        let listener = match tokio::net::TcpListener::bind(addr).await {
            Ok(l) => l,
            Err(e) => {
                // NetCoreServer reports socket errors through OnError and keeps running.
                tracing::warn!("WebApi server caught an error with code {e}");
                return Ok(());
            }
        };
        let token = CancellationToken::new();
        let stop = token.clone();
        let app = axum::Router::new().fallback(dispatch).with_state(api);
        let task = tokio::spawn(async move {
            let serve = axum::serve(listener, app).with_graceful_shutdown(async move {
                stop.cancelled().await;
            });
            if let Err(e) = serve.await {
                tracing::warn!("WebApi server caught an error with code {e}");
            }
        });
        *self.running.lock().expect("webapi poisoned") = Some((token, task));
        tracing::info!("WebApi server started on {}:{}", config.host, config.port);
        Ok(())
    }

    /// `StopAsync`.
    pub async fn stop(&self) {
        let running = self.running.lock().expect("webapi poisoned").take();
        if let Some((token, task)) = running {
            token.cancel();
            let _ = task.await;
        }
        tracing::info!("WebApi server stopped");
    }

    /// `_server?.IsStarted`.
    pub fn is_started(&self) -> bool {
        self.running.lock().map(|r| r.is_some()).unwrap_or(false)
    }
}

#[cfg(test)]
mod session_tests;
