//! Ports of `AAEmu.Commons.Utils.IEnumerableExtensions.RandomElementByWeight` and
//! `AAEmu.Commons.Utils.RandomExtensions`.
//!
//! Note: seeded generators produce different sequences than .NET's `System.Random`, so seeded
//! results don't match the C# server value-for-value.

use rand::Rng;

/// `RandomElementByWeight` for slices (and anything that derefs to one).
pub trait RandomElementByWeight<T> {
    /// Picks an element with probability proportional to `weight_selector(element)`, using the
    /// thread-local RNG. `None` for an empty sequence (C#: `default`).
    fn random_element_by_weight(&self, weight_selector: impl Fn(&T) -> f32) -> Option<&T> {
        self.random_element_by_weight_with(&mut rand::thread_rng(), weight_selector)
    }

    /// Same as [`RandomElementByWeight::random_element_by_weight`] with an explicit RNG.
    fn random_element_by_weight_with<R: Rng + ?Sized>(
        &self,
        rng: &mut R,
        weight_selector: impl Fn(&T) -> f32,
    ) -> Option<&T>;
}

impl<T> RandomElementByWeight<T> for [T] {
    fn random_element_by_weight_with<R: Rng + ?Sized>(
        &self,
        rng: &mut R,
        weight_selector: impl Fn(&T) -> f32,
    ) -> Option<&T> {
        let total_weight: f32 = self.iter().map(&weight_selector).sum();
        // The weight we are after (NextSingle: [0, 1))
        let item_weight_index = rng.gen::<f32>() * total_weight;
        let mut current_weight_index = 0.0f32;
        for item in self {
            current_weight_index += weight_selector(item);
            // If we've hit or passed the weight we are after for this item then it's the one
            if current_weight_index >= item_weight_index {
                return Some(item);
            }
        }
        None
    }
}

/// `RandomExtensions.Next(this Random, float, float)`.
pub trait RandomExt: Rng {
    /// A random `f32` in `[min_value, max_value)` (`min_value` when both are equal).
    fn next_f32_range(&mut self, min_value: f32, max_value: f32) -> f32 {
        self.gen::<f32>() * (max_value - min_value) + min_value
    }
}

impl<R: Rng + ?Sized> RandomExt for R {}

#[cfg(test)]
mod tests {
    use std::collections::HashSet;

    use rand::rngs::StdRng;
    use rand::SeedableRng;

    use super::*;

    struct TestItem {
        name: &'static str,
        weight: f32,
    }

    fn items(v: &[(&'static str, f32)]) -> Vec<TestItem> {
        v.iter()
            .map(|&(name, weight)| TestItem { name, weight })
            .collect()
    }

    // ---- RandomElementByWeightTests.cs ----

    #[test]
    fn random_element_by_weight_should_return_item_when_sequence_has_items() {
        let items = items(&[("A", 1.0), ("B", 1.0), ("C", 1.0)]);
        let r = items.random_element_by_weight(|i| i.weight).unwrap();
        assert_ne!(r.name, "");
    }

    #[test]
    fn random_element_by_weight_should_return_default_when_sequence_is_empty() {
        let items: Vec<TestItem> = Vec::new();
        assert!(items.random_element_by_weight(|i| i.weight).is_none());
    }

    #[test]
    fn random_element_by_weight_should_return_only_item_when_sequence_has_one_item() {
        let items = items(&[("OnlyOne", 10.0)]);
        assert_eq!(
            items.random_element_by_weight(|i| i.weight).unwrap().name,
            "OnlyOne"
        );
    }

    #[test]
    fn random_element_by_weight_should_consider_weights() {
        let items = items(&[("HighWeight", 100.0), ("LowWeight", 1.0)]);
        let high = (0..100)
            .filter(|_| items.random_element_by_weight(|j| j.weight).unwrap().name == "HighWeight")
            .count();
        assert!(high > 50);
    }

    #[test]
    fn random_element_by_weight_should_work_with_zero_weights() {
        let items = items(&[("ZeroWeight", 0.0), ("PositiveWeight", 10.0)]);
        // The zero-weight item can only be picked when the RNG returns exactly 0.0.
        let r = items.random_element_by_weight(|i| i.weight).unwrap();
        assert_eq!(r.name, "PositiveWeight");
    }

    #[test]
    fn random_element_by_weight_should_work_with_all_same_weights() {
        let items = items(&[("A", 5.0), ("B", 5.0), ("C", 5.0)]);
        // Seeded so the "all three appear within 50 draws" check is deterministic.
        let mut rng = StdRng::seed_from_u64(1);
        let results: HashSet<_> = (0..50)
            .filter_map(|_| items.random_element_by_weight_with(&mut rng, |j| j.weight))
            .map(|i| i.name)
            .collect();
        assert_eq!(results.len(), 3);
    }

    #[test]
    fn random_element_by_weight_should_work_with_nullable_types() {
        let items = [Some("A"), Some("B"), Some("C")];
        assert!(items.random_element_by_weight(|_| 1.0).is_some());
    }

    // ---- RandomExtensionsTests.cs ----

    #[test]
    fn next_should_return_value_in_range_when_given_valid_range() {
        let mut random = StdRng::seed_from_u64(12345);
        let r = random.next_f32_range(1.0, 10.0);
        assert!((1.0..=10.0).contains(&r));
    }

    #[test]
    fn next_should_return_min_value_when_random_returns_zero() {
        let mut random = StdRng::seed_from_u64(0);
        assert_eq!(random.next_f32_range(5.0, 5.0), 5.0);
    }

    #[test]
    fn next_should_return_value_greater_than_or_equal_to_min_value() {
        let mut random = StdRng::seed_from_u64(12345);
        assert!(random.next_f32_range(100.0, 200.0) >= 100.0);
    }

    #[test]
    fn next_should_return_value_less_than_max_value() {
        let mut random = StdRng::seed_from_u64(12345);
        assert!(random.next_f32_range(100.0, 200.0) < 200.0);
    }

    #[test]
    fn next_should_work_with_various_ranges() {
        let mut random = rand::thread_rng();
        for (min, max) in [(0.0, 1.0), (-10.0, 10.0), (100.0, 200.0), (0.0, 0.0001)] {
            let r = random.next_f32_range(min, max);
            assert!(r >= min && r <= max, "{r} not in [{min}, {max}]");
        }
    }

    #[test]
    fn next_should_return_same_sequence_when_using_same_seed() {
        let mut r1 = StdRng::seed_from_u64(42);
        let mut r2 = StdRng::seed_from_u64(42);
        for _ in 0..10 {
            assert_eq!(r1.next_f32_range(0.0, 100.0), r2.next_f32_range(0.0, 100.0));
        }
    }
}
