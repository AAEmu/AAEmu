//! Contact and query records shared by the pair functions (P2a), unprojection (P2b), the
//! world queries (P3) and the rigid entity (P4a). Shapes follow `geometry.md` §4.2–§4.3, §5.1;
//! P0 defines them, P2a/P2b may add fields they discover but must not rename or retype these.

use crate::math::Vec3;

/// Feature-code bits in `iFeature` bytes: bits 5–6 kind, bits 0–4 index, bit 7 side flag.
pub mod feature {
    pub const VERTEX: u8 = 0x00;
    pub const EDGE: u8 = 0x20;
    pub const FACE: u8 = 0x40;
    /// Unprojection results: vertex.
    pub const UNPROJ_VERTEX: u8 = 0x80;
    /// Unprojection results: triangle / ray edge.
    pub const UNPROJ_TRI_EDGE: u8 = 0xA0;
    pub const KIND_MASK: u8 = 0x60;
    pub const INDEX_MASK: u8 = 0x1F;
}

/// `prim_inters` (output of an INTERSECT pair function, ≥ 88 bytes in the binary).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct PrimInters {
    /// `+0` / `+12`: point on A (deepest point) and point on B.
    pub pt: [Vec3; 2],
    /// `+24` contact normal; orientation depends on the pair (see the wrapper fix-ups).
    pub n: Vec3,
    /// `+36` `[A.feat0, A.feat1, B.feat0, B.feat1]`.
    pub i_feature: [[u8; 2]; 2],
    /// `+40` input tolerance used by some pairs (e.g. ray–tri).
    pub tolerance: f32,
    /// `+44` material / foreign ids.
    pub id: [i16; 2],
    /// `+48` BV node ids.
    pub i_node: [i32; 2],
    /// `+56`/`+64`/`+68` intersection border polyline (e.g. box–box).
    pub border: Vec<Vec3>,
    /// `+72` alternative normal (triangle pairs).
    pub n_alt: Vec3,
    /// `+84` (set to 1000 by tri–sph).
    pub unk_84: i32,
}

/// `geom_contact_area` (72 bytes, inferred): a polygonal contact patch.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct ContactArea {
    pub min_edge: f32,
    pub prim: [Vec<i32>; 2],
    pub feature: [Vec<i32>; 2],
    pub pt: Vec<Vec3>,
}

/// `geom_contact` (136 bytes, written by `RegisterIntersection`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct GeomContact {
    /// `+0` (double) unprojection depth or time.
    pub t: f64,
    pub pt: Vec3,
    pub n: Vec3,
    /// Unprojection direction (linear) or axis (rotational).
    pub dir: Vec3,
    /// 0 linear, 1 rotational.
    pub i_unproj_mode: i32,
    /// |relative velocity| or angular speed.
    pub vel: f32,
    pub id: [i32; 2],
    /// Triangle index for meshes, -1 for primitives.
    pub i_prim: [i32; 2],
    pub i_feature: [i32; 2],
    pub i_node: [i32; 2],
    /// Border points (defaults to `[pt]`); `idx_border` entries are `iPrim | iFeature << 23`.
    pub border: Vec<Vec3>,
    pub idx_border: Vec<[i32; 2]>,
    pub center: Vec3,
    pub border_consecutive: bool,
    pub area: Option<ContactArea>,
}

/// `intersection_params` (inferred names; defaults are inlined in every constructor).
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct IntersectionParams {
    /// `+0` 0 linear, 1 rotational.
    pub i_unprojection_mode: i32,
    pub center_of_rotation: Vec3,
    pub axis_of_rotation: Vec3,
    /// `+28`.
    pub time_interval: f32,
    /// `+32` velocity-directed unprojection only if |vrel| > this (>= 1e9 disables).
    pub vrel_min: f32,
    /// `+36`.
    pub max_surface_gap_angle: f32,
    /// `+40`.
    pub min_axis_dist: f32,
    /// `+56` preferred unprojection direction.
    pub unproj_dir: Vec3,
    /// `+68`.
    pub max_unproj: f32,
    /// `+72`, `+84`.
    pub pt_outside_pivot: [Vec3; 2],
    /// `+96`.
    pub sweep_test: bool,
    /// `+97`.
    pub keep_prev_contacts: bool,
    /// `+99`.
    pub no_area_contacts: bool,
    /// `+108` skip contact generation, unproject only.
    pub no_intersection: bool,
}

impl Default for IntersectionParams {
    fn default() -> Self {
        Self {
            i_unprojection_mode: 0,
            center_of_rotation: Vec3::ZERO,
            axis_of_rotation: Vec3::ZERO,
            time_interval: 100.0,
            vrel_min: 1e-6,
            max_surface_gap_angle: 0.017_453_3,
            min_axis_dist: 0.0,
            unproj_dir: Vec3::Z,
            max_unproj: 1e10,
            pt_outside_pivot: [Vec3::splat(1e11); 2],
            sweep_test: false,
            keep_prev_contacts: false,
            no_area_contacts: false,
            no_intersection: false,
        }
    }
}

/// `unproj_params` (≥ 52 bytes, inferred).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct UnprojParams {
    /// Table plane: 0 linear, 1 rotational.
    pub mode: i32,
    /// Linear: direction (zero = find the least-penetration direction and write it back).
    /// Rotational: axis.
    pub dir: Vec3,
    pub center: Vec3,
    pub vel: f32,
    /// Linear max distance; rotational sin(max angle).
    pub tmax: f32,
    pub tmin: f32,
    pub min_pt_dist: f32,
    pub max_cos: f32,
    pub flag: i32,
}

/// `unproj_result` (48 bytes).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct UnprojResult {
    /// Linear: t. Rotational: sin φ.
    pub t: f64,
    /// Rotational: cos φ.
    pub cos_t: f64,
    pub pt: Vec3,
    /// Points toward A.
    pub n: Vec3,
    pub i_feature: [i32; 2],
}
