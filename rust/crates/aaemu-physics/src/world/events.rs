//! **P3 — events** (`world.md` §5): 15 event types (sizes [`crate::ids::EVENT_SIZES`]), per
//! type an "immediate" and a "logged" client list sorted by priority (descending), the logged
//! queue (8 KB arena in the binary) delivered by `PumpLoggedEvents` (world slot 77,
//! 0x35094FA0).
//!
//! | slot / function | address | port |
//! |---|---|---|
//! | AddEventClient (75) | 0x35077AE0 | [`World::add_event_client`] |
//! | RemoveEventClient (76) | 0x35077BC0 | [`World::remove_event_client`] |
//! | PumpLoggedEvents (77) | 0x35094FA0 | [`World::pump_logged_events`] |
//! | ClearLoggedEvents (79) | 0x3507D4F0 | [`World::clear_logged_events`] |
//! | signal: state change (type 2) | 0x350962D0 | [`World::signal_state_change`] |
//! | signal: simclass change (type 3) | 0x35096AE0 | [`World::signal_event`] |
//! | signal: collision (type 1) | 0x35037EF0 | [`World::signal_event`] |
//! | signal: post step (type 4) | 0x35037FE0 | [`World::signal_event`] |
//!
//! An entity's flags (`+92`) select which events are generated for it: for type *t* the
//! immediate mask bit and the logged mask bit (`world.md` §5 table). Events carry entity
//! indices instead of pointers.

use super::{EntIdx, World};
use crate::math::Vec3;

/// Event payloads (the fields the port fills; layouts in `world.md` §5).
#[derive(Clone, Debug, PartialEq)]
pub enum Event {
    /// Type 0: bounding-box overlap (GetEntitiesInBox petitioner).
    BBoxOverlap { ent: [EntIdx; 2] },
    /// Type 1: collision.
    Collision {
        ent: [EntIdx; 2],
        pt: Vec3,
        n: Vec3,
        v: [Vec3; 2],
        mass: [f32; 2],
        partid: [i32; 2],
        idmat: [i32; 2],
        penetration: f32,
        normimpulse: f32,
        radius: f32,
    },
    /// Type 2: grid/state change (RepositionEntity with flag 2).
    StateChange { ent: EntIdx, simclass_old: i32, simclass_new: i32, time_idle: f32 },
    /// Type 3: awake / simclass change.
    SimClassChange { ent: EntIdx, simclass_old: i32, simclass_new: i32 },
    /// Type 4: post step.
    PostStep { ent: EntIdx, dt: f32, pos: Vec3, q: crate::math::Quat, step: i32 },
    /// Type 11: area callback (logged copy; immediate clients are the area hooks,
    /// [`World::add_area_callback`]).
    AreaCallback(Box<super::areas::AreaCallbackEvent>),
    /// Type 12: area added / changed / removed.
    AreaChange { area: EntIdx, bbox: [Vec3; 2] },
}

impl Event {
    /// The event type id.
    #[must_use]
    pub fn type_id(&self) -> usize {
        match self {
            Event::BBoxOverlap { .. } => 0,
            Event::Collision { .. } => 1,
            Event::StateChange { .. } => 2,
            Event::SimClassChange { .. } => 3,
            Event::PostStep { .. } => 4,
            Event::AreaCallback(_) => 11,
            Event::AreaChange { .. } => 12,
        }
    }
}

/// An event client: returns 0 to stop the delivery to lower-priority clients.
pub type EventClient = Box<dyn FnMut(&Event) -> i32 + Send>;

struct ClientNode {
    handle: u64,
    prio: f32,
    f: EventClient,
}

impl std::fmt::Debug for ClientNode {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("ClientNode")
            .field("handle", &self.handle)
            .field("prio", &self.prio)
            .finish()
    }
}

/// Event clients and the logged queue.
#[derive(Debug, Default)]
pub struct EventState {
    /// `+21232 + 16·type + 8·logged`.
    clients: Vec<[Vec<ClientNode>; 2]>,
    next_handle: u64,
    /// The logged queue (`+21088`), in signal order.
    pub logged: Vec<Event>,
}

/// Entity flag masks per event type: (immediate, logged).
fn masks(t: usize) -> (u32, u32) {
    match t {
        1 => (0x2000, 0x200_0000),
        2 => (0x1000, 0x100_0000),
        3 => (0x4000, 0x400_0000),
        4 => (0x8_0000, 0x800_0000),
        _ => (u32::MAX, u32::MAX),
    }
}

impl World {
    /// World slot 75 `AddEventClient(type, fn, bLogged, priority)`: insert sorted by
    /// priority (descending; equal priorities after the existing ones). Returns a handle for
    /// [`World::remove_event_client`].
    pub fn add_event_client(&mut self, etype: usize, logged: bool, prio: f32, f: EventClient) -> u64 {
        if self.events.clients.len() < 16 {
            self.events.clients.resize_with(16, Default::default);
        }
        self.events.next_handle += 1;
        let h = self.events.next_handle;
        let list = &mut self.events.clients[etype][usize::from(logged)];
        let pos = list.iter().position(|c| c.prio < prio).unwrap_or(list.len());
        list.insert(pos, ClientNode { handle: h, prio, f });
        h
    }

    /// World slot 76 `RemoveEventClient`.
    pub fn remove_event_client(&mut self, etype: usize, logged: bool, handle: u64) -> bool {
        if let Some(l) = self.events.clients.get_mut(etype) {
            let list = &mut l[usize::from(logged)];
            if let Some(i) = list.iter().position(|c| c.handle == handle) {
                list.remove(i);
                return true;
            }
        }
        false
    }

    fn has_clients(&self, t: usize, logged: bool) -> bool {
        self.events
            .clients
            .get(t)
            .is_some_and(|l| !l[usize::from(logged)].is_empty())
    }

    /// Deliver `ev` to the immediate clients (when `flags` select it) and queue it for the
    /// logged clients. Returns the summed immediate results (1 when there are none).
    pub fn signal_event(&mut self, flags: u32, ev: Event) -> i32 {
        let t = ev.type_id();
        let (mi, ml) = masks(t);
        let mut r = 1;
        if flags & mi != 0 && self.has_clients(t, false) {
            r = 0;
            for c in &mut self.events.clients[t][0] {
                r += (c.f)(&ev);
            }
        }
        if flags & ml != 0 && self.has_clients(t, true) {
            self.events.logged.push(ev);
        }
        r
    }

    /// Call the immediate clients of the event's type and return the sum of their results
    /// (0 without clients) — the loops the rigid code inlines over `W+21232 + 16·type`.
    pub fn call_immediate_clients(&mut self, ev: &Event) -> i32 {
        let t = ev.type_id();
        let mut r = 0;
        if let Some(l) = self.events.clients.get_mut(t) {
            for c in &mut l[0] {
                r += (c.f)(ev);
            }
        }
        r
    }

    /// Append an event to the logged queue unconditionally (the rigid code posts collision
    /// events under the queue lock itself) → its queue index.
    pub fn post_logged_event(&mut self, ev: Event) -> usize {
        self.events.logged.push(ev);
        self.events.logged.len() - 1
    }

    /// Overwrite a queued event (the per-step collision slots of a rigid entity are pointers
    /// into the queue). Indices from an earlier pump are ignored.
    pub fn replace_logged_event(&mut self, idx: usize, ev: Event) {
        if let Some(x) = self.events.logged.get_mut(idx) {
            *x = ev;
        }
    }

    /// `|v₀ − v₁|²` of a queued collision event (`(dx² + dy²) + dz²` in the order of
    /// `sub_350AE0D0`'s slot scan: y, x, z), 1e10 when absent.
    #[must_use]
    pub fn logged_collision_dv(&self, idx: usize) -> f32 {
        match self.events.logged.get(idx) {
            Some(Event::Collision { v, .. }) => {
                let (a, b) = (v[0], v[1]);
                (((a.y - b.y) * (a.y - b.y)) + ((a.x - b.x) * (a.x - b.x))) + ((a.z - b.z) * (a.z - b.z))
            }
            _ => 1e10,
        }
    }

    /// `sub_350962D0`: event 2 for a re-listed entity.
    pub fn signal_state_change(&mut self, e: EntIdx, simclass_old: i32) {
        let ent = self.ent(e);
        let ev = Event::StateChange {
            ent: e,
            simclass_old,
            simclass_new: ent.sim_class(),
            time_idle: ent.time_idle,
        };
        let flags = ent.flags;
        self.signal_event(flags, ev);
    }

    /// World slot 77 `PumpLoggedEvents`: deliver the logged queue (events of deleted
    /// entities are dropped; a client returning 0 stops the delivery of that event).
    pub fn pump_logged_events(&mut self) {
        self.pump_counter += 1;
        let evs = std::mem::take(&mut self.events.logged);
        for ev in evs {
            let alive = |w: &World, e: EntIdx| e.is_none() || w.ents.get(e.0 as usize).is_some_and(|o| o.as_ref().is_some_and(|x| x.deleted == 0));
            let ok = match &ev {
                Event::Collision { ent, .. } | Event::BBoxOverlap { ent } => alive(self, ent[0]) && alive(self, ent[1]),
                Event::StateChange { ent, .. } | Event::SimClassChange { ent, .. } | Event::PostStep { ent, .. } => alive(self, *ent),
                Event::AreaChange { .. } => true,
                Event::AreaCallback(e) => e.ent.is_none_or(|x| alive(self, x)),
            };
            if !ok {
                continue;
            }
            let t = ev.type_id();
            if let Some(l) = self.events.clients.get_mut(t) {
                for c in &mut l[1] {
                    if (c.f)(&ev) == 0 {
                        break;
                    }
                }
            }
        }
    }

    /// World slot 79: drop the logged queue.
    pub fn clear_logged_events(&mut self) {
        self.events.logged.clear();
    }
}
