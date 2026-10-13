//! The live quest objects of one character: `CharacterQuests.ActiveQuests`, the `Quest` /
//! `QuestStep` / `QuestComponent` / `QuestAct` instances and the quest-related
//! `UnitEvents` subscriptions. Lives on `Character::quest_state`.
//!
//! The C# objects reference each other (`Quest.Owner`, `QuestAct.QuestComponent.Parent.Parent`);
//! here a quest is addressed by a [`QuestHandle`] (its slot in `QuestState::quests`) and an act
//! by an [`ActPath`] inside it. A quest stays in `quests` while an operation still works on it
//! after `DropQuest` removed it from `active` (like a C# object kept alive by a local), and is
//! garbage-collected when the operation ends.

use std::collections::HashMap;

use super::model::{kind, status, ItemCreationDefinition};
use super::templates::ActRef;

/// Identity of a `Quest` object of one character.
pub type QuestHandle = u64;

/// `DateTime.MinValue` / `MaxValue` as unix milliseconds.
pub const DATETIME_MIN_MS: i64 = -62_135_596_800_000;
pub const DATETIME_MAX_MS: i64 = 253_402_300_799_999;

/// A `Dictionary<TKey, TValue>` with .NET's enumeration order: entries are appended, a removal
/// frees the slot and the next insertion reuses the most recently freed slot.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DotNetMap<K, V> {
    entries: Vec<Option<(K, V)>>,
    free: Vec<usize>,
}

impl<K, V> Default for DotNetMap<K, V> {
    fn default() -> Self {
        Self {
            entries: Vec::new(),
            free: Vec::new(),
        }
    }
}

impl<K: PartialEq + Copy, V> DotNetMap<K, V> {
    fn slot(&self, key: K) -> Option<usize> {
        self.entries
            .iter()
            .position(|e| e.as_ref().is_some_and(|(k, _)| *k == key))
    }

    pub fn get(&self, key: K) -> Option<&V> {
        self.slot(key)
            .and_then(|i| self.entries[i].as_ref().map(|(_, v)| v))
    }

    pub fn contains_key(&self, key: K) -> bool {
        self.slot(key).is_some()
    }

    /// `Add` (returns false where the C# throws on a duplicate key).
    pub fn add(&mut self, key: K, value: V) -> bool {
        if self.contains_key(key) {
            return false;
        }
        match self.free.pop() {
            Some(i) => self.entries[i] = Some((key, value)),
            None => self.entries.push(Some((key, value))),
        }
        true
    }

    pub fn remove(&mut self, key: K) -> Option<V> {
        let i = self.slot(key)?;
        let (_, v) = self.entries[i].take()?;
        self.free.push(i);
        Some(v)
    }

    pub fn iter(&self) -> impl Iterator<Item = (K, &V)> {
        self.entries
            .iter()
            .filter_map(|e| e.as_ref().map(|(k, v)| (*k, v)))
    }

    pub fn values(&self) -> impl Iterator<Item = &V> {
        self.iter().map(|(_, v)| v)
    }

    pub fn keys(&self) -> impl Iterator<Item = K> + '_ {
        self.iter().map(|(k, _)| k)
    }

    pub fn len(&self) -> usize {
        self.entries.len() - self.free.len()
    }

    pub fn is_empty(&self) -> bool {
        self.len() == 0
    }
}

/// Position of a `QuestAct` inside its quest: step (in `QuestSteps` order), component (in the
/// step's `Components` order), act (in the component's `Acts` order).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct ActPath {
    pub step: usize,
    pub component: usize,
    pub act: usize,
}

/// `QuestAct`.
#[derive(Debug, Clone, PartialEq)]
pub struct QuestActState {
    pub template: ActRef,
    /// `_overrideObjectiveCompleted`.
    pub override_completed: bool,
    /// CheckSphere has no wire objective slot; presence is local runtime state.
    pub sphere_inside: bool,
    pub supplied: bool,
}

/// `QuestComponent`.
#[derive(Debug, Clone, PartialEq)]
pub struct QuestComponentState {
    pub template_id: u32,
    pub override_completed: bool,
    /// `IsCurrentlyActive`.
    pub is_active: bool,
    pub acts: Vec<QuestActState>,
}

/// `QuestStep`.
#[derive(Debug, Clone, PartialEq)]
pub struct QuestStepState {
    /// `ThisStep`.
    pub kind: u8,
    pub components: Vec<QuestComponentState>,
}

/// `Quest`.
#[derive(Debug, Clone, PartialEq)]
pub struct Quest {
    /// `Id` (QuestIdManager).
    pub id: u32,
    pub template_id: u32,
    pub objectives: [i32; 5],
    pub status: u8,
    /// `_step`.
    pub step: u8,
    /// `QuestConditionObj`.
    pub condition: u8,
    pub ready_to_report_npc: bool,
    /// `Time` (unix ms, [`DATETIME_MIN_MS`] = `DateTime.MinValue`).
    pub time_ms: i64,
    pub doodad_id: i64,
    pub obj_id: i64,
    pub component_id: u32,
    pub current_component_id: u32,
    pub acceptor_type: u8,
    pub acceptor_id: u32,
    pub reward_items: Vec<ItemCreationDefinition>,
    pub cleanup_items: Vec<ItemCreationDefinition>,
    pub reward_coins: i32,
    pub reward_exp: i32,
    pub allow_item_rewards: bool,
    pub reward_ratio: f64,
    /// `_questInitializationFinished`.
    pub init_finished: bool,
    pub forced_start: bool,
    /// `_requestEvaluationFlag`.
    pub request_evaluation: bool,
    /// `_skipUpdatePacket`.
    pub skip_update: bool,
    pub selected_reward_index: i32,
    /// `QuestSteps` in insertion order.
    pub steps: Vec<QuestStepState>,
}

impl Quest {
    /// The persistent / wire part (`WriteData` / `Write`).
    pub fn row(&self) -> crate::models::char_data::ActiveQuest {
        crate::models::char_data::ActiveQuest {
            id: self.id,
            template_id: self.template_id,
            status: self.status,
            objectives: self.objectives,
            step: self.step,
            acceptor_type: self.acceptor_type,
            component_id: self.component_id,
            acceptor_id: self.acceptor_id,
            time_ms: self.time_ms,
        }
    }

    /// Index of a step in `QuestSteps` (`TryGetValue`).
    pub fn step_index(&self, k: u8) -> Option<usize> {
        self.steps.iter().position(|s| s.kind == k)
    }

    pub fn new_blank(id: u32, template_id: u32) -> Self {
        Self {
            id,
            template_id,
            objectives: [0; 5],
            status: status::INVALID,
            step: kind::INVALID,
            condition: 0,
            ready_to_report_npc: false,
            time_ms: DATETIME_MIN_MS,
            doodad_id: 0,
            obj_id: 0,
            component_id: 0,
            current_component_id: 0,
            acceptor_type: 0,
            acceptor_id: 0,
            reward_items: Vec::new(),
            cleanup_items: Vec::new(),
            reward_coins: 0,
            reward_exp: 0,
            allow_item_rewards: true,
            reward_ratio: 1.0,
            init_finished: false,
            forced_start: false,
            request_evaluation: false,
            skip_update: false,
            selected_reward_index: 0,
            steps: Vec::new(),
        }
    }
}

/// The quest `UnitEvents` a quest act can subscribe to.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum EventKind {
    MonsterHunt,
    MonsterGroupHunt,
    TalkMade,
    TalkNpcGroupMade,
    Aggro,
    ItemGather,
    ItemGroupGather,
    ItemUse,
    ItemGroupUse,
    Interaction,
    ExpressFire,
    LevelUp,
    MateLevelUp,
    AbilityLevelUp,
    EnterSphere,
    ExitSphere,
    Craft,
    ZoneKill,
    CinemaStarted,
    CinemaEnded,
    ReportNpc,
    AcceptDoodad,
    ReportDoodad,
    ReportJournal,
    QuestComplete,
    GuardNpcDied,
    Kill,
    TimerExpired,
    QuestStepChanged,
}

/// One `Events.OnX += questAct.OnX`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Subscription {
    pub event: EventKind,
    pub quest: QuestHandle,
    pub path: ActPath,
}

/// `Character.Quests` (live part) + `Character.Events` quest subscriptions +
/// `CurrentlyPlayingCinemaId`.
#[derive(Debug, Clone, Default)]
pub struct QuestState {
    /// Live `Quest` objects.
    pub quests: HashMap<QuestHandle, Quest>,
    /// `ActiveQuests`: template id -> quest.
    pub active: DotNetMap<u32, QuestHandle>,
    next_handle: QuestHandle,
    /// The subscriptions in invocation-list order.
    pub subs: Vec<Subscription>,
    pub currently_playing_cinema_id: u32,
    /// Set once `CharacterQuests.Load` built the active quests from the DB rows.
    pub loaded: bool,
    /// Evaluations requested by an operation without a player handle (data-level tests).
    pub local_queue: Vec<QuestHandle>,
}

impl QuestState {
    pub fn alloc(&mut self, quest: Quest) -> QuestHandle {
        self.next_handle += 1;
        let h = self.next_handle;
        self.quests.insert(h, quest);
        h
    }

    pub fn quest(&self, h: QuestHandle) -> Option<&Quest> {
        self.quests.get(&h)
    }

    pub fn quest_mut(&mut self, h: QuestHandle) -> Option<&mut Quest> {
        self.quests.get_mut(&h)
    }

    /// `ActiveQuests.TryGetValue(templateId)`.
    pub fn active_handle(&self, template_id: u32) -> Option<QuestHandle> {
        self.active.get(template_id).copied()
    }

    pub fn active_quest(&self, template_id: u32) -> Option<&Quest> {
        self.active_handle(template_id).and_then(|h| self.quest(h))
    }

    /// `HasQuest`.
    pub fn has_quest(&self, template_id: u32) -> bool {
        self.active.contains_key(template_id)
    }

    pub fn local_queue_push(&mut self, h: QuestHandle) {
        if !self.local_queue.contains(&h) {
            self.local_queue.push(h);
        }
    }

    /// `+=`.
    pub fn subscribe(&mut self, event: EventKind, quest: QuestHandle, path: ActPath) {
        self.subs.push(Subscription { event, quest, path });
    }

    /// `-=` (removes the last occurrence, like `Delegate.Remove`).
    pub fn unsubscribe(&mut self, event: EventKind, quest: QuestHandle, path: ActPath) {
        let s = Subscription { event, quest, path };
        if let Some(i) = self.subs.iter().rposition(|x| *x == s) {
            self.subs.remove(i);
        }
    }

    /// The invocation list of one event at invoke time.
    pub fn subscribers(&self, event: EventKind) -> Vec<Subscription> {
        self.subs
            .iter()
            .filter(|s| s.event == event)
            .copied()
            .collect()
    }

    /// Drops the quest objects nothing references any more (and their leaked subscriptions:
    /// a C# handler of a finalized quest still runs, but on an object no one can observe).
    pub fn collect_garbage(&mut self, keep: &[QuestHandle]) {
        let live: Vec<QuestHandle> = self
            .active
            .values()
            .copied()
            .chain(keep.iter().copied())
            .chain(self.local_queue.iter().copied())
            .collect();
        self.quests.retain(|h, _| live.contains(h));
        self.subs.retain(|s| live.contains(&s.quest));
    }

    /// The active quests' rows (`CharacterQuests.ActiveQuests.Values` order).
    pub fn rows(&self) -> Vec<crate::models::char_data::ActiveQuest> {
        self.active
            .values()
            .filter_map(|h| self.quests.get(h))
            .map(Quest::row)
            .collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dotnet_map_reuses_freed_slots() {
        let mut m = DotNetMap::default();
        assert!(m.add(1u32, 'a'));
        assert!(m.add(2, 'b'));
        assert!(m.add(3, 'c'));
        assert!(!m.add(2, 'x'));
        assert_eq!(m.remove(1), Some('a'));
        assert_eq!(m.remove(3), Some('c'));
        // Last freed slot (3's) first, then 1's.
        m.add(4, 'd');
        m.add(5, 'e');
        assert_eq!(m.keys().collect::<Vec<_>>(), vec![5, 2, 4]);
        assert_eq!(m.len(), 3);
        m.add(6, 'f');
        assert_eq!(m.keys().collect::<Vec<_>>(), vec![5, 2, 4, 6]);
    }

    #[test]
    fn unsubscribe_removes_last_occurrence() {
        let mut st = QuestState::default();
        let p = ActPath {
            step: 0,
            component: 0,
            act: 0,
        };
        st.subscribe(EventKind::ReportDoodad, 1, p);
        st.subscribe(EventKind::TalkMade, 1, p);
        st.subscribe(EventKind::ReportDoodad, 1, p);
        st.unsubscribe(EventKind::ReportDoodad, 1, p);
        assert_eq!(st.subscribers(EventKind::ReportDoodad).len(), 1);
        assert_eq!(st.subs[1].event, EventKind::TalkMade);
    }
}
