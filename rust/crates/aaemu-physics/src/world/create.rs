//! **P3 — entity creation and ids**: `CreatePhysicalEntity` (world slot 21, 0x35082F00),
//! `SetPhysicalEntityId` (slot 25, 0x3507EDE0), the automatic id search (0x35077210),
//! `GetPhysicalEntityById` (slot 27, 0x35079BE0), `GetPhysicalEntityId` (slot 26).

use super::queue::Command;
use super::{EntIdx, World};
use crate::entities::base::{Entity, EntityKind};
use crate::entities::rigid::RigidData;
use crate::math::Vec3;
use crate::params::ParamsPos;

impl World {
    /// World slot 21 `CreatePhysicalEntity(type, lifeTime, params, foreignData, iForeignData,
    /// id, hostPlaceholder, flags)` (0x35082F00) for the ported classes (1 static, 2 rigid).
    /// `id < 0` = automatic. Placeholders (`hostPlaceholder`, `lifeTime > 0` with a host) are
    /// not ported. Returns `None` for unsupported types.
    ///
    /// The entity joins the grid and its simclass list immediately when the calling thread is
    /// a physics thread (index < 4: after the first `TimeStep`) and the world is not locked;
    /// otherwise a grid-reposition command (op 4) is queued and executed at the next
    /// `TimeStep` (the oracle's "entities join the simclass lists at the first step"); while
    /// queued the entity carries flag `0x80000000`.
    pub fn create_physical_entity(
        &mut self,
        etype: i32,
        life_time: f32,
        params: Option<&ParamsPos>,
        foreign_data: u64,
        i_foreign_data: i32,
        id: i32,
    ) -> Option<EntIdx> {
        let kind = match etype {
            1 => EntityKind::Static,
            2 => {
                let body = self.bodies.alloc(self.ents.len() as u64 + 1);
                let head = self.contacts.new_sentinel();
                EntityKind::Rigid(Box::new(RigidData::new(self.gravity(), body, head)))
            }
            _ => {
                self.ent_count_by_type[etype.clamp(0, 9) as usize] += 1;
                return None;
            }
        };
        let mut ent = Entity::new_base(etype, kind);
        if etype == 2 {
            ent.set_sim_class(2);
        }
        self.ent_count_by_type[etype as usize] += 1;
        if etype != 1 {
            self.flag1076 = 0;
        }
        // no host placeholder: own foreign data and a world id
        ent.list_bits = (ent.list_bits & 0x00FF_FFFF) | 0x1100_0000;
        ent.i_foreign_data = i_foreign_data as i16;
        ent.foreign_data = foreign_data;
        let e = self.store(ent);
        // `+1100 -= (+1100 - id) & ((+1100 - id) >> 31)` = max(+1100, id); `+1104 += id >= 0`.
        let d = self.id_low - id;
        self.id_low -= d & (d >> 31);
        self.id_auto += (id >> 31) + 1;
        let id = if id < 0 { self.auto_entity_id() } else { id };
        self.set_physical_entity_id(e, id, true, false);
        self.ent_mut(e).flags |= 0x8000_0000;
        if let Some(p) = params {
            // slot 7 SetParams (lock-or-queue tail included: `processed |= 0x10000` for a position)
            crate::entities::interface::set(self, e, p);
        }
        if self.tls_thread >= 4 || life_time != 0.0 {
            self.queue.push(Command::Reposition { ent: e, flags: 2 });
        } else {
            self.ent_mut(e).flags &= !0x8000_0000;
            self.reposition_entity(e, 2, None);
            self.n_ents_pool += 1;
            if self.n_ents_pool > self.pool_size - 1 {
                self.grow_pools();
            }
        }
        Some(e)
    }

    /// The pool growth of CreatePhysicalEntity / the TimeStep queue ("increasing physical
    /// entity pools %d => %d"): the temp lists grow by 0x10000 entries.
    pub(crate) fn grow_pools(&mut self) {
        let n = self.pool_size + 0x10000;
        self.group_leader.resize(n as usize, EntIdx::NONE);
        self.group_mass.resize(n as usize, 0.0);
        self.group_840.resize(n as usize, 0);
        self.group_848.resize(n as usize, 0);
        self.pool_size = n;
    }

    /// `sub_35077210`: the next free automatic id.
    pub fn auto_entity_id(&mut self) -> i32 {
        let v1 = self.id_bound;
        let v2 = self.id_low;
        if 2 * (self.n_ents_pool - self.id_auto) <= v1 - v2 {
            let taken = |w: &World, i: i32| -> bool {
                (i as usize) < w.id_table.len() && !w.id_table[i as usize].is_none()
            };
            let mut n = 100;
            loop {
                let v5 = self.id_cursor;
                if v5 <= v2 || !taken(self, v5) {
                    break;
                }
                n -= 1;
                self.id_cursor = v5 - 1;
                if n <= 0 {
                    self.id_bound = v1 + 1;
                    return v1;
                }
            }
            if self.id_cursor <= v2 {
                let mut n = 100;
                self.id_cursor = v1 - 2;
                loop {
                    let v7 = self.id_cursor;
                    if v7 <= v2 || !taken(self, v7) {
                        break;
                    }
                    n -= 1;
                    self.id_cursor = v7 - 1;
                    if n <= 0 {
                        self.id_bound = v1 + 1;
                        return v1;
                    }
                }
            }
            let r = self.id_cursor;
            if r <= v2 {
                self.id_bound = v1 + 1;
                v1
            } else {
                self.id_cursor = r - 1;
                r
            }
        } else {
            self.id_bound = v1 + 1;
            v1
        }
    }

    /// World slot 25 `SetPhysicalEntityId(ent, id, bReplace, bThreadSafe)` (0x3507EDE0).
    pub fn set_physical_entity_id(&mut self, e: EntIdx, id: i32, replace: bool, _thread_safe: bool) -> bool {
        let old = self.ent(e).id();
        if (old as u32) < self.id_table.len() as u32 {
            self.id_table[old as usize] = EntIdx::NONE;
            let n = self.id_bound;
            if old == n - 1 && n > 0 {
                loop {
                    let v13 = self.id_bound;
                    if !self.id_table[(v13 - 1) as usize].is_none() {
                        break;
                    }
                    self.id_bound = v13 - 1;
                    if v13 - 1 <= 0 {
                        break;
                    }
                }
            }
            if old == self.id_low {
                let mut v15 = self.id_low - 1;
                self.id_low = v15;
                while v15 > 0 {
                    if !self.id_table[v15 as usize].is_none() {
                        break;
                    }
                    v15 -= 1;
                    self.id_low = v15;
                }
            }
        }
        let d = self.id_bound - (id + 1);
        self.id_bound -= d & (d >> 31);
        if id < 0 {
            return false;
        }
        if id as usize >= self.id_table.len() {
            let cap = ((id as u32 & 0xFFFF_8000) + 0x8000) as usize;
            self.id_table.resize(cap, EntIdx::NONE);
        }
        let cur = self.id_table[id as usize];
        if !cur.is_none() {
            if !replace {
                return false;
            }
            let nid = self.auto_entity_id();
            self.set_physical_entity_id(cur, nid, true, true);
        }
        self.id_table[id as usize] = e;
        let ent = self.ent_mut(e);
        ent.id_bits = (ent.id_bits & 0xFF80_0000) | (id as u32 & 0x7F_FFFF);
        true
    }

    /// World slot 27 `GetPhysicalEntityById(id)` (0x35079BE0) for ids without bit 30.
    #[must_use]
    pub fn entity_by_id(&self, id: i32) -> Option<EntIdx> {
        let i = (id & !0x4000_0000) as u32 as usize;
        self.id_table.get(i).copied().filter(|e| !e.is_none())
    }
}

impl World {
    /// World slot 24 `DestroyPhysicalEntity(ent, mode, bThreadSafe)` (0x35083830), immediate
    /// path (the port never needs the deferred one). Mode 0 deletes (the object moves to the
    /// deleted list, simclass 7, and is freed by the TimeStep cleanup), mode 1 suspends
    /// (hidden list `+816`), mode 2 restores a suspended entity. Returns 1 on success.
    pub fn destroy_physical_entity(&mut self, e: EntIdx, mode: i32) -> i32 {
        let v15 = mode & 3;
        let sc = self.ent(e).sim_class();
        if sc == 5 {
            self.ent_mut(e).deleted = 1;
        } else {
            self.ent_mut(e).deleted = mode + 1;
            if mode == 0 {
                let ent = self.ent_mut(e);
                ent.i_foreign_data = -1;
                ent.foreign_data = 0;
            }
        }
        if sc == 7 {
            return 0;
        }
        let v38 = self.pump_counter;
        let v39 = if self.vars.keep_temp_memory != 0 { v38 + 3 } else { v38 + 2 };
        self.ent_mut(e).deleted = 4 - ((4 - v39) & ((4 - v39) >> 31));
        for c in &mut self.box_cache {
            c.objtypes = -1;
            c.bbox = [Vec3::splat(f32::from_bits(u32::MAX)); 2];
        }
        if (self.ent(e).id_bits as i32) < 0 {
            // hidden (simclass byte negative): unlink from the +816 list
            let (n, p) = (self.ent(e).list_next, self.ent(e).list_prev);
            if !n.is_none() {
                self.ent_mut(n).list_prev = p;
            }
            if !p.is_none() {
                self.ent_mut(p).list_next = n;
            }
            if self.list_816 == e {
                self.list_816 = n;
            }
            let ent = self.ent_mut(e);
            ent.list_prev = EntIdx::NONE;
            ent.list_next = EntIdx::NONE;
        }
        if v15 == 2 {
            let ent = self.ent(e);
            if ent.id_bits & 0xFF00_0000 == 0xFF00_0000 && (ent.list_bits as u16 as i16) >= 0 {
                let lb = ent.list_bits;
                let entm = self.ent_mut(e);
                entm.grid_cells = [-2, -2, -2, -2];
                entm.list_bits |= 0xFFFF;
                entm.set_sim_class(i32::from(lb as u8 & 0xF));
                self.reposition_entity(e, 3, None);
            }
            self.ent_mut(e).deleted = 0;
            return 1;
        }
        crate::entities::detach_hook(self, e, v15);
        let lc = self.ent(e).list_bits as u16;
        if lc < 8 && (self.ent(e).id_bits as i32) >= 0 {
            let (n, p) = (self.ent(e).list_next, self.ent(e).list_prev);
            if !n.is_none() {
                self.ent_mut(n).list_prev = p;
            }
            if !p.is_none() {
                self.ent_mut(p).list_next = n;
            } else {
                self.list_head[lc as usize] = n;
            }
            if self.list_marker[lc as usize] == e {
                self.list_marker[lc as usize] = n;
            }
        }
        {
            let ent = self.ent_mut(e);
            ent.list_prev = EntIdx::NONE;
            ent.list_next = EntIdx::NONE;
        }
        self.grid_detach(e);
        {
            let ent = self.ent_mut(e);
            ent.flags &= !0x10;
            ent.flags |= 0x400;
            ent.grid_thunk = 0;
        }
        if v15 != 0 {
            if (self.ent(e).id_bits as i32) >= 0 {
                let a = crate::params::ActionReset { clear_contacts: 1 };
                crate::entities::action_reset(self, e, &a);
                let ent = self.ent_mut(e);
                let lb = (ent.sim_class() as u8 as u16) | 0x100;
                ent.set_sim_class(-1);
                ent.list_bits = (ent.list_bits & 0xFFFF_0000) | u32::from(lb);
            }
            if v15 == 1 {
                let head = self.list_816;
                if !head.is_none() {
                    self.ent_mut(head).list_prev = e;
                }
                let ent = self.ent_mut(e);
                ent.list_next = head;
                ent.list_prev = EntIdx::NONE;
                self.list_816 = e;
            }
        } else {
            let hidden = (self.ent(e).flags >> 31) & 1 != 0;
            {
                let ent = self.ent_mut(e);
                ent.list_bits |= 0xFFFF;
                ent.i_foreign_data = -1;
                ent.set_sim_class(7);
                ent.foreign_data = 0;
                ent.time_deleted = 0.0;
            }
            let head = self.list_head[7];
            self.ent_mut(e).list_next = head;
            if !head.is_none() {
                self.ent_mut(head).list_prev = e;
            }
            if self.ent(e).id() <= self.id_low {
                self.id_auto -= 1;
            }
            self.set_physical_entity_id(e, -1, true, false);
            self.list_head[7] = e;
            if !hidden {
                let t = self.ent(e).etype.clamp(0, 9) as usize;
                self.ent_count_by_type[t] -= 1;
                self.n_ents_pool -= 1;
            }
        }
        1
    }

    /// Free a deleted entity (the cleanup's `delete`): drops its body.
    pub(crate) fn free_entity(&mut self, e: EntIdx) {
        if let Some(ent) = self.ents[e.0 as usize].take() {
            if let crate::entities::base::EntityKind::Rigid(r) = &ent.kind {
                self.bodies.release(r.body);
            }
        }
    }
}
