//! The GM commands of `Scripts/Commands/**` and `Scripts/SubCommands/**` (the C# script
//! assembly). [`load`] is the `ScriptReflector.Reflect` / `ScriptCompiler.Compile` step: it
//! creates every command and calls its `OnLoad` (`CommandManager.Register`) in the C# type
//! order (the source files in directory order), so name collisions resolve like in C#
//! (`GoldCmd` replaces `AddGold` under `gold`; `AddGold`'s aliases then reach `GoldCmd`).
//!
//! One module per C# command file (snake case); the `SubCommandBase` roots are in [`roots`]
//! (World / Time / FishFinder / Feature / AStar, whose sub commands are in `commands::sub`) and
//! in [`sub`] next to their sub commands.

use std::path::PathBuf;
use std::sync::Arc;

use super::{Command, CommandManager};

#[cfg(test)]
pub mod harness;
pub mod roots;
pub mod sub;
#[cfg(test)]
mod tests;
pub mod util;

pub mod add_badges;
pub mod add_buff;
pub mod add_gold;
pub mod add_labor;
pub mod add_portals;
pub mod add_xp;
pub mod announce;
pub mod appellation;
pub mod around;
pub mod build_house;
pub mod change_level;
pub mod clear_combat;
pub mod coffer_actions;
pub mod damage;
pub mod de_spawn_all;
pub mod despawn;
pub mod disconnect_me;
pub mod dist;
pub mod dominion;
pub mod doodad_location_cmd;
pub mod dungeon;
pub mod fail_skill;
pub mod find_object;
pub mod fly;
pub mod get_attribute;
pub mod get_position;
pub mod god_mode;
pub mod heal;
pub mod height;
pub mod help;
pub mod house_binding_move;
pub mod ignore_cooldowns;
pub mod in_game_cash_shop;
pub mod invisible;
pub mod kick;
pub mod kill;
pub mod kit;
pub mod move_all;
pub mod move_cmd;
pub mod move_to;
pub mod nudge;
pub mod nwrite;
pub mod object_position;
pub mod object_rotation;
pub mod online;
pub mod ping_position;
pub mod quest_cmd;
pub mod reload_auction;
pub mod reload_configs;
pub mod reset_skill_cooldowns;
pub mod revive;
pub mod rotate;
pub mod run;
pub mod scripts_cmd;
pub mod send_packet;
pub mod set_faction;
pub mod set_trade_pack_mail_delay;
pub mod ship_barrier_cmd;
pub mod show_inventory;
pub mod snow;
pub mod solo_party;
pub mod spawn;
pub mod spawn_grid;
pub mod sphere;
pub mod teleport;
pub mod test_ai;
pub mod test_chat_channel;
pub mod test_combat;
pub mod test_echo;
pub mod test_fsets;
pub mod test_height;
pub mod test_house;
pub mod test_mails;
pub mod test_nav_mesh;
pub mod test_slave;
pub mod test_tracker;
pub mod test_transfer;
pub mod test_zone_state;
pub mod tick_doodad;
pub mod tower_def;
pub mod use_skill;
pub mod water_debug_cmd;

/// What the commands read at `OnLoad` (`FileManager.AppPath` for `kits.json`, ...).
#[derive(Debug, Clone, Default)]
pub struct ScriptEnv {
    /// `FileManager.AppPath`.
    pub app_path: PathBuf,
}

/// The common imports of the command modules.
pub mod prelude {
    pub use std::sync::Arc;

    pub use super::super::output::{Color, ICharacter, MessageOutput};
    pub use super::super::params::{
        NumericSubCommandParameter, ParameterValue, Parameters, StringSubCommandParameter,
        SubCommandParameter,
    };
    pub use super::super::subcommand::{optional_parameter, SubCommand, SubCommandBase};
    pub use super::super::{Command, CommandContext, CommandManager, COMMAND_PREFIX};
    pub use super::roots::RootCommand;
    pub use super::util::*;
    pub use super::ScriptEnv;
    pub use crate::server::GameServer;
    pub use crate::systems::social::chat::chat_type;
    pub use crate::world::{PlayerRef, WorldObject};

    /// `string[] CommandNames`.
    pub fn names(n: &[&str]) -> Vec<String> {
        n.iter().map(|s| s.to_string()).collect()
    }
}

/// Every command with the names its `OnLoad` registers, in C# type order.
pub fn all_commands(env: &ScriptEnv) -> Vec<(&'static [&'static str], Arc<dyn Command>)> {
    vec![
        (&["dungeon"], Arc::new(dungeon::DungeonCommand)),
        // AddBadges.cs
        (
            &["vocation", "vocationpoints", "add_vp", "add_vb"],
            Arc::new(add_badges::AddBadges::new(env)),
        ),
        // AddBuff.cs
        (
            &["buff", "addbuff", "add_buff", "buffs"],
            Arc::new(add_buff::AddBuff::new(env)),
        ),
        // AddGold.cs
        (
            &["gold", "addgold", "add_gold"],
            Arc::new(add_gold::AddGold::new(env)),
        ),
        // AddLabor.cs
        (
            &["labor", "addlabor", "add_labor"],
            Arc::new(add_labor::AddLabor::new(env)),
        ),
        // AddPortals.cs
        (
            &["register_portal", "add_portal", "addportal"],
            Arc::new(add_portals::AddPortals::new(env)),
        ),
        // AddXP.cs
        (
            &["xp", "add_xp", "addxp", "givexp"],
            Arc::new(add_xp::AddXP::new(env)),
        ),
        // Announce.cs
        (&["announce"], Arc::new(announce::Announce::new(env))),
        // Appellation.cs
        (
            &["title", "addtitle", "add_title", "appellation"],
            Arc::new(appellation::Appellation::new(env)),
        ),
        // Around.cs
        (&["around", "near"], Arc::new(around::Around::new(env))),
        // AStarCmd.cs
        (&["pathfind", "pf"], Arc::new(roots::astar_cmd())),
        // BuildHouse.cs
        (
            &["build", "build_house"],
            Arc::new(build_house::BuildHouse::new(env)),
        ),
        // ChangeLevel.cs
        (
            &[
                "level",
                "setlevel",
                "set_level",
                "change_level",
                "changelevel",
            ],
            Arc::new(change_level::ChangeLevel::new(env)),
        ),
        // ClearCombat.cs
        (
            &["clearcombat", "clear_combat", "cc"],
            Arc::new(clear_combat::ClearCombat::new(env)),
        ),
        // CofferActions.cs
        (
            &["coffer", "chest"],
            Arc::new(coffer_actions::CofferActions::new(env)),
        ),
        // CrimeCmd.cs
        (&["crime"], Arc::new(sub::crimes::crime_cmd())),
        // Damage.cs
        (&["damage"], Arc::new(damage::Damage::new(env))),
        // Despawn.cs
        (&["despawn"], Arc::new(despawn::Despawn::new(env))),
        // DeSpawnAll.cs
        (
            &["despawnall", "despawn_all"],
            Arc::new(de_spawn_all::DeSpawnAll::new(env)),
        ),
        // DisconnectMe.cs
        (
            &["disconnectme"],
            Arc::new(disconnect_me::DisconnectMe::new(env)),
        ),
        // Dist.cs
        (&["distance", "dist"], Arc::new(dist::Dist::new(env))),
        // DoodadCmd.cs
        (&["doodad"], Arc::new(sub::doodads::doodad_cmd())),
        // DoodadLocationCmd.cs
        (
            &["doodad_location", "doodadlocation", "dloc"],
            Arc::new(doodad_location_cmd::DoodadLocationCmd::new(env)),
        ),
        // FailSkill.cs
        (
            &["failskill", "fail_skill", "skillfail", "skill_fail"],
            Arc::new(fail_skill::FailSkill::new(env)),
        ),
        // FeatureCmd.cs
        (&["feature", "fset", "fs"], Arc::new(roots::feature_cmd())),
        // FindObject.cs
        (
            &["findobject", "findobj"],
            Arc::new(find_object::FindObject::new(env)),
        ),
        // FishFinderCmd.cs
        (&["fishfinder"], Arc::new(roots::fish_finder_cmd())),
        // Fly.cs
        (&["fly"], Arc::new(fly::Fly::new(env))),
        // GetAttribute.cs
        (
            &["getattribute", "getattr", "attr"],
            Arc::new(get_attribute::GetAttribute::new(env)),
        ),
        // GetPosition.cs
        (
            &["position", "pos"],
            Arc::new(get_position::GetPosition::new(env)),
        ),
        // GimmickCmd.cs
        (&["gimmick"], Arc::new(sub::gimmick::gimmick_cmd())),
        // GodMode.cs
        (&["godmode"], Arc::new(god_mode::GodMode::new(env))),
        // GoldCmd.cs
        (&["gold"], Arc::new(sub::gold::gold_cmd())),
        // Heal.cs
        (&["heal"], Arc::new(heal::Heal::new(env))),
        // Height.cs
        (&["height"], Arc::new(height::Height::new(env))),
        // Help.cs
        (&["help", "?"], Arc::new(help::Help::new(env))),
        // HouseBindingMove.cs
        (
            &["house_binding_move", "housebindingmove"],
            Arc::new(house_binding_move::HouseBindingMove::new(env)),
        ),
        // IgnoreCooldowns.cs
        (
            &[
                "ignoreskillcds",
                "disablecooldowns",
                "ignorecooldowns",
                "ignorecd",
            ],
            Arc::new(ignore_cooldowns::IgnoreCooldowns::new(env)),
        ),
        // InGameCashShop.cs
        (
            &["ingamecashshop", "ics"],
            Arc::new(in_game_cash_shop::InGameCashShop::new(env)),
        ),
        // Invisible.cs
        (&["invisible"], Arc::new(invisible::Invisible::new(env))),
        // ItemCmd.cs
        (&["item"], Arc::new(sub::items::item_cmd())),
        // Kick.cs
        (&["kick_player", "kick"], Arc::new(kick::Kick::new(env))),
        // Kill.cs
        (&["kill"], Arc::new(kill::Kill::new(env))),
        // Kit.cs
        (
            &["kit", "addkit", "add_kit"],
            Arc::new(kit::AddKit::new(env)),
        ),
        // Move.cs
        (&["move"], Arc::new(move_cmd::Move::new(env))),
        // MoveAll.cs
        (
            &["moveall", "move_all"],
            Arc::new(move_all::MoveAll::new(env)),
        ),
        // MoveTo.cs
        (&["moveto"], Arc::new(move_to::MoveTo::new(env))),
        // NpcCmd.cs
        (&["npc"], Arc::new(sub::npcs::npc_cmd())),
        // Nudge.cs
        (&["nudge"], Arc::new(nudge::Nudge::new(env))),
        // Nwrite.cs
        (&["nwrite", "nw"], Arc::new(nwrite::Nwrite::new(env))),
        // ObjectPosition.cs
        (
            &["setpos", "npcloc", "npcpos", "doodadloc", "doodadpos"],
            Arc::new(object_position::ObjectPosition::new(env)),
        ),
        // ObjectRotation.cs
        (
            &["setrot", "npcrot", "doodadrot"],
            Arc::new(object_rotation::ObjectRotation::new(env)),
        ),
        // Online.cs
        (
            &["online", "list_online"],
            Arc::new(online::Online::new(env)),
        ),
        // PingPosition.cs
        (
            &["pingpos", "ping_pos", "pingposition"],
            Arc::new(ping_position::PingPosition::new(env)),
        ),
        // QuestCmd.cs
        (&["quest"], Arc::new(quest_cmd::QuestCmd::new(env))),
        // ReloadAuction.cs
        (
            &["reloadauction", "reload_auction", "reloadah", "reload_ah"],
            Arc::new(reload_auction::ReloadAuction::new(env)),
        ),
        // ReloadConfigs.cs
        (
            &["reloadconfig", "reload_configs", "reload_configurations"],
            Arc::new(reload_configs::ReloadConfigs::new(env)),
        ),
        // ResetSkillCooldowns.cs
        (
            &["resetcd", "resetskillcooldowns", "rcd"],
            Arc::new(reset_skill_cooldowns::ResetSkillCooldowns::new(env)),
        ),
        // Revive.cs
        (&["revive"], Arc::new(revive::Revive::new(env))),
        // Rotate.cs
        (&["rotate", "lookatme"], Arc::new(rotate::Rotate::new(env))),
        // Run.cs
        (&["run"], Arc::new(run::Run::new(env))),
        // Scripts.cs
        (&["scripts"], Arc::new(scripts_cmd::Scripts::new(env))),
        // SendPacket.cs
        (&["packet"], Arc::new(send_packet::SendPacket::new(env))),
        // SetFaction.cs
        (
            &["setfaction", "set_faction"],
            Arc::new(set_faction::SetFaction::new(env)),
        ),
        // SetTradePackMailDelay.cs
        (
            &["settradepackmaildelay"],
            Arc::new(set_trade_pack_mail_delay::SetTradePackMailDelay::new(env)),
        ),
        // ShipBarrierCmd.cs
        (
            &["shipbarrier", "sb"],
            Arc::new(ship_barrier_cmd::ShipBarrierCmd::new(env)),
        ),
        // ShowInventory.cs
        (
            &[
                "inventory",
                "showinv",
                "show_inv",
                "showinventory",
                "show_inventory",
            ],
            Arc::new(show_inventory::ShowInventory::new(env)),
        ),
        // SlaveCmd.cs
        (&["slave"], Arc::new(sub::slaves::slave_cmd())),
        // Snow.cs
        (&["snow"], Arc::new(snow::Snow::new(env))),
        // SoloParty.cs
        (
            &["soloparty", "solo_party"],
            Arc::new(solo_party::SoloParty::new(env)),
        ),
        // Spawn.cs
        (&["spawn"], Arc::new(spawn::Spawn::new(env))),
        // SpawnGrid.cs
        (
            &["spawngrid", "spawngroup"],
            Arc::new(spawn_grid::SpawnGrid::new(env)),
        ),
        // Sphere.cs
        (&["sphere"], Arc::new(sphere::Sphere::new(env))),
        // Teleport.cs
        (&["teleport"], Arc::new(teleport::Teleport::new(env))),
        // TestAI.cs
        (&["testai", "ai"], Arc::new(test_ai::TestAI::new(env))),
        // TestChatChannel.cs
        (
            &["testchatchannel", "test_chat_channel", "testchat"],
            Arc::new(test_chat_channel::TestChatChannel::new(env)),
        ),
        // TestCombat.cs
        (
            &["testcombat", "test_combat"],
            Arc::new(test_combat::TestCombat::new(env)),
        ),
        // TestEcho.cs
        (&["echo"], Arc::new(test_echo::TestEcho::new(env))),
        // TestFSets.cs
        (
            &["testfsets", "test_fsets"],
            Arc::new(test_fsets::TestFSets::new(env)),
        ),
        // TestHeight.cs
        (
            &["testheightvisualizer", "test_height_visualizer"],
            Arc::new(test_height::TestHeight::new(env)),
        ),
        (&["dominion"], Arc::new(dominion::Dominion::new(env))),
        (
            &["collectdominion"],
            Arc::new(dominion::CollectDominion::new(env)),
        ),
        (&["joinsiege"], Arc::new(dominion::JoinSiege::new(env))),
        (&["leavesiege"], Arc::new(dominion::LeaveSiege::new(env))),
        // TestHouse.cs
        (
            &["house", "test_house", "testhouse"],
            Arc::new(test_house::TestHouse::new(env)),
        ),
        // TestMails.cs
        (
            &["testmail", "test_mail"],
            Arc::new(test_mails::TestMails::new(env)),
        ),
        // TestNavMesh.cs
        (
            &["testnavmesh", "test_navmesh"],
            Arc::new(test_nav_mesh::TestNavMesh::new(env)),
        ),
        // TestSlave.cs
        (
            &["testslave", "test_slave"],
            Arc::new(test_slave::TestSlave::new(env)),
        ),
        // TestTracker.cs
        (
            &["testtracker", "track", "tt"],
            Arc::new(test_tracker::TestTracker::new(env)),
        ),
        // TestTransfer.cs
        (
            &["testtransfer", "test_transfer"],
            Arc::new(test_transfer::TestTransfer::new(env)),
        ),
        // TestZoneState.cs
        (
            &["zonestate", "zone_state"],
            Arc::new(test_zone_state::TestZoneState::new(env)),
        ),
        // TickDoodad.cs
        (
            &["tickdoodad", "tick_doodad"],
            Arc::new(tick_doodad::TickDoodad::new(env)),
        ),
        // TimeCmd.cs
        (&["time"], Arc::new(roots::time_cmd())),
        // TowerDef.cs
        (
            &["towerdef", "tower_def"],
            Arc::new(tower_def::TowerDef::new(env)),
        ),
        // UseSkill.cs
        (
            &["useskill", "use_skill", "testskill", "test_skill"],
            Arc::new(use_skill::UseSkill::new(env)),
        ),
        // WaterDebugCmd.cs
        (
            &["waterdebug", "water_debug"],
            Arc::new(water_debug_cmd::WaterDebugCmd::new(env)),
        ),
        // WorldCmd.cs
        (&["world"], Arc::new(roots::world_cmd())),
    ]
}

/// `ScriptReflector.Reflect()`: creates the commands and registers them (`OnLoad`).
pub fn load(manager: &CommandManager, env: &ScriptEnv) -> bool {
    for (names, command) in all_commands(env) {
        manager.register_names(names, command);
    }
    true
}

/// Installs [`load`] as the command manager's script loader (`GameService` scripts stage).
pub fn install(manager: &CommandManager, env: ScriptEnv) {
    manager.set_loader(Arc::new(move |m: &CommandManager| load(m, &env)));
}
