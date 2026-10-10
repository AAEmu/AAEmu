//! **P3 — `TimeStep`** (world slot 32, 0x3508BD10; x86 0x3509F6A0), the island step
//! (`sub_3507F9B0`), the post-solve (`sub_3507F210`) and the awake-list job (`sub_3507A3B0`).
//! `world.md` §4, `dynamics.md` §2.1.
//!
//! The port runs the binary's job modes on the calling thread (with one physics thread the
//! binary does the same: no worker exists and the main thread pulls every job itself).
//!
//! Not ported: worker threads, the deformation/breakage queues, the water manager, POD cell
//! ageing and streaming, living and independent entities (P5), profiling.

// NaN-aware comparisons and float literals are written as in the binary (the negated
// comparisons keep its NaN behaviour; the literals are the decompile's single-precision
// constants).
#![allow(clippy::neg_cmp_op_on_partial_ord, clippy::excessive_precision)]

use super::{EntIdx, World};
use crate::entities::base::EntityKind;
use crate::entities::rigid::{step as rstp, update as rstep};
use crate::math::{max_idiom, min_idiom};

/// TimeStep flags (`world.md` §4).
pub mod flags {
    /// Rigid bodies.
    pub const RIGID: i32 = 0x4;
    /// Living entities.
    pub const LIVING: i32 = 0x8;
    /// Independent entities.
    pub const INDEPENDENT: i32 = 0x10;
    /// Cleanup (deleted entities, sleep timeouts).
    pub const CLEANUP: i32 = 0x80;
    /// Only entities with flag 0x800.
    pub const FLAGGED_ONLY: i32 = 0x800;
    /// The flags the oracle (and the zone server) pass: 0x9F.
    pub const DEFAULT: i32 = 0x9F;
}

impl World {
    /// World slot 32 `TimeStep(dt, flags)` (0x3508BD10).
    pub fn time_step(&mut self, dt_in: f32, step_flags: i32) {
        if !(dt_in >= 0.0) {
            return;
        }
        // 1. thread pool: nThreads clamped to 4 (single-threaded port: no workers)
        let nt = self.vars.num_threads;
        self.vars.num_threads = ((nt - 4) & ((nt - 4) >> 31)) + 4;
        // 2. the deferred command queue, then the deferred destroys
        if dt_in > 0.0 {
            // the TLS thread index of the calling thread becomes 0
            self.tls_thread = 0;
        }
        self.flush_command_queue();
        // 3. dt
        let mut dt = dt_in;
        let mut dt7 = dt_in;
        if dt > self.vars.max_world_step {
            dt = self.vars.max_world_step;
            dt7 = dt;
        }
        let g = self.vars.time_granularity;
        let t = if g <= 0.0 {
            dt + self.time
        } else {
            let r = 1.0 / g;
            self.vars.inv_time_granularity = r;
            let q = (r * dt7 + 12_582_912.0).to_bits() as i32 - 1_262_485_504;
            self.itime = self.itime.wrapping_add(q);
            dt = q as f32 * g;
            dt7 = dt;
            self.itime as f32 * g
        };
        self.time = t;
        if self.vars.fixed_step > 0.0 && dt > 0.0 {
            dt = self.vars.fixed_step;
        }
        self.flag1744 = 1;
        self.flagged_step = (step_flags & 0x800) as u32;
        let v61 = ((step_flags >> 1) & 0x800) as u32;
        // gEnv bytes 289/290 (the fake env of the oracle and the zone server: 0)
        self.vars.env_byte_290 = 0;
        self.vars.unk_256 &= 1;
        self.cur_dt = dt;
        self.vars.env_byte_289 = 0;
        if step_flags & 0x800 == 0 {
            self.vars.last_dt = dt;
        }
        if !self.global_area.is_none() {
            let g = self.gravity();
            let a = self.area_mut(self.global_area);
            if !crate::sentinel::is_unused(a.gravity.x) {
                a.gravity = g;
            }
        }
        let do_living = step_flags & 8 != 0;
        // (flag 8: StartStep of the living entities — none in the port)
        if self.vars.single_step_mode == 0 || self.vars.do_step != 0 {
            // slow-motion re-awake
            let v70 = i32::from(self.vars.time_scale != 1.0 && dt > 0.0);
            self.slowmo_counter = v70 + (self.slowmo_counter & -v70);
            if self.slowmo_counter > 4 && self.pump_counter < self.vars.max_event_lag {
                let act = self.list(2);
                for e in act {
                    let v = self.status_velocity(e);
                    if (v.x * v.x + v.y * v.y) + v.z * v.z < 25.0 {
                        crate::entities::awake(self, e, 0, 0);
                    }
                }
                for e in self.list(4) {
                    crate::entities::awake(self, e, 0, 0);
                }
            }
            // (flag 0x10: StartStep of the independent entities — none in the port)
            if step_flags & 4 != 0 && dt > 0.0 && !self.list_head[2].is_none() {
                self.rigid_step(dt, v61);
            }
            self.rigid_loop_counter += 1;
            let _ = do_living;
            if step_flags & 0x80 != 0 {
                self.cleanup_step(dt7);
                self.cleanup_deleted_areas();
            }
        } else {
            self.rigid_loop_counter += 1;
        }
        self.flagged_step = 0;
        self.flag1744 = 0;
        if dt > 0.0 {
            self.step_counter += 1;
        }
    }

    /// `pe_status_dynamics` velocity of an entity (the slow-motion re-awake test).
    fn status_velocity(&self, e: EntIdx) -> crate::math::Vec3 {
        match &self.ent(e).kind {
            EntityKind::Rigid(r) if r.awake() => self.bodies.get(r.body).v,
            _ => crate::math::Vec3::ZERO,
        }
    }

    /// The rigid-body section of TimeStep: up to `max_substeps` iterations, each with two
    /// passes (0: StartStep/GetMaxTimeStep/Step per island, then the awake-list job; 1:
    /// RegisterContacts, the solver and Update per island), islands rebuilt for each pass.
    fn rigid_step(&mut self, dt: f32, v61: u32) {
        let mut iter: i32 = 0;
        loop {
            let ps = self.pool_size as usize;
            if self.group_848.len() < ps.max(1) {
                self.group_848.resize(ps.max(1), 0);
            }
            if ps > 0 {
                self.group_848[ps - 1] = -1;
            }
            let mut done: i32 = 1;
            self.awake_list = EntIdx::NONE;
            self.rigid_loop_counter += 1;
            self.island[0].flag = 0;
            for pass in 0..2 {
                let ngroups = self.build_groups(iter, pass, v61, &mut done, dt);
                self.n_groups = ngroups;
                for gi in 0..ngroups as usize {
                    self.island_step(gi, dt, pass, iter, &mut done);
                }
                if pass == 0 {
                    for gi in 0..ngroups as usize {
                        let mut e = self.group_leader[gi];
                        while !e.is_none() {
                            let ent = self.ent_mut(e);
                            ent.group_mark = 0;
                            ent.group_idx = -1;
                            e = ent.group_next;
                        }
                    }
                    self.flag1744 = 2;
                    self.run_awake_list(dt);
                    self.flag1744 = 1;
                }
            }
            if done != 0 {
                break;
            }
            iter += 1;
            if iter >= self.vars.max_substeps {
                break;
            }
        }
        // entities finished by the post-solve (`+22440` list)
        let mut e = self.done_list;
        while !e.is_none() {
            let ent = self.ent_mut(e);
            let next = ent.job_next;
            ent.group_mark = 0;
            ent.group_idx = -1;
            ent.job_next = EntIdx::NONE;
            e = next;
        }
        self.done_list = EntIdx::NONE;
        let mut e = self.list_head[4];
        while !e.is_none() {
            let ent = self.ent_mut(e);
            ent.group_mark = 0;
            ent.group_idx = -1;
            e = ent.list_next;
        }
    }

    /// The island building of TimeStep: BFS over the colliders of the active (simclass 2)
    /// entities with positive inverse mass; the island list (`+144`) starts with the members
    /// that touch a static collider (sorted by inverse mass), followed by the others in BFS
    /// order. Islands are sorted by their mass (islands touching statics first). Massless
    /// active entities are stepped directly. Returns the number of islands.
    fn build_groups(&mut self, iter: i32, pass: i32, v61: u32, done: &mut i32, _dt: f32) -> i32 {
        let mut ng: usize = 0;
        let mut e = self.list_head[2];
        let need = self.pool_size.max(self.ents.len() as i32 + 1) as usize;
        if self.group_leader.len() < need {
            self.group_leader.resize(need, EntIdx::NONE);
            self.group_mass.resize(need, 0.0);
            self.group_840.resize(need, 0);
            self.group_848.resize(need, 0);
        }
        let first_call = iter == 0 && pass == 0;
        while !e.is_none() {
            let next = self.ent(e).list_next;
            let fl = self.ent(e).flags;
            let skip = (u32::from(self.ent(e).group_mark)
                | (v61 & fl)
                | (self.flagged_step & (fl ^ 0x800)))
                != 0;
            if !skip {
                if crate::entities::mass_inv(self, e) > 0.0 {
                    let gi = ng;
                    {
                        let ent = self.ent_mut(e);
                        ent.group_idx = gi as i32;
                        ent.group_mark = 1;
                    }
                    self.group_840[gi] = 0;
                    self.group_mass[gi] = 1.0 / crate::entities::mass_inv(self, e);
                    if first_call {
                        rstep::start_step(self, e, _dt);
                    }
                    {
                        let ent = self.ent_mut(e);
                        ent.group_next = EntIdx::NONE;
                        ent.bfs_next = EntIdx::NONE;
                    }
                    self.group_leader[gi] = EntIdx::NONE;
                    let mut heaviest = e;
                    let mut tail = e;
                    let mut cur = e;
                    while !cur.is_none() {
                        let cols = self.ent(cur).colliders.clone();
                        let mut inserted = false;
                        for &c in &cols {
                            if crate::entities::mass_inv(self, c) > 0.0 {
                                let cf = self.ent(c).flags;
                                if (u32::from(self.ent(c).group_mark)
                                    | (self.flagged_step & (cf ^ 0x800)))
                                    == 0
                                {
                                    {
                                        let ce = self.ent_mut(c);
                                        ce.flags &= !v61;
                                    }
                                    self.ent_mut(tail).bfs_next = c;
                                    tail = c;
                                    {
                                        let ce = self.ent_mut(c);
                                        ce.bfs_next = EntIdx::NONE;
                                        ce.group_next = EntIdx::NONE;
                                        ce.group_mark = 1;
                                        ce.group_idx = gi as i32;
                                    }
                                    if first_call {
                                        rstep::start_step(self, c, _dt);
                                    }
                                    let mi = crate::entities::mass_inv(self, c);
                                    self.group_mass[gi] += 1.0 / mi;
                                    if crate::entities::mass_inv(self, heaviest) > mi {
                                        heaviest = c;
                                    }
                                }
                            } else {
                                if !inserted {
                                    self.insert_sorted_group_member(gi, cur);
                                    inserted = true;
                                }
                                self.group_840[gi] = 1;
                            }
                        }
                        cur = self.ent(cur).bfs_next;
                    }
                    if self.group_leader[gi].is_none() {
                        self.group_leader[gi] = heaviest;
                    }
                    ng += 1;
                } else {
                    if first_call {
                        rstep::start_step(self, e, _dt);
                        self.ent_mut(e).group_idx = -1;
                    }
                    if pass == 0 {
                        let mts = rstep::get_max_time_step(self, e, _dt);
                        rstp::step(self, e, mts);
                        let r = rstep::update(self, e, mts, 1.0);
                        *done &= r as i32;
                    }
                }
            }
            e = next;
        }
        // island order: by mass, islands touching statics shifted up by 1.01·max
        let mut mx = if ng > 0 { self.group_mass[0] } else { 0.0 };
        for i in 1..ng {
            mx = max_idiom(mx, self.group_mass[i]);
        }
        let bonus = mx * 1.01;
        for i in 0..ng {
            self.group_mass[i] += self.group_840[i] as f32 * bonus;
        }
        for i in 0..ng {
            self.group_840[i] = i as i32;
        }
        if ng > 0 {
            sort_groups(
                &mut self.group_leader[..ng],
                &mut self.group_mass[..ng],
                &mut self.group_840[..ng],
                0,
                ng as i32 - 1,
            );
        }
        for j in 0..ng {
            let k = self.group_840[j] as usize;
            self.group_848[k] = j as i32;
        }
        // rebuild each island list: the members already linked (touching statics, sorted),
        // then BFS from them appending the rest in collider order
        for gi in 0..ng {
            let mut last = self.group_leader[gi];
            loop {
                self.ent_mut(last).group_mark = 0;
                let n = self.ent(last).group_next;
                if n.is_none() {
                    break;
                }
                last = n;
            }
            let mut cur = self.group_leader[gi];
            let mut tail = last;
            while !cur.is_none() {
                let cols = self.ent(cur).colliders.clone();
                for &c in &cols {
                    if crate::entities::mass_inv(self, c) > 0.0
                        && self.ent(c).group_mark == 1
                        && ((self.ent(c).flags ^ 0x800) & self.flagged_step) == 0
                    {
                        self.ent_mut(tail).group_next = c;
                        tail = c;
                        let ce = self.ent_mut(c);
                        ce.group_mark = 0;
                        ce.group_next = EntIdx::NONE;
                    }
                }
                cur = self.ent(cur).group_next;
            }
        }
        self.group_dt_clamp = bonus;
        ng as i32
    }

    /// Insert `e` into island `gi`'s list (`+144`) sorted by GetMassInv (the binary's
    /// insertion loop, ties after the existing ones).
    fn insert_sorted_group_member(&mut self, gi: usize, e: EntIdx) {
        let me = crate::entities::mass_inv(self, e);
        let head = self.group_leader[gi];
        if head.is_none() {
            self.ent_mut(e).group_next = head;
            self.group_leader[gi] = e;
            return;
        }
        let mut v150 = head;
        loop {
            let nx = self.ent(v150).group_next;
            if nx.is_none() {
                break;
            }
            if crate::entities::mass_inv(self, nx) > me {
                break;
            }
            v150 = nx;
        }
        if crate::entities::mass_inv(self, v150) <= me {
            let n = self.ent(v150).group_next;
            self.ent_mut(e).group_next = n;
            self.ent_mut(v150).group_next = e;
        } else {
            self.ent_mut(e).group_next = v150;
            self.group_leader[gi] = e;
        }
    }

    /// `sub_3507F9B0`: one island. Pass 0: GetMaxTimeStep (min over members and massive
    /// colliders) and Step of every member (StepBack of all when one fails); pass 1:
    /// RegisterContacts, the solver, the post-solve.
    fn island_step(&mut self, gi: usize, dt: f32, pass: i32, iter: i32, done: &mut i32) {
        let tid = self.tls_thread.clamp(0, 4) as usize;
        let mass = self.group_mass[gi];
        let clamp = self.group_dt_clamp;
        let v19 = mass - (((clamp - mass).to_bits() >> 31) as f32) * clamp;
        self.group_dt = v19;
        self.island[tid].dt = v19;
        self.island[tid].flag = 0;
        let mut v12: f32 = 100.0;
        let mut v14: f32 = -1.0;
        let mut v15: f32 = 0.0;
        let mut v16: u32 = 0x10000;
        let mut v21 = ((pass ^ 1) as f32) * dt;
        let leader = self.group_leader[gi];
        let mut m = leader;
        while !m.is_none() {
            v16 &= self.ent(m).flags;
            let cols = self.ent(m).colliders.clone();
            for c in cols {
                if (self.ent(c).id_bits & 0xFF00_0000) as i32 > 0x100_0000
                    && crate::entities::mass_inv(self, c) <= 0.0
                {
                    if pass != 0 {
                        if self.ent(c).flags & 0x4_0000 != 0 {
                            rstep::get_max_time_step(self, c, dt);
                            let lt = rstep::last_time_step(self, c);
                            v14 = ((v14 - lt).abs() + (lt + v14)) * 0.5;
                        }
                        let lt = rstep::last_time_step(self, c);
                        v21 = ((v21 - lt).abs() + (lt + v21)) * 0.5;
                        let mf = rstep::max_friction(self, c);
                        v12 = ((mf + v12) - (v12 - mf).abs()) * 0.5;
                        let b = crate::entities::rigid::colliders::body_of(self, c);
                        let ebb = self.ent(c).bbox;
                        let v42 = ((ebb[1].x - ebb[0].x - (ebb[1].y - ebb[0].y)).abs()
                            + ((ebb[1].x - ebb[0].x) + (ebb[1].y - ebb[0].y)))
                            * 0.5;
                        let v41 = ebb[1].z - ebb[0].z;
                        let h = (((v42 - v41).abs() + (v42 + v41)) * 0.5)
                            * (((v42 - v41).abs() + (v42 + v41)) * 0.5);
                        v15 += (((b.w.x * b.w.x + b.w.y * b.w.y) + b.w.z * b.w.z) * h
                            + ((b.v.x * b.v.x + b.v.y * b.v.y) + b.v.z * b.v.z))
                            * self.group_dt;
                    } else {
                        let mts = rstep::get_max_time_step(self, c, dt);
                        v21 = ((mts + v21) - (v21 - mts).abs()) * 0.5;
                    }
                    let sc = self.ent(c).sim_class();
                    v16 &= self.ent(c).flags | !((((sc - 2) ^ (sc - 3)) >> 31) as u32);
                }
            }
            m = self.ent(m).group_next;
        }
        self.island[tid].max_friction = v12;
        self.island[tid].flag = i32::from(v16 != 0);
        self.last_island_max_friction = v12;
        if pass == 0 {
            let mut m = leader;
            while !m.is_none() {
                let mts = rstep::get_max_time_step(self, m, dt);
                v21 = ((mts + v21) - (v21 - mts).abs()) * 0.5;
                m = self.ent(m).group_next;
            }
            let mut ok = 1;
            let mut m = leader;
            while !m.is_none() {
                let next = self.ent(m).group_next;
                if ((self.ent(m).id_bits & 0xFF00_0000) as i32) < 0x300_0000 {
                    ok &= i32::from(rstp::step(self, m, v21));
                }
                self.ent_mut(m).group_mark = 1;
                m = next;
            }
            if ok == 0 {
                let mut m = leader;
                while !m.is_none() {
                    rstep::step_back(self, m, v21);
                    m = self.ent(m).group_next;
                }
            }
            let mut m = leader;
            while !m.is_none() {
                self.ent_mut(m).group_mark = 2;
                m = self.ent(m).group_next;
            }
            return;
        }
        if !(dt > 0.0) {
            return;
        }
        // pass 1
        let mut v52: f32 = 0.0;
        let mut m = leader;
        while !m.is_none() {
            if (self.ent(m).id_bits & 0xFF00_0000) as i32 > 0x100_0000 {
                let lt = rstep::last_time_step(self, m);
                v52 = ((v52 - lt).abs() + (lt + v52)) * 0.5;
            }
            m = self.ent(m).group_next;
        }
        if v52 == 0.0 {
            v52 = dt;
        }
        self.solver_thread(tid).init();
        let mut n_ents = 0;
        if self.vars.group_iters_large == self.vars.group_iters_small {
            let mut m = leader;
            while !m.is_none() {
                rstep::register_contacts(self, m, v52, self.vars.group_iters_small);
                v15 += rstep::calc_energy(self, m, v52);
                n_ents += 1;
                m = self.ent(m).group_next;
            }
        } else {
            let mut j = 0;
            let mut m = leader;
            while !m.is_none() {
                j += rstep::contact_count(self, m, self.vars.group_iters_small);
                v15 += rstep::calc_energy(self, m, v52);
                n_ents += 1;
                m = self.ent(m).group_next;
            }
            let iters = if j <= self.vars.group_contacts_threshold {
                self.vars.group_iters_small
            } else {
                self.vars.group_iters_large
            };
            let mut m = leader;
            while !m.is_none() {
                rstep::register_contacts(self, m, v52, iters);
                m = self.ent(m).group_next;
            }
        }
        let v69 = self.group_mass[gi] * 0.000_025;
        let e0 = ((v69 - v15).abs() + (v69 + v15)) * 0.5;
        if let Some(path) = std::env::var_os("AAEMU_PHYS_DUMP_SOLVER") {
            self.dump_solver_case(tid, v52, e0, &path);
        }
        let res = {
            let sol = self.solver[tid]
                .get_or_insert_with(|| Box::new(crate::solver::SolverThread::new(tid)));
            sol.invoke(v52, &self.vars, e0, 0, &mut self.bodies, &mut self.contacts)
        };
        self.post_solve(gi, iter, v52, e0, n_ents, v14, done, &res, tid);
    }

    /// `sub_3507F210`: OnContactResolved for breakable contacts, group damping, Update of
    /// every member, the island-finished test and the done list.
    #[allow(clippy::too_many_arguments)]
    fn post_solve(
        &mut self,
        gi: usize,
        iter: i32,
        dt: f32,
        _e0: f32,
        n_ents: i32,
        _v14: f32,
        done: &mut i32,
        res: &crate::solver::SolveResult,
        _tid: usize,
    ) {
        // breakable contacts (part flag 0x400000) → slot 59 OnContactResolved: constraints and
        // breakable parts are not ported
        let leader = self.group_leader[gi];
        let v32_flag = ((self.vars.group_damping_min_ents - n_ents - 1) as u32 >> 31) as f32;
        let mut damping = 1.0 - v32_flag * (dt * self.vars.group_damping);
        let mut e_sum = 0.0f32;
        let mut m = leader;
        while !m.is_none() {
            e_sum += rstep::calc_energy(self, m, 0.0);
            if self.ent(m).flags & 0x4_0000 != 0 {
                damping = rstep::get_damping(self, m, dt);
                break;
            }
            let d = rstep::get_damping(self, m, dt);
            damping = min_idiom(d, damping);
            m = self.ent(m).group_next;
        }
        // energy clamp: islands that gained energy in the solve are damped to sqrt(E0/E)
        // (10 % more headroom for small islands, < 15 solver bodies)
        let k = ((((res.ret - 15) as u32) >> 31) as f32) * 0.1 + 1.0;
        if e_sum > _e0 * k {
            let r = (_e0 / e_sum).sqrt();
            damping = ((r + damping) - (damping - r).abs()) * 0.5;
        }
        if _v14 > -0.5 {
            damping = _v14;
        }
        let mut v15 = 1;
        let mut m = leader;
        while !m.is_none() {
            v15 &= rstep::update(self, m, dt, damping) as i32;
            m = self.ent(m).group_next;
        }
        let it = iter;
        let a = self.vars.max_substeps - it - 2;
        let b = self.vars.unk_308 - res.ret - 1;
        let c = self.vars.unk_304 - it - 2;
        let v38 = (((a | (b & c)) as u32) >> 31) as i32 | v15;
        if v38 != 0 {
            let mut m = leader;
            while !m.is_none() {
                let next = self.ent(m).group_next;
                let dl = self.done_list;
                let ent = self.ent_mut(m);
                ent.group_mark = 3;
                if ((ent.id_bits & 0xFF00_0000) as i32) < 0x300_0000 && ent.job_next.is_none() {
                    ent.job_next = dl;
                    self.done_list = m;
                }
                m = next;
            }
        } else {
            let mut m = leader;
            while !m.is_none() {
                let ent = self.ent_mut(m);
                ent.group_mark = 0;
                m = ent.group_next;
            }
        }
        *done &= v38;
    }

    /// Job mode 2 (`sub_3507A3B0`): the entities queued on the awake list (`+1752`) during
    /// pass 0 are stepped once more.
    fn run_awake_list(&mut self, _dt: f32) {
        let mut list = Vec::new();
        let mut e = self.awake_list;
        while !e.is_none() {
            list.push(e);
            e = self.ent(e).job_next;
        }
        self.awake_list = EntIdx::NONE;
        if list.is_empty() {
            return;
        }
        // `sub_35077370`: Hoare quicksort by the `+232` group pointer, then relinked
        let mut keys: Vec<u64> = list
            .iter()
            .map(|&e| {
                let g = self.ent(e).unk232;
                if g.is_none() {
                    0
                } else {
                    u64::from(g.0) + 1
                }
            })
            .collect();
        let n = list.len() as i32;
        sort_by_group(&mut list, &mut keys, 0, n - 1);
        for e in list {
            let tid = self.tls_thread.clamp(0, 4) as usize;
            let fl = self.ent(e).flags;
            self.island[tid].flag = i32::from(fl & 0x1_0000 != 0);
            let ent = self.ent_mut(e);
            ent.job_next = EntIdx::NONE;
            ent.flags &= !0x4000_0000;
            let edt = self.ent(e).time_idle;
            rstp::step(self, e, edt);
            self.ent_mut(e).group_mark = 0;
        }
    }

    /// The cleanup section (flag 0x80): deleted entities freed after 30 s, idle timeouts of
    /// the static/sleeping entities inserted without a list marker, idle timers of the active
    /// list reset.
    fn cleanup_step(&mut self, dt7: f32) {
        // deleted entities (simclass-7 list)
        let mut e = self.list_head[7];
        while !e.is_none() {
            let next = self.ent(e).list_next;
            if self.vars.dedicated != 0 {
                self.ent_mut(e).time_deleted += self.cur_dt;
            }
            let ent = self.ent(e);
            if self.pump_counter >= ent.deleted
                && (ent.time_deleted > 30.0 || (ent.pod_refs <= 0 && ent.processed < 0x20000))
            {
                let (n, p) = (ent.list_next, ent.list_prev);
                if !n.is_none() {
                    self.ent_mut(n).list_prev = p;
                }
                if !p.is_none() {
                    self.ent_mut(p).list_next = n;
                } else {
                    self.list_head[7] = n;
                }
                self.free_entity(e);
            }
            e = next;
        }
        for k in 0..2 {
            let mut e = self.list_head[k];
            while !e.is_none() && e != self.list_marker[k] {
                let next = self.ent(e).list_next;
                let special = self.ent(e).parts.iter().any(|p| p.flags & 0x8_0000 != 0)
                    || self.ent(e).has_structure;
                if special {
                    let ent = self.ent_mut(e);
                    ent.list_bits = (ent.list_bits & 0xF1FF_FFFF) | 0x100_0000;
                    self.relist_entity(e);
                } else if self.ent(e).pod_refs == 0 {
                    let v224 = dt7 + self.ent(e).time_idle;
                    let ok = v224 <= self.ent(e).max_time_idle;
                    self.ent_mut(e).time_idle = v224;
                    if !ok || v224 < 0.0 {
                        self.destroy_physical_entity(e, 0);
                    }
                }
                e = next;
            }
        }
        let mut e = self.list_head[2];
        while !e.is_none() && e != self.list_marker[2] {
            let ent = self.ent_mut(e);
            ent.time_idle = 0.0;
            e = ent.list_next;
        }
    }
}

/// `sub_35079CC0` with the island arrays (leaders, keys, permutation): quicksort descending.
fn sort_groups(list: &mut [EntIdx], keys: &mut [f32], perm: &mut [i32], mut lo: i32, hi: i32) {
    while lo < hi {
        let mid = ((lo + hi) >> 1) as usize;
        list.swap(lo as usize, mid);
        keys.swap(lo as usize, mid);
        perm.swap(lo as usize, mid);
        let pivot = lo as usize;
        let mut diff = 0.0f32;
        let mut store = lo;
        for k in (lo + 1)..=hi {
            let v = keys[k as usize];
            diff += (v - keys[pivot]).abs();
            if v > keys[pivot] {
                store += 1;
                list.swap(store as usize, k as usize);
                keys.swap(store as usize, k as usize);
                perm.swap(store as usize, k as usize);
            }
        }
        list.swap(pivot, store as usize);
        keys.swap(pivot, store as usize);
        perm.swap(pivot, store as usize);
        if diff <= 0.0 {
            break;
        }
        sort_groups(list, keys, perm, lo, store - 1);
        lo = store + 1;
    }
}

/// `sub_35077370(arr, lo, hi)`: Hoare quicksort of entity pointers by their `+232` value
/// (keys compared as addresses), recursing on the left part and looping on the right.
fn sort_by_group(list: &mut [EntIdx], keys: &mut [u64], lo: i32, hi: i32) {
    let mut v3 = lo;
    let v4 = hi;
    let mut v6 = lo;
    loop {
        let mut v7 = v3;
        let mut v8 = v4;
        let mut v9 = v4;
        let pivot = keys[((v3 + v4) >> 1) as usize];
        while v6 <= v9 {
            while keys[v6 as usize] < pivot {
                v6 += 1;
                v7 += 1;
            }
            while pivot < keys[v9 as usize] {
                v9 -= 1;
                v8 -= 1;
            }
            if v6 > v9 {
                break;
            }
            list.swap(v6 as usize, v9 as usize);
            keys.swap(v6 as usize, v9 as usize);
            v9 -= 1;
            v6 += 1;
            v7 += 1;
            v8 -= 1;
        }
        if v3 < v8 {
            sort_by_group(list, keys, v3, v8);
        }
        if v7 >= v4 {
            break;
        }
        v3 = v7;
    }
}

impl World {
    /// Debug aid: append the island's solver input as a `physoracle --p4b` case line
    /// ([`crate::solver::probe::Case`]) to `path` (env `AAEMU_PHYS_DUMP_SOLVER`).
    fn dump_solver_case(&mut self, tid: usize, dt: f32, e0: f32, path: &std::ffi::OsStr) {
        use crate::solver::{BodyId, ContactId, ContactOwner};
        let Some(sol) = self.solver[tid].as_ref() else {
            return;
        };
        let cl = sol.contacts();
        let cg = sol.cg_allowed();
        let mut bodies: Vec<BodyId> = Vec::new();
        for &c in &cl {
            for b in self.contacts.get(c).pbody {
                if b != BodyId::STATIC && !bodies.contains(&b) {
                    bodies.push(b);
                }
            }
        }
        bodies.sort_by_key(|b| self.bodies.sort_key[b.0 as usize]);
        let map_b = |b: BodyId| -> BodyId {
            if b == BodyId::STATIC {
                b
            } else {
                BodyId(
                    bodies
                        .iter()
                        .position(|&x| x == b)
                        .map_or(0, |i| i as u32 + 1),
                )
            }
        };
        let contacts: Vec<crate::solver::Contact> = cl
            .iter()
            .map(|&c| {
                let mut k = *self.contacts.get(c);
                k.pbody = [map_b(k.pbody[0]), map_b(k.pbody[1])];
                k.owner = match k.owner {
                    ContactOwner::Count(o) => match cl.iter().position(|&x| x == o) {
                        Some(i) => ContactOwner::Count(ContactId(i as u32 + 1)),
                        None => ContactOwner::Null,
                    },
                    x => x,
                };
                k.next = ContactId::NONE;
                k.prev = ContactId::NONE;
                k
            })
            .collect();
        let case = crate::solver::probe::Case {
            dt,
            e0,
            prev: 0,
            cg,
            vars: Vec::new(),
            bodies: bodies.iter().map(|&b| *self.bodies.get(b)).collect(),
            dummy: *self.bodies.get(BodyId::STATIC),
            contacts,
            multi: Vec::new(),
            nrep: 1,
        };
        use std::io::Write;
        if let Ok(mut f) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(path)
        {
            let _ = writeln!(f, "{}", case.to_line());
        }
    }
}
