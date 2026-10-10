//! `Scripts/Commands/Appellation.cs`.

use super::prelude::*;

/// `Appellation`.
pub struct Appellation;

impl Appellation {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Appellation {
    fn command_names(&self) -> Vec<String> {
        names(&["title", "addtitle", "add_title", "appellation"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        match try_parse::<u32>(&args[0]) {
            Some(id) => {
                let server = ctx.server()?;
                // `CharacterManager.GetAppellationsTemplate(id)`.
                if !server.game_data.characters.appellations.contains_key(&id) {
                    CommandManager::send_error_text(
                        self,
                        out,
                        &format!("<titleId> {id} does not exist in the database"),
                    );
                } else {
                    // `character.Appellations.Add(id)`.
                    let character = ctx.player()?;
                    server
                        .systems
                        .skills
                        .ext
                        .social()
                        .give_appellation(server, &character, id);
                }
            }
            None => CommandManager::send_error_text(self, out, "Error parsing <titleId> !"),
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<titleId>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Adds title using <titleId>".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::data::GameData;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn title_adds_an_appellation() {
        let mut data = GameData::empty();
        data.characters.appellations.insert(42, 0);
        let server = harness::server_with(data);
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);

        let (_, out) = harness::run(&server, &gm.p, "title 42").await;
        assert!(out.is_empty());
        assert_eq!(gm.p.data().appellations.appellations, vec![42]);
        assert_eq!(
            gm.packets(),
            vec![(sc::SCAppellationGainedPacket, 42u32.to_le_bytes().to_vec())]
        );
        // Duplicate: only a warning.
        harness::run(&server, &gm.p, "addtitle 42").await;
        assert!(gm.packets().is_empty());

        let (_, out) = harness::run(&server, &gm.p, "title 43").await;
        assert_eq!(
            out,
            [harness::error(
                "title",
                "<titleId> 43 does not exist in the database"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "title x").await;
        assert_eq!(out, [harness::error("title", "Error parsing <titleId> !")]);
    }
}
