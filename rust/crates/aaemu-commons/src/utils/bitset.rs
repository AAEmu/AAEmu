//! Port of `AAEmu.Commons.Utils.BitSet`: a fixed-size set of bits.

/// Fixed-size bit set (C# `BitSet`, backed by a `BitArray`).
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct BitSet {
    words: Vec<u32>,
    count: usize,
}

impl BitSet {
    pub fn new(count: usize) -> Self {
        Self {
            words: vec![0; count.div_ceil(32)],
            count,
        }
    }

    /// Number of bits (C# `Count`).
    pub fn count(&self) -> usize {
        self.count
    }

    /// Clears all bits (C# `Clear()`).
    pub fn clear_all(&mut self) {
        self.words.fill(0);
    }

    /// Clears one bit (C# `Clear(int)`). Panics if out of range (C#:
    /// `ArgumentOutOfRangeException`).
    pub fn clear(&mut self, index: usize) {
        self.check(index);
        self.words[index / 32] &= !(1 << (index % 32));
    }

    pub fn set(&mut self, index: usize) {
        self.check(index);
        self.words[index / 32] |= 1 << (index % 32);
    }

    pub fn get(&self, index: usize) -> bool {
        self.check(index);
        self.words[index / 32] & (1 << (index % 32)) != 0
    }

    fn check(&self, index: usize) {
        assert!(
            index < self.count,
            "BitSet index {index} out of range (count {})",
            self.count
        );
    }

    /// First set bit at or after `start_from`, or `None` (C#: `-1`).
    pub fn next_set(&self, start_from: usize) -> Option<usize> {
        (start_from..self.count).find(|&i| self.get(i))
    }

    /// First clear bit at or after `start_from`, or `None` (C#: `-1`).
    pub fn next_clear(&self, start_from: usize) -> Option<usize> {
        (start_from..self.count).find(|&i| !self.get(i))
    }

    /// Bitwise OR with `other` (over `other.count()` bits).
    ///
    /// Note: the C# `Or` *assigned* `other`'s bits (`_bits[i] = other[i]`), clearing bits that
    /// are set here but not in `other`. This is a real OR.
    pub fn or(&mut self, other: &BitSet) {
        for i in 0..other.count.min(self.count) {
            if other.get(i) {
                self.set(i);
            }
        }
    }

    /// Bits packed into 32-bit words, LSB first (C# `ToIntArray`). The C# allocated
    /// `Count / 32` words, which made `BitArray.CopyTo` throw when `Count` wasn't a multiple of
    /// 32; here the length is rounded up.
    pub fn to_int_array(&self) -> Vec<i32> {
        self.words.iter().map(|&w| w as i32).collect()
    }

    /// Bits packed into bytes, LSB first (C# `ToByteArray`; length rounded up, see
    /// [`BitSet::to_int_array`]).
    pub fn to_byte_array(&self) -> Vec<u8> {
        let mut out: Vec<u8> = self.words.iter().flat_map(|w| w.to_le_bytes()).collect();
        out.truncate(self.count.div_ceil(8));
        out
    }
}

impl std::ops::Index<usize> for BitSet {
    type Output = bool;
    fn index(&self, index: usize) -> &bool {
        if self.get(index) {
            &true
        } else {
            &false
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn set_get_scan() {
        let mut b = BitSet::new(70);
        assert_eq!(b.count(), 70);
        assert_eq!(b.next_set(0), None);
        assert_eq!(b.next_clear(0), Some(0));
        b.set(3);
        b.set(64);
        assert!(b.get(3) && b[64] && !b[4]);
        assert_eq!(b.next_set(0), Some(3));
        assert_eq!(b.next_set(4), Some(64));
        assert_eq!(b.next_set(65), None);
        assert_eq!(b.next_set(100), None);
        b.clear(3);
        assert_eq!(b.next_set(0), Some(64));
        let mut full = BitSet::new(5);
        (0..5).for_each(|i| full.set(i));
        assert_eq!(full.next_clear(0), None);
        assert_eq!(b.to_int_array(), vec![0, 0, 1]);
        assert_eq!(b.to_byte_array().len(), 9);
        assert_eq!(b.to_byte_array()[8], 1);
        b.clear_all();
        assert_eq!(b.next_set(0), None);
    }

    #[test]
    fn or_keeps_existing_bits() {
        let mut a = BitSet::new(8);
        a.set(0);
        let mut o = BitSet::new(8);
        o.set(1);
        a.or(&o);
        assert!(a[0] && a[1]);
    }

    #[test]
    #[should_panic]
    fn out_of_range_panics() {
        BitSet::new(4).set(4);
    }
}
