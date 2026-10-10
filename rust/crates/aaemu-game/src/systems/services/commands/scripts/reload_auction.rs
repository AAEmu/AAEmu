//! `Scripts/Commands/ReloadAuction.cs`.

use futures::future::BoxFuture;

use super::prelude::*;

/// `ReloadAuction`.
pub struct ReloadAuction;

impl ReloadAuction {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ReloadAuction {
    fn command_names(&self) -> Vec<String> {
        names(&["reloadauction", "reload_auction", "reloadah", "reload_ah"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("ReloadAuction runs through execute_async")
    }

    fn execute_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        _args: &'a [String],
        _out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(async move {
            let server = ctx.server()?;
            // `AuctionManager.Instance.Load()` (nothing is reported back).
            server.systems.economy.auction.load(server).await;
            Ok(())
        })
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Reloads the AuctionManager".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn reload_auction_runs_the_loader() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        // No MySQL: `Load` logs the failure and keeps the (empty) lots.
        let (handled, out) = harness::run(&server, &gm.p, "reload_ah").await;
        assert!(handled);
        assert!(out.is_empty());
        assert!(server.systems.economy.auction.all_lots().is_empty());
    }
}
