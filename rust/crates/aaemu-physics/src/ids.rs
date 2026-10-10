//! Type ids, simulation classes, event ids, flag bits and the engine's struct-size tables.
//!
//! Numbers come from the x64 10.x binary (identical in the 1.2 client, CRYPHYSICS-MAP §2);
//! the size tables were read from the running DLL by the oracle (`tests/golden/engine_tables.trace`).

macro_rules! id_enum {
    ($(#[$m:meta])* $vis:vis enum $name:ident : $repr:ty { $($(#[$vm:meta])* $var:ident = $val:literal,)* }) => {
        $(#[$m])*
        #[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
        #[repr($repr)]
        $vis enum $name { $($(#[$vm])* $var = $val,)* }

        impl $name {
            /// All values in id order.
            pub const ALL: &'static [$name] = &[$($name::$var,)*];

            /// The numeric id.
            #[inline]
            #[must_use]
            pub const fn id(self) -> $repr {
                self as $repr
            }

            /// Look up an id.
            #[must_use]
            pub const fn from_id(v: $repr) -> Option<Self> {
                match v {
                    $($val => Some($name::$var),)*
                    _ => None,
                }
            }
        }
    };
}

id_enum! {
    /// Geometry type (`IGeometry::GetType`, slot 1). Also the row/column index of the
    /// OVERLAP / INTERSECT / UNPROJECT tables and the primitive type in BV records.
    pub enum GeomType: i32 {
        Box = 0,
        TriMesh = 1,
        Heightfield = 2,
        Ray = 3,
        Sphere = 4,
        Cylinder = 5,
        Capsule = 6,
        Voxel = 7,
    }
}

impl GeomType {
    /// Number of geometry types (dimension of the pair tables).
    pub const COUNT: usize = 8;
}

id_enum! {
    /// BV-tree type (BV vtable slot 1); its own numbering, distinct from [`GeomType`].
    pub enum BvTreeType: i32 {
        Obb = 0,
        Aabb = 1,
        SingleBox = 2,
        Ray = 3,
        Heightfield = 4,
        Voxel = 5,
    }
}

id_enum! {
    /// Physical entity type (`CreatePhysicalEntity` type, entity slot 1 GetType).
    pub enum EntityType: i32 {
        /// `CPhysicalEntity` (0x1C0 bytes).
        Static = 1,
        /// `CRigidEntity` (0x4B0).
        Rigid = 2,
        /// `CWheeledVehicleEntity` : rigid (0x2BF8).
        WheeledVehicle = 3,
        /// `CLivingEntity` (0x468).
        Living = 4,
        /// `CParticleEntity` (0x2A8).
        Particle = 5,
        /// `CArticulatedEntity` : rigid (0x618).
        Articulated = 6,
        /// `CRopeEntity` (0x400).
        Rope = 7,
        /// `CSoftEntity` (0x360).
        Soft = 8,
        /// `CPhysArea` (0x228; GetType returns 9, not creatable through CreatePhysicalEntity).
        Area = 9,
    }
}

id_enum! {
    /// Simulation class (entity byte +0x47). The world keeps one entity list per class at
    /// `world + 664 + 8·k`. Confirmed with the oracle (`listwhere`): static → 664, rigid awake →
    /// 680 and, once asleep, 672; living → 688; particle → 696 (and the independent stepping
    /// list at +760). Entities enter their list at the first `TimeStep` after creation.
    pub enum SimClass: i8 {
        Static = 0,
        SleepingRigid = 1,
        ActiveRigid = 2,
        Living = 3,
        Independent = 4,
        /// Used for areas and in grid thunk ordering (simclass-5 thunks are kept first).
        Area = 5,
        Unused6 = 6,
        /// Deleted / pending deletion (list at +720).
        Deleted = 7,
    }
}

/// Object-type mask bits for entity queries (`GetEntitiesInBox`, `RayWorldIntersection`
/// `objtypes`): bit `1 << simclass`, plus extra bits.
pub mod objtypes {
    pub const STATIC: u32 = 1 << 0;
    pub const SLEEPING_RIGID: u32 = 1 << 1;
    pub const RIGID: u32 = 1 << 2;
    pub const LIVING: u32 = 1 << 3;
    pub const INDEPENDENT: u32 = 1 << 4;
    /// Include areas (walks the area list with a bbox test).
    pub const AREAS: u32 = 0x20;
    /// Include the per-caller terrain entity (world+1568+8·iCaller; heightfield via SetHeightfieldData).
    pub const TERRAIN: u32 = 0x100;
    /// "Water" objtype (world+20844 = 0x200 when the global area has a water plane).
    pub const WATER: u32 = 0x200;
    /// Everything the oracle scenarios use by default.
    pub const ALL: u32 = 0x13F;
}

/// RayWorldIntersection flags (`SRWIParams +52`), as decoded from slot 42 (0x351AA730).
pub mod rwi_flags {
    /// Low 4 bits: pierceability threshold compared with the surface's pierceability.
    pub const PIERCEABILITY_MASK: u32 = 0xF;
    /// Adds 1 to the geomFlagsAll value derived from the high word.
    pub const COLLTYPE_BIT0: u32 = 0x40;
    /// Post-sort hits (solid hit in slot 0, pierceable hits after).
    pub const SORT_HITS: u32 = 0x200;
    /// "Any collision type": geomFlagsAll = 0, geomFlagsAny = the high-word value.
    pub const COLLTYPE_ANY: u32 = 0x400;
    /// Queue the ray (processed by world slot 43, result through event 9).
    pub const QUEUE: u32 = 0x800;
    /// High word: part flags that must all be present (geomFlagsAll); 0 means 0x8000
    /// ([`super::part_flags::COLLTYPE_RAY`]). geomFlagsAny defaults to 0xFDFF.
    pub const COLLTYPE_SHIFT: u32 = 16;
    /// Default geomFlagsAny.
    pub const DEFAULT_FLAGS_ANY: u32 = 0xFDFF;
}

/// Part flags (`geom_part +0x50`, `pe_geomparams +68`). Collision-class semantics of the low
/// bits are inferred; the ones the engine itself tests are listed.
pub mod part_flags {
    /// Part is hit by rays with default RWI flags (geomFlagsAll default).
    pub const COLLTYPE_RAY: u32 = 0x8000;
    /// Part takes part in buoyancy ("floats").
    pub const FLOATS: u32 = 0x1_0000;
    /// AddGeometry: only replace the proxy geometry of an existing part (stripped before storing).
    pub const PROXY_ONLY: u32 = 0x2_0000;
    /// Breakable geometry.
    pub const BREAKABLE: u32 = 0x8_0000;
    /// Special contact path in rigid Step.
    pub const SPECIAL_CONTACT: u32 = 0x10_0000;
    /// Has a lattice / structure (breakable constraint).
    pub const HAS_STRUCTURE: u32 = 0x40_0000;
    /// Removed by a queued remove_all_parts.
    pub const REMOVED: u32 = 0x1000_0000;
    /// Wheel part (no friction in ordinary contacts, collider mask 0).
    pub const WHEEL: u32 = 0x4000_0000;
    /// Terrain entities created by SetHeightfieldData carry 0xFDFF.
    pub const TERRAIN_DEFAULT: u32 = 0xFDFF;
}

/// Entity flags (`CPhysicalEntity +0x5C`), default 0x8640.
pub mod entity_flags {
    pub const DEFAULT: u32 = 0x8640;
    /// Keep-awake-like: damping >= 0.9 and an energy cap in Update.
    pub const KEEP_AWAKE_DAMP: u32 = 0x1;
    /// Auto part detachment active.
    pub const AUTO_DETACH: u32 = 0x2;
    /// No splash events.
    pub const NO_SPLASH: u32 = 0x4;
    /// Apply air resistance (rigid Step).
    pub const AIR_RESISTANCE: u32 = 0x8;
    /// Large-object grid/OBB mode (converted to 0x400 when the cell count is small).
    pub const LARGE_OBJECT: u32 = 0x10;
    /// Hidden from the grid / traceable (particles: register in the world grid).
    pub const GRID_HIDDEN: u32 = 0x400;
    /// Skip the "entities in box" wake.
    pub const NO_BOX_WAKE: u32 = 0x8000;
    /// Ignore buoyancy from the global area for CheckAreas callers.
    pub const IGNORE_GLOBAL_AREA: u32 = 0x2_0000;
    pub const ALERT: u32 = 0x10_0000;
    /// Impulses scaled by a world var.
    pub const IMPULSE_SCALE: u32 = 0x20_0000;
    /// Ignore areas.
    pub const IGNORE_AREAS: u32 = 0x80_0000;
}

/// `pe_params` ids.
pub mod params_id {
    pub const POS: i32 = 0;
    pub const PLAYER_DIMENSIONS: i32 = 1;
    pub const CAR: i32 = 2;
    pub const PARTICLE: i32 = 3;
    pub const PLAYER_DYNAMICS: i32 = 4;
    pub const JOINT: i32 = 5;
    pub const PART: i32 = 6;
    pub const SENSORS: i32 = 7;
    pub const ARTICULATED_BODY: i32 = 8;
    pub const OUTER_ENTITY: i32 = 9;
    pub const SIMULATION: i32 = 10;
    pub const FOREIGN_DATA: i32 = 11;
    pub const BUOYANCY: i32 = 12;
    pub const ROPE: i32 = 13;
    pub const BBOX: i32 = 14;
    pub const FLAGS: i32 = 15;
    pub const WHEEL: i32 = 16;
    pub const SOFTBODY: i32 = 17;
    pub const AREA: i32 = 18;
    pub const UNKNOWN_19: i32 = 19;
    pub const GROUND_PLANE: i32 = 20;
    pub const STRUCTURAL_JOINT: i32 = 21;
    pub const WATER_MANAGER: i32 = 22;
    pub const TIMEOUT: i32 = 23;
    pub const SKELETON: i32 = 24;
    pub const PART_VELOCITY: i32 = 25;
    pub const BYTE_FLAG: i32 = 26;
    /// x64 10.x only: soft-body ignore-part-id list.
    pub const IGNORE_PART_IDS: i32 = 117;
}

/// `pe_status` ids (there is no size table; ids from the handlers).
pub mod status_id {
    pub const POS: i32 = 1;
    pub const LIVING: i32 = 2;
    pub const VEHICLE: i32 = 4;
    pub const WHEEL: i32 = 5;
    pub const JOINT: i32 = 6;
    pub const AWAKE: i32 = 7;
    pub const DYNAMICS: i32 = 8;
    pub const COLLISIONS: i32 = 9;
    pub const ID: i32 = 10;
    pub const LIVING_11: i32 = 11;
    pub const NPARTS: i32 = 12;
    pub const CONTAINS_POINT: i32 = 13;
    pub const ROPE: i32 = 14;
    pub const VEHICLE_ABILITIES: i32 = 15;
    pub const PLACEHOLDER: i32 = 16;
    pub const SOFT_VERTICES: i32 = 17;
    pub const CONTACT_PATCH: i32 = 19;
    pub const ARTIC_20: i32 = 20;
    pub const CHECK_STANCE: i32 = 21;
    pub const EXTENT: i32 = 24;
    pub const RANDOM_POS: i32 = 25;
    pub const CONSTRAINT: i32 = 26;
    pub const LOCKED_COPY: i32 = 27;
}

/// `pe_action` ids.
pub mod action_id {
    pub const MOVE: i32 = 1;
    pub const IMPULSE: i32 = 2;
    pub const DRIVE: i32 = 3;
    pub const RESET: i32 = 4;
    pub const ADD_CONSTRAINT: i32 = 5;
    pub const UPDATE_CONSTRAINT: i32 = 6;
    pub const REGISTER_COLL_EVENT: i32 = 7;
    pub const AWAKE: i32 = 8;
    pub const REMOVE_ALL_PARTS: i32 = 9;
    pub const SET_VELOCITY: i32 = 10;
    pub const ATTACH_POINTS: i32 = 11;
    pub const TARGET_VTX: i32 = 12;
    pub const RESET_PART_MTX: i32 = 13;
    pub const ROPE_RELAY: i32 = 14;
    pub const AUTO_PART_DETACHMENT: i32 = 15;
    pub const MOVE_PARTS: i32 = 16;
    pub const SET_PART_POSES: i32 = 17;
    pub const NET_SYNC: i32 = 18;
}

/// `pe_geomparams` ids.
pub mod geomparams_id {
    pub const PLAIN: i32 = 0;
    pub const CAR: i32 = 1;
    pub const ARTICULATED: i32 = 2;
}

/// `sizeof(pe_params)` by id: the table the DLL builds at 0x3537C640 (read by the oracle).
pub const PARAMS_SIZES: [u32; 27] = [
    72, 64, 144, 168, 104, 208, 160, 24, 176, 24, 84, 32, 80, 248, 28, 16, 80, 100, 72, 36, 32,
    120, 52, 12, 48, 32, 8,
];
/// `sizeof(pe_action)` by id (0x3537C5F0); 0 = no struct (ids 0 and 14).
pub const ACTION_SIZES: [u32; 19] = [
    0, 24, 80, 36, 8, 128, 48, 88, 12, 4, 40, 48, 48, 12, 0, 12, 72, 104, 88,
];
/// `sizeof(pe_geomparams)` by id (0x3537C5D8).
pub const GEOMPARAMS_SIZES: [u32; 3] = [112, 184, 120];

id_enum! {
    /// World event types (world slot 103 SignalEvent, slots 75/76 client registration).
    pub enum EventType: i32 {
        /// bbox overlap (2 entities): GetEntitiesInBox petitioner hits, living step.
        BBoxOverlap = 0,
        /// collision (2 entities).
        Collision = 1,
        /// grid / state change (RepositionEntity).
        StateChange = 2,
        /// simulation-class / awake change.
        ChangeSimClass = 3,
        /// post-step (carries the step index at +72).
        PostStep = 4,
        /// geometry / mesh update (structure).
        UpdateMesh = 5,
        /// part created by breakage.
        CreateEntityPart = 6,
        /// parts removed.
        RemoveEntityParts = 7,
        /// joint broken.
        JointBreak = 8,
        /// queued RayWorldIntersection result.
        RwiResult = 9,
        /// queued PrimitiveWorldIntersection result.
        PwiResult = 10,
        /// area callback (game-supplied gravity / buoyancy).
        Area = 11,
        /// area added / changed / removed.
        AreaChange = 12,
        /// part / joint id change.
        EntityIdChange = 13,
        /// external only (slot 103).
        External14 = 14,
    }
}

/// Event struct sizes by [`EventType`] id.
pub const EVENT_SIZES: [u32; 15] = [
    56, 160, 56, 64, 80, 136, 200, 64, 128, 72, 88, 176, 64, 48, 48,
];

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn enums_round_trip() {
        for g in GeomType::ALL {
            assert_eq!(GeomType::from_id(g.id()), Some(*g));
        }
        assert_eq!(GeomType::ALL.len(), GeomType::COUNT);
        assert_eq!(EntityType::from_id(2), Some(EntityType::Rigid));
        assert_eq!(EntityType::from_id(0), None);
        assert_eq!(SimClass::from_id(7), Some(SimClass::Deleted));
        assert_eq!(BvTreeType::Ray.id(), 3);
        assert_eq!(EventType::ALL.len(), EVENT_SIZES.len());
    }

    #[test]
    fn size_tables() {
        assert_eq!(PARAMS_SIZES[params_id::POS as usize], 72);
        assert_eq!(PARAMS_SIZES[params_id::BUOYANCY as usize], 80);
        assert_eq!(ACTION_SIZES[action_id::IMPULSE as usize], 80);
        assert_eq!(GEOMPARAMS_SIZES[geomparams_id::PLAIN as usize], 112);
    }
}
