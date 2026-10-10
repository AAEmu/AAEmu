//! `Scripts/Commands/Sphere.cs` + `Utils/SphereCommandUtil.cs`.

use std::path::PathBuf;

use serde_json::Value;

use super::prelude::*;
use crate::systems::quests::model::kind;
use crate::systems::quests::spheres::distance3;
use crate::systems::services::webapi::json::JValue;
use crate::systems::skills::packets_special::SCTeleportUnitPacket;
use crate::utils::json_models::{JsonPosition, JsonQuestSphere};

/// `Sphere`.
pub struct Sphere {
    /// `FileManager.AppPath`.
    app_path: PathBuf,
}

impl Sphere {
    pub fn new(env: &ScriptEnv) -> Self {
        Self {
            app_path: env.app_path.clone(),
        }
    }
}

impl Command for Sphere {
    fn command_names(&self) -> Vec<String> {
        names(&["sphere"])
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
        sphere_util::get_command_choice(self, &self.app_path, out, ctx, &args[0], args)
    }

    fn get_command_line_help(&self) -> String {
        "<list||add||remove||quest||goto>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Sphere related commands ".into()
    }
}

/// `Utils/SphereCommandUtil.cs`.
pub mod sphere_util {
    use super::*;

    /// `GetCommandChoice(command, messageOutput, character, choice, args)`.
    pub fn get_command_choice(
        command: &dyn Command,
        app_path: &std::path::Path,
        out: &mut dyn MessageOutput,
        ctx: &CommandContext<'_>,
        choice: &str,
        args: &[String],
    ) -> anyhow::Result<()> {
        match choice {
            "quest" => {
                if args.len() > 1 {
                    if let Some(quest_id) = try_parse::<u32>(&args[1]) {
                        get_quest_spheres(ctx, quest_id)?;
                    }
                } else {
                    CommandManager::send_error_text(command, out, "/sphere quest <questId>");
                }
            }
            "add" => {
                if args.len() >= 4 {
                    if let (Some(quest_id), Some(sphere_id), Some(radius)) = (
                        try_parse::<u32>(&args[1]),
                        try_parse::<u32>(&args[2]),
                        parse_f32(&args[3]),
                    ) {
                        add_quest_sphere(ctx, app_path, quest_id, sphere_id, radius)?;
                    }
                } else {
                    CommandManager::send_error_text(
                        command,
                        out,
                        "/sphere add <questId> <sphereId> <radius>",
                    );
                    CommandManager::send_error_text(
                        command,
                        out,
                        "Adding a sphere uses character's current position!",
                    );
                }
            }
            "list" => {
                if args.len() > 1 {
                    if let Some(quest_id) = try_parse::<u32>(&args[1]) {
                        get_sphere_list(ctx, quest_id)?;
                    }
                } else {
                    CommandManager::send_error_text(command, out, "/sphere list <questId>");
                }
            }
            "remove" => {
                if args.len() > 1 {
                    if let Some(json_id) = try_parse::<u32>(&args[1]) {
                        remove_quest_sphere(ctx, app_path, json_id)?;
                    }
                } else {
                    CommandManager::send_error_text(command, out, "/sphere remove <jsonId>");
                }
            }
            "goto" => {
                if args.len() > 1 {
                    if let Some(json_id) = try_parse::<u32>(&args[1]) {
                        goto_sphere(ctx, app_path, json_id)?;
                    }
                } else {
                    CommandManager::send_error_text(command, out, "/sphere goto <jsonId>");
                }
            }
            _ => CommandManager::send_default_help_text(command, out),
        }
        Ok(())
    }

    /// `SphereGameData.GetQuestSphere(questId)`: `None` for an unknown quest. The C# compares
    /// the acts' `DetailType`, which the detail act templates never set: the list is always
    /// empty. Its inner loop runs over the *component* count (`actIndex < components.Length`),
    /// so a component with fewer acts than its step has components throws.
    pub fn get_quest_sphere(
        server: &GameServer,
        quest_id: u32,
    ) -> anyhow::Result<Option<Vec<String>>> {
        let data = &server.game_data.systems.quests;
        let Some(quest_template) = data.template(quest_id) else {
            return Ok(None);
        };
        let sphere_ids = Vec::new();
        for step in kind::ALL {
            let components = quest_template.components_of(&data.components, step);
            if components.is_empty() {
                continue;
            }
            for component in &components {
                let acts = data.acts_in_component(component.id);
                if !acts.is_empty() {
                    for act_index in 0..components.len() {
                        if act_index >= acts.len() {
                            anyhow::bail!(
                                "Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')"
                            );
                        }
                        // C#: `act.DetailType == "QuestActObjSphere"` (always null).
                    }
                }
            }
        }
        Ok(Some(sphere_ids))
    }

    /// `GetQuestSpheres(character, questId)`.
    fn get_quest_spheres(ctx: &CommandContext<'_>, quest_id: u32) -> anyhow::Result<()> {
        let server = ctx.server()?;
        match get_quest_sphere(server, quest_id)? {
            Some(sphere_ids) => {
                for x in sphere_ids {
                    ctx.character
                        .send_message(&format!("Found SphereId: {x} for QuestId: {quest_id}"));
                }
            }
            None => ctx
                .character
                .send_message(&format!("No Spheres required for QuestId: {quest_id}")),
        }
        Ok(())
    }

    /// `$"{FileManager.AppPath}Data/Worlds/{world.Template.Name}/quest_sphere.json"`.
    fn sphere_file(app_path: &std::path::Path, world_name: &str) -> String {
        let mut base = app_path.to_string_lossy().into_owned();
        if !base.is_empty() && !base.ends_with(['/', '\\']) {
            base.push('/');
        }
        format!("{base}Data/Worlds/{world_name}/quest_sphere.json")
    }

    /// `FileManager.GetFileContents(file)` (empty when the file does not exist).
    fn file_contents(path: &str) -> anyhow::Result<String> {
        if !std::path::Path::new(path).is_file() {
            return Ok(String::new());
        }
        let text = std::fs::read_to_string(path)?;
        Ok(text.trim_start_matches('\u{feff}').to_string())
    }

    /// A member of a JSON object (Json.NET matches property names case-insensitively).
    fn member<'a>(v: &'a Value, name: &str) -> Option<&'a Value> {
        let o = v.as_object()?;
        o.get(name).or_else(|| {
            o.iter()
                .find(|(k, _)| k.eq_ignore_ascii_case(name))
                .map(|(_, v)| v)
        })
    }

    fn number(v: Option<&Value>) -> Option<f64> {
        match v? {
            Value::Number(n) => n.as_f64(),
            Value::String(s) => parse_f64(s),
            Value::Null => Some(0.0),
            _ => None,
        }
    }

    /// `JsonHelper.TryDeserializeObject(contents, out List<JsonQuestSphere>, out _)`.
    fn deserialize(contents: &str) -> Option<Vec<JsonQuestSphere>> {
        let v: Value = serde_json::from_str(contents).ok()?;
        let Value::Array(items) = v else {
            return None;
        };
        let mut res = Vec::with_capacity(items.len());
        for item in &items {
            if !item.is_object() {
                return None;
            }
            let position = match member(item, "Position") {
                None | Some(Value::Null) => None,
                Some(p @ Value::Object(_)) => Some(JsonPosition {
                    x: number(member(p, "X")).unwrap_or(0.0) as f32,
                    y: number(member(p, "Y")).unwrap_or(0.0) as f32,
                    z: number(member(p, "Z")).unwrap_or(0.0) as f32,
                    roll: number(member(p, "Roll")).unwrap_or(0.0) as f32,
                    pitch: number(member(p, "Pitch")).unwrap_or(0.0) as f32,
                    yaw: number(member(p, "Yaw")).unwrap_or(0.0) as f32,
                }),
                Some(_) => return None,
            };
            let uint = |name: &str| -> Option<u32> {
                match member(item, name) {
                    None => Some(0),
                    Some(x) => number(Some(x)).and_then(|f| {
                        (f >= 0.0 && f <= f64::from(u32::MAX) && f.fract() == 0.0)
                            .then_some(f as u32)
                    }),
                }
            };
            res.push(JsonQuestSphere {
                id: uint("Id")?,
                quest_id: uint("QuestId")?,
                sphere_id: uint("SphereId")?,
                radius: match member(item, "Radius") {
                    None => 0.0,
                    Some(x) => number(Some(x))? as f32,
                },
                position,
            });
        }
        Some(res)
    }

    /// `JsonConvert.SerializeObject(spheres.ToArray(), Formatting.Indented)` (default
    /// serializer: every property, floats as `0.0`).
    fn serialize(spheres: &[JsonQuestSphere]) -> String {
        let items = spheres
            .iter()
            .map(|s| {
                JValue::object([
                    ("Id", JValue::from(s.id)),
                    ("QuestId", JValue::from(s.quest_id)),
                    ("SphereId", JValue::from(s.sphere_id)),
                    ("Radius", JValue::Single(s.radius)),
                    (
                        "Position",
                        s.position.map_or(JValue::Null, |p| {
                            JValue::object([
                                ("X", JValue::Single(p.x)),
                                ("Y", JValue::Single(p.y)),
                                ("Z", JValue::Single(p.z)),
                                ("Roll", JValue::Single(p.roll)),
                                ("Pitch", JValue::Single(p.pitch)),
                                ("Yaw", JValue::Single(p.yaw)),
                            ])
                        }),
                    ),
                ])
            })
            .collect();
        JValue::Array(items).to_json_indented()
    }

    /// The quest sphere files of the character's world (`foreach world ... if
    /// (character.Transform.WorldId != world.Id) continue;`): (path, parsed list) or the warning
    /// for an empty / missing file.
    fn for_each_world_file(
        ctx: &CommandContext<'_>,
        app_path: &std::path::Path,
        mut f: impl FnMut(&str, Vec<JsonQuestSphere>) -> anyhow::Result<()>,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let player = ctx.player()?;
        let world_id = player.data().unit.transform.instance_id;
        for world in server.world.worlds() {
            if world_id != world.id {
                continue;
            }
            let path = sphere_file(app_path, &world.template.name);
            let contents = file_contents(&path)?;
            if contents.trim().is_empty() {
                tracing::warn!("File {path} doesn't exists or is empty.");
                continue;
            }
            let Some(spheres) = deserialize(&contents) else {
                anyhow::bail!("SpawnManager: Parse {path} file");
            };
            f(&path, spheres)?;
        }
        Ok(())
    }

    /// `AddQuestSphere(character, questId, sphereId, radius)`.
    fn add_quest_sphere(
        ctx: &CommandContext<'_>,
        app_path: &std::path::Path,
        quest_id: u32,
        sphere_id: u32,
        radius: f32,
    ) -> anyhow::Result<()> {
        let player = ctx.player()?;
        for_each_world_file(ctx, app_path, |path, mut spheres| {
            let uuid = match spheres.last() {
                Some(last) => last.id.wrapping_add(1),
                None => spheres.len() as u32 + 1,
            };
            let t = player.data().unit.transform;
            spheres.push(JsonQuestSphere {
                id: uuid,
                quest_id,
                sphere_id,
                radius,
                position: Some(JsonPosition {
                    x: t.x,
                    y: t.y,
                    z: t.z,
                    ..JsonPosition::default()
                }),
            });
            std::fs::write(path, serialize(&spheres))?;
            ctx.character.send_message("Sphere successfully added!");
            Ok(())
        })
    }

    /// `GetSphereList(character, questId)`.
    fn get_sphere_list(ctx: &CommandContext<'_>, quest_id: u32) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let player = ctx.player()?;
        // `character.ParentWorld.SphereQuestManager` (one manager in Rust).
        let world = parent_world(server, &player).ok_or_else(|| {
            anyhow::anyhow!("Object reference not set to an instance of an object.")
        })?;
        let triggers = server.systems.quests.spheres.triggers();
        let pos = world.world_pos_rot(&WorldObject::from(&player));
        let mut count = 0;
        for quest_trigger in triggers {
            if quest_trigger.character_id != player.character_id {
                continue;
            }
            // C#: compares `Quest.Id` (the quest object id), not the template id.
            let quest_object_id = player
                .data()
                .quest_state
                .active_quest(quest_trigger.quest_template_id)
                .map_or(0, |q| q.id);
            if quest_id > 0 && quest_object_id != quest_id {
                continue;
            }
            let s = &quest_trigger.sphere;
            let current_distance = distance3((pos.x, pos.y, pos.z), s.xyz);
            ctx.character.send_message(&format!(
                "[Sphere] Quest {} - Component: {} - TriggerSphere  xyz:{} , radius:{}m, at {}m away",
                quest_trigger.quest_template_id,
                s.component_id,
                vector3_string([s.xyz.0, s.xyz.1, s.xyz.2]),
                f32s(s.radius),
                fixed(f64::from(current_distance), 1)
            ));
            count += 1;
        }
        if count <= 0 {
            let suffix = if quest_id > 0 {
                format!(" for QuestId {quest_id}")
            } else {
                String::new()
            };
            ctx.character
                .send_message(&format!("[Sphere] No active sphere triggers found{suffix}"));
        }
        Ok(())
    }

    /// `RemoveQuestSphere(character, jsonId)`.
    pub fn remove_quest_sphere(
        ctx: &CommandContext<'_>,
        app_path: &std::path::Path,
        json_id: u32,
    ) -> anyhow::Result<()> {
        let mut found = false;
        for_each_world_file(ctx, app_path, |path, mut spheres| {
            if let Some(index) = spheres.iter().position(|s| s.id == json_id) {
                found = true;
                spheres.remove(index);
            }
            if !found {
                ctx.character
                    .send_message(&format!("Json entry with ID {json_id} does not exist!"));
            }
            std::fs::write(path, serialize(&spheres))?;
            if found {
                ctx.character.send_message(&format!(
                    "Removed Json entry with ID {json_id} successfully"
                ));
            }
            Ok(())
        })
    }

    /// `GotoSphere(character, jsonId)`.
    pub fn goto_sphere(
        ctx: &CommandContext<'_>,
        app_path: &std::path::Path,
        json_id: u32,
    ) -> anyhow::Result<()> {
        let player = ctx.player()?;
        for_each_world_file(ctx, app_path, |_, spheres| {
            match spheres.iter().find(|s| s.id == json_id) {
                Some(sphere) => {
                    // C#: `sphere.Position.X` of a null position throws.
                    let p = sphere.position.ok_or_else(|| {
                        anyhow::anyhow!("Object reference not set to an instance of an object.")
                    })?;
                    player.send(SCTeleportUnitPacket {
                        reason: 0,
                        error_message: 0,
                        x: p.x,
                        y: p.y,
                        z: p.z,
                        z2: 0.0,
                    });
                }
                None => ctx
                    .character
                    .send_message(&format!("Json entry with ID {json_id} does not exist!")),
            }
            Ok(())
        })
    }
}

#[cfg(test)]
mod tests {
    use std::path::PathBuf;
    use std::sync::Arc;

    use super::super::{harness, ScriptEnv};
    use super::Sphere;
    use crate::data::GameData;
    use crate::opcodes::sc;
    use crate::server::GameServer;
    use crate::systems::quests::data::RawSignSphere;
    use crate::systems::quests::model::kind;
    use crate::systems::quests::templates::{ActKind, QuestComponentTemplate, QuestTemplate};

    /// A server whose `sphere` command uses `app` as `FileManager.AppPath`.
    fn server(data: GameData, app: &std::path::Path) -> Arc<GameServer> {
        let server = harness::server_with(data);
        server.systems.services.commands.register_names(
            &["sphere"],
            Arc::new(Sphere::new(&ScriptEnv {
                app_path: app.to_path_buf(),
            })),
        );
        server
    }

    fn temp_app(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("aaemu-sphere-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(dir.join("Data/Worlds/main_world")).unwrap();
        dir
    }

    fn file(app: &std::path::Path) -> PathBuf {
        app.join("Data/Worlds/main_world/quest_sphere.json")
    }

    #[tokio::test]
    async fn sphere_usage_and_help() {
        let app = temp_app("usage");
        let server = server(GameData::empty(), &app);
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "sphere").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/sphere|r |cFFEEEEAA<list||add||remove||quest||goto>|r\n|cFF888888Sphere related commands |r"]
        );
        let (_, out) = harness::run(&server, &gm.p, "sphere what").await;
        assert_eq!(out.len(), 1);
        for (cmd, text) in [
            ("quest", "/sphere quest <questId>"),
            ("list", "/sphere list <questId>"),
            ("remove", "/sphere remove <jsonId>"),
            ("goto", "/sphere goto <jsonId>"),
        ] {
            let (_, out) = harness::run(&server, &gm.p, &format!("sphere {cmd}")).await;
            assert_eq!(out, [harness::error("sphere", text)]);
        }
        let (_, out) = harness::run(&server, &gm.p, "sphere add 1 2").await;
        assert_eq!(
            out,
            [
                harness::error("sphere", "/sphere add <questId> <sphereId> <radius>"),
                harness::error(
                    "sphere",
                    "Adding a sphere uses character's current position!"
                ),
            ]
        );
        let _ = std::fs::remove_dir_all(&app);
    }

    #[tokio::test]
    async fn sphere_add_goto_remove_edit_the_json_file() {
        let app = temp_app("file");
        let server = server(GameData::empty(), &app);
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 200.5);
        // Missing file: only a warning.
        let (_, out) = harness::run(&server, &gm.p, "sphere add 5 6 7.5").await;
        assert!(out.is_empty());
        assert!(gm.chat().is_empty());
        assert!(!file(&app).exists());

        std::fs::write(file(&app), "[]").unwrap();
        harness::run(&server, &gm.p, "sphere add 5 6 7.5").await;
        harness::run(&server, &gm.p, "sphere add 8 9 1").await;
        assert_eq!(
            gm.chat(),
            ["Sphere successfully added!", "Sphere successfully added!"]
        );
        let nl = if cfg!(windows) { "\r\n" } else { "\n" };
        let text = std::fs::read_to_string(file(&app)).unwrap();
        let expected_first = [
            "[",
            "  {",
            "    \"Id\": 1,",
            "    \"QuestId\": 5,",
            "    \"SphereId\": 6,",
            "    \"Radius\": 7.5,",
            "    \"Position\": {",
            "      \"X\": 100.0,",
            "      \"Y\": 200.5,",
            "      \"Z\": 100.0,",
            "      \"Roll\": 0.0,",
            "      \"Pitch\": 0.0,",
            "      \"Yaw\": 0.0",
            "    }",
            "  },",
            "  {",
            "    \"Id\": 2,",
        ]
        .join(nl);
        assert!(text.starts_with(&expected_first), "{text}");

        // goto
        harness::run(&server, &gm.p, "sphere goto 2").await;
        let frames = gm.packets();
        assert_eq!(frames.len(), 1);
        assert_eq!(frames[0].0, sc::SCTeleportUnitPacket);
        harness::run(&server, &gm.p, "sphere goto 3").await;
        assert_eq!(gm.chat(), ["Json entry with ID 3 does not exist!"]);

        // remove
        harness::run(&server, &gm.p, "sphere remove 1").await;
        assert_eq!(gm.chat(), ["Removed Json entry with ID 1 successfully"]);
        harness::run(&server, &gm.p, "sphere remove 1").await;
        assert_eq!(gm.chat(), ["Json entry with ID 1 does not exist!"]);
        let text = std::fs::read_to_string(file(&app)).unwrap();
        assert!(text.contains("\"Id\": 2,") && !text.contains("\"Id\": 1,"));

        // A broken file throws the GameException.
        std::fs::write(file(&app), "{nope").unwrap();
        harness::run(&server, &gm.p, "sphere goto 2").await;
        let path = format!(
            "{}/Data/Worlds/main_world/quest_sphere.json",
            app.to_string_lossy()
        );
        assert_eq!(
            gm.chat(),
            [format!("|cFFFF0000SpawnManager: Parse {path} file|r")]
        );
        let _ = std::fs::remove_dir_all(&app);
    }

    fn quest_data() -> GameData {
        let mut gd = GameData::empty();
        let q = &mut gd.systems.quests;
        q.add_template(QuestTemplate {
            id: 100,
            ..QuestTemplate::default()
        });
        // Two progress components, the first with one act: the C# act loop runs to the
        // component count and throws.
        q.add_template(QuestTemplate {
            id: 200,
            ..QuestTemplate::default()
        });
        for (id, quest_id, kind_id) in [
            (1001, 100, kind::PROGRESS),
            (2001, 200, kind::PROGRESS),
            (2002, 200, kind::PROGRESS),
        ] {
            q.add_component(QuestComponentTemplate {
                id,
                quest_id,
                kind_id,
                ..QuestComponentTemplate::default()
            });
        }
        q.add_act(1001, 1001, 1001, 0, ActKind::CheckSphere { sphere_id: 5 });
        q.add_act(2001, 2001, 2001, 0, ActKind::CheckSphere { sphere_id: 6 });
        q.finish();
        gd
    }

    #[tokio::test]
    async fn sphere_quest_and_list() {
        let app = temp_app("quest");
        let server = server(quest_data(), &app);
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        harness::run(&server, &gm.p, "sphere quest 7").await;
        assert_eq!(gm.chat(), ["No Spheres required for QuestId: 7"]);
        // The C# never matches the (null) detail type: no output.
        harness::run(&server, &gm.p, "sphere quest 100").await;
        assert!(gm.chat().is_empty());
        harness::run(&server, &gm.p, "sphere quest 200").await;
        assert_eq!(
            gm.chat(),
            ["|cFFFF0000Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')|r"]
        );

        // list: no triggers
        harness::run(&server, &gm.p, "sphere list 0").await;
        assert_eq!(gm.chat(), ["[Sphere] No active sphere triggers found"]);
        // A trigger of quest 100 component 1001 (sphere at (110, 100, 100), radius 5).
        let spheres = &server.systems.quests.spheres;
        spheres.load(
            &server.game_data.systems.quests,
            &[RawSignSphere {
                zone_id: 1,
                quest_id: 100,
                component_id: 1001,
                local: [110.0, 100.0, 100.0],
                radius: 5.0,
            }],
            "main_world",
            |_| (0, 0),
        );
        assert_eq!(spheres.add_triggers(1, 100, 1001, 0), 1);
        spheres.tick(&server);
        harness::run(&server, &gm.p, "sphere list 0").await;
        assert_eq!(
            gm.chat(),
            ["[Sphere] Quest 100 - Component: 1001 - TriggerSphere  xyz:<110, 100, 100> , radius:5m, at 10.0m away"]
        );
        // C#: the filter compares the quest object id (no active quest -> 0).
        harness::run(&server, &gm.p, "sphere list 100").await;
        assert_eq!(
            gm.chat(),
            ["[Sphere] No active sphere triggers found for QuestId 100"]
        );
        let _ = std::fs::remove_dir_all(&app);
    }
}
