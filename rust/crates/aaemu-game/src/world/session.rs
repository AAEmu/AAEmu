//! Character session logic that spans several managers: selecting a character
//! (`CSSelectCharacterPacket`), spawning (`CSSpawnCharacterPacket`, `CSNotifyInGamePacket`,
//! `CSNotifyInGameCompletedPacket`, `Character.Spawn`), zone changes (`Character.OnZoneChange`),
//! movement (`CSMoveUnitPacket`), chat (`CSSendChatMessagePacket`), leaving the world
//! (`EnterWorldManager.Leave` / `LeaveWorldTask`) and disconnects
//! (`GameConnection.OnDisconnect` / `SaveAndRemoveFromWorld`).

use std::sync::Arc;
use std::time::Duration;

use chrono::Utc;

use crate::db_character::{
    add_buff_by_id, apply_active_buffs, fetch_active_buffs, load_character_details,
    offline_blocked_infos, offline_friend_infos, save_directly_to_database, SavedBuffRow,
};
use crate::error::Result;
use crate::managers::chat::chat_type;
use crate::managers::expedition::MemberRefresh;
use crate::managers::friend::format_friend;
use crate::models::buff::{Buff, SkillCaster, BUFF_LOGGED_ON};
use crate::models::char_data::VisualOptions;
use crate::models::char_data::{BlockedInfo, FriendInfo};
use crate::models::character::{Character, DEFAULT_INSTANCE_ID};
use crate::models::container::slot_type;
use crate::models::item::Item;
use crate::models::movement::{MoveKind, MoveType};
use crate::net::connection::{ConnCommand, GameConnection, GameState};
use crate::packets::proxy::ChangeStatePacket;
use crate::packets::world::*;
use crate::server::GameServer;
use crate::world::{broadcast, unit_state, Player, PlayerRef, WorldInstance};

/// `LeaveWorldTargetType`.
pub mod leave_target {
    pub const QUIT_GAME: u8 = 0;
    pub const CHARACTER_SELECT: u8 = 1;
    pub const SERVER_SELECT: u8 = 2;
}

fn now_ms() -> i64 {
    Utc::now().timestamp_millis()
}

/// The `Character` fields `SCChatMessagePacket` uses.
pub fn chat_sender(c: &Character, obj_id: u32) -> ChatSender {
    ChatSender {
        faction_id: c.faction_id,
        obj_id,
        id: c.id,
        race: c.race,
        name: c.name.clone(),
        gm: false,
    }
}

/// `Character.SendMessage(type, message)` (system text, no sender).
pub fn send_message(p: &PlayerRef, chat_type: i16, message: &str) {
    p.send(SCChatMessagePacket {
        chat_type,
        sender: None,
        message: message.to_string(),
        ability: 0,
        language_type: 0,
    });
}

/// `Faction.MotherId` of a faction id (0 when the faction is unknown).
fn mother_id(server: &GameServer, faction_id: u32) -> u32 {
    server
        .game_data
        .factions
        .get(faction_id)
        .map_or(0, |f| f.mother_id)
}

/// `FriendMananger.SendStatusChange(unit, forOnline, value)`.
fn send_friend_status_change(server: &GameServer, p: &PlayerRef, for_online: bool, value: bool) {
    let owners = server.friends.owners_of(p.character_id);
    if owners.is_empty() {
        return;
    }
    let mut info = format_friend(&p.data(), false);
    if for_online {
        info.is_online = value;
    } else {
        info.in_party = value;
    }
    for owner in owners {
        if let Some(o) = server.world.character_by_id(owner) {
            o.send(SCFriendStatusChangedPacket {
                friend: info.clone(),
            });
        }
    }
}

/// `Character.IsOnline` setter.
pub fn set_online(server: &GameServer, p: &PlayerRef, value: bool) {
    if p.data().is_online == value {
        return;
    }
    send_friend_status_change(server, p, true, value);
    p.data().is_online = value;
    // !value -> TeamManager.SetOffline (teams are a later phase).
}

/// `Expedition.SendPacket`: every online member.
fn send_to_expedition(
    server: &GameServer,
    members: &[u32],
    packet: &SCExpeditionMemberStatusChangedPacket,
) {
    for id in members {
        if let Some(p) = server.world.character_by_id(*id) {
            p.send(packet.clone());
        }
    }
}

fn member_refresh(c: &Character) -> MemberRefresh {
    MemberRefresh {
        character_id: c.id,
        name: c.name.clone(),
        level: c.level,
        zone_id: c.transform.zone_id,
        abilities: [c.ability1, c.ability2, c.ability3],
        position: (c.transform.x, c.transform.y, c.transform.z),
    }
}

/// `Expedition.OnCharacterRefresh` / the member part of `OnCharacterLogin`.
fn expedition_refresh(server: &GameServer, p: &PlayerRef) {
    let (exp_id, refresh) = {
        let c = p.data();
        (c.expedition_id, member_refresh(&c))
    };
    if exp_id == 0 {
        return;
    }
    if let Some((member, members)) =
        server
            .expeditions
            .refresh_member(exp_id, &refresh, Utc::now().timestamp())
    {
        send_to_expedition(
            server,
            &members,
            &SCExpeditionMemberStatusChangedPacket { member, flag: 0 },
        );
    }
}

/// `Character.OnZoneChange(lastZoneKey, newZoneKey)`.
pub fn on_zone_change(server: &GameServer, p: &PlayerRef, last_zone: u32, new_zone: u32) {
    let zones = &server.game_data.zones;
    let last = zones.zone_by_key(last_zone).cloned();
    let new = zones.zone_by_key(new_zone).cloned();
    let last_group = last.as_ref().map_or(0, |z| z.group_id as i16);
    let new_group = new.as_ref().map_or(0, |z| z.group_id as i16);
    if last_group != 0 {
        zone_chat(server, last_zone).leave(p);
    }
    let online = p.data().is_online;
    if new_group != 0 && online {
        zone_chat(server, new_zone).join(p);
    }
    if new.is_some() {
        expedition_refresh(server, p);
    }
    match new {
        Some(z) if !z.closed => {}
        Some(z) => {
            // Send extra info to player if we are still in a real but unreleased zone
            p.send(SCChatMessagePacket {
                chat_type: chat_type::SYSTEM,
                sender: None,
                message: format!(
                    "|cFFFF0000You have entered a closed zone ({} - {})!\nPlease leave immediately!|r",
                    z.zone_key, z.name
                ),
                ability: 0,
                language_type: 0,
            });
            // Later phase: the forced transport out of closed zones for access level < 100
            // (5 x SCErrorMsg(ClosedZone) every 2 s, then SCCharacterResurrected at the closest
            // return portal).
            tracing::warn!("{} entered closed zone {}", p.data().name, z.zone_key);
        }
        None => {
            tracing::warn!("{} entered unknown zone {new_zone}", p.data().name);
        }
    }
}

/// `ChatManager.GetZoneChat(zoneKey)`.
fn zone_chat(server: &GameServer, zone_key: u32) -> crate::managers::chat::ChannelRef {
    let zones = &server.game_data.zones;
    let group = zones.zone_by_key(zone_key).map_or(0, |z| z.group_id);
    let name = zones.group(group).map(|g| g.name.clone());
    server
        .chat
        .zone_chat(group, name.as_deref(), &server.ids.chat)
}

/// `Portal.Write` offsets (`ZoneManager.GetZoneOriginCell`).
fn portal_wire(server: &GameServer, p: &crate::data::world_data::Portal) -> PortalWire {
    let (ox, oy) = if p.zone_id != 0 {
        server
            .game_data
            .worlds
            .get_world_template_by_zone_key(p.zone_id)
            .and_then(|w| w.xml_world_zones().get(&p.zone_id))
            .map_or((0.0, 0.0), |z| (z.origin_x as f32, z.origin_y as f32))
    } else {
        (0.0, 0.0)
    };
    PortalWire {
        id: p.id,
        name: p.name.clone(),
        zone_id: p.zone_id,
        off_x: p.x - ox * 1024.0,
        off_y: p.y - oy * 1024.0,
        z: p.z,
        z_rot: p.z_rot,
    }
}

/// `Inventory.SendFragmentedInventory`.
fn send_fragmented_inventory(
    conn: &mut GameConnection,
    slot: u8,
    num_items: u8,
    bag: Vec<Option<Item>>,
) {
    if !bag.len().is_multiple_of(10) {
        tracing::warn!(
            "SendFragmentedInventory: Inventory Size not a multiple of 10 ({num_items})"
        );
    }
    if bag.len() != usize::from(num_items) {
        tracing::warn!(
            "SendFragmentedInventory: Inventory Size Mismatch; expected {num_items} got {}",
            bag.len()
        );
    }
    for (i, chunk) in bag.chunks(10).enumerate() {
        conn.send(&SCCharacterInvenContentsPacket {
            slot_type: slot,
            num_chunks: 1,
            start_chunk_idx: i as u8,
            items: chunk.to_vec(),
        });
    }
}

/// What the select path reads from MySQL besides `Character.Load()`: the offline friend /
/// blocked infos and the saved buffs (`LoadActiveBuffs`).
#[derive(Debug, Clone, Default)]
pub struct SelectDbData {
    pub offline_friends: Vec<FriendInfo>,
    pub offline_blocked: Vec<BlockedInfo>,
    pub saved_buffs: Vec<SavedBuffRow>,
}

/// `CSSelectCharacterPacket.Read`: the MySQL part, then [`select_loaded`].
pub async fn select_character(
    server: &GameServer,
    conn: &mut GameConnection,
    character_id: u32,
) -> Result<()> {
    let Some(idx) = conn.characters.iter().position(|c| c.id == character_id) else {
        tracing::error!(
            "Character {character_id} not found in list of loaded characters of this account {}",
            conn.account_id()
        );
        return Ok(());
    };
    let mut c = conn.characters[idx].clone();
    // Force player into main_world when coming from character select
    c.transform.instance_id = DEFAULT_INSTANCE_ID;
    load_character_details(server, &mut c).await?;
    let offline_friend_ids: Vec<u32> = c
        .friends
        .iter()
        .map(|f| f.friend_id)
        .filter(|id| server.world.character_by_id(*id).is_none())
        .collect();
    let offline_blocked_ids: Vec<u32> = c
        .blocked
        .iter()
        .map(|b| b.1)
        .filter(|id| server.world.character_by_id(*id).is_none())
        .collect();
    let db = SelectDbData {
        offline_friends: offline_friend_infos(&server.db, &offline_friend_ids).await?,
        offline_blocked: offline_blocked_infos(&server.db, &offline_blocked_ids).await?,
        saved_buffs: fetch_active_buffs(server, c.id).await,
    };
    select_loaded(server, conn, c, db)
}

/// The rest of `CSSelectCharacterPacket.Read` once `Character.Load()` is done (no MySQL access).
pub fn select_loaded(
    server: &GameServer,
    conn: &mut GameConnection,
    mut c: Character,
    db: SelectDbData,
) -> Result<()> {
    // Mates of all instances: later phase.
    let obj_id = match server.world.used_obj_id(c.id) {
        Some(o) => o,
        None => {
            let o = server
                .ids
                .object
                .get_next_id()
                .map_err(|e| crate::GameError::Protocol(e.to_string()))?;
            server.world.remember_obj_id(c.id, o);
            o
        }
    };
    c.obj_id = obj_id;
    let player = Player::new(c, conn.handle());
    conn.active_char = Some(player.clone());
    server.world.try_add_character(&player);
    // Slaves / simulation: later phase.

    let data = &server.game_data;
    let snapshot = player.data().clone();
    conn.send(&unit_state::character_state(&snapshot));
    conn.send(&SCCharacterGamePointsPacket {
        honor_point: snapshot.honor_point,
        vocation_point: snapshot.vocation_point,
    });
    // Inventory.Send
    conn.send(&SCCharacterInvenInitPacket {
        num_inven_slots: u32::from(snapshot.num_inventory_slots),
        num_bank_slots: snapshot.num_bank_slots as u32,
    });
    let bag = snapshot
        .inventory
        .bag()
        .lock()
        .expect("bag")
        .slotted_items();
    send_fragmented_inventory(
        conn,
        slot_type::INVENTORY,
        snapshot.num_inventory_slots,
        bag,
    );
    let bank = snapshot
        .inventory
        .warehouse()
        .lock()
        .expect("bank")
        .slotted_items();
    send_fragmented_inventory(conn, slot_type::BANK, snapshot.num_bank_slots as u8, bank);
    // SetInitialItemExpirationTimers: later phase (item timers).
    conn.send(&SCActionSlotsPacket {
        slots: snapshot.slots.clone(),
    });
    // Quests.Send / SendCompleted
    let now = now_ms();
    if snapshot.quests.active.len() <= 20 {
        conn.send(&SCQuestsPacket {
            quests: snapshot.quests.active.clone(),
            now_ms: now,
        });
    } else {
        for chunk in snapshot.quests.active.chunks(20) {
            conn.send(&SCQuestsPacket {
                quests: chunk.to_vec(),
                now_ms: now,
            });
        }
    }
    if snapshot.quests.completed.len() <= 200 {
        conn.send(&SCCompletedQuestsPacket {
            quests: snapshot.quests.completed.clone(),
        });
    } else {
        for chunk in snapshot.quests.completed.chunks(200) {
            conn.send(&SCCompletedQuestsPacket {
                quests: chunk.to_vec(),
            });
        }
    }
    conn.send(&SCActabilityPacket {
        last: true,
        actabilities: snapshot.actability.actabilities.clone(),
    });
    conn.send(&SCCountUnreadMailPacket {
        count: snapshot.unread_mail,
    });
    for chunk in snapshot.appellations.appellations.chunks(512) {
        conn.send(&SCAppellationsPacket {
            appellations: chunk
                .iter()
                .map(|&a| (a, a == snapshot.appellations.active))
                .collect(),
        });
    }
    // Portals.Send
    if !snapshot.portals.private_portals.is_empty() {
        conn.send(&SCCharacterPortalsPacket {
            portals: snapshot
                .portals
                .private_portals
                .iter()
                .map(|p| portal_wire(server, p))
                .collect(),
        });
    }
    if !snapshot.portals.district_portals.is_empty() {
        conn.send(&SCCharacterReturnDistrictsPacket {
            portals: snapshot
                .portals
                .district_portals
                .iter()
                .map(|p| portal_wire(server, p))
                .collect(),
            return_district_id: data
                .portals
                .district_return_point(snapshot.return_district_id, snapshot.faction_id),
        });
    }
    // Friends.Send / Blocked.Send (online infos first, then the DB rows)
    if !snapshot.friends.is_empty() {
        let mut infos = Vec::new();
        for f in &snapshot.friends {
            if let Some(o) = server.world.character_by_id(f.friend_id) {
                infos.push(format_friend(&o.data(), false));
            }
        }
        infos.extend(db.offline_friends.iter().cloned());
        conn.send(&SCFriendsPacket { friends: infos });
    }
    if !snapshot.blocked.is_empty() {
        let mut infos = Vec::new();
        for (_, b) in &snapshot.blocked {
            if let Some(o) = server.world.character_by_id(*b) {
                let c = o.data();
                infos.push(BlockedInfo {
                    character_id: c.id,
                    name: c.name.clone(),
                });
            }
        }
        infos.extend(db.offline_blocked.iter().cloned());
        conn.send(&SCBlockedUsersPacket { blocked: infos });
    }
    // Houses (SCMyHousePacket): later phase.
    for conflict in &data.zones.conflicts {
        conn.send(&SCConflictZoneStatePacket {
            zone_group_id: conflict.zone_group_id,
            state: conflict.current_state,
            end_time: conflict.next_state_time,
        });
    }
    // FactionManager.SendFactions / SendRelations
    if data.factions.factions.is_empty() {
        conn.send(&SCFactionListEmptyPacket);
    } else {
        for chunk in data.factions.factions.chunks(20) {
            conn.send(&SCFactionListPacket {
                factions: chunk.to_vec(),
            });
        }
    }
    if data.factions.relations.is_empty() {
        conn.send(&SCFactionRelationListEmptyPacket);
    } else {
        for chunk in data.factions.relations.chunks(200) {
            conn.send(&SCFactionRelationListPacket {
                relations: chunk.to_vec(),
            });
        }
    }
    // ExpeditionManager.SendExpeditions
    for chunk in server.expeditions.faction_chunks() {
        conn.send(&SCFactionListPacket { factions: chunk });
    }
    conn.send(&SCExpeditionRolePolicyListPacket {
        policies: Vec::new(),
    });
    if snapshot.expedition_id != 0 {
        if let Some(e) = server.expeditions.get(snapshot.expedition_id) {
            conn.send(&SCExpeditionRolePolicyListPacket {
                policies: e.policies.clone(),
            });
            let total = e.members.len() as u32;
            for chunk in e.members.chunks(20) {
                conn.send(&SCExpeditionMemberListPacket {
                    total,
                    id: e.faction.id,
                    members: chunk.to_vec(),
                });
            }
        }
    }
    for key in [1u16, 2, 5] {
        conn.send(&SCResponseUIDataPacket {
            character_id: snapshot.id,
            ui_data_type: key,
            ui_data: snapshot.get_option(key),
        });
    }

    {
        let now = now_ms();
        let mut c = player.data();
        add_buff_by_id(server, &mut c, BUFF_LOGGED_ON, now);
        if let Some(t) = data.characters.template(c.race, c.gender) {
            for &buff in &t.buffs {
                match data.skills.buff(buff) {
                    Some(bt) => {
                        let caster = SkillCaster {
                            kind: 0,
                            obj_id: c.obj_id,
                        };
                        let mut b = Buff::new(bt.clone(), caster, Some((c.id, c.level)), now);
                        b.passive = true;
                        c.buffs.add_buff(b, 0, 0, now);
                    }
                    None => tracing::error!("Template buff {buff} has no template"),
                }
            }
        }
    }
    {
        let mut live = player.data();
        apply_active_buffs(server, &mut live, &db.saved_buffs, now_ms());
        live.check_wanted_threshold(server, now_ms());
        // UpdateGearBonuses(null, null): gear bonuses are a later phase.
        live.restore_saved_hp_mp(&data.formulas);
        live.breath = 60_000; // LungCapacity
    }
    let zone = player.data().transform.zone_id;
    on_zone_change(server, &player, 0, zone);
    Ok(())
}

/// `CSSpawnCharacterPacket.Read`.
pub fn spawn_character(server: &GameServer, conn: &mut GameConnection, visual: VisualOptions) {
    let Some(player) = conn.active_char.clone() else {
        tracing::error!(
            "CSSpawnCharacterPacket: ActiveChar is null for account {} — no character was selected. Disconnecting.",
            conn.account_id()
        );
        conn.shutdown();
        return;
    };
    conn.state = GameState::World;
    let state = {
        let mut c = player.data();
        c.visual_options = Some(visual);
        unit_state::unit_state(&mut c, &server.game_data, now_ms())
    };
    conn.send(&state);
    let sub = server.time.subscribe(&player.conn);
    player.subscribers().push(sub);
    tracing::info!("CSSpawnCharacterPacket");
}

/// The world instance a player is in (`ParentWorld`).
fn parent_world(server: &GameServer, p: &PlayerRef) -> Option<Arc<WorldInstance>> {
    let inst = p.data().transform.instance_id;
    server.world.world(inst)
}

/// `Character.Spawn()` (`GameObject.Spawn`: `ParentWorld.AddObject` + `Show`).
pub fn spawn(server: &GameServer, p: &PlayerRef) {
    let Some(world) = parent_world(server, p) else {
        tracing::error!("Tried to spawn a object without a owning parent world");
        return;
    };
    world.add_object(p);
    server.world.try_add_character(p);
    if let Some(z) = world.show(p, &server.game_data, now_ms()) {
        on_zone_change(server, &z.player, z.old_zone, z.new_zone);
    }
}

/// `CSNotifyInGamePacket.Execute`.
pub fn notify_in_game(server: &GameServer, conn: &mut GameConnection) {
    let Some(p) = conn.active_char.clone() else {
        tracing::error!("CSNotifyInGamePacket without an active character");
        return;
    };
    set_online(server, &p, true);
    spawn(server, &p);
    let (zone, race, faction, expedition, name, id) = {
        let c = p.data();
        (
            c.transform.zone_id,
            c.race,
            c.faction_id,
            c.expedition_id,
            c.name.clone(),
            c.id,
        )
    };
    zone_chat(server, zone).join(&p); // shout, trade, lfg
    server.chat.nation_chat(race).join(&p);
    server
        .chat
        .faction_chat(mother_id(server, faction))
        .join(&p);
    // TeamManager.UpdateAtLogin: later phase.
    if expedition != 0 {
        expedition_refresh(server, &p);
        if let Some(e) = server.expeditions.get(expedition) {
            server
                .chat
                .guild_chat(e.faction.id, &e.faction.name, &server.ids.chat)
                .join(&p);
        }
    }
    // UpdateGearBonuses / TrialManager.HandlePlayerLogin: later phases.
    tracing::info!("NotifyInGame: {name} ({id})");
}

/// `CSNotifyInGameCompletedPacket` (`WorldManager.OnPlayerJoin`).
pub fn notify_in_game_completed(server: &GameServer, conn: &mut GameConnection) {
    let Some(p) = conn.active_char.clone() else {
        tracing::error!("CSNotifyInGameCompletedPacket without an active character");
        return;
    };
    p.send(SCOnOffSnowPacket {
        on: server
            .world
            .is_snowing
            .load(std::sync::atomic::Ordering::Relaxed),
    });
    // Family login: later phase.
    let c = p.data();
    tracing::info!("NotifyInGameCompleted SubZoneId 0, {} ({})", c.name, c.id);
}

/// `MathUtil.ConvertDirectionToRadian(sbyte)`.
fn direction_to_radian(d: i8) -> f32 {
    crate::models::movement::convert_direction_to_radian(d) as f32
}

/// `CSMoveUnitPacket.Execute` for this phase's units (characters).
pub async fn move_unit(server: &GameServer, conn: &mut GameConnection, obj_id: u32, mt: MoveType) {
    let Some(me) = conn.active_char.clone() else {
        return;
    };
    {
        let c = me.data();
        if c.disabled_set_position {
            return;
        }
    }
    let Some(world) = parent_world(server, &me) else {
        return;
    };
    // `ParentWorld.GetBaseUnit(objId)`: only players exist in this phase.
    let target = if obj_id == me.obj_id {
        Some(me.clone())
    } else {
        server
            .world
            .character_by_obj_id(obj_id)
            .filter(|p| p.data().transform.instance_id == world.id)
    };
    let Some(target) = target else {
        tracing::warn!("Invalid target {obj_id} from {}", me.data().name);
        return;
    };
    match &mt.kind {
        MoveKind::ShipRequest(_) | MoveKind::Vehicle(_) => {
            // `targetUnit is not Slave` -> return (slaves are a later phase).
        }
        MoveKind::Unit(u) => {
            // RemoveEffects (BuffRemoveOn.Move): later phase. IsRiding: mates are a later phase.
            let first_move = {
                let mut c = target.data();
                let first = !c.finished_loading;
                c.finished_loading = true;
                first
            };
            if first_move && Arc::ptr_eq(&target, &me) {
                // SetPlayerMoved: MOTD on the first movement after loading.
                send_message(&me, chat_type::SYSTEM, &server.config.world.motd);
            }
            // Standing on / sticky parents (slaves, transfers): later phase.
            {
                let mut c = target.data();
                c.transform.set_position(
                    u.x,
                    u.y,
                    u.z,
                    direction_to_radian(u.rotation_x),
                    direction_to_radian(u.rotation_y),
                    direction_to_radian(u.rotation_z),
                );
            }
            let packet = SCOneUnitMovementPacket {
                obj_id,
                move_type: mt.clone(),
            };
            broadcast(&world, &target, &packet, true);
            // FinalizeTransform -> AddVisibleObject (region change) -> SetPosition.
            if let Some(z) = world.add_visible_object(&target, &server.game_data, now_ms()) {
                on_zone_change(server, &z.player, z.old_zone, z.new_zone);
            }
            // DoFallDamage(FallVel): later phase (HP / death).
        }
        _ => {
            tracing::warn!(
                "Unknown MoveType: {:?} by {} for {}",
                mt.move_type(),
                me.data().name,
                target.data().name
            );
        }
    }
}

/// `CharacterManager.GetEffectiveAccessLevel`.
async fn effective_access_level(server: &GameServer, p: &PlayerRef) -> i32 {
    let (access, account) = {
        let c = p.data();
        (c.access_level, c.account_id)
    };
    let details = crate::db::get_account_details(&server.db, &server.config, account).await;
    access.max(details.access_level)
}

/// `CSSendChatMessagePacket.Read`.
#[allow(clippy::too_many_arguments)]
pub async fn send_chat_message(
    server: &GameServer,
    conn: &mut GameConnection,
    chat: i16,
    target_name: &str,
    message: &str,
    language_type: u8,
    ability: i32,
) {
    let Some(me) = conn.active_char.clone() else {
        return;
    };
    tracing::debug!("{message}");
    if let Some(cmd) = message.strip_prefix('/') {
        // CommandManager has no commands in this phase (`_commands.Count <= 0`): it answers with
        // an error and returns false, so the text is still handled as chat below.
        let _ = cmd;
        if effective_access_level(server, &me).await >= 100 {
            send_message(&me, chat_type::SYSTEM, "|cFFFF0000[Error] No commands have been loaded, this is usually because of compile errors. Try using \"/scripts reload\" after the issues have been fixed.|r");
        } else {
            send_message(&me, chat_type::SYSTEM, "[Error] No commands available.");
        }
    }
    let sender = {
        let c = me.data();
        chat_sender(&c, me.obj_id)
    };
    let msg = |t: i16, s: &ChatSender| SCChatMessagePacket {
        chat_type: t,
        sender: Some(s.clone()),
        message: message.to_string(),
        ability,
        language_type,
    };
    let error = |e: i16| me.send(SCErrorMsgPacket::new(e, 0, true));
    match chat {
        chat_type::WHISPER => {
            let target = server.world.character_by_name(target_name);
            match target {
                Some(t) if t.data().is_online => {
                    let (tf, mf) = (
                        mother_id(server, t.data().faction_id),
                        mother_id(server, sender.faction_id),
                    );
                    if tf != mf {
                        error(error_message::CHAT_CANNOT_WHISPER_TO_HOSTILE);
                    } else {
                        t.send(msg(chat_type::WHISPER, &sender));
                        let target_sender = chat_sender(&t.data(), t.obj_id);
                        conn.send(&msg(chat_type::WHISPERED, &target_sender));
                    }
                }
                _ => error(error_message::WHISPER_NO_TARGET),
            }
        }
        chat_type::WHITE => {
            if let Some(world) = parent_world(server, &me) {
                broadcast(&world, &me, &msg(chat, &sender), true);
            }
        }
        chat_type::RAID_LEADER | chat_type::RAID => error(error_message::CHAT_NOT_IN_RAID),
        chat_type::PARTY => error(error_message::CHAT_NOT_IN_PARTY),
        chat_type::TRADE | chat_type::GROUP_FIND | chat_type::SHOUT => {
            let zone = me.data().transform.zone_id;
            zone_chat(server, zone).send_packet(&msg(chat, &sender));
        }
        chat_type::CLAN => {
            let exp = me.data().expedition_id;
            match server.expeditions.get(exp).filter(|_| exp != 0) {
                Some(e) => {
                    server
                        .chat
                        .guild_chat(e.faction.id, &e.faction.name, &server.ids.chat)
                        .send_message(
                            Some(&sender),
                            |_| sender.clone(),
                            message,
                            ability,
                            language_type,
                        );
                }
                None => error(error_message::CHAT_NOT_IN_EXPEDITION),
            }
        }
        chat_type::FAMILY => {
            let family = me.data().family;
            if family > 0 {
                server
                    .chat
                    .family_chat(family, &server.ids.chat)
                    .send_message(
                        Some(&sender),
                        |_| sender.clone(),
                        message,
                        ability,
                        language_type,
                    );
            } else {
                error(error_message::CHAT_NOT_IN_FAMILY);
            }
        }
        chat_type::REGION => {
            server.chat.nation_chat(sender.race).send_message(
                Some(&sender),
                |_| sender.clone(),
                message,
                ability,
                language_type,
            );
        }
        chat_type::ALLY => {
            server
                .chat
                .faction_chat(mother_id(server, sender.faction_id))
                .send_message(
                    Some(&sender),
                    |_| sender.clone(),
                    message,
                    ability,
                    language_type,
                );
        }
        chat_type::JUDGE => {
            // GetTrialChat needs the TrialManager (later phase).
            tracing::warn!("Trial chat is not ported yet ({})", sender.name);
        }
        other => tracing::warn!("Unsupported chat type {other} from {}", sender.name),
    }
}

/// `EnterWorldManager.Leave` for `QuitGame` / `CharacterSelect`.
pub fn leave(server: &GameServer, conn: &mut GameConnection, target: u8) {
    if conn.state != GameState::World || conn.leave_task.is_some() {
        return;
    }
    if target == leave_target::QUIT_GAME {
        if let Some(p) = &conn.active_char {
            send_message(p, chat_type::SYSTEM, &server.config.world.logout_message);
        }
    }
    // Combat (x30) and active slave (+10 min) extensions: later phases.
    let logout_time = 10_000;
    conn.send(&SCPrepareLeaveWorldPacket {
        time: logout_time,
        target,
        idle_kick: false,
    });
    let tx = conn.command_sender();
    conn.leave_task = Some(tokio::spawn(async move {
        tokio::time::sleep(Duration::from_millis(logout_time as u64)).await;
        let _ = tx.send(ConnCommand::LeaveWorld(target));
    }));
}

/// `CSCancelLeaveWorldPacket`.
pub fn cancel_leave(conn: &mut GameConnection) {
    let Some(task) = conn.leave_task.take() else {
        return;
    };
    task.abort();
    conn.send(&SCLeaveWorldCanceledPacket);
}

/// `GameObject.Delete()` for a player: hide + remove from the instance.
fn delete(server: &GameServer, p: &PlayerRef) {
    if let Some(world) = parent_world(server, p) {
        world.hide(p);
        world.remove_object(p.obj_id);
    }
}

/// `EnterWorldManager.LeaveWorldTask(connection, target, activeChar)`.
pub async fn leave_world_task(
    server: &GameServer,
    conn: Option<&mut GameConnection>,
    target: u8,
    active: Option<PlayerRef>,
) {
    if let Some(p) = &active {
        {
            let mut c = p.data();
            c.disabled_set_position = true;
        }
        set_online(server, p, false);
        p.data().leave_time = Utc::now().timestamp();
        // Quest timers, mates, slaves, coffers, dismount: later phases.
        server.chat.leave_all_channels(p);
        // Family logout: later phase.
        let (exp, id) = {
            let c = p.data();
            (c.expedition_id, c.id)
        };
        if exp != 0 {
            if let Some((member, members)) =
                server
                    .expeditions
                    .logout_member(exp, id, Utc::now().timestamp())
            {
                send_to_expedition(
                    server,
                    &members,
                    &SCExpeditionMemberStatusChangedPacket { member, flag: 0 },
                );
            }
            if let Some(e) = server.expeditions.get(exp) {
                server
                    .chat
                    .guild_chat(e.faction.id, &e.faction.name, &server.ids.chat)
                    .leave(p);
            }
        }
        delete(server, p);
        // BuyBackItems.Wipe: later phase.
        for sub in p.subscribers().drain(..) {
            server.time.unsubscribe(sub);
        }
        server.world.try_remove_character(p.obj_id);
    }
    save_and_remove_from_world(server, active.as_ref()).await;
    if let Some(conn) = conn {
        conn.state = GameState::Lobby;
        conn.leave_task = None;
        if let Some(p) = &active {
            // `Connection.Characters` holds the same object as `ActiveChar` in C#.
            let c = p.data().clone();
            if let Some(slot) = conn.characters.iter_mut().find(|x| x.id == c.id) {
                *slot = c;
            }
        }
        conn.send(&SCLeaveWorldGrantedPacket { target });
        conn.send(&ChangeStatePacket { state: 0 });
    }
}

/// `GameConnection.SaveAndRemoveFromWorld(activeChar)`.
pub async fn save_and_remove_from_world(server: &GameServer, active: Option<&PlayerRef>) {
    let Some(p) = active else {
        return;
    };
    // RadarManager / buff effect tasks: later phases.
    delete(server, p);
    if server
        .world
        .character_by_obj_id(p.obj_id)
        .is_some_and(|x| Arc::ptr_eq(&x, p))
    {
        server.world.try_remove_character(p.obj_id);
    }
    let mut c = p.data().clone();
    if save_directly_to_database(&server.db, &mut c).await {
        p.data().updated = c.updated;
    }
}

/// The `ActiveChar` part of `GameConnection.OnDisconnect`.
pub async fn on_disconnect(server: &GameServer, conn: &mut GameConnection) {
    let Some(p) = conn.active_char.clone() else {
        return;
    };
    if p.data().is_online {
        set_online(server, &p, false);
    }
    for sub in p.subscribers().drain(..) {
        server.time.unsubscribe(sub);
    }
    // Events.OnDisconnect, mates / slaves / coffers: later phases. Note (C#): chat channels are
    // not left on a hard disconnect.
    save_and_remove_from_world(server, Some(&p)).await;
}

impl Player {
    /// `Character.Subscribers` (time-of-day subscription ids).
    pub fn subscribers(&self) -> std::sync::MutexGuard<'_, Vec<u64>> {
        self.subscriptions.lock().expect("subscribers poisoned")
    }
}
