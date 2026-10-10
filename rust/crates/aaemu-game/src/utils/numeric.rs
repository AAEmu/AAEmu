//! `Utils/NumericExtensions` (`DegToRad`, `RadToDeg`, the Jitter vector swaps, cell / path
//! indexes and offsets).

/// `double.DegToRad()`.
pub fn deg_to_rad(v: f64) -> f64 {
    std::f64::consts::PI / 180.0 * v
}

/// `float.DegToRad()`.
pub fn deg_to_rad_f32(v: f32) -> f32 {
    std::f32::consts::PI / 180.0 * v
}

/// `double.RadToDeg()`.
pub fn rad_to_deg(v: f64) -> f64 {
    v / std::f64::consts::PI * 180.0
}

/// `float.RadToDeg()`.
pub fn rad_to_deg_f32(v: f32) -> f32 {
    v / std::f32::consts::PI * 180.0
}

/// `JVector.ToVector()` / `Vector3.ToJVector()`: swaps Y and Z.
pub fn swap_yz(v: [f32; 3]) -> [f32; 3] {
    [v[0], v[2], v[1]]
}

/// `ToJVectorFix` / `ToVectorFix`: `(X, 0, Z)`.
pub fn flatten_y(v: [f32; 3]) -> [f32; 3] {
    [v[0], 0.0, v[2]]
}

/// `ToCellIndex(pos)` (1024 m cells).
pub fn to_cell_index(pos: [f32; 3]) -> (i32, i32) {
    (
        f64::from(pos[0] / 1024.0).floor() as i32,
        f64::from(pos[1] / 1024.0).floor() as i32,
    )
}

/// `ToPathsIndex(pos)` (256 m cells).
pub fn to_paths_index(pos: [f32; 3]) -> (i32, i32) {
    (
        f64::from(pos[0] / 256.0).floor() as i32,
        f64::from(pos[1] / 256.0).floor() as i32,
    )
}

/// `ToCellOffset(pos)`: `X - floor(X / 1024)` (the C# subtracts the cell index, not the cell
/// origin; mirrored).
pub fn to_cell_offset(pos: [f32; 3]) -> (f32, f32) {
    (
        pos[0] - f64::from(pos[0] / 1024.0).floor() as f32,
        pos[1] - f64::from(pos[1] / 1024.0).floor() as f32,
    )
}

/// `ToPathsOffset(pos)` (same quirk with 256).
pub fn to_paths_offset(pos: [f32; 3]) -> (f32, f32) {
    (
        pos[0] - f64::from(pos[0] / 256.0).floor() as f32,
        pos[1] - f64::from(pos[1] / 256.0).floor() as f32,
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cells() {
        assert_eq!(to_cell_index([2048.5, -1.0, 0.0]), (2, -1));
        assert_eq!(to_paths_index([512.0, 255.9, 0.0]), (2, 0));
        assert_eq!(to_cell_offset([2048.5, 10.0, 0.0]), (2046.5, 10.0));
        assert_eq!(swap_yz([1.0, 2.0, 3.0]), [1.0, 3.0, 2.0]);
        assert_eq!(flatten_y([1.0, 2.0, 3.0]), [1.0, 0.0, 3.0]);
        assert!((deg_to_rad(180.0) - std::f64::consts::PI).abs() < 1e-12);
    }
}
