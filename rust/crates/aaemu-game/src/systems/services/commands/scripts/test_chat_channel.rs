//! `Scripts/Commands/TestChatChannel.cs`.

use super::prelude::*;
use crate::packets::world::{SCJoinedChatChannelPacket, SCLeavedChatChannelPacket};

/// `TestChatChannel`.
pub struct TestChatChannel;

impl TestChatChannel {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for TestChatChannel {
    fn command_names(&self) -> Vec<String> {
        names(&["testchatchannel", "test_chat_channel", "testchat"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // `StringComparison.CurrentCultureIgnoreCase` (invariant culture).
        let is = |i: usize, word: &str| args[i].to_lowercase() == word;
        let server = ctx.server()?;

        if args.len() == 1 && is(0, "list") {
            CommandManager::send_normal_text(self, out, "List all channels");
            for c in server.chat.list_all_channels() {
                let member_count = c.members().len();
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "{} - T:{} ST:{} F:{} => {} ({member_count})",
                        c.internal_id,
                        enum_name(i64::from(c.chat_type), CHAT_TYPE),
                        c.sub_type,
                        enum_name(i64::from(c.faction), FACTIONS_ENUM),
                        c.internal_name()
                    ),
                );
            }
            CommandManager::send_normal_text(self, out, "End of list");
            return Ok(());
        }

        if args.len() == 2 && is(0, "list") {
            let Some(channel_id) = try_parse::<u32>(&args[1]) else {
                CommandManager::send_error_text(self, out, "ChannelId Parse error");
                return Ok(());
            };
            let Some(this_channel) = server
                .chat
                .list_all_channels()
                .into_iter()
                .find(|x| x.internal_id == channel_id)
            else {
                CommandManager::send_error_text(
                    self,
                    out,
                    &format!("ChannelId {channel_id} not found"),
                );
                return Ok(());
            };
            let members = this_channel.members();
            CommandManager::send_normal_text(
                self,
                out,
                &format!(
                    "List {} members of {} ({})",
                    members.len(),
                    this_channel.internal_name(),
                    this_channel.internal_id
                ),
            );
            let mut t = String::new();
            let mut c = 0;
            let mut first = true;
            for m in &members {
                if first {
                    first = false;
                    t += &name_of(m);
                } else {
                    t += &format!(", {}", name_of(m));
                }
                c += 1;
                // C#: `t` is not reset, every line repeats the names before it.
                if c >= 10 {
                    CommandManager::send_normal_text(self, out, &t);
                    c = 0;
                }
            }
            if c > 0 {
                CommandManager::send_normal_text(self, out, &t);
            }
            CommandManager::send_normal_text(self, out, "End of list");
            return Ok(());
        }

        if args.len() == 1 && is(0, "clean") {
            let removed = server.chat.clean_up_channels(&server.ids.chat);
            CommandManager::send_normal_text(
                self,
                out,
                &format!("{removed} empty channel(s) removed"),
            );
            return Ok(());
        }

        if args.len() < 4 {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }

        let chat_type = enum_try_parse(
            &args[1],
            CHAT_TYPE,
            i64::from(i16::MIN),
            i64::from(i16::MAX),
        );
        let chat_sub_type = try_parse::<u8>(&args[2]);
        let chat_faction = enum_try_parse(&args[3], FACTIONS_ENUM, 0, i64::from(u32::MAX));
        let (Some(chat_type), Some(chat_sub_type), Some(chat_faction)) =
            (chat_type, chat_sub_type, chat_faction)
        else {
            CommandManager::send_error_text(self, out, "Parse error");
            return Ok(());
        };

        let character = ctx.player()?;
        if is(0, "join") {
            character.send(SCJoinedChatChannelPacket {
                chat_type: chat_type as i16,
                sub_type: i16::from(chat_sub_type),
                faction_id: chat_faction as u32,
            });
        }
        if is(0, "leave") {
            character.send(SCLeavedChatChannelPacket {
                chat_type: chat_type as i16,
                sub_type: i16::from(chat_sub_type),
                faction_id: chat_faction as u32,
            });
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<list [id]||clean||<<join||leave> <chatTypeId> <chatSubType> <chatFaction>>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Command used to manually send join/leave channel packets to yourself used for testing\rYou can also use list to show a list of all current chat channels, or clean to remove any non-system channel that has zero users in it.".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn testchat_lists_and_sends_channel_packets() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let channel = server.chat.faction_chat(101);
        channel.join(&gm.p);

        let (_, out) = harness::run(&server, &gm.p, "testchat LIST").await;
        assert_eq!(out.first().unwrap(), "[testchatchannel] List all channels");
        assert_eq!(out.last().unwrap(), "[testchatchannel] End of list");
        let line = format!(
            "[testchatchannel] {} - T:{} ST:{} F:{} => {} (1)",
            channel.internal_id,
            super::enum_name(i64::from(channel.chat_type), super::CHAT_TYPE),
            channel.sub_type,
            super::enum_name(i64::from(channel.faction), super::FACTIONS_ENUM),
            channel.internal_name()
        );
        assert!(out.contains(&line), "{out:?}");

        let (_, out) = harness::run(
            &server,
            &gm.p,
            &format!("testchat list {}", channel.internal_id),
        )
        .await;
        assert_eq!(
            out,
            [
                format!(
                    "[testchatchannel] List 1 members of {} ({})",
                    channel.internal_name(),
                    channel.internal_id
                ),
                "[testchatchannel] Gm".to_string(),
                "[testchatchannel] End of list".to_string(),
            ]
        );
        let (_, out) = harness::run(&server, &gm.p, "testchat list 999999").await;
        assert_eq!(
            out,
            [harness::error(
                "testchatchannel",
                "ChannelId 999999 not found"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "testchat list x").await;
        assert_eq!(
            out,
            [harness::error("testchatchannel", "ChannelId Parse error")]
        );
        let (_, out) = harness::run(&server, &gm.p, "testchat clean").await;
        assert_eq!(out, ["[testchatchannel] 0 empty channel(s) removed"]);

        gm.chat();
        let (_, out) = harness::run(&server, &gm.p, "testchat join shout 2 nuian").await;
        assert!(out.is_empty());
        let mut body = 1i16.to_le_bytes().to_vec();
        body.extend(2i16.to_le_bytes());
        body.extend(101u32.to_le_bytes());
        body.extend(0u16.to_le_bytes()); // empty name
        assert_eq!(gm.packets(), vec![(sc::SCJoinedChatChannelPacket, body)]);
        harness::run(&server, &gm.p, "testchat leave -2 0 1").await;
        let mut body = (-2i16).to_le_bytes().to_vec();
        body.extend(0i16.to_le_bytes());
        body.extend(1u32.to_le_bytes());
        assert_eq!(gm.packets(), vec![(sc::SCLeavedChatChannelPacket, body)]);

        let (_, out) = harness::run(&server, &gm.p, "testchat join nope 2 1").await;
        assert_eq!(out, [harness::error("testchatchannel", "Parse error")]);
        let (_, out) = harness::run(&server, &gm.p, "testchat join").await;
        assert!(out[0].starts_with("Help for |cFFFFFFFF/testchatchannel|r"));
    }
}
