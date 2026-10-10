//! World object handles: the Rust shape of the C# `GameObject` hierarchy as it lives in a
//! `WorldInstance`.
//!
//! * Every object is an `Arc` handle: [`PlayerRef`] for player characters (it also owns the
//!   connection), [`Entity<D>`] for everything else (`NpcRef = Arc<Entity<Npc>>`, ...).
//! * The handle keeps the immutable identity (`obj_id`) and the world bookkeeping the visibility
//!   code needs without touching the object's data ([`ObjectCore`]: region, `IsVisible`,
//!   transform children). The C# object's members are the data `D` behind one `Mutex`
//!   ([`Entity::data`]).
//! * [`WorldObject`] is the closed set of kinds a region can hold (`GameObject` in C#), with
//!   accessors for the shared traits: [`WorldObject::with_unit`] (`Unit`),
//!   [`WorldObject::with_base_unit`] (`BaseUnit`), [`WorldObject::transform`].
//!
//! Locking rules (deadlock freedom):
//! 1. Lock order is `WorldInstance` grid -> one object's data -> `ObjectCore` leaf locks.
//! 2. Hold at most one object's data lock at a time. To combine two objects, copy what you
//!    need out of the first, drop its guard, then lock the second.
//! 3. Never call into the world (`show`, `add_visible_object`, lookups that lock the grid,
//!    broadcasts) while holding an object's data lock.
//! 4. `ObjectCore` locks are leaves: never lock anything else while holding them.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};

use crate::models::doodad::Doodad;
use crate::models::gimmick::Gimmick;
use crate::models::house::House;
use crate::models::mate::Mate;
use crate::models::npc::Npc;
use crate::models::shipyard::Shipyard;
use crate::models::slave::Slave;
use crate::models::transfer::Transfer;
use crate::models::transform::Transform;
use crate::models::unit::{BaseUnitData, GameObjectData, UnitData};

use super::PlayerRef;

/// `GameObject.Region`: (instance id, sector x, sector y).
pub type RegionKey = (u32, i32, i32);

/// The world bookkeeping of a `GameObject` that the visibility code owns.
#[derive(Debug, Default)]
pub struct ObjectCore {
    region: Mutex<Option<RegionKey>>,
    visible: AtomicBool,
    /// `Transform.Children` (the objects attached to this one, see
    /// `WorldInstance::attach_child`).
    children: Mutex<Vec<WorldObject>>,
}

impl ObjectCore {
    /// `GameObject.Region`.
    pub fn region(&self) -> Option<RegionKey> {
        *self.region.lock().expect("region poisoned")
    }

    pub(crate) fn set_region(&self, region: Option<RegionKey>) {
        *self.region.lock().expect("region poisoned") = region;
    }

    /// `IsVisible`.
    pub fn is_visible(&self) -> bool {
        self.visible.load(Ordering::Relaxed)
    }

    pub(crate) fn set_visible(&self, v: bool) {
        self.visible.store(v, Ordering::Relaxed);
    }

    /// A snapshot of `Transform.Children`.
    pub fn children(&self) -> Vec<WorldObject> {
        self.children.lock().expect("children poisoned").clone()
    }

    pub(crate) fn add_child(&self, child: WorldObject) {
        let mut c = self.children.lock().expect("children poisoned");
        if !c.iter().any(|o| o.obj_id() == child.obj_id()) {
            c.push(child);
        }
    }

    pub(crate) fn remove_child(&self, obj_id: u32) -> bool {
        let mut c = self.children.lock().expect("children poisoned");
        let before = c.len();
        c.retain(|o| o.obj_id() != obj_id);
        c.len() != before
    }

    pub(crate) fn take_children(&self) -> Vec<WorldObject> {
        std::mem::take(&mut *self.children.lock().expect("children poisoned"))
    }
}

/// A non-player world object: identity + [`ObjectCore`] + the C# object's members `D`.
#[derive(Debug)]
pub struct Entity<D> {
    pub obj_id: u32,
    core: ObjectCore,
    data: Mutex<D>,
}

impl<D> Entity<D> {
    /// A new handle (not yet in any world: see `WorldInstance::spawn`).
    pub fn new(obj_id: u32, data: D) -> Arc<Self> {
        Arc::new(Self {
            obj_id,
            core: ObjectCore::default(),
            data: Mutex::new(data),
        })
    }

    /// Locks the object's members (see the module locking rules).
    pub fn data(&self) -> MutexGuard<'_, D> {
        self.data.lock().expect("object data poisoned")
    }

    pub fn core(&self) -> &ObjectCore {
        &self.core
    }

    pub fn region(&self) -> Option<RegionKey> {
        self.core.region()
    }

    pub fn is_visible(&self) -> bool {
        self.core.is_visible()
    }
}

pub type NpcRef = Arc<Entity<Npc>>;
pub type DoodadRef = Arc<Entity<Doodad>>;
pub type SlaveRef = Arc<Entity<Slave>>;
pub type MateRef = Arc<Entity<Mate>>;
pub type HouseRef = Arc<Entity<House>>;
pub type TransferRef = Arc<Entity<Transfer>>;
pub type ShipyardRef = Arc<Entity<Shipyard>>;
pub type GimmickRef = Arc<Entity<Gimmick>>;

/// The kind of a [`WorldObject`] (`obj is T` checks).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum ObjectKind {
    Player,
    Npc,
    Doodad,
    Slave,
    Mate,
    House,
    Transfer,
    Shipyard,
    Gimmick,
}

impl ObjectKind {
    /// `obj is Unit` (every kind but doodads; gimmicks are units in C#).
    pub fn is_unit(self) -> bool {
        self != ObjectKind::Doodad
    }
}

/// A `GameObject` held by a region / instance.
#[derive(Debug, Clone)]
pub enum WorldObject {
    Player(PlayerRef),
    Npc(NpcRef),
    Doodad(DoodadRef),
    Slave(SlaveRef),
    Mate(MateRef),
    House(HouseRef),
    Transfer(TransferRef),
    Shipyard(ShipyardRef),
    Gimmick(GimmickRef),
}

/// Runs `$body` with `$d` bound to the locked data of any non-player variant, and `$p` for players.
macro_rules! dispatch {
    ($obj:expr, $d:ident => $body:expr, player $p:ident => $pbody:expr) => {
        match $obj {
            WorldObject::Player($p) => $pbody,
            WorldObject::Npc(e) => {
                #[allow(unused_mut)]
                let mut $d = e.data();
                $body
            }
            WorldObject::Doodad(e) => {
                #[allow(unused_mut)]
                let mut $d = e.data();
                $body
            }
            WorldObject::Slave(e) => {
                #[allow(unused_mut)]
                let mut $d = e.data();
                $body
            }
            WorldObject::Mate(e) => {
                #[allow(unused_mut)]
                let mut $d = e.data();
                $body
            }
            WorldObject::House(e) => {
                #[allow(unused_mut)]
                let mut $d = e.data();
                $body
            }
            WorldObject::Transfer(e) => {
                #[allow(unused_mut)]
                let mut $d = e.data();
                $body
            }
            WorldObject::Shipyard(e) => {
                #[allow(unused_mut)]
                let mut $d = e.data();
                $body
            }
            WorldObject::Gimmick(e) => {
                #[allow(unused_mut)]
                let mut $d = e.data();
                $body
            }
        }
    };
}

impl WorldObject {
    pub fn obj_id(&self) -> u32 {
        match self {
            WorldObject::Player(p) => p.obj_id,
            WorldObject::Npc(e) => e.obj_id,
            WorldObject::Doodad(e) => e.obj_id,
            WorldObject::Slave(e) => e.obj_id,
            WorldObject::Mate(e) => e.obj_id,
            WorldObject::House(e) => e.obj_id,
            WorldObject::Transfer(e) => e.obj_id,
            WorldObject::Shipyard(e) => e.obj_id,
            WorldObject::Gimmick(e) => e.obj_id,
        }
    }

    pub fn core(&self) -> &ObjectCore {
        match self {
            WorldObject::Player(p) => &p.core,
            WorldObject::Npc(e) => e.core(),
            WorldObject::Doodad(e) => e.core(),
            WorldObject::Slave(e) => e.core(),
            WorldObject::Mate(e) => e.core(),
            WorldObject::House(e) => e.core(),
            WorldObject::Transfer(e) => e.core(),
            WorldObject::Shipyard(e) => e.core(),
            WorldObject::Gimmick(e) => e.core(),
        }
    }

    pub fn kind(&self) -> ObjectKind {
        match self {
            WorldObject::Player(_) => ObjectKind::Player,
            WorldObject::Npc(_) => ObjectKind::Npc,
            WorldObject::Doodad(_) => ObjectKind::Doodad,
            WorldObject::Slave(_) => ObjectKind::Slave,
            WorldObject::Mate(_) => ObjectKind::Mate,
            WorldObject::House(_) => ObjectKind::House,
            WorldObject::Transfer(_) => ObjectKind::Transfer,
            WorldObject::Shipyard(_) => ObjectKind::Shipyard,
            WorldObject::Gimmick(_) => ObjectKind::Gimmick,
        }
    }

    /// `obj is Unit`.
    pub fn is_unit(&self) -> bool {
        self.kind().is_unit()
    }

    /// `IsVisible`.
    pub fn is_visible(&self) -> bool {
        self.core().is_visible()
    }

    /// `GameObject.Region`.
    pub fn region(&self) -> Option<RegionKey> {
        self.core().region()
    }

    /// Same object (handle identity).
    pub fn same(&self, other: &WorldObject) -> bool {
        self.obj_id() == other.obj_id() && self.kind() == other.kind()
    }

    pub fn as_player(&self) -> Option<&PlayerRef> {
        match self {
            WorldObject::Player(p) => Some(p),
            _ => None,
        }
    }

    pub fn as_npc(&self) -> Option<&NpcRef> {
        match self {
            WorldObject::Npc(e) => Some(e),
            _ => None,
        }
    }

    pub fn as_doodad(&self) -> Option<&DoodadRef> {
        match self {
            WorldObject::Doodad(e) => Some(e),
            _ => None,
        }
    }

    pub fn as_slave(&self) -> Option<&SlaveRef> {
        match self {
            WorldObject::Slave(e) => Some(e),
            _ => None,
        }
    }

    pub fn as_mate(&self) -> Option<&MateRef> {
        match self {
            WorldObject::Mate(e) => Some(e),
            _ => None,
        }
    }

    pub fn as_house(&self) -> Option<&HouseRef> {
        match self {
            WorldObject::House(e) => Some(e),
            _ => None,
        }
    }

    pub fn as_transfer(&self) -> Option<&TransferRef> {
        match self {
            WorldObject::Transfer(e) => Some(e),
            _ => None,
        }
    }

    pub fn as_shipyard(&self) -> Option<&ShipyardRef> {
        match self {
            WorldObject::Shipyard(e) => Some(e),
            _ => None,
        }
    }

    pub fn as_gimmick(&self) -> Option<&GimmickRef> {
        match self {
            WorldObject::Gimmick(e) => Some(e),
            _ => None,
        }
    }

    /// A copy of `Transform` (locks the object's data briefly).
    pub fn transform(&self) -> Transform {
        dispatch!(self, d => *d.transform(), player p => p.data().unit.transform)
    }

    /// Mutates `Transform` (locks the object's data). Region / visibility updates are the
    /// caller's job (`WorldInstance::add_visible_object`).
    pub fn with_transform<R>(&self, f: impl FnOnce(&mut Transform) -> R) -> R {
        dispatch!(self, d => f(d.transform_mut()), player p => f(&mut p.data().unit.transform))
    }

    /// `obj as BaseUnit` (every kind): runs `f` with the object's data locked.
    pub fn with_base_unit<R>(&self, f: impl FnOnce(&mut dyn BaseUnitData) -> R) -> R {
        dispatch!(self, d => f(&mut *d), player p => f(&mut *p.data()))
    }

    /// `obj as Unit`: runs `f` with the object's data locked; `None` for doodads.
    pub fn with_unit<R>(&self, f: impl FnOnce(&mut dyn UnitData) -> R) -> Option<R> {
        match self {
            WorldObject::Player(p) => Some(f(&mut *p.data())),
            WorldObject::Npc(e) => Some(f(&mut *e.data())),
            WorldObject::Doodad(_) => None,
            WorldObject::Slave(e) => Some(f(&mut *e.data())),
            WorldObject::Mate(e) => Some(f(&mut *e.data())),
            WorldObject::House(e) => Some(f(&mut *e.data())),
            WorldObject::Transfer(e) => Some(f(&mut *e.data())),
            WorldObject::Shipyard(e) => Some(f(&mut *e.data())),
            WorldObject::Gimmick(e) => Some(f(&mut *e.data())),
        }
    }

    /// `BaseUnit.Scale` (1 for objects without one).
    pub fn scale(&self) -> f32 {
        self.with_base_unit(|b| b.scale())
    }
}

macro_rules! from_ref {
    ($variant:ident, $ty:ty) => {
        impl From<$ty> for WorldObject {
            fn from(v: $ty) -> Self {
                WorldObject::$variant(v)
            }
        }
        impl From<&$ty> for WorldObject {
            fn from(v: &$ty) -> Self {
                WorldObject::$variant(v.clone())
            }
        }
    };
}

from_ref!(Player, PlayerRef);
from_ref!(Npc, NpcRef);
from_ref!(Doodad, DoodadRef);
from_ref!(Slave, SlaveRef);
from_ref!(Mate, MateRef);
from_ref!(House, HouseRef);
from_ref!(Transfer, TransferRef);
from_ref!(Shipyard, ShipyardRef);
from_ref!(Gimmick, GimmickRef);

impl From<&WorldObject> for WorldObject {
    fn from(v: &WorldObject) -> Self {
        v.clone()
    }
}
