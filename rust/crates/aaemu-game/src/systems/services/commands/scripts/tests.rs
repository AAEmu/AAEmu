//! The registered command set equals the C# one (`Scripts/Commands/*.cs`: `CommandNames` and the
//! names each `OnLoad` registers), and the collisions resolve as in C#.

use super::harness;
use super::ScriptEnv;
use crate::systems::services::commands::CommandManager;

/// `(file, CommandNames, names registered by OnLoad)` of every `Scripts/Commands/*.cs`, in the
/// C# type (directory) order.
const CSHARP_COMMANDS: &[(&str, &[&str], &[&str])] = &[
    (
        "AddBadges",
        &["vocation", "vocationpoints", "add_vp", "add_vb"],
        &["vocation", "vocationpoints", "add_vp", "add_vb"],
    ),
    (
        "AddBuff",
        &["buff", "addbuff", "add_buff", "buffs"],
        &["buff", "addbuff", "add_buff", "buffs"],
    ),
    (
        "AddGold",
        &["gold", "addgold", "add_gold"],
        &["gold", "addgold", "add_gold"],
    ),
    (
        "AddLabor",
        &["labor", "addlabor", "add_labor"],
        &["labor", "addlabor", "add_labor"],
    ),
    (
        "AddPortals",
        &["register_portal", "add_portal", "addportal"],
        &["register_portal", "add_portal", "addportal"],
    ),
    (
        "AddXP",
        &["xp", "add_xp", "addxp", "givexp"],
        &["xp", "add_xp", "addxp", "givexp"],
    ),
    ("Announce", &["announce"], &["announce"]),
    (
        "Appellation",
        &["title", "addtitle", "add_title", "appellation"],
        &["title", "addtitle", "add_title", "appellation"],
    ),
    ("Around", &["around", "near"], &["around", "near"]),
    ("AStarCmd", &["pathfind", "pf"], &["pathfind", "pf"]),
    (
        "BuildHouse",
        &["build", "build_house"],
        &["build", "build_house"],
    ),
    (
        "ChangeLevel",
        &[
            "level",
            "setlevel",
            "set_level",
            "change_level",
            "changelevel",
        ],
        &[
            "level",
            "setlevel",
            "set_level",
            "change_level",
            "changelevel",
        ],
    ),
    (
        "ClearCombat",
        &["clearcombat", "clear_combat", "cc"],
        &["clearcombat", "clear_combat", "cc"],
    ),
    ("CofferActions", &["coffer", "chest"], &["coffer", "chest"]),
    ("CrimeCmd", &["doodad"], &["crime"]),
    ("Damage", &["damage"], &["damage"]),
    ("Despawn", &["despawn"], &["despawn"]),
    (
        "DeSpawnAll",
        &["despawnall", "despawn_all"],
        &["despawnall", "despawn_all"],
    ),
    ("DisconnectMe", &["disconnectme"], &["disconnectme"]),
    ("Dist", &["distance", "dist"], &["distance", "dist"]),
    ("DoodadCmd", &["doodad"], &["doodad"]),
    (
        "DoodadLocationCmd",
        &["doodad_location", "doodadlocation", "dloc"],
        &["doodad_location", "doodadlocation", "dloc"],
    ),
    (
        "FailSkill",
        &["failskill", "fail_skill", "skillfail", "skill_fail"],
        &["failskill", "fail_skill", "skillfail", "skill_fail"],
    ),
    ("FeatureCmd", &["feature"], &["feature", "fset", "fs"]),
    (
        "FindObject",
        &["findobject", "findobj"],
        &["findobject", "findobj"],
    ),
    ("FishFinderCmd", &["fishfinder"], &["fishfinder"]),
    ("Fly", &["fly"], &["fly"]),
    (
        "GetAttribute",
        &["getattribute", "getattr", "attr"],
        &["getattribute", "getattr", "attr"],
    ),
    ("GetPosition", &["position", "pos"], &["position", "pos"]),
    ("GimmickCmd", &["gimmick"], &["gimmick"]),
    ("GodMode", &["godmode"], &["godmode"]),
    ("GoldCmd", &["gold"], &["gold"]),
    ("Heal", &["heal"], &["heal"]),
    ("Height", &["height"], &["height"]),
    ("Help", &["help", "?"], &["help", "?"]),
    (
        "HouseBindingMove",
        &["house_binding_move", "housebindingmove"],
        &["house_binding_move", "housebindingmove"],
    ),
    (
        "IgnoreCooldowns",
        &[
            "ignoreskillcds",
            "disablecooldowns",
            "ignorecooldowns",
            "ignorecd",
        ],
        &[
            "ignoreskillcds",
            "disablecooldowns",
            "ignorecooldowns",
            "ignorecd",
        ],
    ),
    (
        "InGameCashShop",
        &["ingamecashshop", "ics"],
        &["ingamecashshop", "ics"],
    ),
    ("Invisible", &["invisible"], &["invisible"]),
    ("ItemCmd", &["item"], &["item"]),
    ("Kick", &["kick_player", "kick"], &["kick_player", "kick"]),
    ("Kill", &["kill"], &["kill"]),
    (
        "Kit",
        &["kit", "addkit", "add_kit"],
        &["kit", "addkit", "add_kit"],
    ),
    ("Move", &["move"], &["move"]),
    (
        "MoveAll",
        &["moveall", "move_all"],
        &["moveall", "move_all"],
    ),
    ("MoveTo", &["moveto"], &["moveto"]),
    ("NpcCmd", &["npc"], &["npc"]),
    ("Nudge", &["nudge"], &["nudge"]),
    ("Nwrite", &["nwrite", "nw"], &["nwrite", "nw"]),
    (
        "ObjectPosition",
        &["setpos", "npcloc", "npcpos", "doodadloc", "doodadpos"],
        &["setpos", "npcloc", "npcpos", "doodadloc", "doodadpos"],
    ),
    (
        "ObjectRotation",
        &["setrot", "npcrot", "doodadrot"],
        &["setrot", "npcrot", "doodadrot"],
    ),
    (
        "Online",
        &["online", "list_online"],
        &["online", "list_online"],
    ),
    (
        "PingPosition",
        &["pingpos", "ping_pos", "pingposition"],
        &["pingpos", "ping_pos", "pingposition"],
    ),
    ("QuestCmd", &["quest"], &["quest"]),
    (
        "ReloadAuction",
        &["reloadauction", "reload_auction", "reloadah", "reload_ah"],
        &["reloadauction", "reload_auction", "reloadah", "reload_ah"],
    ),
    (
        "ReloadConfigs",
        &["reloadconfig", "reload_configs", "reload_configurations"],
        &["reloadconfig", "reload_configs", "reload_configurations"],
    ),
    (
        "ResetSkillCooldowns",
        &["resetcd", "resetskillcooldowns", "rcd"],
        &["resetcd", "resetskillcooldowns", "rcd"],
    ),
    ("Revive", &["revive"], &["revive"]),
    ("Rotate", &["rotate", "lookatme"], &["rotate", "lookatme"]),
    ("Run", &["run"], &["run"]),
    ("Scripts", &["scripts"], &["scripts"]),
    ("SendPacket", &["packet"], &["packet"]),
    (
        "SetFaction",
        &["setfaction", "set_faction"],
        &["setfaction", "set_faction"],
    ),
    (
        "SetTradePackMailDelay",
        &["settradepackmaildelay"],
        &["settradepackmaildelay"],
    ),
    (
        "ShipBarrierCmd",
        &["shipbarrier", "sb"],
        &["shipbarrier", "sb"],
    ),
    (
        "ShowInventory",
        &[
            "inventory",
            "showinv",
            "show_inv",
            "showinventory",
            "show_inventory",
        ],
        &[
            "inventory",
            "showinv",
            "show_inv",
            "showinventory",
            "show_inventory",
        ],
    ),
    ("SlaveCmd", &["slave"], &["slave"]),
    ("Snow", &["snow"], &["snow"]),
    (
        "SoloParty",
        &["soloparty", "solo_party"],
        &["soloparty", "solo_party"],
    ),
    ("Spawn", &["spawn"], &["spawn"]),
    (
        "SpawnGrid",
        &["spawngrid", "spawngroup"],
        &["spawngrid", "spawngroup"],
    ),
    ("Sphere", &["sphere"], &["sphere"]),
    ("Teleport", &["teleport"], &["teleport"]),
    ("TestAI", &["testai", "ai"], &["testai", "ai"]),
    (
        "TestChatChannel",
        &["testchatchannel", "test_chat_channel", "testchat"],
        &["testchatchannel", "test_chat_channel", "testchat"],
    ),
    (
        "TestCombat",
        &["testcombat", "test_combat"],
        &["testcombat", "test_combat"],
    ),
    ("TestEcho", &["echo"], &["echo"]),
    (
        "TestFSets",
        &["testfsets", "test_fsets"],
        &["testfsets", "test_fsets"],
    ),
    (
        "TestHeight",
        &["testheightvisualizer", "test_height_visualizer"],
        &["testheightvisualizer", "test_height_visualizer"],
    ),
    (
        "TestHouse",
        &["house", "test_house", "testhouse"],
        &["house", "test_house", "testhouse"],
    ),
    (
        "TestMails",
        &["testmail", "test_mail"],
        &["testmail", "test_mail"],
    ),
    (
        "TestNavMesh",
        &["testnavmesh", "test_navmesh"],
        &["testnavmesh", "test_navmesh"],
    ),
    (
        "TestSlave",
        &["testslave", "test_slave"],
        &["testslave", "test_slave"],
    ),
    (
        "TestTracker",
        &["testtracker", "track", "tt"],
        &["testtracker", "track", "tt"],
    ),
    (
        "TestTransfer",
        &["testtransfer", "test_transfer"],
        &["testtransfer", "test_transfer"],
    ),
    (
        "TestZoneState",
        &["zonestate", "zone_state"],
        &["zonestate", "zone_state"],
    ),
    (
        "TickDoodad",
        &["tickdoodad", "tick_doodad"],
        &["tickdoodad", "tick_doodad"],
    ),
    ("TimeCmd", &["time"], &["time"]),
    (
        "TowerDef",
        &["towerdef", "tower_def"],
        &["towerdef", "tower_def"],
    ),
    (
        "UseSkill",
        &["useskill", "use_skill", "testskill", "test_skill"],
        &["useskill", "use_skill", "testskill", "test_skill"],
    ),
    (
        "WaterDebugCmd",
        &["waterdebug", "water_debug"],
        &["waterdebug", "water_debug"],
    ),
    ("WorldCmd", &["world"], &["world"]),
];

#[test]
fn registered_commands_match_the_csharp_scripts() {
    let commands: Vec<_> = super::all_commands(&ScriptEnv::default())
        .into_iter()
        .filter(|(names, _)| *names != ["dominion"] && *names != ["collectdominion"])
        .collect();
    assert_eq!(commands.len(), CSHARP_COMMANDS.len());
    for ((registered, command), (file, names, onload)) in commands.iter().zip(CSHARP_COMMANDS) {
        assert_eq!(registered, onload, "{file}: OnLoad names");
        assert_eq!(&command.command_names(), names, "{file}: CommandNames");
    }
}

/// Reads the C# sources (when the worktree has them) and checks the table above.
#[test]
fn csharp_table_matches_the_csharp_sources() {
    let dir = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../../AAEmu.Game/Scripts/Commands");
    let Ok(entries) = std::fs::read_dir(&dir) else {
        return;
    };
    let mut files: Vec<String> = entries
        .filter_map(|e| e.ok())
        .map(|e| e.file_name().to_string_lossy().into_owned())
        .filter(|f| f.ends_with(".cs"))
        .collect();
    // NTFS directory order (upper-cased ordinal).
    files.sort_by_key(|f| f.to_uppercase());
    let stems: Vec<&str> = files.iter().map(|f| f.trim_end_matches(".cs")).collect();
    let table: Vec<&str> = CSHARP_COMMANDS.iter().map(|(f, _, _)| *f).collect();
    assert_eq!(stems, table);
    for (file, names, _) in CSHARP_COMMANDS {
        let src = std::fs::read_to_string(dir.join(format!("{file}.cs"))).expect("read");
        let start = src
            .find("CommandNames { get; set; } = [")
            .expect("CommandNames")
            + 30;
        let end = start + src[start..].find(']').expect("]");
        let parsed: Vec<String> = src[start..end]
            .split(',')
            .map(|s| s.trim().trim_matches('"').to_string())
            .collect();
        assert_eq!(&parsed, names, "{file}");
    }
}

#[test]
fn load_resolves_collisions_like_csharp() {
    let m = CommandManager::default();
    assert!(super::load(&m, &ScriptEnv::default()));
    let keys = m.get_command_keys();
    // Original commands plus the Rust Dominion administration and collection commands.
    assert_eq!(keys.len(), 93);
    // `GoldCmd` (registered after `AddGold`) owns `gold`, and `AddGold`'s aliases lead to it.
    for alias in ["gold", "addgold", "add_gold"] {
        let c = m.get_command_interface_by_name(alias).expect(alias);
        assert!(c.as_sub_command().is_some(), "{alias}");
        assert_eq!(c.get_command_help_text(), "/gold/");
    }
    // `CrimeCmd` registers `crime` (its CommandNames say `doodad`).
    let crime = m.get_command_interface_by_name("crime").expect("crime");
    assert_eq!(crime.get_command_help_text(), "/doodad");
    let doodad = m.get_command_interface_by_name("doodad").expect("doodad");
    assert_eq!(doodad.command_names(), ["doodad"]);
    // `FeatureCmd` registers its aliases although CommandNames only has `feature`.
    assert!(m.get_command_interface_by_name("fs").is_some());
    assert_eq!(m.get_command_name_base("fset"), "feature");
}

#[tokio::test]
async fn harness_runs_a_command() {
    let server = harness::server();
    let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
    let (handled, out) = harness::run(&server, &gm.p, "echo hello  world").await;
    assert!(handled);
    assert!(out.is_empty());
    assert_eq!(gm.chat(), ["|cFFFFFFFF[Echo]|r hello world "]);
}
