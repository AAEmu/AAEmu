//! The social parts of the character session (`world::session` calls these at the C# call
//! sites): `IsOnline` (team offline handling), `CSNotifyInGamePacket` (team reconnect, jury
//! queue), `WorldManager.OnPlayerJoin` (family login), `LeaveWorldTask` (family logout),
//! `Character.SetPosition` (party position updates) and the party / raid / trial chat routing
//! of `CSSendChatMessagePacket`.

use crate::packets::world::{ChatSender, SCChatMessagePacket};
use crate::server::GameServer;
use crate::systems::social::chat::chat_type;
use crate::systems::social::util::{error, send_error};
use crate::world::PlayerRef;

/// `Character.IsOnline` setter, after the flag changed (`if (!value) SetOffline(this)`).
pub fn on_online_changed(server: &GameServer, p: &PlayerRef, value: bool) {
    if !value {
        server.systems.social.trials.handle_player_logout(server, p);
        server.chat.leave_user_channels(p, &server.ids.chat);
        server.systems.social.teams.set_offline(server, p);
    }
}

/// `CSNotifyInGamePacket`: `TeamManager.UpdateAtLogin` (before the guild login).
pub fn on_notify_in_game_team(server: &GameServer, p: &PlayerRef) {
    server.systems.social.teams.update_at_login(server, p);
}

/// `CSNotifyInGamePacket`: `TrialManager.HandlePlayerLogin` (after the guild login).
pub fn on_notify_in_game_trial(server: &GameServer, p: &PlayerRef) {
    crate::systems::social::trial::handle_player_login(server, p);
    crate::systems::social::dominion::send_snapshots(server, p);
    crate::systems::housing::dominion::participants::sync_player(server, p);
}

/// `CSNotifyInGamePacket`: the achievement entry snapshot (`AchievementManager` login path):
/// back-credits the character's current state, pushes the touched `SCAchievementsPacket` list and
/// pays every reward still owed. Runs after the other login steps so the team / expedition /
/// family memberships it back-credits are already restored.
pub fn on_notify_in_game_achievements(server: &GameServer, p: &PlayerRef) {
    crate::systems::social::achievement_hooks::initial_snapshot(server, p, chrono::Utc::now());
}

/// `WorldManager.OnPlayerJoin`: `FamilyManager.OnCharacterLogin` when in a family.
pub fn on_player_join(server: &GameServer, p: &PlayerRef) {
    if p.data().family > 0 {
        crate::systems::social::family::on_character_login(server, p);
    }
}

/// `LeaveWorldTask`: `FamilyManager.OnCharacterLogout` when in a family (after the chat
/// channels were left, before the guild logout).
pub fn on_leave_world_family(server: &GameServer, p: &PlayerRef) {
    if p.data().family > 0 {
        crate::systems::social::family::on_character_logout(server, p);
    }
}

/// `Character.SetPosition` (moved): `TeamManager.UpdatePosition(Id)` when `InParty`.
pub fn on_moved(server: &GameServer, p: &PlayerRef) {
    if p.data().in_party {
        server
            .systems
            .social
            .teams
            .update_position(server, p.character_id);
    }
}

/// `CSSendChatMessagePacket` for `ChatType.Raid` / `RaidLeader` / `Party` / `Judge`. Returns
/// `false` for other chat types.
pub fn route_chat(
    server: &GameServer,
    me: &PlayerRef,
    chat: i16,
    sender: &ChatSender,
    message: &str,
    ability: i32,
    language_type: u8,
) -> bool {
    match chat {
        chat_type::RAID_LEADER | chat_type::RAID => {
            match server
                .systems
                .social
                .teams
                .active_team_by_unit(me.character_id)
            {
                Some(team) => {
                    if chat == chat_type::RAID_LEADER && team.owner_id != me.character_id {
                        send_error(me, error::CHAT_NOT_RAID_OWNER);
                    } else {
                        crate::systems::social::team::raid_chat(server, &team).send_chat_packet(
                            &SCChatMessagePacket {
                                chat_type: chat,
                                sender: Some(sender.clone()),
                                message: message.to_string(),
                                ability,
                                language_type,
                            },
                        );
                    }
                }
                None => send_error(me, error::CHAT_NOT_IN_RAID),
            }
            true
        }
        chat_type::PARTY => {
            match server
                .systems
                .social
                .teams
                .active_team_by_unit(me.character_id)
            {
                Some(team) => {
                    crate::systems::social::team::party_chat(server, &team, me.character_id)
                        .send_message(
                            Some(sender),
                            |_| sender.clone(),
                            message,
                            ability,
                            language_type,
                        );
                }
                None => send_error(me, error::CHAT_NOT_IN_PARTY),
            }
            true
        }
        chat_type::JUDGE => {
            if let Some(ch) = crate::systems::social::trial::trial_chat(server, me) {
                ch.send_message(
                    Some(sender),
                    |_| sender.clone(),
                    message,
                    ability,
                    language_type,
                );
            }
            true
        }
        _ => false,
    }
}

/// The guild and family part of `CharacterManager.DeleteCharacterAssets(character, ..)`
/// (`ExpeditionManager.Leave`, `FamilyManager.LeaveFamily`) for a deleted lobby character.
pub async fn delete_character_assets(server: &GameServer, c: &crate::models::character::Character) {
    if c.unit.expedition_id != 0 && server.expeditions.exists(c.unit.expedition_id) {
        crate::systems::social::expedition::leave_offline(
            server,
            c.id,
            &c.unit.name,
            c.unit.expedition_id,
        )
        .await;
    }
    if c.family > 0 {
        crate::systems::social::family::leave_family_offline(server, c.id, c.family).await;
    }
}
