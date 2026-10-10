//! Minimal HTTP health endpoints (`/health/live`, `/health/ready`), served on the ports of the
//! `urls` setting (`ASPNETCORE_URLS`) like the C# `MapHealthChecks` when run under an
//! orchestrator such as Aspire. No checks are registered, so both report `Healthy`.

use std::net::SocketAddr;

use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};
use tokio_util::sync::CancellationToken;

/// Extracts the ports of a `;`-separated URL list (`http://+:5000;http://localhost:5001`).
pub fn ports_from_urls(urls: &str) -> Vec<u16> {
    urls.split(';')
        .filter_map(|u| {
            let u = u.trim();
            let rest = u.split_once("://").map_or(u, |(_, r)| r);
            let authority = rest.split('/').next()?;
            let default = if u.starts_with("https") { 443 } else { 80 };
            match authority.rsplit_once(':') {
                Some((_, p)) if !p.ends_with(']') => p.parse().ok(),
                _ => Some(default),
            }
        })
        .collect()
}

fn response_for(request: &str) -> &'static str {
    let path = request
        .lines()
        .next()
        .and_then(|l| l.split_whitespace().nth(1))
        .unwrap_or("");
    let path = path.split('?').next().unwrap_or("");
    match path {
        "/health/live" | "/health/ready" => {
            "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nCache-Control: no-store, no-cache\r\nContent-Length: 7\r\nConnection: close\r\n\r\nHealthy"
        }
        _ => "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
    }
}

async fn serve(mut stream: TcpStream) {
    let mut buf = Vec::with_capacity(1024);
    let mut chunk = [0u8; 1024];
    while !buf.windows(4).any(|w| w == b"\r\n\r\n") && buf.len() < 8192 {
        match stream.read(&mut chunk).await {
            Ok(0) | Err(_) => break,
            Ok(n) => buf.extend_from_slice(&chunk[..n]),
        }
    }
    let _ = stream
        .write_all(response_for(&String::from_utf8_lossy(&buf)).as_bytes())
        .await;
    let _ = stream.shutdown().await;
}

/// Serves health checks on `port` until `shutdown` is cancelled.
pub async fn run(port: u16, shutdown: CancellationToken) -> std::io::Result<()> {
    let listener = TcpListener::bind(SocketAddr::from(([0, 0, 0, 0], port))).await?;
    tracing::info!("Health checks listening on port {}", port);
    loop {
        tokio::select! {
            _ = shutdown.cancelled() => return Ok(()),
            accepted = listener.accept() => {
                if let Ok((stream, _)) = accepted {
                    tokio::spawn(serve(stream));
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_urls() {
        assert_eq!(
            ports_from_urls("http://+:5000;https://localhost:5001/"),
            vec![5000, 5001]
        );
        assert_eq!(ports_from_urls("http://example"), vec![80]);
    }

    #[test]
    fn routes() {
        assert!(response_for("GET /health/live HTTP/1.1\r\n\r\n").starts_with("HTTP/1.1 200"));
        assert!(response_for("GET /health/ready?x=1 HTTP/1.1\r\n\r\n").starts_with("HTTP/1.1 200"));
        assert!(response_for("GET / HTTP/1.1\r\n\r\n").starts_with("HTTP/1.1 404"));
    }
}
