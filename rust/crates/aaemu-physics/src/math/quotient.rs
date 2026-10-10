//! `quotientf`: a ratio `x / y` compared without dividing.
//!
//! The intersection, unprojection and grid-traversal code keep parameters such as hit times as
//! `(numerator, denominator)` pairs and compare them by cross-multiplication. The binary adds a
//! tie-breaker of `(a.x − b.x) · 1e-20` to every comparison, e.g. in the ray grid walk 0x351AD6A0:
//! `(q2.y·q1.x − q1.y·q2.x) + (q1.x − q2.x)·9.9999997e-21 <= 0` selects `q2`.
//! Comparisons are only meaningful with non-negative denominators ([`Quotient::fixsign`]).

use super::scalar::{sgnnz, QUOTIENT_EPS};

/// `x / y` kept as a pair.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Quotient {
    pub x: f32,
    pub y: f32,
}

impl Default for Quotient {
    /// `0 / 1`.
    fn default() -> Self {
        Self { x: 0.0, y: 1.0 }
    }
}

impl Quotient {
    #[inline]
    #[must_use]
    pub const fn new(x: f32, y: f32) -> Self {
        Self { x, y }
    }

    /// The comparison value `(a.x·b.y − b.x·a.y) + (a.x − b.x)·1e-20`; negative means `a < b`
    /// (for non-negative denominators).
    #[inline]
    #[must_use]
    pub fn cmp_value(self, b: Quotient) -> f32 {
        (self.x * b.y - b.x * self.y) + (self.x - b.x) * QUOTIENT_EPS
    }

    #[inline]
    #[must_use]
    pub fn lt(self, b: Quotient) -> bool {
        self.cmp_value(b) < 0.0
    }

    #[inline]
    #[must_use]
    pub fn le(self, b: Quotient) -> bool {
        self.cmp_value(b) <= 0.0
    }

    #[inline]
    #[must_use]
    pub fn gt(self, b: Quotient) -> bool {
        self.cmp_value(b) > 0.0
    }

    #[inline]
    #[must_use]
    pub fn ge(self, b: Quotient) -> bool {
        self.cmp_value(b) >= 0.0
    }

    /// `max` as the binary selects it: `if a.cmp_value(b) <= 0 { b } else { a }`.
    #[inline]
    #[must_use]
    pub fn max(self, b: Quotient) -> Quotient {
        if self.le(b) {
            b
        } else {
            self
        }
    }

    /// `min`: `if a.cmp_value(b) >= 0 { b } else { a }`.
    #[inline]
    #[must_use]
    pub fn min(self, b: Quotient) -> Quotient {
        if self.ge(b) {
            b
        } else {
            self
        }
    }

    /// Make the denominator non-negative by multiplying both parts by `sgnnz(y)`.
    #[inline]
    #[must_use]
    pub fn fixsign(self) -> Quotient {
        let s = sgnnz(self.y) as f32;
        Quotient::new(self.x * s, self.y * s)
    }

    /// The value, with `0` for a zero denominator (`y == 0 ? 0 : x / y`, as in 0x351AD6A0).
    #[inline]
    #[must_use]
    pub fn val(self) -> f32 {
        if self.y == 0.0 {
            0.0
        } else {
            self.x / self.y
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn compare() {
        let a = Quotient::new(1.0, 4.0);
        let b = Quotient::new(1.0, 2.0);
        assert!(a.lt(b) && b.gt(a));
        assert_eq!(a.max(b), b);
        assert_eq!(a.min(b), a);
        // equal ratios: the 1e-20 tie-breaker orders by numerator
        let c = Quotient::new(2.0, 8.0);
        assert!(a.lt(c));
        assert_eq!(a.max(a), a);
        assert_eq!(Quotient::new(3.0, -6.0).fixsign(), Quotient::new(-3.0, 6.0));
        assert_eq!(Quotient::new(-0.0, -0.0).fixsign().y, 0.0);
        assert_eq!(Quotient::new(3.0, 0.0).val(), 0.0);
        assert_eq!(Quotient::new(3.0, 6.0).val(), 0.5);
        assert_eq!(Quotient::default().val(), 0.0);
    }
}
