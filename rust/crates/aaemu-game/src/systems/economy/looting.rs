//! `Models/Game/Items/Containers/LootingContainer` (one per base unit, kept here by object id)
//! and the loot packets `CSLootOpenBagPacket`, `CSLootItemPacket`, `CSLootCloseBagPacket`,
//! `CSLootDicePacket`, `CSChangeLootingRulePacket`.
//!
//! Entry points for other subsystems (all on `server.systems.economy.loot`):
//! * [`LootManager::generate_loot`] — `Unit.DoDie` -> `LootingContainer.GenerateLoot(killer)`
//!   (npcs subsystem, after a unit died);
//! * [`LootManager::item_count`] — `npc.LootingContainer.Items.Count` (`NpcSpawner` despawn
//!   extension by [`LOOT_DESPAWN_EXTENSION_TIME`]);
//! * [`LootManager::npcs_to_make_public`] / [`LootManager::make_loot_public`] —
//!   `WorldInstance.GetNpcsToMakePublicLooting` + `MakeLootPublic` (SpawnManager loop);
//! * [`LootManager::forget`] — drop the container of a deleted object.
//!
//! Locking: every operation that can grant items begins an [`ItemTxn`] first, then locks the
//! looting container (a leaf below the economy operation lock). The [`LootHooks`] may be called
//! while both are held: they must not start item transactions.

use std::collections::{BTreeMap, HashMap};
use std::sync::{Arc, Mutex, MutexGuard};

use aaemu_commons::PacketStream;

use crate::data::items::{bind_type, ITEM_COINS};
use crate::error::Result;
use crate::models::container::{equip_slot, slot_type};
use crate::models::item::Item;
use crate::models::item_task::task_type;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::PacketRegistry;
use crate::opcodes::cs;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::{NpcRef, PlayerRef, WorldObject};

use super::error_message;
use super::inventory::{self, Inv, ItemTxn};
use super::item_handlers::{active, sync_client_packet};
use super::loot::{self, LootConfig, LootRng, PlayerLoot, SharedRandom};
use super::loot_packets::{
    loot_owner_type, looting_rule_method, LootingRule, SCLootBagDataPacket, SCLootDiceNotifyPacket,
    SCLootDicePacket, SCLootDiceSummaryPacket, SCLootItemFailedPacket, SCLootItemTookPacket,
    SCLootableStatePacket,
};

/// `LootingContainer.MaxLootingRange`.
pub const MAX_LOOTING_RANGE: f32 = 200.0;
/// `LootingContainer.MakeLootPublicTime` (seconds).
pub const MAKE_LOOT_PUBLIC_TIME: f64 = 180.0;
/// `LootingContainer.LootDespawnExtensionTime` (seconds).
pub const LOOT_DESPAWN_EXTENSION_TIME: f64 = 300.0;
/// `LootingContainer.PostLootMinimumDespawnTime` (seconds).
pub const POST_LOOT_MINIMUM_DESPAWN_TIME: f64 = 2.0;

// ---------------------------------------------------------------------------------------------
// Hooks
// ---------------------------------------------------------------------------------------------

/// `npc.CharacterTagging` (who has loot rights).
#[derive(Clone, Default)]
pub struct NpcTagging {
    /// `TagTeam` (0 = none).
    pub tag_team: u32,
    /// `Tagger`.
    pub tagger: Option<PlayerRef>,
}

/// NPC / doodad behaviour the looting needs (`systems::npcs`).
pub trait LootNpcHooks: Send + Sync {
    /// `npc.CharacterTagging`.
    fn tagging(&self, _server: &GameServer, _npc: &NpcRef) -> NpcTagging {
        NpcTagging::default()
    }

    /// `npc.Spawner?.DespawnTime` (seconds; `None` = no spawner).
    fn spawner_despawn_time(&self, _server: &GameServer, _npc: &NpcRef) -> Option<f64> {
        None
    }

    /// `npc.Despawn = at` (unix milliseconds).
    fn set_despawn(&self, _server: &GameServer, _npc: &NpcRef, _at_ms: i64) {}

    /// `CSLootOpenBagPacket.TryHandleFuncDrivenLoot(doodad)`: loot-driven doodad funcs
    /// (`RecoverItem` for trade packs, `doodad.Use` + `DoodadDeleteTask` for loot doodads).
    /// Called only for doodads whose looting container is empty. `true` = handled.
    fn try_handle_func_driven_loot(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _doodad: &WorldObject,
    ) -> bool {
        false
    }

    /// `a.GetDistanceTo(b)` (2D, minus the actor model radii; the npcs subsystem's
    /// implementation is installed). `None` = use the raw 2D distance.
    fn distance_to(&self, _server: &GameServer, _a: &WorldObject, _b: &WorldObject) -> Option<f32> {
        None
    }
}

/// A team as the looting sees it (`TeamManager.GetActiveTeam(id)`).
#[derive(Clone)]
pub struct TeamLootInfo {
    pub team_id: u32,
    /// `Members[].Character` (online members).
    pub members: Vec<PlayerRef>,
    /// `LootingRule`.
    pub looting_rule: LootingRule,
}

/// Team behaviour (`systems::social`, `TeamManager` / `Team`).
pub trait LootTeamHooks: Send + Sync {
    /// `TeamManager.GetActiveTeam(teamId)`.
    fn active_team(&self, _server: &GameServer, _team_id: u32) -> Option<TeamLootInfo> {
        None
    }

    /// `KillerTeam.GetNextLootWinner(eligiblePlayers, lootOwner)` (round robin).
    fn next_loot_winner(
        &self,
        _server: &GameServer,
        _team_id: u32,
        _eligible: &[PlayerRef],
        _loot_owner_obj_id: u32,
    ) -> Option<PlayerRef> {
        None
    }

    /// `TeamManager.ChangeLootingRule(player, teamId, changeFlags, method, grade, master,
    /// rollForBoP)` (`CSChangeLootingRulePacket`).
    fn change_looting_rule(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _team_id: u32,
        _change_flags: u8,
        _rule: LootingRule,
    ) {
    }
}

/// Quest state (`systems::quests`).
pub trait LootQuestHooks: Send + Sync {
    /// `player.Quests.HasQuest(questId)`.
    fn has_quest(&self, _server: &GameServer, _player: &PlayerRef, _quest_id: u32) -> bool {
        false
    }
}

/// The loot hook slots (`server.systems.economy.loot.hooks`).
#[derive(Debug, Default)]
pub struct LootHooks {
    pub npcs: HookSlot<dyn LootNpcHooks>,
    pub teams: HookSlot<dyn LootTeamHooks>,
    pub quests: HookSlot<dyn LootQuestHooks>,
}

/// `player.Quests.HasQuest(questId)` through the hooks (`false` without a quest system).
pub fn has_quest(server: &GameServer, player: &PlayerRef, quest_id: u32) -> bool {
    server
        .systems
        .economy
        .loot
        .hooks
        .quests
        .get()
        .is_some_and(|h| h.has_quest(server, player, quest_id))
}

// ---------------------------------------------------------------------------------------------
// LootingContainer
// ---------------------------------------------------------------------------------------------

/// `LootingContainerItemEntry`.
#[derive(Debug, Clone, PartialEq)]
pub struct LootEntry {
    pub item_index: u16,
    /// `PlayerRolls` (character id, roll; 0 = not rolled, -1 = pass) in dictionary order.
    pub player_rolls: Vec<(u32, i8)>,
    /// `HighestRoller` (character id).
    pub highest_roller: u32,
    pub item: Item,
}

impl LootEntry {
    fn set_roll(&mut self, id: u32, roll: i8) {
        match self.player_rolls.iter_mut().find(|(p, _)| *p == id) {
            Some(r) => r.1 = roll,
            None => self.player_rolls.push((id, roll)),
        }
    }
}

/// `LootingContainer` of one base unit.
#[derive(Debug, Clone, Default)]
pub struct LootingContainer {
    /// `LootOwner.ObjId`.
    pub owner_obj_id: u32,
    /// `LootOwnerType` (`None` until loot was generated).
    pub owner_type: u16,
    /// `KillerTeam` (team id).
    pub killer_team: Option<u32>,
    /// `TeamLootingRule` (`null` until loot was generated).
    pub team_looting_rule: Option<LootingRule>,
    /// `CreationTime` (unix milliseconds, 0 = `DateTime.MinValue`).
    pub creation_time_ms: i64,
    /// `Items` (by item index).
    pub items: BTreeMap<u16, LootEntry>,
    pub already_generated: bool,
    /// `EligiblePlayers` (character ids, insertion order).
    pub eligible_players: Vec<u32>,
    /// `OpenedBy` (character ids).
    pub opened_by: Vec<u32>,
}

impl LootingContainer {
    pub fn new(owner_obj_id: u32) -> Self {
        Self {
            owner_obj_id,
            ..Default::default()
        }
    }

    /// `RegisterItems(items)`: the fake loot item id is
    /// `(ObjId << 32) + (LootOwnerType << 16) + ItemIndex`.
    pub fn register_items(&mut self, items: Vec<Item>) {
        for mut item in items {
            let index = (self.items.len() + 1) as u16;
            item.id = (u64::from(self.owner_obj_id) << 32)
                + (u64::from(self.owner_type) << 16)
                + u64::from(index);
            self.items.insert(
                index,
                LootEntry {
                    item_index: index,
                    player_rolls: Vec::new(),
                    highest_roller: 0,
                    item,
                },
            );
        }
    }

    /// `CanMakePublic()`.
    pub fn can_make_public(&self, now_ms: i64) -> bool {
        self.team_looting_rule
            .is_some_and(|r| r.loot_method != looting_rule_method::PUBLIC)
            && !self.items.is_empty()
            && self.creation_time_ms > 0
            && self.creation_time_ms + (MAKE_LOOT_PUBLIC_TIME * 1000.0) as i64 <= now_ms
    }

    fn add_eligible(&mut self, id: u32) {
        if !self.eligible_players.contains(&id) {
            self.eligible_players.push(id);
        }
    }
}

/// `server.systems.economy.loot`: the looting containers by owner object id and the hooks.
#[derive(Debug, Default)]
pub struct LootManager {
    containers: Containers,
    pub hooks: LootHooks,
}

/// The containers by owner object id. Shared (`Arc`) with the object id manager's release
/// observer ([`LootManager::install_release_hook`]).
type Containers = Arc<Mutex<HashMap<u32, Arc<Mutex<LootingContainer>>>>>;

type Guard<'a> = MutexGuard<'a, LootingContainer>;

fn lock(c: &Mutex<LootingContainer>) -> Guard<'_> {
    c.lock().unwrap_or_else(|e| e.into_inner())
}

/// The context of one looting operation.
struct Ctx<'t, 'a> {
    txn: &'t mut ItemTxn<'a>,
    rng: &'t mut dyn LootRng,
}

impl Ctx<'_, '_> {
    fn server(&self) -> &GameServer {
        self.txn.server
    }

    fn send(&mut self, player_id: u32, p: impl crate::net::packet::ServerPacket + 'static) {
        self.txn.env.fx.send(player_id, p);
    }

    fn player(&self, id: u32) -> Option<PlayerRef> {
        self.txn.server.world.character_by_id(id)
    }
}

fn forget_container(containers: &Containers, obj_id: u32) {
    containers
        .lock()
        .unwrap_or_else(|e| e.into_inner())
        .remove(&obj_id);
}

impl LootManager {
    /// The looting container of `obj_id` (created on first use, like the C# per-unit field).
    pub fn container(&self, obj_id: u32) -> Arc<Mutex<LootingContainer>> {
        self.containers
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .entry(obj_id)
            .or_insert_with(|| Arc::new(Mutex::new(LootingContainer::new(obj_id))))
            .clone()
    }

    /// The looting container of `obj_id` if it has one.
    pub fn get(&self, obj_id: u32) -> Option<Arc<Mutex<LootingContainer>>> {
        self.containers
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .get(&obj_id)
            .cloned()
    }

    /// Drops the container of a deleted object (the C# field dies with the unit; here the
    /// container is keyed by object id, which is reused once released).
    pub fn forget(&self, obj_id: u32) {
        forget_container(&self.containers, obj_id);
    }

    /// Makes every release of an object id (`ObjectIdManager.ReleaseId`, whichever subsystem
    /// despawns the object) drop that id's container, so a new object on a recycled id starts
    /// with a fresh `LootingContainer` like the C# per-unit field.
    pub fn install_release_hook(&self, ids: &crate::managers::id::IdManager) {
        let containers = self.containers.clone();
        ids.install_release_hook(move |obj_id| forget_container(&containers, obj_id));
    }

    /// `LootingContainer.Items.Count`.
    pub fn item_count(&self, obj_id: u32) -> usize {
        self.get(obj_id).map_or(0, |c| lock(&c).items.len())
    }

    /// `LootingContainer.GenerateLoot(killer)` for `owner` (`Unit.DoDie`).
    pub fn generate_loot(server: &GameServer, owner: &WorldObject, killer: Option<&WorldObject>) {
        Self::generate_loot_with(server, owner, killer, &mut SharedRandom);
    }

    /// [`LootManager::generate_loot`] with an explicit random source.
    pub fn generate_loot_with(
        server: &GameServer,
        owner: &WorldObject,
        killer: Option<&WorldObject>,
        rng: &mut dyn LootRng,
    ) {
        let lm = &server.systems.economy.loot;
        let c = lm.container(owner.obj_id());
        let mut eligible_send = Vec::new();
        let packet;
        {
            let mut g = lock(&c);
            // Do not allow multiple generations of loot
            if g.already_generated {
                return;
            }
            g.already_generated = true;
            g.owner_type = match owner {
                WorldObject::Npc(_) => loot_owner_type::NPC,
                WorldObject::Doodad(_) => loot_owner_type::DOODAD,
                _ => loot_owner_type::NONE,
            };
            let now_ms = chrono::Utc::now().timestamp_millis();
            g.creation_time_ms = now_ms;
            g.items.clear();
            let npc = match owner {
                WorldObject::Npc(n) => n.clone(),
                WorldObject::Doodad(d) => {
                    tracing::warn!(
                        "Not yet implemented for doodads, LootOwner: {}:{}",
                        g.owner_type,
                        d.obj_id
                    );
                    return;
                }
                other => {
                    tracing::warn!("Unsupported LootOwner: {}:{}", g.owner_type, other.obj_id());
                    return;
                }
            };
            let template_id = npc.data().unit.template_id;
            let packs = server.game_data.items.loot_packs_by_npc(template_id);
            if packs.is_empty() {
                return;
            }
            let mut drop_rate = 1f32;
            let mut gold_rate = 1f32;
            g.eligible_players.clear();
            let npc_hooks = lm.hooks.npcs.get();
            let tagging = npc_hooks
                .as_ref()
                .map(|h| h.tagging(server, &npc))
                .unwrap_or_default();
            let team = lm
                .hooks
                .teams
                .get()
                .and_then(|h| h.active_team(server, tagging.tag_team));
            g.killer_team = team.as_ref().map(|t| t.team_id);
            g.team_looting_rule = Some(team.as_ref().map_or(
                LootingRule {
                    loot_method: looting_rule_method::FREE_FOR_ALL,
                    ..LootingRule::default()
                },
                |t| t.looting_rule,
            ));
            let killer_player = killer.and_then(|k| k.as_player().cloned());
            let mut eligible: Vec<PlayerRef> = Vec::new();
            let add = |p: PlayerRef, eligible: &mut Vec<PlayerRef>| {
                if !eligible.iter().any(|e| e.character_id == p.character_id) {
                    eligible.push(p);
                }
            };
            let npc_obj = WorldObject::from(&npc);
            if tagging.tag_team != 0 {
                if let Some(t) = &team {
                    for m in &t.members {
                        let d = distance(server, &WorldObject::from(m), &npc_obj);
                        if d <= MAX_LOOTING_RANGE {
                            add(m.clone(), &mut eligible);
                        }
                    }
                } else if let Some(t) = &tagging.tagger {
                    add(t.clone(), &mut eligible);
                }
            } else if let Some(t) = &tagging.tagger {
                // Set to FreeForAll when only the tagger has rights
                g.team_looting_rule = Some(LootingRule {
                    loot_method: looting_rule_method::FREE_FOR_ALL,
                    minimum_grade: 0,
                    loot_master: killer_player.as_ref().map_or(0, |p| p.character_id),
                    ..LootingRule::default()
                });
                add(t.clone(), &mut eligible);
            }
            if !eligible.is_empty() {
                let mut max_drop = -100f32;
                let mut max_gold = -100f32;
                for p in &eligible {
                    let (d, gl) = loot::player_loot_rates(p, now_ms);
                    if d > max_drop {
                        max_drop = d;
                    }
                    if gl > max_gold {
                        max_gold = gl;
                    }
                }
                drop_rate = max_drop;
                gold_rate = max_gold;
            } else if let Some(p) = &killer_player {
                let (d, gl) = loot::player_loot_rates(p, now_ms);
                drop_rate *= d;
                gold_rate *= gl;
                tracing::info!(
                    "Unit killed without aggro: {} ({template_id}) by {}",
                    npc.obj_id,
                    p.data().unit.name
                );
                add(p.clone(), &mut eligible);
            }
            for p in &eligible {
                g.add_eligible(p.character_id);
            }

            // Generate the actual loot
            let cfg = LootConfig::from_world(&server.config.world);
            let lp = killer_player
                .as_ref()
                .map(|p| PlayerLoot { server, player: p });
            let mut results = Vec::new();
            for d in packs {
                let Some(pack) = server.game_data.systems.economy.loot.pack(d.loot_pack_id) else {
                    continue;
                };
                match loot::generate_pack_v2(
                    pack,
                    &server.game_data.items,
                    cfg,
                    drop_rate,
                    gold_rate,
                    lp.as_ref().map(|p| p as &dyn loot::LootPlayer),
                    0,
                    None,
                    rng,
                ) {
                    Ok(r) => results.extend(r),
                    Err(e) => {
                        // C#: the exception escapes GenerateLoot.
                        tracing::error!("GenerateLoot for npc {}: {e}", npc.obj_id);
                        return;
                    }
                }
            }
            // Group by origin group (order of first appearance)
            let mut groups: Vec<u32> = Vec::new();
            for r in &results {
                if !groups.contains(&r.group) {
                    groups.push(r.group);
                }
            }
            let now = now_ms / 1000;
            for group in groups {
                let mut to_add = Vec::new();
                for r in results.iter().filter(|r| r.group == group) {
                    match server.items.create_detached(
                        r.item_id,
                        r.count,
                        r.grade,
                        server.config.id,
                        &server.game_data.items,
                        now,
                    ) {
                        Some(i) => to_add.push(i),
                        None => {
                            // C#: RegisterItems throws a NullReferenceException on the null item.
                            g.register_items(to_add);
                            tracing::error!(
                                "GenerateLoot: unknown loot item template {}",
                                r.item_id
                            );
                            return;
                        }
                    }
                }
                g.register_items(to_add);
            }
            if g.items.is_empty() {
                return;
            }
            // UpdateLootState (has items: no despawn change)
            packet = SCLootableStatePacket {
                loot_owner_type: g.owner_type,
                loot_owner_obj_id: g.owner_obj_id,
                has_loot: true,
            };
            eligible_send.extend(g.eligible_players.iter().copied());
        }
        for id in eligible_send {
            if let Some(p) = server.world.character_by_id(id) {
                p.send(packet);
            }
        }
    }

    /// `WorldInstance.GetNpcsToMakePublicLooting()` over the containers of `npc_obj_ids`.
    pub fn npcs_to_make_public(&self, npc_obj_ids: &[u32]) -> Vec<u32> {
        let now = chrono::Utc::now().timestamp_millis();
        npc_obj_ids
            .iter()
            .copied()
            .filter(|id| self.get(*id).is_some_and(|c| lock(&c).can_make_public(now)))
            .collect()
    }

    /// `LootingContainer.MakeLootPublic()`.
    pub fn make_loot_public(server: &GameServer, obj_id: u32) {
        let lm = &server.systems.economy.loot;
        let Some(c) = lm.get(obj_id) else { return };
        let packet;
        {
            let mut txn = ItemTxn::begin(server);
            let mut rng = SharedRandom;
            let mut ctx = Ctx {
                txn: &mut txn,
                rng: &mut rng,
            };
            let mut g = lock(&c);
            force_loot_roll_to_finish(&mut ctx, &mut g);
            if g.items.is_empty() {
                return;
            }
            // Force full public looting for all non-claimed items
            if let Some(r) = g.team_looting_rule.as_mut() {
                r.loot_method = looting_rule_method::PUBLIC;
                r.minimum_grade = 0;
                r.roll_for_bind_on_pickup = false;
            }
            packet = SCLootableStatePacket {
                loot_owner_type: g.owner_type,
                loot_owner_obj_id: g.owner_obj_id,
                has_loot: true,
            };
        }
        // LootOwner?.BroadcastPacket(packet, false)
        if let Some(obj) = server.world.object(obj_id) {
            if let Some(world) = server.world.world_of(&obj) {
                crate::world::broadcast(&world, obj, &packet, false);
            }
        }
    }

    /// `LootingContainer.OpenBag(player, object2, lootAll)`.
    pub fn open_bag(server: &GameServer, player: &PlayerRef, owner_obj_id: u32, loot_all: bool) {
        Self::open_bag_with(server, player, owner_obj_id, loot_all, &mut SharedRandom);
    }

    pub fn open_bag_with(
        server: &GameServer,
        player: &PlayerRef,
        owner_obj_id: u32,
        loot_all: bool,
        rng: &mut dyn LootRng,
    ) {
        let c = server.systems.economy.loot.container(owner_obj_id);
        let mut txn = ItemTxn::begin(server);
        let mut ctx = Ctx { txn: &mut txn, rng };
        let mut g = lock(&c);
        if !g.opened_by.contains(&player.character_id) {
            g.opened_by.push(player.character_id);
        }
        if loot_all {
            let indexes: Vec<u16> = g.items.keys().copied().collect();
            for idx in indexes {
                try_take_loot_locked(&mut ctx, &mut g, player, idx, true);
            }
        }
        let remaining: Vec<Item> = g.items.values().map(|e| e.item.clone()).collect();
        if !remaining.is_empty() {
            for id in g.opened_by.clone() {
                ctx.send(
                    id,
                    SCLootBagDataPacket {
                        items: remaining.clone(),
                        loot_all,
                    },
                );
            }
        }
    }

    /// `LootingContainer.TryTakeLoot(player, itemIndex, null, didLootAll)`.
    pub fn try_take_loot(
        server: &GameServer,
        player: &PlayerRef,
        owner_obj_id: u32,
        item_index: u16,
        did_loot_all: bool,
    ) -> bool {
        Self::try_take_loot_with(
            server,
            player,
            owner_obj_id,
            item_index,
            did_loot_all,
            &mut SharedRandom,
        )
    }

    pub fn try_take_loot_with(
        server: &GameServer,
        player: &PlayerRef,
        owner_obj_id: u32,
        item_index: u16,
        did_loot_all: bool,
        rng: &mut dyn LootRng,
    ) -> bool {
        let c = server.systems.economy.loot.container(owner_obj_id);
        let mut txn = ItemTxn::begin(server);
        let mut ctx = Ctx { txn: &mut txn, rng };
        let mut g = lock(&c);
        try_take_loot_locked(&mut ctx, &mut g, player, item_index, did_loot_all)
    }

    /// `LootingContainer.CloseBag(player, ...)`.
    pub fn close_bag(&self, player: &PlayerRef, owner_obj_id: u32) {
        let c = self.container(owner_obj_id);
        lock(&c).opened_by.retain(|p| *p != player.character_id);
    }

    /// `LootingContainer.DoPlayerRoll(player, itemIndex, rollRequest)`.
    pub fn do_player_roll(
        server: &GameServer,
        player: &PlayerRef,
        owner_obj_id: u32,
        item_index: u16,
        roll_request: bool,
        rng: &mut dyn LootRng,
    ) {
        let c = server.systems.economy.loot.container(owner_obj_id);
        let mut txn = ItemTxn::begin(server);
        let mut ctx = Ctx { txn: &mut txn, rng };
        let mut g = lock(&c);
        let name = player.data().unit.name.clone();
        let Some(entry) = g.items.get_mut(&item_index) else {
            return;
        };
        let roll = if roll_request {
            ctx.rng.next_i32(1, 100) as i8
        } else {
            -1
        };
        entry.set_roll(player.character_id, roll);
        let item = entry.item.clone();
        let targets: Vec<u32> = entry.player_rolls.iter().map(|(p, _)| *p).collect();
        let done = !entry.player_rolls.iter().any(|(_, r)| *r == 0);
        // Notify the others of this roll result
        for t in targets {
            ctx.send(
                t,
                SCLootDiceNotifyPacket {
                    char_name: name.clone(),
                    item: item.clone(),
                    dice: roll,
                },
            );
        }
        if done {
            finish_rolling(&mut ctx, &mut g, item_index);
        }
    }
}

/// `GetDistanceTo` (see [`LootNpcHooks::distance_to`]).
fn distance(server: &GameServer, a: &WorldObject, b: &WorldObject) -> f32 {
    if let Some(d) = server
        .systems
        .economy
        .loot
        .hooks
        .npcs
        .get()
        .and_then(|h| h.distance_to(server, a, b))
    {
        return d;
    }
    let pos = |o: &WorldObject| match server.world.world_of(o) {
        Some(w) => w.world_pos_rot(o),
        None => o.transform().local(),
    };
    let (pa, pb) = (pos(a), pos(b));
    if pa.x == pb.x && pa.y == pb.y && pa.z == pb.z {
        return 0.0;
    }
    pa.distance_to(&pb, false).max(0.0)
}

fn failed(ctx: &mut Ctx<'_, '_>, g: &Guard<'_>, to: u32, error: i16, idx: u16, template_id: u32) {
    ctx.send(
        to,
        SCLootItemFailedPacket {
            error_message: error,
            loot_owner_type: g.owner_type,
            loot_owner_obj_id: g.owner_obj_id,
            item_index: idx,
            item_template_id: template_id,
        },
    );
}

/// `TryTakeLootLocked`.
fn try_take_loot_locked(
    ctx: &mut Ctx<'_, '_>,
    g: &mut Guard<'_>,
    player: &PlayerRef,
    idx: u16,
    did_loot_all: bool,
) -> bool {
    let pid = player.character_id;
    let Some(entry) = g.items.get(&idx) else {
        return false;
    };
    let (highest, template_id, grade) = (
        entry.highest_roller,
        entry.item.template_id,
        entry.item.grade,
    );
    let rolls_in_progress = entry.player_rolls.len() > 1;
    // Check if it's already claimed by somebody else
    if highest > 0 && highest != pid {
        ctx.send(
            pid,
            SCErrorMsgPacket::new(error_message::NO_PERMISSION_TO_LOOT, highest, true),
        );
        failed(
            ctx,
            g,
            pid,
            error_message::NO_PERMISSION_TO_LOOT,
            idx,
            template_id,
        );
        return false;
    }
    let template = ctx.server().game_data.items.template(template_id).cloned();
    // Check for quest items eligibility
    let quest = template.as_ref().map_or(0, |t| t.loot_quest_id);
    if quest > 0 && !has_quest(ctx.server(), player, quest) {
        failed(
            ctx,
            g,
            pid,
            error_message::NEED_QUEST_TO_INTERACT,
            idx,
            template_id,
        );
        return false;
    }
    // Check if we already have looting right, if so, try to loot again
    if highest == pid {
        return try_distribute(ctx, g, player, idx, did_loot_all);
    }
    // Check if rolls in progress (for more than one player only)
    if rolls_in_progress {
        return false;
    }
    let Some(mut rule) = g.team_looting_rule else {
        // C#: NullReferenceException on TeamLootingRule (loot never generated).
        tracing::error!("TryTakeLoot on a looting container without a looting rule");
        return false;
    };
    let bop = template
        .as_ref()
        .is_some_and(|t| t.bind_type_has_flag(bind_type::BIND_ON_PICKUP));
    let mut roll_mandatory = (rule.minimum_grade > 0 && grade >= rule.minimum_grade)
        || (rule.roll_for_bind_on_pickup && bop);
    let mut allow = false;
    let mut target = player.clone();
    match rule.loot_method {
        looting_rule_method::FREE_FOR_ALL => allow = true,
        looting_rule_method::ROTATE_WINNER => {
            if g.eligible_players.len() <= 1 {
                // Only one possible player, so always allow
                allow = true;
            } else if let Some(team_id) = g.killer_team {
                let eligible: Vec<PlayerRef> = g
                    .eligible_players
                    .iter()
                    .filter_map(|id| ctx.player(*id))
                    .collect();
                let winner = ctx
                    .server()
                    .systems
                    .economy
                    .loot
                    .hooks
                    .teams
                    .get()
                    .and_then(|h| {
                        h.next_loot_winner(ctx.server(), team_id, &eligible, g.owner_obj_id)
                    });
                let winner_id = winner.as_ref().map_or(0, |w| w.character_id);
                if let Some(e) = g.items.get_mut(&idx) {
                    e.highest_roller = winner_id;
                }
                if let Some(w) = winner {
                    let res = try_distribute(ctx, g, &w, idx, did_loot_all);
                    return w.character_id == pid && res;
                }
            } else {
                tracing::warn!("TryTakeLoot, We have no valid Team to apply RotateWinner to. Reverting it to public as a failsafe");
                allow = true;
                roll_mandatory = false;
                rule.loot_method = looting_rule_method::PUBLIC;
                g.team_looting_rule = Some(rule);
            }
        }
        looting_rule_method::LOOT_MASTER => {
            allow = true;
            if let Some(m) = ctx.player(rule.loot_master) {
                target = m;
            }
        }
        looting_rule_method::PUBLIC => {
            allow = true;
            roll_mandatory = false;
        }
        _ => {}
    }
    if !allow {
        return false;
    }
    // If a roll is required, then add all eligible players to the roll pool
    let eligible = g.eligible_players.clone();
    let Some(entry) = g.items.get_mut(&idx) else {
        return false;
    };
    if roll_mandatory && entry.highest_roller == 0 {
        for e in eligible {
            if !entry.player_rolls.iter().any(|(p, _)| *p == e) {
                entry.player_rolls.push((e, 0));
            }
        }
    }
    // If more than one person needs to roll, send out rolls to all players
    if entry.player_rolls.len() > 1 {
        let item = entry.item.clone();
        let rollers: Vec<u32> = entry.player_rolls.iter().map(|(p, _)| *p).collect();
        for r in rollers {
            ctx.send(r, SCLootDicePacket { item: item.clone() });
        }
        return false;
    }
    try_distribute(ctx, g, &target, idx, did_loot_all)
}

/// `FinishRolling(itemEntry)`.
fn finish_rolling(ctx: &mut Ctx<'_, '_>, g: &mut Guard<'_>, idx: u16) {
    let (owner_type, owner) = (g.owner_type, g.owner_obj_id);
    let Some(entry) = g.items.get_mut(&idx) else {
        return;
    };
    let rolls = entry.player_rolls.clone();
    // All done? Send summary as well to all
    for (p, _) in &rolls {
        ctx.send(
            *p,
            SCLootDiceSummaryPacket {
                loot_owner_type: owner_type,
                loot_owner: owner,
                item_index: idx,
                dice_list: rolls.clone(),
            },
        );
    }
    // Find the "highest" rolled value (C#: OrderBy ascending + FirstOrDefault = the LOWEST
    // positive roll, or 0)
    let highest = rolls
        .iter()
        .map(|(_, r)| *r)
        .filter(|r| *r > 0)
        .min()
        .unwrap_or(0);
    // Find highest roller(s) (stable OrderBy)
    let mut highest_entries: Vec<(u32, i8)> = rolls
        .iter()
        .copied()
        .filter(|(_, r)| *r >= highest)
        .collect();
    highest_entries.sort_by_key(|(_, r)| *r);
    if highest_entries.is_empty() {
        // Everybody passed or didn't roll, set it to public
        entry.player_rolls.clear();
        return;
    }
    let pick = ctx.rng.next_below(highest_entries.len() as i32) as usize;
    let winner_id = highest_entries[pick.min(highest_entries.len() - 1)].0;
    entry.highest_roller = winner_id;
    if let Some(w) = ctx.player(winner_id) {
        try_distribute(ctx, g, &w, idx, false);
    }
}

/// `ForceLootRollToFinish()`.
fn force_loot_roll_to_finish(ctx: &mut Ctx<'_, '_>, g: &mut Guard<'_>) {
    let indexes: Vec<u16> = g.items.keys().copied().collect();
    for idx in indexes {
        let Some(entry) = g.items.get_mut(&idx) else {
            continue;
        };
        if entry.player_rolls.iter().all(|(_, r)| *r != 0) {
            continue;
        }
        for r in entry.player_rolls.iter_mut() {
            if r.1 == 0 {
                r.1 = -1;
            }
        }
        finish_rolling(ctx, g, idx);
    }
}

/// `UpdateLootState()`.
fn update_loot_state(ctx: &mut Ctx<'_, '_>, g: &Guard<'_>) {
    let has_items = !g.items.is_empty();
    for id in g.eligible_players.clone() {
        ctx.send(
            id,
            SCLootableStatePacket {
                loot_owner_type: g.owner_type,
                loot_owner_obj_id: g.owner_obj_id,
                has_loot: has_items,
            },
        );
    }
    if has_items {
        return;
    }
    match g.owner_type {
        loot_owner_type::NPC => {
            let server = ctx.server();
            let Some(npc) = server
                .world
                .object(g.owner_obj_id)
                .and_then(|o| o.as_npc().cloned())
            else {
                return;
            };
            let Some(h) = server.systems.economy.loot.hooks.npcs.get() else {
                return;
            };
            let mut min = g.creation_time_ms;
            if let Some(t) = h.spawner_despawn_time(server, &npc) {
                min = g.creation_time_ms + (t * 1000.0) as i64;
            }
            let now = chrono::Utc::now().timestamp_millis();
            if min < now {
                min = now + (POST_LOOT_MINIMUM_DESPAWN_TIME * 1000.0) as i64;
            }
            h.set_despawn(server, &npc, min);
        }
        t => tracing::warn!("UpdateLootState, Unsupported LootOwnerType: {t} after looting"),
    }
}

/// `TryDistributeLootToPlayer(player, itemEntry, didLootAll)`.
fn try_distribute(
    ctx: &mut Ctx<'_, '_>,
    g: &mut Guard<'_>,
    player: &PlayerRef,
    idx: u16,
    did_loot_all: bool,
) -> bool {
    // TryReserveLootItem
    let Some(mut entry) = g.items.remove(&idx) else {
        return false;
    };
    let pid = player.character_id;
    let old_id = entry.item.id;
    let template_id = entry.item.template_id;
    let count = entry.item.count;
    let server = ctx.txn.server;
    let inv = Inv::of(player);
    let auto_equip = server.game_data.items.is_auto_equip_trade_pack(template_id);
    // Auto-equipped packs need the backpack slot (and bag room only to remove a glider).
    // Reject malformed multi-pack loot before moving the worn item or allocating an id.
    let no_room = if auto_equip {
        count != 1
    } else {
        let money = player.data().money;
        inv.bag()
            .lock()
            .expect("container poisoned")
            .space_left_for_item(&entry.item, &server.game_data.items, money)
            < count
    };
    if no_room {
        g.items.insert(idx, entry);
        failed(ctx, g, pid, error_message::BAG_FULL, idx, template_id);
        return false;
    }
    if template_id == ITEM_COINS {
        inventory::add_money(
            &mut ctx.txn.out(),
            player,
            slot_type::INVENTORY,
            count,
            task_type::DEPOSIT_MONEY,
        );
    } else if auto_equip {
        // Auto-equip tradepack item branch.
        if inventory::take_off_backpack(ctx.txn, &inv, task_type::RECOVER_DOODAD_ITEM, true) {
            let new_id = match server.ids.item.get_next_id() {
                Ok(id) => id,
                Err(e) => {
                    tracing::error!("{e}");
                    g.items.insert(idx, entry);
                    return false;
                }
            };
            let mut item = entry.item.clone();
            item.id = u64::from(new_id);
            let res = inv
                .equipment()
                .lock()
                .expect("container poisoned")
                .add_item(
                    task_type::RECOVER_DOODAD_ITEM,
                    item,
                    equip_slot::BACKPACK,
                    &server.game_data.items,
                    &mut ctx.txn.env.fx,
                );
            if !matches!(res, Ok(true)) {
                server.ids.item.release_id(new_id);
                entry.item.id = old_id;
                g.items.insert(idx, entry);
                failed(ctx, g, pid, error_message::BAG_FULL, idx, template_id);
                return false;
            }
            entry.item.id = u64::from(new_id);
        } else {
            tracing::warn!(
                "AutoEquipTradePack: Failed to take off backpack for auto-equip tradepack item TemplateId={template_id}."
            );
            g.items.insert(idx, entry);
            failed(ctx, g, pid, error_message::BAG_FULL, idx, template_id);
            return false;
        }
    } else {
        // C#: a new item id is allocated for the loot entry and, on success, never released
        // (AcquireDefaultItem creates its own items) — mirrored id leak.
        let new_id = match server.ids.item.get_next_id() {
            Ok(id) => id,
            Err(e) => {
                tracing::error!("{e}");
                g.items.insert(idx, entry);
                return false;
            }
        };
        entry.item.id = u64::from(new_id);
        let task = if did_loot_all {
            task_type::LOOT_ALL
        } else {
            task_type::LOOT
        };
        let ok = inv
            .bag()
            .lock()
            .expect("container poisoned")
            .acquire_default_item(
                task,
                template_id,
                count,
                i32::from(entry.item.grade),
                0,
                &mut ctx.txn.env,
            );
        if !ok {
            server.ids.item.release_id(new_id);
            entry.item.id = old_id;
            g.items.insert(idx, entry);
            failed(ctx, g, pid, error_message::BAG_FULL, idx, template_id);
            return false;
        }
    }
    ctx.send(
        pid,
        SCLootItemTookPacket {
            item_template_id: template_id,
            item_index: idx,
            loot_owner_type: g.owner_type,
            loot_owner_id: g.owner_obj_id,
            count,
        },
    );
    if g.items.is_empty() {
        update_loot_state(ctx, g);
    }
    true
}

// ---------------------------------------------------------------------------------------------
// C->S packets
// ---------------------------------------------------------------------------------------------

/// `CSLootOpenBagPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSLootOpenBagPacket {
    pub obj_id: u32,
    pub obj2_id: u32,
    pub loot_all: bool,
}

impl CSLootOpenBagPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            obj2_id: s.read_bc()?,
            loot_all: s.read_bool()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(player) = active(conn) else { return };
        let Some(world) = server.world.world_of(&WorldObject::from(&player)) else {
            return;
        };
        let owner = world.object(self.obj_id);
        let object2 = world.object(self.obj2_id);
        let lm = &server.systems.economy.loot;
        let try_func = |target: &Option<WorldObject>| -> bool {
            let Some(t @ WorldObject::Doodad(_)) = target else {
                return false;
            };
            if lm.item_count(t.obj_id()) > 0 {
                return false;
            }
            lm.hooks
                .npcs
                .get()
                .is_some_and(|h| h.try_handle_func_driven_loot(server, &player, t))
        };
        if try_func(&owner) || try_func(&object2) {
            return;
        }
        if let Some(o) = owner {
            LootManager::open_bag(server, &player, o.obj_id(), self.loot_all);
        }
    }
}
sync_client_packet!(CSLootOpenBagPacket);

/// `CSLootItemPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSLootItemPacket {
    pub item_index: u16,
    pub owner_type: u16,
    pub owner_obj_id: u32,
    pub u1: u16,
    pub u2: u16,
}

impl CSLootItemPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            item_index: s.read_u16()?,
            owner_type: s.read_u16()?,
            owner_obj_id: s.read_bc()?,
            u1: s.read_u16()?,
            u2: s.read_u16()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(player) = active(conn) else { return };
        let Some(world) = server.world.world_of(&WorldObject::from(&player)) else {
            return;
        };
        if world.object(self.owner_obj_id).is_some() {
            LootManager::try_take_loot(server, &player, self.owner_obj_id, self.item_index, false);
        }
    }
}
sync_client_packet!(CSLootItemPacket);

/// `CSLootCloseBagPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSLootCloseBagPacket {
    pub item_index: u16,
    pub owner_type: u16,
    pub owner_obj_id: u32,
    pub b: u8,
}

impl CSLootCloseBagPacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            item_index: s.read_u16()?,
            owner_type: s.read_u16()?,
            owner_obj_id: s.read_bc()?,
            b: s.read_u8()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(player) = active(conn) else { return };
        let Some(world) = server.world.world_of(&WorldObject::from(&player)) else {
            return;
        };
        if world.object(self.owner_obj_id).is_some() {
            server
                .systems
                .economy
                .loot
                .close_bag(&player, self.owner_obj_id);
        }
    }
}
sync_client_packet!(CSLootCloseBagPacket);

/// `CSLootDicePacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSLootDicePacket {
    pub item_index: u16,
    pub loot_owner_type: u16,
    pub loot_owner_obj_id: u32,
    pub b: u8,
    pub roll_request: bool,
    pub remaining: Vec<u8>,
}

impl CSLootDicePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        let item_index = s.read_u16()?;
        let loot_owner_type = s.read_u16()?;
        let loot_owner_obj_id = s.read_bc()?;
        let b = s.read_u8()?;
        let roll_request = s.read_bool()?;
        let left = s.remaining();
        let remaining = if left > 0 {
            s.read_bytes(left)?
        } else {
            Vec::new()
        };
        Ok(Self {
            item_index,
            loot_owner_type,
            loot_owner_obj_id,
            b,
            roll_request,
            remaining,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(player) = active(conn) else { return };
        let Some(world) = server.world.world_of(&WorldObject::from(&player)) else {
            return;
        };
        let found = match self.loot_owner_type {
            loot_owner_type::NPC => world.npc(self.loot_owner_obj_id).is_some(),
            loot_owner_type::DOODAD => world.doodad(self.loot_owner_obj_id).is_some(),
            _ => false,
        };
        if !found {
            tracing::warn!(
                "CSLootDice, LootOwner not found: {}:{} by {}",
                self.loot_owner_type,
                self.loot_owner_obj_id,
                player.data().unit.name
            );
            return;
        }
        LootManager::do_player_roll(
            server,
            &player,
            self.loot_owner_obj_id,
            self.item_index,
            self.roll_request,
            &mut SharedRandom,
        );
    }
}
sync_client_packet!(CSLootDicePacket);

/// `CSChangeLootingRulePacket`. Not registered: the opcode is handled by
/// `systems::social` (`TeamManager.ChangeLootingRule`); kept for its hook path.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSChangeLootingRulePacket {
    pub team_id: u32,
    pub looting_rule: LootingRule,
    pub change_flags: u8,
}

impl CSChangeLootingRulePacket {
    fn read_body(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            team_id: s.read_u32()?,
            looting_rule: LootingRule::read(s)?,
            change_flags: s.read_u8()?,
        })
    }

    fn run(self, server: &GameServer, conn: &mut GameConnection) {
        let Some(player) = active(conn) else { return };
        if let Some(h) = server.systems.economy.loot.hooks.teams.get() {
            h.change_looting_rule(
                server,
                &player,
                self.team_id,
                self.change_flags,
                self.looting_rule,
            );
        }
    }
}
sync_client_packet!(CSChangeLootingRulePacket);

/// Registers the loot packets.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSLootOpenBagPacket>(LEVEL_PLAIN, cs::CSLootOpenBagPacket);
    reg.register::<CSLootItemPacket>(LEVEL_PLAIN, cs::CSLootItemPacket);
    reg.register::<CSLootCloseBagPacket>(LEVEL_PLAIN, cs::CSLootCloseBagPacket);
    reg.register::<CSLootDicePacket>(LEVEL_PLAIN, cs::CSLootDicePacket);
}
