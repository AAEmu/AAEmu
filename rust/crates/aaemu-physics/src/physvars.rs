//! The world's physics-vars block (`CPhysicalWorld + 104 .. + 632`, returned by world slot 20
//! `GetPhysVars`) with the exact constructor defaults.
//!
//! The defaults were read from the running DLL right after `CreatePhysicalWorld(NULL)` (oracle
//! command `dumpvars`, golden `tests/golden/engine_tables.trace`) and match the constructor
//! 0x350827B0 (x86 0x350889B0). Field names come from usage in the decompile (`world.md` §2,
//! `dynamics.md` §3.5); `unk_<offset>` fields have not been identified. Offsets in the field
//! table are **world offsets** (struct offset + 104), the way the RE notes cite them.
//!
//! Non-default sources: `dedicated` (612) is 1 when `gEnv` is NULL or `ISystem` slot 47 returns 2
//! (dedicated server) — the zone server therefore runs with 1; `last_dt`, `inv_time_granularity`,
//! `env_byte_289/290` are rewritten by every `TimeStep`.

use crate::math::Vec3;

/// World offset of the block.
pub const WORLD_OFFSET: usize = 104;
/// Size of the block in bytes.
pub const SIZE: usize = 528;

trait VarField: Sized {
    fn put(&self, out: &mut [u8]);
    fn take(b: &[u8]) -> Self;
}
impl VarField for f32 {
    fn put(&self, out: &mut [u8]) {
        out[..4].copy_from_slice(&self.to_le_bytes());
    }
    fn take(b: &[u8]) -> Self {
        f32::from_le_bytes(b[..4].try_into().expect("4 bytes"))
    }
}
impl VarField for i32 {
    fn put(&self, out: &mut [u8]) {
        out[..4].copy_from_slice(&self.to_le_bytes());
    }
    fn take(b: &[u8]) -> Self {
        i32::from_le_bytes(b[..4].try_into().expect("4 bytes"))
    }
}
impl VarField for i64 {
    fn put(&self, out: &mut [u8]) {
        out[..8].copy_from_slice(&self.to_le_bytes());
    }
    fn take(b: &[u8]) -> Self {
        i64::from_le_bytes(b[..8].try_into().expect("8 bytes"))
    }
}

macro_rules! physvars {
    ($($off:literal => $name:ident : $ty:ident = $def:expr, $doc:literal;)*) => {
        /// The physics-vars block. See the module docs.
        #[derive(Clone, Debug)]
        pub struct PhysVars {
            $(
                #[doc = concat!("`world+", stringify!($off), "`: ", $doc)]
                pub $name: $ty,
            )*
        }

        impl Default for PhysVars {
            fn default() -> Self {
                Self { $($name: $def,)* }
            }
        }

        impl PhysVars {
            /// `(world offset, field name, type)` for every field, in memory order.
            pub const FIELDS: &'static [(usize, &'static str, &'static str)] =
                &[$(($off, stringify!($name), stringify!($ty)),)*];

            /// Little-endian image of the block (528 bytes, as in the DLL).
            #[must_use]
            pub fn to_bytes(&self) -> [u8; SIZE] {
                let mut b = [0u8; SIZE];
                $( VarField::put(&self.$name, &mut b[$off - WORLD_OFFSET..]); )*
                b
            }

            /// Parse a block image.
            #[must_use]
            pub fn from_bytes(b: &[u8; SIZE]) -> Self {
                Self { $($name: VarField::take(&b[$off - WORLD_OFFSET..]),)* }
            }
        }
    };
}

physvars! {
    104 => unk_104: i32 = 8, "?. Default 8.";
    108 => unk_108: f32 = f32::from_bits(0x42480000), "?. Default 50.";
    112 => solver_max_iters_floor: i32 = 6000, "PGS stage-1 iteration floor: maxIt = max(4*nC, this) (CRYPHYSICS-MAP Appendix A). Default 6000.";
    116 => solver_iters_per_contact: i32 = 4, "PGS stage-1 iterations per contact. Default 4.";
    120 => unk_120: i32 = 6000, "?. Default 6000.";
    124 => solver_tol_n: f32 = f32::from_bits(0x3BA3D70A), "PGS normal-velocity tolerance (tolN); also read by 0x350D06A0. Default 0.00499999989.";
    128 => solver_lcp_tol: f32 = f32::from_bits(0x3BA3D70A), "stage-2 \"LCPCG\" tolerance. Default 0.00499999989.";
    132 => group_contacts_threshold: i32 = 150, "rigid group step: sum of per-entity contact counts <= this uses group_iters_small. Default 150.";
    136 => group_iters_small: i32 = 8, "iteration count passed to entity slots 39/40 for small groups. Default 8.";
    140 => group_iters_large: i32 = 4, "same for large groups. Default 4.";
    144 => lcp_iters_min: i32 = 120, "stage-2 iteration limit min(nU, this). Default 120.";
    148 => lcp_iters_max: i32 = 250, "stage-2 iteration limit min(2*nU, this). Default 250.";
    152 => unk_152: i32 = 12000, "?. Default 12000.";
    156 => unk_156: i32 = 25000, "?. Default 25000.";
    160 => lcp_passes: i32 = 5, "stage-2 outer passes; > 0 also enables the island difficulty test. Default 5.";
    164 => lcp_min_improvement: f32 = f32::from_bits(0x3D4CCCCD), "stage-2 stall: improvement below this fraction. Default 0.0500000007.";
    168 => lcp_stall_iters: i32 = 4, "stage-2 stall: this many weak iterations. Default 4.";
    172 => unk_172: f32 = f32::from_bits(0x3D4CCCCD), "?. Default 0.0500000007.";
    176 => solver_tol_t: f32 = f32::from_bits(0x3CA3D70A), "PGS tangential tolerance (tolT); distance threshold in 0x350DD620 / 0x350D06A0. Default 0.0199999996.";
    180 => lcp_max_dv: f32 = f32::from_bits(0x43FA0000), "stage 2 is rejected when |dv| exceeds this. Default 500.";
    184 => lcp_max_dw: f32 = f32::from_bits(0x43FA0000), "stage 2 is rejected when |dw| exceeds this. Default 500.";
    188 => unk_188: f32 = f32::from_bits(0x41200000), "?. Default 10.";
    192 => unproject_by_velocity: i32 = 0, "stage-3 results go into the velocities instead of the pose when != 0. Default 0.";
    196 => heavy_light_mass_ratio: f32 = f32::from_bits(0x42C80000), "island \"hard\" test: a light body squeezed between bodies heavier by this ratio. Default 100.";
    200 => hard_speed: f32 = f32::from_bits(0x41700000), "island \"hard\" test: any body faster than this. Default 15.";
    204 => lcp_max_contacts: i32 = 100, "stage 2 runs only below this contact count. Default 100.";
    208 => single_step_mode: i32 = 0, "when != 0, rigid/independent stepping only runs while do_step != 0. Default 0.";
    212 => do_step: i32 = 0, "\"do one step\" trigger, cleared at the end of a step; 2 also dumps the world to D:/worldents.txt. Default 0.";
    216 => fixed_step: f32 = f32::from_bits(0x00000000), "if > 0 and dt > 0, dt := this. Default 0.";
    220 => time_granularity: f32 = f32::from_bits(0x38D1B717), "dt is quantised to multiples of this; iPhysicsTime = time / granularity. Default 9.99999975e-05.";
    224 => max_world_step: f32 = f32::from_bits(0x3E4CCCCD), "dt is clamped to this. Default 0.200000003.";
    228 => debug_flags: i32 = 0, "bit 0x40: report rays to the debug callback (world+656). Default 0.";
    232 => unk_232: i32 = 0, "? (CPhysicalEntity slot 73). Default 0.";
    236 => grid_flags: i32 = 0, "bit 0: rays/boxes leaving the grid also visit the out-of-grid cell. Default 0.";
    240 => contact_gap: f32 = f32::from_bits(0x3C23D70A), "allowed penetration: vreq = min(max_unproject_vel, (depth - this) * unproject_vel_gain). Default 0.00999999978.";
    244 => living_eps: f32 = f32::from_bits(0x3C23D70A), "living-entity distance epsilon. Default 0.00999999978.";
    248 => min_bounce_speed: f32 = f32::from_bits(0x40C00000), "restitution only applies when vn < -this. Default 6.";
    252 => unk_252: i32 = 1, "?. Default 1.";
    256 => unk_256: i32 = 0, "ANDed every step with !gEnv.byte[290]. Default 0.";
    260 => unproject_vel_gain: f32 = f32::from_bits(0x41200000), "unprojection velocity per metre of penetration. Default 10.";
    264 => max_unproject_vel: f32 = f32::from_bits(0x40200000), "maximum unprojection velocity. Default 2.5.";
    268 => unk_268: f32 = f32::from_bits(0x41200000), "? (rope). Default 10.";
    272 => unk_272: i32 = 1, "?. Default 1.";
    276 => max_substeps: i32 = 5, "outer rigid loop count and per-independent-entity step loop (the ctor writes 10, then 5). Default 5.";
    280 => unk_280: i32 = 512, "?. Default 512.";
    284 => gravity_x: f32 = f32::from_bits(0x00000000), "world gravity x (copied into the global area). Default 0.";
    288 => gravity_y: f32 = f32::from_bits(0x00000000), "world gravity y. Default 0.";
    292 => gravity_z: f32 = f32::from_bits(0xC11CCCCD), "world gravity z. Default -9.80000019.";
    296 => group_damping_min_ents: i32 = 8, "group damping applies to islands with more entities than this. Default 8.";
    300 => group_damping: f32 = f32::from_bits(0x3F000000), "group damping factor 1 - dt * this. Default 0.5.";
    304 => unk_304: i32 = 5, "?. Default 5.";
    308 => unk_308: i32 = 30, "?. Default 30.";
    312 => validation_strict: i32 = 0, "validation strictness for SetParams and grid insertion. Default 0.";
    316 => debug_316: i32 = 0, "debug switch (world slot 58). Default 0.";
    320 => debug_320: i32 = 0, "debug switch (world slot 58). Default 0.";
    324 => debug_324: i32 = 0, "debug switch (world slot 58). Default 0.";
    328 => env_byte_290: i32 = 0, "= gEnv.byte[290], refreshed every step. Default 0.";
    332 => env_byte_289: i32 = 0, "= gEnv.byte[289], refreshed every step. Default 0.";
    336 => debug_336: i32 = 0, "debug switch read by several Step functions. Default 0.";
    340 => profile_functions: i32 = 0, "function profiling on. Default 0.";
    344 => profile_entity_types: i32 = 0, "entity-type profiling on. Default 0.";
    348 => unk_348: i32 = 800, "?. Default 800.";
    352 => unk_352: i32 = 512, "?. Default 512.";
    356 => max_entity_cells: i32 = 300000, "max grid cells covered by one entity. Default 300000.";
    360 => max_particle_cells: i32 = 128, "same limit for particles (simclass 5). Default 128.";
    364 => max_velocity: f32 = f32::from_bits(0x42C80000), "maximum linear velocity (rigid velocity clamp); also a size limit in articulated code. Default 100.";
    368 => unk_368: f32 = f32::from_bits(0x43160000), "?. Default 150.";
    372 => unk_372: f32 = f32::from_bits(0x3CF5C28F), "? (squared in 0x350AD060). Default 0.0299999993.";
    376 => unk_376: f32 = f32::from_bits(0x3E99999A), "?. Default 0.300000012.";
    380 => unk_380: i32 = 1, "? (CRigidEntity slot 81). Default 1.";
    384 => unk_384: i32 = 1, "?. Default 1.";
    388 => max_contacts_per_entity: i32 = 256, "RegisterContactPoint allocates new contacts below this count. Default 256.";
    392 => breakage_enabled: i32 = 0, "breakage switch read by the structure update. Default 0.";
    396 => unk_396: i32 = 100000, "?. Default 100000.";
    400 => breakage_event_log: i32 = 1, "structure events are signalled with mask (this + 1): 1 immediate, 2 logged. Default 1.";
    404 => breakage_404: f32 = f32::from_bits(0x3DCCCCCD), "used by the structure update 0x35061030. Default 0.100000001.";
    408 => unk_408: f32 = f32::from_bits(0x3F99999A), "? (0x3505F140). Default 1.20000005.";
    412 => unk_412: i32 = 7, "?. Default 7.";
    416 => unk_416: i32 = 0, "?. Default 0.";
    420 => last_dt: f32 = f32::from_bits(0x00000000), "dt of the last step (written every step unless flag 0x800). Default 0.";
    424 => thread_bias: i32 = 0, "thread-index bias. Default 0.";
    428 => unk_428: f32 = f32::from_bits(0x3F800000), "? (breakage). Default 1.";
    432 => inv_time_granularity: f32 = f32::from_bits(0x00000000), "1 / time_granularity, written by TimeStep. Default 0.";
    436 => unk_436: f32 = f32::from_bits(0x501502F9), "?. Default 1e+10.";
    440 => unk_440: i32 = -2147483648, "?. Default -2147483648.";
    444 => unk_444: i32 = -1, "?. Default -1.";
    448 => unk_448: i32 = 0, "?. Default 0.";
    452 => max_splash_events: f32 = f32::from_bits(0x00000000), "splash events against water are posted when > 0. Default 0.";
    456 => splash_456: f32 = f32::from_bits(0x40E00000), "splash threshold. Default 7.";
    460 => splash_460: f32 = f32::from_bits(0x466A6000), "splash threshold. Default 15000.";
    464 => splash_464: f32 = f32::from_bits(0x40900000), "splash threshold. Default 4.5.";
    468 => splash_dist: f32 = f32::from_bits(0x41F00000), "max distance of the CoM from the splash reference point. Default 30.";
    472 => splash_472: f32 = f32::from_bits(0x48127C00), "splash threshold. Default 150000.";
    476 => splash_476: f32 = f32::from_bits(0x41200000), "splash threshold. Default 10.";
    480 => unk_480: i32 = 0, "?. Default 0.";
    484 => unk_484: f32 = f32::from_bits(0x3F800000), "?. Default 1.";
    488 => unk_488: i32 = 0, "?. Default 0.";
    492 => unk_492: i32 = 0, "?. Default 0.";
    496 => time_scale: f32 = f32::from_bits(0x3F800000), "when != 1 and dt > 0 slow rigid bodies are re-awakened after 5 such steps. Default 1.";
    500 => unk_500: i32 = 0, "?. Default 0.";
    504 => num_threads: i32 = 1, "number of physics threads (clamped to <= 4; 1 = no workers). Default 1.";
    508 => worker_stack_kb: i32 = 1536, "worker stack size in KB. Default 1536.";
    512 => unk_512: i32 = 4, "?. Default 4.";
    516 => unk_516: i32 = 1, "?. Default 1.";
    520 => worker_param: i32 = 4, "passed with the stack size when spawning workers. Default 4.";
    524 => unk_524: i32 = 0, "?. Default 0.";
    528 => unk_528: i64 = 0, "? (64-bit). Default 0.";
    536 => event_handler_budget: i64 = 3000000000, "event handlers whose 300*ticks exceed this are logged. Default 3000000000.";
    544 => unk_544: f32 = f32::from_bits(0x3DCCCCCD), "?. Default 0.100000001.";
    548 => unk_548: f32 = f32::from_bits(0x3DCCCCCD), "?. Default 0.100000001.";
    552 => unk_552: f32 = f32::from_bits(0x3F7D70A4), "?. Default 0.99000001.";
    556 => unk_556: f32 = f32::from_bits(0x3C23D70A), "?. Default 0.00999999978.";
    560 => unk_560: f32 = f32::from_bits(0x40A00000), "?. Default 5.";
    564 => unk_564: i32 = 1, "? (0x35055860). Default 1.";
    568 => max_event_lag: i32 = 20, "max event-pump lag for the re-awake pass. Default 20.";
    572 => keep_temp_memory: i32 = 0, "if 0, TimeStep frees the temp pools at the end of a step. Default 0.";
    576 => unk_576: i32 = 0, "?. Default 0.";
    580 => rwi_flags_or: i32 = 5, "ORed into an internal RWI flag word. Default 5.";
    584 => breakage_tolerance: f32 = f32::from_bits(0x3C23D70A), "breakage tolerance (0x35061030). Default 0.00999999978.";
    588 => unk_588: f32 = f32::from_bits(0x41000000), "? (written last by the ctor). Default 8.";
    592 => soft_divisor: f32 = f32::from_bits(0x42C80000), "soft-entity divisor. Default 100.";
    596 => rope_seg_divisor: f32 = f32::from_bits(0x3CA3D70A), "rope segment length divisor. Default 0.0199999996.";
    600 => unk_600: i32 = 0, "?. Default 0.";
    604 => soft_604: f32 = f32::from_bits(0x40E00000), "soft entity. Default 7.";
    608 => unk_608: f32 = f32::from_bits(0x3F800000), "?. Default 1.";
    612 => dedicated: i32 = 1, "dedicated-server mode (1 when gEnv is NULL or ISystem slot 47 returns 2): deleted entities age by dt and are freed after 30 s. Default 1.";
    616 => unk_616: i64 = 0, "? (64-bit). Default 0.";
    624 => living_eps2: f32 = f32::from_bits(0x3A83126F), "living-entity epsilon. Default 0.00100000005.";
    628 => rigid_eps: f32 = f32::from_bits(0x3A83126F), "rigid epsilon (0x350C4880). Default 0.00100000005.";
}

impl PhysVars {
    /// World gravity `(gravity_x, gravity_y, gravity_z)`.
    #[must_use]
    pub fn gravity(&self) -> Vec3 {
        Vec3::new(self.gravity_x, self.gravity_y, self.gravity_z)
    }

    /// Set the world gravity.
    pub fn set_gravity(&mut self, g: Vec3) {
        self.gravity_x = g.x;
        self.gravity_y = g.y;
        self.gravity_z = g.z;
    }

    /// The block as 32-bit words (oracle `vars` line order).
    #[must_use]
    pub fn to_words(&self) -> [u32; SIZE / 4] {
        let b = self.to_bytes();
        let mut w = [0u32; SIZE / 4];
        for (i, c) in b.as_chunks::<4>().0.iter().enumerate() {
            w[i] = u32::from_le_bytes(*c);
        }
        w
    }
}

/// Surface-table defaults written by the world constructor for each of the 512 surface types:
/// bounciness (world+4456), static friction (+6504), dynamic friction (+8552; SetSurfaceParameters
/// sets it to 2/3 of the static value) and flags (+18792).
pub mod surface_defaults {
    /// Number of surface types.
    pub const COUNT: usize = 512;
    pub const BOUNCINESS: f32 = 0.0;
    /// 1.2 (bits 0x3F99999A).
    pub const FRICTION: f32 = f32::from_bits(0x3F99_999A);
    /// 0.8 (bits 0x3F4CCCCD).
    pub const DYNAMIC_FRICTION: f32 = f32::from_bits(0x3F4C_CCCD);
    pub const FLAGS: i32 = 0;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn layout_is_contiguous() {
        let mut off = WORLD_OFFSET;
        for &(o, name, ty) in PhysVars::FIELDS {
            assert_eq!(o, off, "gap before {name}");
            off += if ty == "i64" { 8 } else { 4 };
        }
        assert_eq!(off, WORLD_OFFSET + SIZE);
    }

    #[test]
    fn known_defaults() {
        let v = PhysVars::default();
        assert_eq!(v.gravity(), Vec3::new(0.0, 0.0, -9.8));
        assert_eq!(v.time_granularity, 1e-4);
        assert_eq!(v.max_world_step, 0.2);
        assert_eq!(v.max_substeps, 5);
        assert_eq!(v.num_threads, 1);
        assert_eq!(v.contact_gap, 0.01);
        assert_eq!(v.unproject_vel_gain, 10.0);
        assert_eq!(v.max_unproject_vel, 2.5);
        assert_eq!(v.max_contacts_per_entity, 256);
        assert_eq!(v.event_handler_budget, 3_000_000_000);
        let nonzero = v.to_words().iter().filter(|w| **w != 0).count();
        assert_eq!(nonzero, 93);
        let b = v.to_bytes();
        assert_eq!(PhysVars::from_bytes(&b).to_bytes(), b);
    }
}
