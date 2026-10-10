//! `Scripts/Commands/SoloParty.cs`.

use super::prelude::*;

/// `SoloParty`.
pub struct SoloParty;

impl SoloParty {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for SoloParty {
    fn command_names(&self) -> Vec<String> {
        names(&["soloparty", "solo_party"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let teams = &server.systems.social.teams;
        if teams.active_team_by_unit(character.character_id).is_some() {
            ctx.character
                .send_message("|cFFFFFF00[SoloParty] You are already in a party !|r");
        } else {
            teams.create_solo_team(server, &character, true);
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        format!(
            "Creates a party with just yourself in it. This can be useful to use with \"{COMMAND_PREFIX}teleport .\" command."
        )
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn soloparty_creates_a_one_member_party() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "soloparty").await;
        assert!(out.is_empty());
        let team = server
            .systems
            .social
            .teams
            .active_team_by_unit(1)
            .expect("team");
        assert!(team.is_party);
        assert_eq!(team.members_count(), 1);
        assert!(gm.p.data().in_party);
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCJoinedTeamPacket));

        harness::run(&server, &gm.p, "solo_party").await;
        assert_eq!(
            gm.chat(),
            ["|cFFFFFF00[SoloParty] You are already in a party !|r"]
        );
        assert_eq!(server.systems.social.teams.team_count(), 1);
    }
}
