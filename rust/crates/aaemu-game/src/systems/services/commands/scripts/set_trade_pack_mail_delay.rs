//! `Scripts/Commands/SetTradePackMailDelay.cs`.

use super::prelude::*;

/// `SetTradePackMailDelay`.
pub struct SetTradePackMailDelay;

impl SetTradePackMailDelay {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// `AppConfiguration.Instance.Specialty.TradePackMailDelayInMinutes = delay` (and the copy the
/// mail manager uses when it dates a trade pack mail).
pub fn set_trade_pack_mail_delay(server: &GameServer, delay: f64) {
    server
        .config
        .update(|c| c.specialty.trade_pack_mail_delay_in_minutes = delay);
    server
        .systems
        .economy
        .mail
        .set_trade_pack_mail_delay_minutes(delay);
}

impl Command for SetTradePackMailDelay {
    fn command_names(&self) -> Vec<String> {
        names(&["settradepackmaildelay"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let Some(first) = args.first() else {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        };
        if let Some(delay) = parse_f64(first) {
            set_trade_pack_mail_delay(ctx.server()?, delay);
            CommandManager::send_normal_text(
                self,
                out,
                &format!("Trade Pack Mail Delay set to {} minute(s)", f64s(delay)),
            );
            return Ok(());
        }
        CommandManager::send_error_text(self, out, &format!("Invalid delay: {first}"));
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<delay_in_minutes>".into()
    }

    fn get_command_help_text(&self) -> String {
        format!(
            "Changes the delay in receiving the trade pack reward mail to <delay> minute(s)\n\
             Example usage:\n\
             Receive the reward without delay: {COMMAND_PREFIX} settradepackmaildelay 0\n\
             Receive the reward in five minutes: {COMMAND_PREFIX} settradepackmaildelay 5\n\
             Receive the reward in 4 hours: {COMMAND_PREFIX} settradepackmaildelay 240\n"
        )
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn set_trade_pack_mail_delay() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "settradepackmaildelay 2.5").await;
        assert_eq!(
            out,
            ["[settradepackmaildelay] Trade Pack Mail Delay set to 2.5 minute(s)"]
        );
        assert_eq!(
            server.config.specialty.trade_pack_mail_delay_in_minutes,
            2.5
        );
        assert_eq!(
            server.systems.economy.mail.trade_pack_mail_delay_minutes(),
            2.5
        );
        let (_, out) = harness::run(&server, &gm.p, "settradepackmaildelay soon").await;
        assert_eq!(
            out,
            [harness::error(
                "settradepackmaildelay",
                "Invalid delay: soon"
            )]
        );
    }
}
