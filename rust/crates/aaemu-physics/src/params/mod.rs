//! The parameter interface: `pe_params_*`, `pe_status_*`, `pe_action_*` and `pe_geomparams*`
//! with their **x64 field layouts** and "unused" defaults.
//!
//! Every struct here is a plain Rust struct whose fields carry the engine's sentinels
//! ([`crate::sentinel`]); [`PeStruct::write`] / [`PeStruct::read`] convert it to and from the
//! exact byte image the DLL reads (type id at +0, little endian, pointers 8 bytes). The byte
//! images are what the oracle consumes (`params … hex=`), so these layouts are tested against the
//! real engine. The engine API built in later phases (P3/P4) takes these structs (or typed
//! wrappers of them) and treats sentinel fields as `None`.
//!
//! Layout sources: `entities.md` §0–§5, `dynamics.md` §1.4–§1.5, `world.md` §6–§7. Sizes are the
//! DLL's own tables ([`crate::ids::PARAMS_SIZES`] etc.); status structs have no size table, so
//! their size is the end of the last known field rounded to 8. Fields not listed in a struct
//! are written as zero (their meaning is not mapped); names marked *(inferred)* in the source
//! notes keep that status.

pub mod action;
pub mod geomparams;
pub mod pe_params;
pub mod status;

use crate::math::{Mat33, Quat, Vec3};
use crate::sentinel::RawPtr;

pub use action::*;
pub use geomparams::*;
pub use pe_params::*;
pub use status::*;

/// Which family a struct belongs to (they share the `int type` header but not the id space).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PeKind {
    Params,
    Status,
    Action,
    GeomParams,
}

/// One field of a layout.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct FieldInfo {
    pub offset: usize,
    pub size: usize,
    pub name: &'static str,
}

/// A parameter struct with a fixed x64 byte layout.
pub trait PeStruct: Sized + Default {
    const KIND: PeKind;
    /// The `type` written at +0.
    const TYPE_ID: i32;
    /// Size in bytes (the engine's table value where one exists).
    const SIZE: usize;
    /// The mapped fields, in declaration order (excluding the type header).
    const FIELDS: &'static [FieldInfo];

    /// Write the byte image into `out[..SIZE]` (unmapped bytes are zeroed).
    fn write(&self, out: &mut [u8]);
    /// Read a byte image (the type id is not checked).
    fn read(b: &[u8]) -> Self;

    /// The byte image as a vector.
    fn to_bytes(&self) -> Vec<u8> {
        let mut v = vec![0u8; Self::SIZE];
        self.write(&mut v);
        v
    }

    /// The byte image as lowercase hex (oracle `hex=` argument).
    fn to_hex(&self) -> String {
        self.to_bytes().iter().map(|b| format!("{b:02x}")).collect()
    }
}

/// A field type with a fixed little-endian encoding.
pub trait LayoutField: Copy {
    const SIZE: usize;
    fn put(&self, out: &mut [u8]);
    fn take(b: &[u8]) -> Self;
}

impl LayoutField for f32 {
    const SIZE: usize = 4;
    fn put(&self, out: &mut [u8]) {
        out[..4].copy_from_slice(&self.to_bits().to_le_bytes());
    }
    fn take(b: &[u8]) -> Self {
        f32::from_bits(u32::from_le_bytes(b[..4].try_into().expect("4 bytes")))
    }
}
impl LayoutField for i32 {
    const SIZE: usize = 4;
    fn put(&self, out: &mut [u8]) {
        out[..4].copy_from_slice(&self.to_le_bytes());
    }
    fn take(b: &[u8]) -> Self {
        i32::from_le_bytes(b[..4].try_into().expect("4 bytes"))
    }
}
impl LayoutField for u32 {
    const SIZE: usize = 4;
    fn put(&self, out: &mut [u8]) {
        out[..4].copy_from_slice(&self.to_le_bytes());
    }
    fn take(b: &[u8]) -> Self {
        u32::from_le_bytes(b[..4].try_into().expect("4 bytes"))
    }
}
impl LayoutField for u8 {
    const SIZE: usize = 1;
    fn put(&self, out: &mut [u8]) {
        out[0] = *self;
    }
    fn take(b: &[u8]) -> Self {
        b[0]
    }
}
impl LayoutField for RawPtr {
    const SIZE: usize = 8;
    fn put(&self, out: &mut [u8]) {
        out[..8].copy_from_slice(&self.0.to_le_bytes());
    }
    fn take(b: &[u8]) -> Self {
        RawPtr(u64::from_le_bytes(b[..8].try_into().expect("8 bytes")))
    }
}
impl LayoutField for Vec3 {
    const SIZE: usize = 12;
    fn put(&self, out: &mut [u8]) {
        self.x.put(out);
        self.y.put(&mut out[4..]);
        self.z.put(&mut out[8..]);
    }
    fn take(b: &[u8]) -> Self {
        Vec3::new(f32::take(b), f32::take(&b[4..]), f32::take(&b[8..]))
    }
}
impl LayoutField for Quat {
    const SIZE: usize = 16;
    fn put(&self, out: &mut [u8]) {
        for (i, v) in self.to_array().iter().enumerate() {
            v.put(&mut out[4 * i..]);
        }
    }
    fn take(b: &[u8]) -> Self {
        Quat::new(
            f32::take(b),
            f32::take(&b[4..]),
            f32::take(&b[8..]),
            f32::take(&b[12..]),
        )
    }
}
impl LayoutField for Mat33 {
    const SIZE: usize = 36;
    fn put(&self, out: &mut [u8]) {
        for (i, v) in self.to_array().iter().enumerate() {
            v.put(&mut out[4 * i..]);
        }
    }
    fn take(b: &[u8]) -> Self {
        let mut a = [0f32; 9];
        for (i, v) in a.iter_mut().enumerate() {
            *v = f32::take(&b[4 * i..]);
        }
        Mat33::from_array(a)
    }
}
impl<T: LayoutField, const N: usize> LayoutField for [T; N] {
    const SIZE: usize = T::SIZE * N;
    fn put(&self, out: &mut [u8]) {
        for (i, v) in self.iter().enumerate() {
            v.put(&mut out[T::SIZE * i..]);
        }
    }
    fn take(b: &[u8]) -> Self {
        std::array::from_fn(|i| T::take(&b[T::SIZE * i..]))
    }
}

/// Defines a parameter struct: fields with offsets, types, defaults and docs.
macro_rules! pe_struct {
    (
        $(#[$m:meta])*
        $name:ident : $kind:ident ($id:expr, size $size:expr) {
            $( $(#[$fm:meta])* $off:literal => $field:ident : $ty:ty = $def:expr, )*
        }
    ) => {
        $(#[$m])*
        #[derive(Clone, Copy, Debug)]
        pub struct $name {
            $( $(#[$fm])* pub $field: $ty, )*
        }

        impl Default for $name {
            fn default() -> Self {
                Self { $( $field: $def, )* }
            }
        }

        impl $crate::params::PeStruct for $name {
            const KIND: $crate::params::PeKind = $crate::params::PeKind::$kind;
            const TYPE_ID: i32 = $id;
            const SIZE: usize = $size;
            const FIELDS: &'static [$crate::params::FieldInfo] = &[
                $( $crate::params::FieldInfo {
                    offset: $off,
                    size: <$ty as $crate::params::LayoutField>::SIZE,
                    name: stringify!($field),
                }, )*
            ];

            #[allow(unused_variables)]
            fn write(&self, out: &mut [u8]) {
                out[..$size].fill(0);
                $crate::params::LayoutField::put(&($id as i32), out);
                $( $crate::params::LayoutField::put(&self.$field, &mut out[$off..]); )*
            }

            #[allow(unused_variables)]
            fn read(b: &[u8]) -> Self {
                Self { $( $field: $crate::params::LayoutField::take(&b[$off..]), )* }
            }
        }
    };
}
pub(crate) use pe_struct;

/// Checks that a layout is sorted, non-overlapping, after the type header and inside `SIZE`.
/// Used by the layout tests of every struct.
#[must_use]
pub fn layout_problems<T: PeStruct>() -> Vec<String> {
    let mut problems = Vec::new();
    let mut end = 4;
    for f in T::FIELDS {
        if f.offset < end {
            problems.push(format!(
                "{} at {} overlaps the previous field (ends at {end})",
                f.name, f.offset
            ));
        }
        end = f.offset + f.size;
        if end > T::SIZE {
            problems.push(format!("{} ends at {end} > size {}", f.name, T::SIZE));
        }
    }
    problems
}
