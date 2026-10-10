//! `Scripts/Commands/InGameCashShop.cs`.

use futures::future::BoxFuture;

use super::prelude::*;
use crate::systems::economy::cash_shop;

/// `InGameCashShop`.
pub struct InGameCashShop;

impl InGameCashShop {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for InGameCashShop {
    fn command_names(&self) -> Vec<String> {
        names(&["ingamecashshop", "ics"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("InGameCashShop runs through execute_async")
    }

    fn execute_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        args: &'a [String],
        out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(async move {
            let server = ctx.server()?;
            let shop = &server.systems.economy.cash_shop;
            let do_command = args
                .first()
                .map_or_else(|| "list".to_string(), |a| a.to_lowercase());
            match do_command.as_str() {
                "list" => {
                    let (items, menu) = {
                        let st = shop.state();
                        (st.shop_items.len(), st.menu_items.len())
                    };
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!("Currently loaded {items} shop items listed as {menu} entries across all tabs in the cash shop."),
                    );
                }
                "on" => {
                    cash_shop::enable_shop(server);
                    CommandManager::send_normal_text(self, out, "Shop has been enabled");
                }
                "off" => {
                    cash_shop::disable_shop(server);
                    CommandManager::send_normal_text(self, out, "Shop has been disabled");
                }
                "reload" => {
                    if shop.enabled() {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            "First disable the shop before reloading it",
                        );
                    } else {
                        cash_shop::load_only(server).await?;
                        CommandManager::send_normal_text(self, out, "Items reloaded");
                    }
                }
                _ => CommandManager::send_error_text(
                    self,
                    out,
                    &format!("Unknown command: {do_command}"),
                ),
            }
            Ok(())
        })
    }

    fn get_command_line_help(&self) -> String {
        "<on||off||reload>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Enables or disables the InGameCashShop as well as allows to reloading of the data".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn ics_list_on_off_reload() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let shop = &server.systems.economy.cash_shop;
        let (_, out) = harness::run(&server, &gm.p, "ics").await;
        assert_eq!(
            out,
            ["[ingamecashshop] Currently loaded 0 shop items listed as 0 entries across all tabs in the cash shop."]
        );
        let (_, out) = harness::run(&server, &gm.p, "ics ON").await;
        assert_eq!(out, ["[ingamecashshop] Shop has been enabled"]);
        assert!(shop.enabled());
        let (_, out) = harness::run(&server, &gm.p, "ics reload").await;
        assert_eq!(
            out,
            ["[ingamecashshop] First disable the shop before reloading it"]
        );
        let (_, out) = harness::run(&server, &gm.p, "ingamecashshop off").await;
        assert_eq!(out, ["[ingamecashshop] Shop has been disabled"]);
        assert!(!shop.enabled());
        let (_, out) = harness::run(&server, &gm.p, "ics sell").await;
        assert_eq!(
            out,
            [harness::error("ingamecashshop", "Unknown command: sell")]
        );
    }
}
