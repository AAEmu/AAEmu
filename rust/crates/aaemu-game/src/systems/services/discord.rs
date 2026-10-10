//! `Services/DiscordBotService`: with a `DiscordToken` configured, a Discord bot shows
//! "Playing with N other players" (updated every 5 s) and answers `/online` with the online
//! character names. Without a token nothing starts (as in C#).
//!
//! Discord.Net's `DiscordSocketClient` defaults to the unprivileged gateway intents (no
//! message-content intent), and so does this client.

use std::sync::{Arc, Mutex, Weak};
use std::time::Duration;

use tokio_util::sync::CancellationToken;
use twilight_gateway::{EventTypeFlags, Intents, MessageSender, Shard, ShardId, StreamExt as _};
use twilight_http::Client;
use twilight_model::gateway::event::Event;
use twilight_model::gateway::payload::outgoing::UpdatePresence;
use twilight_model::gateway::presence::{ActivityType, MinimalActivity, Status};

use crate::server::GameServer;

/// The interval of `UpdatePlayerCountPeriodically`.
pub const PLAYER_COUNT_INTERVAL: Duration = Duration::from_secs(5);

/// `new Discord.Game("with " + playerCount + " other players")`.
pub fn activity_name(player_count: usize) -> String {
    format!("with {player_count} other players")
}

/// The answer to `/online` (`None` for any other message).
pub fn message_reply(content: &str, player_names: &[String]) -> Option<String> {
    if content != "/online" {
        return None;
    }
    Some(if player_names.is_empty() {
        "No players online.".to_string()
    } else {
        format!("Players online: {}", player_names.join(", "))
    })
}

/// `GatewayIntents.AllUnprivileged`.
pub fn unprivileged_intents() -> Intents {
    Intents::all() - (Intents::GUILD_MEMBERS | Intents::GUILD_PRESENCES | Intents::MESSAGE_CONTENT)
}

fn online_names(server: &GameServer) -> Vec<String> {
    server
        .world
        .all_characters()
        .iter()
        .map(|p| p.data().unit.name.clone())
        .collect()
}

/// `DiscordBotService` (`IHostedService`).
#[derive(Default)]
pub struct DiscordBotService {
    running: Mutex<Option<(CancellationToken, tokio::task::JoinHandle<()>)>>,
}

impl std::fmt::Debug for DiscordBotService {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("DiscordBotService")
            .field("started", &self.is_started())
            .finish()
    }
}

impl DiscordBotService {
    /// `StartAsync`: logs in and starts the gateway loop (nothing without a token).
    pub fn start(&self, server: &Arc<GameServer>) {
        let token = server.config.discord_token.clone().unwrap_or_default();
        if token.is_empty() {
            return;
        }
        let stop = CancellationToken::new();
        let task = tokio::spawn(run(Arc::downgrade(server), token, stop.clone()));
        *self.running.lock().expect("discord poisoned") = Some((stop, task));
    }

    /// `StopAsync`.
    pub async fn stop(&self) {
        let running = self.running.lock().expect("discord poisoned").take();
        if let Some((stop, task)) = running {
            stop.cancel();
            let _ = task.await;
        }
    }

    pub fn is_started(&self) -> bool {
        self.running.lock().map(|r| r.is_some()).unwrap_or(false)
    }
}

/// `UpdatePlayerCountPeriodically`: the "Playing ..." presence every 5 s.
async fn update_player_count(
    server: Weak<GameServer>,
    sender: MessageSender,
    stop: CancellationToken,
) {
    loop {
        let Some(s) = server.upgrade() else { return };
        let count = s.world.all_characters().len();
        drop(s);
        let activity = MinimalActivity {
            kind: ActivityType::Playing,
            name: activity_name(count),
            url: None,
        };
        match UpdatePresence::new(vec![activity.into()], false, None, Status::Online) {
            Ok(cmd) => {
                if sender.command(&cmd).is_err() {
                    return;
                }
            }
            Err(e) => tracing::warn!("Discord presence: {e}"),
        }
        tokio::select! {
            _ = stop.cancelled() => return,
            _ = tokio::time::sleep(PLAYER_COUNT_INTERVAL) => {}
        }
    }
}

async fn run(server: Weak<GameServer>, token: String, stop: CancellationToken) {
    let http = Arc::new(Client::new(token.clone()));
    let mut shard = Shard::new(ShardId::ONE, token, unprivileged_intents());
    let presence = tokio::spawn(update_player_count(
        server.clone(),
        shard.sender(),
        stop.clone(),
    ));
    loop {
        let item = tokio::select! {
            _ = stop.cancelled() => break,
            item = shard.next_event(EventTypeFlags::MESSAGE_CREATE | EventTypeFlags::READY) => item,
        };
        let Some(item) = item else { break };
        let event = match item {
            Ok(e) => e,
            Err(e) => {
                tracing::warn!("Discord gateway: {e}");
                continue;
            }
        };
        if let Event::MessageCreate(msg) = event {
            let Some(s) = server.upgrade() else { break };
            let names = online_names(&s);
            drop(s);
            if let Some(reply) = message_reply(&msg.content, &names) {
                let http = http.clone();
                let channel = msg.channel_id;
                tokio::spawn(async move {
                    if let Err(e) = http.create_message(channel).content(&reply).await {
                        tracing::warn!("Discord send: {e}");
                    }
                });
            }
        }
    }
    presence.abort();
    shard.close(twilight_gateway::CloseFrame::NORMAL);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn replies_and_activity() {
        assert_eq!(activity_name(3), "with 3 other players");
        assert_eq!(message_reply("hi", &[]), None);
        assert_eq!(
            message_reply("/online", &[]).as_deref(),
            Some("No players online.")
        );
        assert_eq!(
            message_reply("/online", &["Al".into(), "Bo".into()]).as_deref(),
            Some("Players online: Al, Bo")
        );
        assert!(!unprivileged_intents().contains(Intents::MESSAGE_CONTENT));
        assert!(unprivileged_intents().contains(Intents::GUILD_MESSAGES));
    }

    #[tokio::test]
    async fn no_token_no_bot() {
        let server = Arc::new(crate::test_support::test_server());
        let bot = DiscordBotService::default();
        bot.start(&server);
        assert!(!bot.is_started());
        bot.stop().await;
    }
}
