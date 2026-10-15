//! `Scripts/Commands/Scripts.cs`.

use std::time::Duration;

use chrono::{TimeDelta, Utc};
use futures::future::BoxFuture;

use super::prelude::*;
use crate::systems::services::shutdown::{SCNoticeMessagePacket, ShutdownTask};

/// `System.Drawing.Color.Aqua`.
const AQUA: Color = Color::argb(255, 0, 255, 255);
/// `System.Drawing.Color.Magenta`.
const MAGENTA: Color = Color::argb(255, 255, 0, 255);

/// `Scripts`.
pub struct Scripts;

impl Scripts {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }

    /// `case "shutdown"`.
    async fn shutdown(
        &self,
        server: &GameServer,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let now = Utc::now().naive_utc();
        let mut shutdown_time = now + TimeDelta::minutes(30);
        let mut do_cancel = false;
        if let Some(a) = args.get(1) {
            if a.eq_ignore_ascii_case("cancel") {
                do_cancel = true;
            } else if a.eq_ignore_ascii_case("now") {
                shutdown_time = Utc::now().naive_utc();
            } else if let Some(minutes) = try_parse::<u32>(a) {
                shutdown_time = Utc::now().naive_utc() + TimeDelta::minutes(i64::from(minutes));
            }
        }
        let remaining = shutdown_time - Utc::now().naive_utc();
        let remaining_minutes = fixed(
            remaining.num_microseconds().unwrap_or(i64::MAX) as f64 / 60_000_000.0,
            0,
        );
        let services = &server.systems.services;
        let current = services
            .shutdown_task
            .lock()
            .expect("shutdown task poisoned")
            .clone();
        if do_cancel && server.is_shutting_down() {
            // Deviation: T-0 passed, the players are already being returned to the world list.
            CommandManager::send_error_text(
                self,
                out,
                "Too late to cancel, the server is shutting down right now.",
            );
            return Ok(());
        }
        if let (Some(task), true) = (&current, do_cancel) {
            services.tasks.cancel_task(task.as_ref());
            CommandManager::send_normal_text(self, out, "Shutdown cancelled.");
            server
                .world
                .broadcast_to_server(&SCNoticeMessagePacket::new(
                    3,
                    AQUA,
                    10000,
                    "The server shutdown has been cancelled!",
                ));
            *services
                .shutdown_task
                .lock()
                .expect("shutdown task poisoned") = None;
            return Ok(());
        }
        if do_cancel {
            CommandManager::send_error_text(self, out, "No shutdown to cancel");
            return Ok(());
        }
        if let Some(task) = &current {
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "Shutdown was already in progress, changed to {remaining_minutes} minutes from now."
                ),
            );
            task.change_shutdown_time(shutdown_time + TimeDelta::seconds(1));
            return Ok(());
        }
        if server.save.do_save(server).await {
            let arc = server.self_arc().ok_or_else(|| {
                anyhow::anyhow!("Object reference not set to an instance of an object.")
            })?;
            // Manual Normal Shutdown (-1)
            let task = ShutdownTask::new(&arc, shutdown_time + TimeDelta::seconds(5), -1);
            *services
                .shutdown_task
                .lock()
                .expect("shutdown task poisoned") = Some(task.clone());
            services
                .tasks
                .schedule(task, None, Some(Duration::from_secs(1)), -1);
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "Shutdown sequence started, shutting down in |nn;{remaining_minutes}|r minutes"
                ),
            );
        } else {
            CommandManager::send_normal_text(
                self,
                out,
                "Shutdown cancelled because there were save errors, if you want to shutdown regardless without saving, use |nn;/scripts forcedshutdown|r instead.",
            );
        }
        Ok(())
    }
}

impl Command for Scripts {
    fn command_names(&self) -> Vec<String> {
        names(&["scripts"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("Scripts runs through execute_async")
    }

    fn execute_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        args: &'a [String],
        out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(async move {
            let Some(action) = args.first() else {
                ctx.character.send_message(&format!(
                    "[Scripts] Using: {COMMAND_PREFIX}scripts <action>"
                ));
                return Ok(());
            };
            let server = ctx.server()?;
            let commands = &server.systems.services.commands;
            match action.as_str() {
                "reload" | "reboot" => {
                    commands.clear();
                    if commands.reload() {
                        CommandManager::send_normal_text(self, out, "[Scripts] Reload - Success");
                    } else {
                        CommandManager::send_error_text(
                            self,
                            out,
                            "Reload - There were errors, please check the server logs for details !",
                        );
                    }
                }
                "save" => {
                    if server.save.do_save(server).await {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            "Save - Done saving user database",
                        );
                    } else {
                        CommandManager::send_error_text(
                            self,
                            out,
                            "Save - Failed saving user database, was possible already in the process of saving, please check server console for details.",
                        );
                    }
                }
                "reloadslavepoints" => {
                    server
                        .systems
                        .vehicles
                        .reload_slave_attachment_point_locations(server);
                    CommandManager::send_normal_text(
                        self,
                        out,
                        "Slave Attachment Point Locations reloaded from .json file",
                    );
                }
                "shutdown" => self.shutdown(server, args, out).await?,
                "forcedshutdown" => {
                    server
                        .world
                        .broadcast_to_server(&SCNoticeMessagePacket::new(
                            3,
                            MAGENTA,
                            15000,
                            "The server is shutting down right now!",
                        ));
                    CommandManager::send_normal_text(self, out, "Shutting down immediately!");
                    // Manual Forced Shutdown (-2)
                    std::process::exit(-2);
                }
                _ => CommandManager::send_error_text(self, out, "Undefined action..."),
            }
            Ok(())
        })
    }

    fn get_command_line_help(&self) -> String {
        "<action>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Does script related actions. Allowed <action> are: reload, reboot, save, shutdown".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn scripts_actions() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (handled, out) = harness::run(&server, &gm.p, "scripts").await;
        assert!(handled && out.is_empty());
        assert_eq!(gm.chat(), ["[Scripts] Using: /scripts <action>"]);
        let (_, out) = harness::run(&server, &gm.p, "scripts reload").await;
        assert_eq!(out, ["[scripts] [Scripts] Reload - Success"]);
        assert_eq!(
            server.systems.services.commands.get_command_keys().len(),
            96
        );
        let (_, out) = harness::run(&server, &gm.p, "scripts what").await;
        assert_eq!(out, [harness::error("scripts", "Undefined action...")]);
        let (_, out) = harness::run(&server, &gm.p, "scripts shutdown cancel").await;
        assert_eq!(out, [harness::error("scripts", "No shutdown to cancel")]);
        let (_, out) = harness::run(&server, &gm.p, "scripts reloadslavepoints").await;
        assert_eq!(
            out,
            ["[scripts] Slave Attachment Point Locations reloaded from .json file"]
        );
        gm.frames();
    }

    #[tokio::test]
    async fn scripts_shutdown_cancel_works_until_t0() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let task = crate::systems::services::shutdown::ShutdownTask::with_exit(
            &server,
            chrono::Utc::now().naive_utc() + chrono::TimeDelta::minutes(30),
            -1,
            std::sync::Arc::new(|_| {}),
        );
        *server.systems.services.shutdown_task.lock().unwrap() = Some(task.clone());
        // Before T-0 nothing is refused or kicked and the cancel goes through.
        task.run(&server).await;
        assert!(!server.is_shutting_down());
        assert!(gm.frames().is_empty());
        let (_, out) = harness::run(&server, &gm.p, "scripts shutdown cancel").await;
        assert_eq!(out, ["[scripts] Shutdown cancelled."]);
        assert!(!server.is_shutting_down());
        let ops: Vec<u16> = gm.frames().iter().map(|f| f.0).collect();
        // The cancel notice (and the command's chat reply), no kick or leave.
        assert!(ops.contains(&sc::SCNoticeMessagePacket));
        assert!(!ops.contains(&sc::SCKickedPacket));
        assert!(!ops.contains(&sc::SCLeaveWorldGrantedPacket));
        assert!(gm.client.commands().is_empty());

        // After T-0 (players are being returned to the world list) it is too late.
        *server.systems.services.shutdown_task.lock().unwrap() = Some(task);
        server.set_shutting_down(true);
        let (_, out) = harness::run(&server, &gm.p, "scripts shutdown cancel").await;
        assert_eq!(
            out,
            [harness::error(
                "scripts",
                "Too late to cancel, the server is shutting down right now."
            )]
        );
        assert!(server
            .systems
            .services
            .shutdown_task
            .lock()
            .unwrap()
            .is_some());
    }

    #[tokio::test]
    async fn scripts_shutdown_with_a_pending_task() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        // A pending shutdown (the save step needs MySQL): reschedule, then cancel.
        let task = crate::systems::services::shutdown::ShutdownTask::with_exit(
            &server,
            chrono::Utc::now().naive_utc() + chrono::TimeDelta::minutes(30),
            -1,
            std::sync::Arc::new(|_| {}),
        );
        *server.systems.services.shutdown_task.lock().unwrap() = Some(task);
        let (_, out) = harness::run(&server, &gm.p, "scripts shutdown 10").await;
        assert_eq!(
            out,
            ["[scripts] Shutdown was already in progress, changed to 10 minutes from now."]
        );
        gm.frames();
        let (_, out) = harness::run(&server, &gm.p, "scripts shutdown cancel").await;
        assert_eq!(out, ["[scripts] Shutdown cancelled."]);
        assert!(server
            .systems
            .services
            .shutdown_task
            .lock()
            .unwrap()
            .is_none());
        assert!(gm
            .frames()
            .iter()
            .any(|(op, _)| *op == sc::SCNoticeMessagePacket));
    }
}
