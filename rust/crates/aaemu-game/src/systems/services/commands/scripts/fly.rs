//! `Scripts/Commands/Fly.cs`.

use std::sync::Mutex;

use super::prelude::*;
use crate::systems::skills::packets::SCUnitFlyingStateChangedPacket;

/// `Fly`.
pub struct Fly {
    /// `characterFlyStateCache` (character ids; static in C#, so it lives as long as the
    /// loaded script).
    character_fly_state_cache: Mutex<Vec<u32>>,
}

impl Fly {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self {
            character_fly_state_cache: Mutex::new(Vec::new()),
        }
    }

    fn get_cache_state(&self, character_id: u32) -> bool {
        self.character_fly_state_cache
            .lock()
            .expect("fly cache poisoned")
            .contains(&character_id)
    }

    fn set_cache_state(&self, character_id: u32, state: bool) {
        if state && !self.get_cache_state(character_id) {
            self.character_fly_state_cache
                .lock()
                .expect("fly cache poisoned")
                .push(character_id);
        } else {
            let mut cache = self
                .character_fly_state_cache
                .lock()
                .expect("fly cache poisoned");
            if let Some(i) = cache.iter().position(|id| *id == character_id) {
                cache.remove(i);
            }
        }
    }
}

impl Command for Fly {
    fn command_names(&self) -> Vec<String> {
        names(&["fly"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let character = ctx.player()?;
        let mut target = character.clone();

        let mut first_arg = 0;
        if !args.is_empty() {
            let server = ctx.server()?;
            (target, first_arg) = get_target_or_self(server, &character, Some(&args[0]));
        }

        // We cache the playerId, not the ObjectId
        let mut is_flying = !self.get_cache_state(target.character_id);

        if args.len() > first_arg {
            match parse_bool(&args[first_arg]) {
                Some(v) => is_flying = v,
                None => {
                    CommandManager::send_error_text(self, out, "<fly state> bool parse error!");
                    return Ok(());
                }
            }
        }

        target.send(SCUnitFlyingStateChangedPacket {
            obj_id: target.obj_id,
            flying: is_flying,
        });
        send_player_message(
            &target,
            &format!("State changed to |cFFFFFFFF{}|r.", bool_str(is_flying)),
        );
        self.set_cache_state(target.character_id, is_flying);
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(target) [true || false]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Enables or disables fly-mode (also makes you move at high-speed)".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn fly_toggles_and_sets_the_flying_state() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);

        // Toggle on, then off.
        let (_, out) = harness::run(&server, &gm.p, "fly").await;
        assert!(out.is_empty());
        let frames = gm.frames();
        assert_eq!(
            frames[0],
            (
                sc::SCUnitFlyingStateChangedPacket,
                vec![0x01, 0x01, 0x00, 1]
            )
        );
        assert_eq!(
            harness::chat_text(&frames[1].1),
            "State changed to |cFFFFFFFFTrue|r."
        );
        harness::run(&server, &gm.p, "fly").await;
        assert_eq!(gm.chat(), ["State changed to |cFFFFFFFFFalse|r."]);

        // Explicit state for another player.
        harness::run(&server, &gm.p, "fly Bob true").await;
        assert_eq!(bob.chat(), ["State changed to |cFFFFFFFFTrue|r."]);
        harness::run(&server, &gm.p, "fly Bob").await;
        assert_eq!(bob.chat(), ["State changed to |cFFFFFFFFFalse|r."]);

        let (_, out) = harness::run(&server, &gm.p, "fly maybe").await;
        assert_eq!(
            out,
            [harness::error("fly", "<fly state> bool parse error!")]
        );
    }
}
