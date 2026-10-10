//! Port of `AAEmu.Commons.Utils.TimeSpanExtensions`.

/// `IsBetween`: whether `time` lies in `[start_time, end_time]`, where a range with
/// `end_time < start_time` wraps around (e.g. 22:00 - 02:00) and `start_time == end_time`
/// means "always".
pub fn is_between<T: PartialOrd>(time: T, start_time: T, end_time: T) -> bool {
    if end_time == start_time {
        return true;
    }
    if end_time < start_time {
        return time <= end_time || time >= start_time;
    }
    time >= start_time && time <= end_time
}

/// Method syntax for [`is_between`] on `chrono::TimeDelta` (C# `TimeSpan`) and
/// `std::time::Duration`.
pub trait TimeSpanExtensions: PartialOrd + Sized {
    fn is_between(&self, start_time: Self, end_time: Self) -> bool {
        is_between(self, &start_time, &end_time)
    }
}

impl TimeSpanExtensions for chrono::TimeDelta {}
impl TimeSpanExtensions for std::time::Duration {}

/// Tests ported from `AAEmu.UnitTests/Commons/Utils/TimeSpanExtensionsTests.cs`.
#[cfg(test)]
mod tests {
    use chrono::TimeDelta;

    use super::*;

    fn h(hours: i64) -> TimeDelta {
        TimeDelta::hours(hours)
    }

    #[test]
    fn is_between_should_return_true_when_time_is_between_start_and_end() {
        assert!(h(12).is_between(h(6), h(18)));
    }

    #[test]
    fn is_between_should_return_true_when_time_equals_start() {
        assert!(h(6).is_between(h(6), h(18)));
    }

    #[test]
    fn is_between_should_return_true_when_time_equals_end() {
        assert!(h(18).is_between(h(6), h(18)));
    }

    #[test]
    fn is_between_should_return_false_when_time_is_outside_range() {
        assert!(!h(3).is_between(h(6), h(18)));
    }

    #[test]
    fn is_between_should_return_true_when_end_time_less_than_start_time_and_time_is_in_first_range()
    {
        assert!(h(23).is_between(h(22), h(2)));
    }

    #[test]
    fn is_between_should_return_true_when_end_time_less_than_start_time_and_time_is_in_second_range(
    ) {
        assert!(h(1).is_between(h(22), h(2)));
    }

    #[test]
    fn is_between_should_return_false_when_end_time_less_than_start_time_and_time_is_in_middle_gap()
    {
        assert!(!h(12).is_between(h(22), h(2)));
    }

    #[test]
    fn is_between_should_return_true_when_start_time_equals_end_time() {
        assert!(h(12).is_between(h(12), h(12)));
    }

    #[test]
    fn is_between_should_handle_zero_hours() {
        for (hours, start, end) in [(0, 0, 10), (5, 0, 10), (10, 0, 10)] {
            assert!(h(hours).is_between(h(start), h(end)));
        }
    }

    #[test]
    fn std_duration() {
        use std::time::Duration;
        assert!(Duration::from_secs(5).is_between(Duration::from_secs(1), Duration::from_secs(9)));
    }
}
