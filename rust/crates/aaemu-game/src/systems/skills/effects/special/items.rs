//! Special effect actions on items, loot, music and account points: `GainItem`,
//! `GainItemWithEmblemImprint`, `ItemConversion`, `ApplyReagents`, `Dyeing`, `FishingLoot`,
//! `ItemCapScale`, `ItemCapScaleReset`, `CleanupLookConvert`, `Skinize`, `ItemSocketing`,
//! `GradeEnchant`, `CapturePet`, `PlayUserMusic`, `PauseUserMusic`, `UserMusicSaveNotes`,
//! `GiveBmMileage`, `GiveCashPoint`.
//!
//! The item manager work (inventory lookups, item tasks, loot packs, templates, music sheets)
//! goes through `ext::ItemSkillHooks`; the skill side (cancellation, errors, skill end, buffs,
//! account points) is ported here.

use std::sync::Arc;

use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

use super::super::super::broadcast;
use super::super::super::buffs;
use super::super::super::combat as unit_combat;
use super::super::super::ext::{GradeEnchantRequest, ItemActionResult};
use super::super::super::geometry::{instance_and_zone, world_pos};
use super::super::super::packets::SCSkillEndedPacket;
use super::super::super::packets_special::{
    SCBmPointPacket, SCICSCashPointPacket, SCPauseUserMusicPacket, SCSendUserMusicPacket,
};
use super::super::super::stats::{stat, StatKind};
use super::super::super::types::{
    CastAction, KillReason, SkillCastTarget, SkillCaster, SkillObject,
};
use super::super::EffectArgs;
use super::log_values;
use super::misc::send_system_message;

/// `ItemTaskType` values used here.
pub mod item_task {
    pub const CONSUME_SKILL_SOURCE: u8 = 13;
    pub const CAPTURE_PET: u8 = 24;
    pub const SKILL_REAGENTS: u8 = 40;
    pub const SKILL_EFFECT_GAIN_ITEM: u8 = 42;
}

/// `ErrorMessageType` values used here.
mod error {
    pub const BAG_FULL: i16 = 25;
    pub const BAG_INVALID_ITEM: i16 = 181;
}

/// `EquipmentItemSlot.Musical`.
const SLOT_MUSICAL: i32 = 18;
/// `ItemCategory.Lute` / `ItemCategory.Flute`.
const CATEGORY_LUTE: u32 = 80;
const CATEGORY_FLUTE: u32 = 81;
/// `BuffConstants.LutePlay` / `BuffConstants.FlutePlay`.
const BUFF_LUTE_PLAY: u32 = 6177;
const BUFF_FLUTE_PLAY: u32 = 6176;
/// `SkillsEnum.CloseTheScore`.
const SKILL_CLOSE_THE_SCORE: u32 = 22217;
/// `TagsEnum.PlaySong`.
const TAG_PLAY_SONG: u32 = 1155;

fn player_caster<'a>(args: &'a EffectArgs<'_>) -> Option<&'a PlayerRef> {
    args.caster.as_ref().and_then(WorldObject::as_player)
}

fn send_error(p: &PlayerRef, error: i16) {
    p.send(SCErrorMsgPacket::new(error, 0, true));
}

/// `skill.Cancelled = true` (an error log where the C# skill is null).
fn cancel_skill(args: &EffectArgs<'_>, action: &str) {
    match args.source.skill() {
        Some(s) => s.state().cancelled = true,
        None => {
            tracing::error!("Special effects: {action} without a skill (NullReferenceException)")
        }
    }
}

/// `SkillItem.ItemId` / `ItemTemplateId` of the caster object.
fn skill_item(c: &SkillCaster) -> Option<(u64, u32)> {
    match *c {
        SkillCaster::Item {
            item_id,
            item_template_id,
            ..
        } => Some((item_id, item_template_id)),
        _ => None,
    }
}

/// `SkillCastItemTarget.Id`.
fn item_target(t: &SkillCastTarget) -> Option<u64> {
    match *t {
        SkillCastTarget::Item { id, .. } => Some(id),
        _ => None,
    }
}

/// `character.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true)`.
fn broadcast_skill_ended(server: &GameServer, args: &EffectArgs<'_>, action: &str) {
    let Some(caster) = &args.caster else {
        return;
    };
    let Some(s) = args.source.skill() else {
        tracing::error!("Special effects: {action} without a skill (NullReferenceException)");
        return;
    };
    broadcast(server, caster, &SCSkillEndedPacket { tl: s.tl_id() }, true);
}

/// `GainItem`: `Inventory.TryAddNewItem(SkillEffectGainItem, value1, max(value2, 1), 0)`.
pub fn gain_item(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let Some(p) = player_caster(args) else {
        return;
    };
    tracing::debug!(
        "Special effects: GainItem value1 {}, value2 {}, value3 {}, value4 {}",
        v[0],
        v[1],
        v[2],
        v[3]
    );
    if v[0] <= 0 {
        tracing::warn!(
            "Special effects: GainItem ignored invalid item template id {}",
            v[0]
        );
        return;
    }
    let item_id = v[0] as u32;
    let item_count = if v[1] > 0 { v[1] } else { 1 };
    let acquired = server.systems.skills.ext.items().try_add_new_item(
        server,
        p,
        item_task::SKILL_EFFECT_GAIN_ITEM,
        item_id,
        item_count,
        0,
    );
    if !acquired {
        tracing::warn!(
            "Special effects: GainItem failed to give item {item_id} x{item_count} to character {}",
            p.character_id
        );
    }
}

/// `GainItemWithEmblemImprint`: `UccManager.CreateStamp(target, sourceItem)`.
pub fn gain_item_with_emblem_imprint(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "GainItemWithEmblemImprint", v);
    let Some((item_id, _)) = skill_item(&args.caster_caster) else {
        tracing::error!(
            "Special effects: GainItemWithEmblemImprint casterObj is not a SkillItem (InvalidCastException)"
        );
        return;
    };
    if let Some(p) = args.target.as_obj().and_then(WorldObject::as_player) {
        server
            .systems
            .skills
            .ext
            .items()
            .ucc_create_stamp(server, p, item_id);
    }
}

/// `ItemConversion`: disenchants the target item (skill cancelled on any failure).
pub fn item_conversion(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "ItemConversion", v);
    let Some(p) = player_caster(args) else {
        cancel_skill(args, "ItemConversion");
        return;
    };
    let Some(item_id) = item_target(&args.target_caster) else {
        cancel_skill(args, "ItemConversion");
        return;
    };
    if server
        .systems
        .skills
        .ext
        .items()
        .item_conversion(server, p, item_id)
    {
        cancel_skill(args, "ItemConversion");
    }
}

/// `ApplyReagents`: the skill's reagents (or the casting item) are consumed from the bag.
pub fn apply_reagents(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "ApplyReagents", v);
    let Some(skill) = args.source.skill() else {
        tracing::error!("Special effects: ApplyReagents without a skill (NullReferenceException)");
        return;
    };
    let items = server.systems.skills.ext.items();
    let reagents = server
        .game_data
        .systems
        .skills
        .reagents_for(skill.template.id);
    if !reagents.is_empty() {
        let Some(p) = player_caster(args) else {
            tracing::error!(
                "Special effects: ApplyReagents caster is not a Character (InvalidCastException)"
            );
            return;
        };
        for r in reagents {
            items.consume_bag_item(
                server,
                p,
                item_task::SKILL_REAGENTS,
                r.item_id,
                i32::from(r.amount),
            );
        }
    } else if let Some((_, template_id)) = skill_item(&args.caster_caster) {
        let Some(p) = player_caster(args) else {
            tracing::error!(
                "Special effects: ApplyReagents caster is not a Character (InvalidCastException)"
            );
            return;
        };
        items.consume_bag_item(server, p, item_task::SKILL_REAGENTS, template_id, 1);
    }
}

/// `Dyeing`: the dye (caster item) colours the target equipment item.
pub fn dyeing(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "Dyeing", v);
    let Some(p) = player_caster(args) else {
        tracing::error!("Special effects: Dyeing caster is not a Character (InvalidCastException)");
        return;
    };
    let Some(target_item_id) = item_target(&args.target_caster) else {
        tracing::error!(
            "Special effects: Dyeing targetObj is not a SkillCastItemTarget (InvalidCastException)"
        );
        return;
    };
    let Some((dye_item_id, dye_template_id)) = skill_item(&args.caster_caster) else {
        tracing::error!(
            "Special effects: Dyeing casterObj is not a SkillItem (NullReferenceException)"
        );
        return;
    };
    server.systems.skills.ext.items().dye_item(
        server,
        p,
        dye_item_id,
        dye_template_id,
        target_item_id,
    );
}

/// `FishingLoot`: the land (target above z 101) or sea fishing loot pack of the target's zone
/// group; `BagFull` when it does not fit.
pub fn fishing_loot(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    let Some(p) = player_caster(args) else {
        return;
    };
    tracing::debug!(
        "Special effects: FishingLoot value1 {}, value2 {}, value3 {}, value4 {}",
        v[0],
        v[1],
        v[2],
        v[3]
    );
    let zone_key = instance_and_zone(&args.target).1;
    let zones = &server.game_data.zones;
    let Some(zone) = zones.zone_by_key(zone_key) else {
        tracing::error!(
            "Special effects: FishingLoot unknown zone {zone_key} (NullReferenceException)"
        );
        return;
    };
    let zone_group_id = zone.group_id;
    if zones.group(zone_group_id).is_none() {
        let name = p.data().unit.name.clone();
        tracing::warn!("{name} seems to be trying to fish out of bounds.");
        return;
    }
    let land = world_pos(server, &args.target).z > 101.0;
    let given = server
        .systems
        .skills
        .ext
        .items()
        .give_fishing_loot(server, p, zone_group_id, land);
    // `None`: no pack / an empty pack (return); `Some(false)`: `GiveLootPack` failed.
    if given == Some(false) {
        send_error(p, error::BAG_FULL);
    }
}

/// The common checks of `ItemCapScale` / `ItemCapScaleReset`: `(Character)caster`,
/// `(SkillItem)casterObj`, `(SkillCastItemTarget)targetObj` (a wrong type throws in the C#, a
/// null returns).
fn temper_args<'a>(args: &'a EffectArgs<'_>, action: &str) -> Option<(&'a PlayerRef, u64)> {
    let Some(p) = player_caster(args) else {
        tracing::error!(
            "Special effects: {action} caster is not a Character (InvalidCastException)"
        );
        return None;
    };
    if skill_item(&args.caster_caster).is_none() {
        tracing::error!(
            "Special effects: {action} casterObj is not a SkillItem (InvalidCastException)"
        );
        return None;
    }
    let Some(target_item_id) = item_target(&args.target_caster) else {
        tracing::error!(
            "Special effects: {action} targetObj is not a SkillCastItemTarget (InvalidCastException)"
        );
        return None;
    };
    Some((p, target_item_id))
}

/// `ItemCapScale`: random physical / magical tempers on the target item, reported as a system
/// message.
pub fn item_cap_scale(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "ItemCapScale", v);
    let Some((p, target_item_id)) = temper_args(args, "ItemCapScale") else {
        return;
    };
    let Some(skill) = args.source.skill() else {
        tracing::error!("Special effects: ItemCapScale without a skill (NullReferenceException)");
        return;
    };
    if let Some((physical, magical)) = server.systems.skills.ext.items().item_cap_scale(
        server,
        p,
        target_item_id,
        skill.template.id,
    ) {
        // Note: According to various videos I have found, there is no information on the %
        // reached by a temper in-game. This is sent to help indicate what was achieved.
        send_system_message(
            p,
            &format!("Temper:\n |cFFFFFFFF{physical}%|r Physical\n|cFFFFFFFF{magical}%|r Magical"),
        );
    }
}

/// `ItemCapScaleReset`: the target item's tempers are reset.
pub fn item_cap_scale_reset(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "ItemCapScaleReset", v);
    let Some((p, target_item_id)) = temper_args(args, "ItemCapScaleReset") else {
        return;
    };
    if server
        .systems
        .skills
        .ext
        .items()
        .item_cap_scale_reset(server, p, target_item_id)
    {
        // No indication in-game that the item's tempering has been successfully reset
        send_system_message(p, "Tempering reset");
    }
}

/// The shared flow of `CleanupLookConvert` / `Skinize`.
fn look_convert(
    server: &GameServer,
    args: &EffectArgs<'_>,
    action: &str,
    f: impl FnOnce(&GameServer, &PlayerRef, u64, Option<u64>) -> ItemActionResult,
) {
    let Some(p) = player_caster(args) else {
        cancel_skill(args, action);
        return;
    };
    let Some(target_item_id) = item_target(&args.target_caster) else {
        cancel_skill(args, action);
        return;
    };
    let powder = skill_item(&args.caster_caster).map(|(id, _)| id);
    match f(server, p, target_item_id, powder) {
        ItemActionResult::Done => {}
        ItemActionResult::Cancel => cancel_skill(args, action),
        ItemActionResult::CancelWithError(e) => {
            cancel_skill(args, action);
            send_error(p, e);
        }
    }
}

/// `CleanupLookConvert`: removes the image (look) from the target item, consuming the powder.
pub fn cleanup_look_convert(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "CleanupLookConvert", v);
    let items = server.systems.skills.ext.items();
    look_convert(server, args, "CleanupLookConvert", |s, p, t, powder| {
        items.cleanup_look_convert(s, p, t, powder)
    });
}

/// `Skinize`: turns the target item into an image item, consuming the powder.
pub fn skinize(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "Skinize", v);
    let items = server.systems.skills.ext.items();
    look_convert(server, args, "Skinize", |s, p, t, powder| {
        items.skinize(s, p, t, powder)
    });
}

/// `ItemSocketing`: a lunagem (or dawnstone) on the target equipment; ends the skill.
pub fn item_socketing(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    tracing::debug!(
        "Special effects: ItemSocketing value1 {}, value2 {}, value3 {}, value4 {}",
        v[0],
        v[1],
        v[2],
        v[3]
    );
    let Some(p) = player_caster(args) else {
        tracing::error!(
            "Special effects: ItemSocketing caster {:?} is not a character",
            args.caster.as_ref().map(WorldObject::obj_id)
        );
        return;
    };
    let Some((gem_item_id, _)) = skill_item(&args.caster_caster) else {
        tracing::error!(
            "Special effects: ItemSocketing casterObj {:?} is not a SkillItem",
            args.caster_caster
        );
        return;
    };
    let Some(target_item_id) = item_target(&args.target_caster) else {
        tracing::error!(
            "Special effects: ItemSocketing targetObj {:?} is not a SkillCastItemTarget",
            args.target_caster
        );
        return;
    };
    if server
        .systems
        .skills
        .ext
        .items()
        .item_socketing(server, p, gem_item_id, target_item_id)
    {
        broadcast_skill_ended(server, args, "ItemSocketing");
    }
}

/// `GradeEnchant`: regrades the target item with the scroll (and optional charm); ends the
/// skill.
pub fn grade_enchant(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "GradeEnchant", v);
    let Some(p) = player_caster(args) else {
        return;
    };
    let Some((scroll_item_id, scroll_template_id)) = skill_item(&args.caster_caster) else {
        return;
    };
    let Some(target_item_id) = item_target(&args.target_caster) else {
        return;
    };
    let support_item_id = match args.skill_object {
        SkillObject::ItemGradeEnchantingSupport {
            support_item_id, ..
        } => *support_item_id,
        _ => 0,
    };
    let request = GradeEnchantRequest {
        scroll_item_id,
        scroll_template_id,
        target_item_id,
        support_item_id,
        is_lucky: v[0] != 0,
        item_type: v[2],
    };
    if server
        .systems
        .skills
        .ext
        .items()
        .grade_enchant(server, p, &request)
    {
        broadcast_skill_ended(server, args, "GradeEnchant");
    }
}

/// `CapturePet`: the NPC owning the casting buff becomes its pet item (`PetItemId`), is killed
/// (`KillReason.Capture`) and despawned.
pub fn capture_pet(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    // Only players are allowed to capture pets
    let Some(caster) = &args.caster else {
        return;
    };
    let Some(p) = caster.as_player() else {
        return;
    };
    tracing::debug!(
        "Special effects: CapturePet value1 {}, value2 {}, value3 {}, value4 {}",
        v[0],
        v[1],
        v[2],
        v[3]
    );
    // Need to grab the actual target from the buff owner itself, as target will be the player
    let target_object = match args.cast {
        CastAction::Buff { owner_obj_id, .. } => server.world.object(owner_obj_id),
        _ => None,
    };
    let Some(npc_obj) = target_object.filter(|o| o.as_npc().is_some()) else {
        let name = p.data().unit.name.clone();
        tracing::warn!("Special effects: CapturePet {name} tried to capture a non-Npc");
        return;
    };
    let Some(npc) = npc_obj.as_npc() else {
        return;
    };
    let npcs = server.systems.skills.ext.npcs();
    let items = server.systems.skills.ext.items();
    let pet_item_id = npcs.npc_pet_item_id(server, npc);
    if pet_item_id > 0 {
        if !items.item_template_exists(server, pet_item_id) {
            send_error(p, error::BAG_INVALID_ITEM);
            return;
        }
        // And player can get the item
        if !items.acquire_default_item(server, p, item_task::CAPTURE_PET, pet_item_id, 1) {
            send_error(p, error::BAG_FULL);
            return;
        }
        // Capture (kill) the target
        let max_hp = stat(server, &npc_obj, StatKind::MaxHp) as i32;
        unit_combat::reduce_current_hp(server, &npc_obj, Some(caster), max_hp, KillReason::Capture);
        // Immediately despawn it
        npcs.despawn_npc(server, npc);
    } else {
        let template_id = npc.data().unit.template_id;
        send_system_message(
            p,
            &format!(
                "Unable to capture this target yet, take a screenshot of this, and inform the developers. NPC {template_id}"
            ),
        );
    }
}

/// `PlayUserMusic`: broadcasts the character's MIDI cache and starts the instrument's play
/// buff.
pub fn play_user_music(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "PlayUserMusic", v);
    let Some(target) = args.target.as_obj() else {
        return;
    };
    let Some(p) = target.as_player() else {
        return;
    };
    let items = server.systems.skills.ext.items();
    let name = p.data().unit.name.clone();
    let midi = items.midi_cache(server, p.character_id);
    broadcast(
        server,
        target,
        &SCSendUserMusicPacket {
            obj_id: p.obj_id,
            author: name,
            midi,
        },
        true,
    );
    match items.equipped_item_category(server, p, SLOT_MUSICAL) {
        Some(category) => {
            let buff_id = match category {
                CATEGORY_LUTE => Some(BUFF_LUTE_PLAY),
                CATEGORY_FLUTE => Some(BUFF_FLUTE_PLAY),
                _ => None,
            };
            match (buff_id, &args.caster) {
                (Some(id), Some(caster)) => buffs::add_buff_by_id(server, target, id, caster, 0),
                (Some(_), None) => tracing::error!(
                    "SpecialEffectAction - PlayUserMusic - no caster (NullReferenceException)"
                ),
                (None, _) => tracing::trace!(
                    "SpecialEffectAction - PlayUserMusic - Equipped instrument slot is not a known instrument !"
                ),
            }
        }
        None => tracing::trace!("SpecialEffectAction - PlayUserMusic - No instrument equipped !"),
    }
}

/// `PauseUserMusic`: `SCPauseUserMusicPacket`; on "Close the Score" the play-song buffs end.
pub fn pause_user_music(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "PauseUserMusic", v);
    let skill_id = args.source.skill().map(|s| s.template.id);
    tracing::trace!(
        "Special effects: PauseUserMusic -> {}",
        if skill_id == Some(SKILL_CLOSE_THE_SCORE) {
            "Stop"
        } else {
            "Pause"
        }
    );
    let Some(target) = args.target.as_obj() else {
        return;
    };
    broadcast(
        server,
        target,
        &SCPauseUserMusicPacket {
            obj_id: target.obj_id(),
        },
        true,
    );
    if skill_id == Some(SKILL_CLOSE_THE_SCORE) {
        let ids = server
            .game_data
            .systems
            .skills
            .buffs_by_tag(TAG_PLAY_SONG)
            .map(<[u32]>::to_vec)
            .unwrap_or_default();
        for id in ids {
            if target.with_base_unit(|b| b.buffs().check_buff(id)) {
                buffs::remove_buff(server, target, id);
            }
        }
    }
}

/// `UserMusicSaveNotes`: `MusicManager.CreateSheetMusic(player, item)`.
pub fn user_music_save_notes(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "UserMusicSaveNotes", v);
    match (player_caster(args), skill_item(&args.caster_caster)) {
        (Some(p), Some((item_id, _))) => {
            if !server
                .systems
                .skills
                .ext
                .items()
                .create_sheet_music(server, p, item_id)
            {
                let name = p.data().unit.name.clone();
                tracing::error!(
                    "Special effects: UserMusicSaveNotes - Error saving music for {name} !"
                );
            }
        }
        _ => tracing::error!("Special effects: UserMusicSaveNotes - Invalid Arguments"),
    }
}

/// Runs `f` on Tokio with an owned server (the C# account manager calls are synchronous DB
/// queries; here they run as a task).
fn spawn_db<F, Fut>(server: &GameServer, f: F)
where
    F: FnOnce(Arc<GameServer>) -> Fut + Send + 'static,
    Fut: std::future::Future<Output = ()> + Send + 'static,
{
    let Some(srv) = server.systems.skills.server() else {
        tracing::debug!("skills: no bound server, account update not run");
        return;
    };
    let Ok(handle) = tokio::runtime::Handle::try_current() else {
        tracing::debug!("skills: no Tokio runtime, account update not run");
        return;
    };
    handle.spawn(f(srv));
}

/// `AccountManager.AddLoyalty` / `AddCredits`: `INSERT ... ON DUPLICATE KEY UPDATE col = col +
/// amount`; `true` when a row was affected (errors are logged).
async fn add_account_points(
    server: &GameServer,
    column: &str,
    account_id: u32,
    amount: i32,
) -> bool {
    let sql = format!(
        "INSERT INTO accounts (account_id, {column}) VALUES(?, ?) ON DUPLICATE KEY UPDATE {column} = {column} + ?"
    );
    match sqlx::query(&sql)
        .bind(account_id)
        .bind(amount)
        .bind(amount)
        .execute(&server.db)
        .await
    {
        Ok(r) => r.rows_affected() > 0,
        Err(e) => {
            tracing::error!("{e}");
            false
        }
    }
}

/// `GiveBmMileage`: `value1` loyalty on the account, then `SCBmPointPacket` with the new total.
///
/// Deviation: the account update runs as an async task (the C# blocks on the DB).
pub fn give_bm_mileage(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "GiveBmMileage", v);
    let Some(p) = player_caster(args) else {
        return;
    };
    let p = p.clone();
    let amount = v[0];
    spawn_db(server, move |srv| async move {
        let account_id = p.data().account_id;
        add_account_points(&srv, "loyalty", account_id, amount).await;
        let details = crate::db::get_account_details(&srv.db, &srv.config, account_id).await;
        let bm_point = i64::from(details.loyalty);
        p.data().bm_point = bm_point;
        p.send(SCBmPointPacket { bm_point });
    });
}

/// `GiveCashPoint`: consumes the casting item and credits `value1` to the account, then sends
/// the credits total (`SCICSCashPointPacket`).
///
/// Deviation: the account update runs as an async task (the C# blocks on the DB).
pub fn give_cash_point(server: &GameServer, args: &EffectArgs<'_>, v: [i32; 4]) {
    log_values(args, "GiveCashPoint", v);
    let Some(p) = player_caster(args) else {
        return;
    };
    let Some((_, template_id)) = skill_item(&args.caster_caster) else {
        return;
    };
    let consumed = server.systems.skills.ext.items().consume_bag_item(
        server,
        p,
        item_task::CONSUME_SKILL_SOURCE,
        template_id,
        1,
    ) > 0;
    let p = p.clone();
    let amount = v[0];
    spawn_db(server, move |srv| async move {
        let account_id = p.data().account_id;
        if consumed {
            if !add_account_points(&srv, "credits", account_id, amount).await {
                tracing::error!("Failed to credit Account:{account_id} with {amount} credits.");
            } else {
                // TODO (C#): Proper message?
                send_system_message(&p, &format!("You received {amount} credits."));
            }
        }
        let points = crate::db::get_account_details(&srv.db, &srv.config, account_id).await;
        p.send(SCICSCashPointPacket {
            point: points.credits,
        });
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn skill_item_and_item_target_extraction() {
        let c = SkillCaster::Item {
            obj_id: 1,
            item_id: 99,
            item_template_id: 500,
            type1: 0,
            type2: 0,
        };
        assert_eq!(skill_item(&c), Some((99, 500)));
        assert_eq!(skill_item(&SkillCaster::Unit(1)), None);
        let t = SkillCastTarget::Item {
            obj_id: 0,
            id: 1234,
            type1: 0,
            type2: 0,
        };
        assert_eq!(item_target(&t), Some(1234));
        assert_eq!(item_target(&SkillCastTarget::Unit(3)), None);
    }
}
