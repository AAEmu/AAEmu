//! `Scripts/Commands/ReloadConfigs.cs` (+ `Program.ReloadConfiguration`).

use std::path::Path;

use super::super::sub::{apply_world_setting, WorldSetting};
use super::prelude::*;
use super::set_trade_pack_mail_delay::set_trade_pack_mail_delay;
use crate::config::AppConfig;

/// `ReloadConfigs`.
pub struct ReloadConfigs;

impl ReloadConfigs {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

/// The configuration part of the launch arguments (`Program._launchArgs` without the optional
/// leading application directory, as `main` splits them).
fn launch_config_args() -> Vec<String> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    match args.first() {
        Some(first)
            if !first.starts_with("--") && !first.contains('=') && Path::new(first).is_dir() =>
        {
            args[1..].to_vec()
        }
        _ => args,
    }
}

/// `Program.ReloadConfiguration()`: rebuilds the configuration from the JSON files, user
/// secrets, environment and launch arguments, and refreshes the copies the subsystems keep of
/// the world rates and the trade pack mail delay.
pub fn reload_configuration(server: &GameServer) -> anyhow::Result<()> {
    let app_path = server.config.app_path.clone();
    let config = AppConfig::load_with(&app_path, &launch_config_args())?;
    server.config.replace(config);
    let world = &server.config.world;
    for (setting, value) in [
        (WorldSetting::GrowthRate, world.growth_rate),
        (WorldSetting::VocationRate, world.vocation_rate),
        (WorldSetting::HonorRate, world.honor_rate),
    ] {
        apply_world_setting(server, setting, value as f32);
    }
    set_trade_pack_mail_delay(
        server,
        server.config.specialty.trade_pack_mail_delay_in_minutes,
    );
    Ok(())
}

impl Command for ReloadConfigs {
    fn command_names(&self) -> Vec<String> {
        names(&["reloadconfig", "reload_configs", "reload_configurations"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        match reload_configuration(ctx.server()?) {
            Ok(()) => CommandManager::send_normal_text(self, out, "Configuration reloaded"),
            Err(e) => {
                tracing::error!("{e}");
                CommandManager::send_error_text(
                    self,
                    out,
                    "Configurations failed reloading - check error output",
                );
            }
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Reloads the ConfigurationManager".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn reload_configs_reads_the_files_again() {
        let server = harness::server();
        let dir = std::env::temp_dir().join(format!("aaemu-reloadcfg-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(
            dir.join("Config.json"),
            r#"{ "World": { "ExpRate": 7, "GrowthRate": 3 }, "Specialty": { "TradePackMailDelayInMinutes": 5 } }"#,
        )
        .unwrap();
        server.config.update(|c| c.app_path = dir.clone());
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "reload_configs").await;
        assert_eq!(out, ["[reloadconfig] Configuration reloaded"]);
        assert_eq!(server.config.world.exp_rate, 7.0);
        assert_eq!(server.config.app_path, dir);
        assert_eq!(server.systems.npcs.doodads.growth_rate(), 3.0);
        assert_eq!(
            server.systems.economy.mail.trade_pack_mail_delay_minutes(),
            5.0
        );
        // A broken file: the error text.
        std::fs::write(dir.join("Config.json"), "{ nope").unwrap();
        let (_, out) = harness::run(&server, &gm.p, "reloadconfig").await;
        assert_eq!(
            out,
            [harness::error(
                "reloadconfig",
                "Configurations failed reloading - check error output"
            )]
        );
        let _ = std::fs::remove_dir_all(&dir);
    }
}
