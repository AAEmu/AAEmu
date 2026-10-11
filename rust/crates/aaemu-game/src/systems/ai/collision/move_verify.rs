//! The zone's movement verifier (`ZWReportMoveHack`, type 57): x2game `sub_3921E300` (entry,
//! from the WZUnitMovement handler 0x39362E80), samples `sub_39218810` (character) /
//! `sub_39219740` (vehicle), report `sub_3921BD10`. It runs only with
//! `movement_verify_enable` (CGame +1156, default 0) → `Physics.MovementVerifyEnable`
//! (default off).
//!
//! Ported checks (the ones that are arithmetic on the movement stream plus
//! `GetTerrainElevation`; `ZONE-PHYSICS-USAGE.md` §1.3, `zone_protocol_physics.md` §1):
//!
//! | reason | check | reliability |
//! |---|---|---|
//! | 0 | client time going backwards (0x3921C4C0) | 100 |
//! | 11 | under terrain: `terrainZ − z > 0.1`, `count·80 / samples` (0x3921D720) | computed |
//! | 12 | `z < movement_verify_min_z_pos` (−1) (0x3921D720) | `min(10·count, 100)` |
//! | 15 / 16 / 17 | teleport: 2-D speed > 150 / 100 / 50 m/s over > 50 / 25 / 10 m (0x3921C0A0), ≥ 5000 ms between reports | 90 / 80 / 50 |
//! | 19 | vehicle warp: driver move > 500 m (`d² > 250000`, 0x3924B550) | 50 |
//!
//! Not ported (need the zone actor's own simulation): 1 clock rate, 2 on-ground, 3 gravity,
//! 4 air-standing, 6 speed windows, 9 packet rate, 10 over-velocity, 14 pattern analysis,
//! 18 vehicle levitation. AAEmu has no consumer for the report: it is logged (warn) and
//! counted.

use std::sync::atomic::{AtomicU64, Ordering};

use dashmap::DashMap;

/// `movement_verify_min_z_pos`.
pub const MIN_Z_POS: f32 = -1.0;
/// Under-terrain tolerance (`terrainZ − z > 0.1`).
pub const UNDER_TERRAIN_TOLERANCE: f32 = 0.1;
/// `movement_verify_sample_max`.
pub const SAMPLE_MAX: u32 = 200;
/// Minimum time between teleport reports.
pub const TELEPORT_REPORT_INTERVAL_MS: i64 = 5000;
/// Vehicle warp (`dist² > 250000`).
pub const VEHICLE_WARP_DIST2: f32 = 250_000.0;

/// A ZWReportMoveHack.
#[derive(Debug, Clone, PartialEq)]
pub struct MoveHackReport {
    pub reason: i32,
    pub obj_id: u32,
    pub text: String,
    pub reliability: u8,
    pub extra: f32,
}

/// One movement sample (world position, client / server times).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct MoveSample {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub client_ms: i64,
    pub server_ms: i64,
    /// Terrain elevation under the position (`GetTerrainElevation`, 0 = unknown).
    pub terrain_z: f32,
    /// The sample is a teleport / ship / climb / forced sync (skipped by the warp checks).
    pub exempt: bool,
}

#[derive(Debug, Clone, Default)]
struct UnitState {
    last: Option<MoveSample>,
    samples: u32,
    under_terrain: u32,
    below_min_z: u32,
    last_teleport_report_ms: i64,
}

/// The per-unit verifiers.
#[derive(Debug, Default)]
pub struct MoveVerifier {
    units: DashMap<u32, UnitState>,
    pub reports: AtomicU64,
}

/// The teleport class of a move (`sub_3921C0A0`): reason and reliability.
pub fn teleport_class(dist2d: f32, dt_s: f32) -> Option<(i32, u8)> {
    if dt_s <= 0.0 {
        return None;
    }
    let speed = dist2d / dt_s;
    if speed > 150.0 && dist2d > 50.0 {
        Some((15, 90))
    } else if speed > 100.0 && dist2d > 25.0 {
        Some((16, 80))
    } else if speed > 50.0 && dist2d > 10.0 {
        Some((17, 50))
    } else {
        None
    }
}

impl MoveVerifier {
    /// Verifies a character movement sample; returns the reports it produces.
    pub fn verify(&self, obj_id: u32, s: MoveSample) -> Vec<MoveHackReport> {
        let mut out = Vec::new();
        let mut st = self.units.entry(obj_id).or_insert_with(|| UnitState {
            last_teleport_report_ms: i64::MIN / 2,
            ..UnitState::default()
        });
        let report = |reason: i32, text: String, reliability: u8, extra: f32| MoveHackReport {
            reason,
            obj_id,
            text,
            reliability,
            extra,
        };
        if let Some(prev) = st.last {
            if s.client_ms < prev.client_ms {
                out.push(report(
                    0,
                    format!("client time {} < {}", s.client_ms, prev.client_ms),
                    100,
                    (prev.client_ms - s.client_ms) as f32,
                ));
            }
            if !s.exempt && !prev.exempt {
                let d = ((s.x - prev.x).powi(2) + (s.y - prev.y).powi(2)).sqrt();
                let dt = (s.server_ms - prev.server_ms) as f32 / 1000.0;
                if let Some((reason, rel)) = teleport_class(d, dt) {
                    if s.server_ms - st.last_teleport_report_ms >= TELEPORT_REPORT_INTERVAL_MS {
                        st.last_teleport_report_ms = s.server_ms;
                        out.push(report(
                            reason,
                            format!("moved {d:.1} m in {dt:.3} s"),
                            rel,
                            d / dt,
                        ));
                    }
                }
            }
        }
        st.samples += 1;
        if s.terrain_z != 0.0 && s.terrain_z - s.z > UNDER_TERRAIN_TOLERANCE {
            st.under_terrain += 1;
        }
        if s.z < MIN_Z_POS {
            st.below_min_z += 1;
        }
        if st.samples >= SAMPLE_MAX {
            if st.under_terrain > 0 {
                let rel = (st.under_terrain * 80 / st.samples).min(100) as u8;
                out.push(report(
                    11,
                    format!("under terrain {}/{}", st.under_terrain, st.samples),
                    rel,
                    st.under_terrain as f32,
                ));
            }
            if st.below_min_z > 0 {
                out.push(report(
                    12,
                    format!("z < {MIN_Z_POS} {}/{}", st.below_min_z, st.samples),
                    (10 * st.below_min_z).min(100) as u8,
                    st.below_min_z as f32,
                ));
            }
            st.samples = 0;
            st.under_terrain = 0;
            st.below_min_z = 0;
        }
        st.last = Some(s);
        drop(st);
        for r in &out {
            self.reports.fetch_add(1, Ordering::Relaxed);
            tracing::warn!(
                "ZWReportMoveHack unit {} reason {} reliability {}: {}",
                r.obj_id,
                r.reason,
                r.reliability,
                r.text
            );
        }
        out
    }

    /// Vehicle warp (`sub_3924B550`): the driver moved the vehicle more than 500 m.
    pub fn verify_vehicle(
        &self,
        obj_id: u32,
        from: (f32, f32, f32),
        to: (f32, f32, f32),
    ) -> Option<MoveHackReport> {
        let d2 = (to.0 - from.0).powi(2) + (to.1 - from.1).powi(2) + (to.2 - from.2).powi(2);
        (d2 > VEHICLE_WARP_DIST2).then(|| {
            self.reports.fetch_add(1, Ordering::Relaxed);
            let r = MoveHackReport {
                reason: 19,
                obj_id,
                text: format!("vehicle warp {:.1} m", d2.sqrt()),
                reliability: 50,
                extra: d2.sqrt(),
            };
            tracing::warn!("ZWReportMoveHack unit {obj_id} reason 19: {}", r.text);
            r
        })
    }

    /// Forgets a unit (logout / teleport).
    pub fn reset(&self, obj_id: u32) {
        self.units.remove(&obj_id);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn s(x: f32, z: f32, ms: i64, terrain: f32) -> MoveSample {
        MoveSample {
            x,
            y: 0.0,
            z,
            client_ms: ms,
            server_ms: ms,
            terrain_z: terrain,
            exempt: false,
        }
    }

    #[test]
    fn teleport_classes() {
        assert_eq!(teleport_class(60.0, 0.2), Some((15, 90)));
        assert_eq!(teleport_class(30.0, 0.2), Some((16, 80)));
        assert_eq!(teleport_class(12.0, 0.2), Some((17, 50)));
        assert_eq!(teleport_class(8.0, 0.1), None);
        assert_eq!(teleport_class(5.0, 1.0), None);
    }

    #[test]
    fn verifier_reports() {
        let v = MoveVerifier::default();
        assert!(v.verify(1, s(0.0, 100.0, 1000, 99.0)).is_empty());
        let r = v.verify(1, s(100.0, 100.0, 1200, 99.0));
        assert_eq!(r[0].reason, 15);
        // within 5 s: no second teleport report
        assert!(v.verify(1, s(200.0, 100.0, 1400, 99.0)).is_empty());
        let r = v.verify(1, s(200.0, 100.0, 1300, 99.0));
        assert_eq!(r[0].reason, 0);
        // under terrain + below min z over a sample window
        let v = MoveVerifier::default();
        let mut all = Vec::new();
        for i in 0..SAMPLE_MAX {
            all.extend(v.verify(2, s(0.0, -2.0, 1000 + i64::from(i) * 100, 5.0)));
        }
        assert!(all.iter().any(|r| r.reason == 11 && r.reliability == 80));
        assert!(all.iter().any(|r| r.reason == 12 && r.reliability == 100));
        assert!(v
            .verify_vehicle(3, (0.0, 0.0, 0.0), (600.0, 0.0, 0.0))
            .is_some());
        assert!(v
            .verify_vehicle(3, (0.0, 0.0, 0.0), (400.0, 0.0, 0.0))
            .is_none());
    }
}
