//! Port of `AAEmu.Game.Models.Game.Features.FeatureSet` / `Feature` and
//! `FeaturesManager.Initialize`.

use std::fmt;

use aaemu_commons::{Marshal, PacketStream};

/// `Feature` bit indexes (byte = value / 8, bit = value % 8). Only named members are listed;
/// `flag_x_y` placeholders are omitted.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum Feature {
    Siege = 0,
    AllowFamilyChanges = 1,
    HouseSale = 3,
    Premium = 4,
    Nations = 6,
    Ranking = 36,
    Ingamecashshop = 38,
    CustomUiButton = 39,
    Customsaveload = 40,
    BmMileage = 43,
    AaPoint = 44,
    ItemSecure = 45,
    Secondpass = 46,
    SlaveCustomize = 48,
    BeautyshopBypass = 52,
    FreeLpRaise = 53,
    IngameshopSecondpass = 55,
    BackpackProfitShare = 56,
    SensitiveOperation = 58,
    TaxItem = 59,
    Achievement = 63,
    DwarfWarborn = 74,
    MailCoolTime = 75,
    HudAuctionButton = 81,
    AuctionPostBuff = 83,
    HouseTaxPrepay = 84,
}

/// Default fset bytes (`FeatureSet._fset`).
pub const DEFAULT_FSET: [u8; 11] = [
    0x11, 0x37, 0x0F, 0x0F, 0x79, 0x69, 0xb3, 0x8d, 0x32, 0x0c, 0x1a,
];
const PLAYER_LEVEL_LIMIT_INDEX: usize = 1;
const MATE_LEVEL_LIMIT_INDEX: usize = 8;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FeatureSet {
    fset: [u8; 11],
}

impl Default for FeatureSet {
    fn default() -> Self {
        Self { fset: DEFAULT_FSET }
    }
}

impl FeatureSet {
    /// `FeaturesManager.Initialize()`.
    pub fn initialized(max_player_level: u8, max_mate_level: u8) -> Self {
        let mut f = Self::default();
        f.set_player_level_limit(max_player_level);
        f.set_mate_level_limit(max_mate_level);
        f.set(Feature::HouseSale, true);
        f.set(Feature::Nations, true);
        f.set(Feature::AllowFamilyChanges, true);
        f.set(Feature::DwarfWarborn, false);
        f.set(Feature::SensitiveOperation, false);
        f.set(Feature::Secondpass, false);
        f.set(Feature::IngameshopSecondpass, false);
        f.set(Feature::ItemSecure, false);
        f.set(Feature::CustomUiButton, true);
        f
    }

    fn index(feature: Feature) -> (usize, u8) {
        let v = feature as u8;
        (usize::from(v / 8), v % 8)
    }

    pub fn check(&self, feature: Feature) -> bool {
        let (byte, bit) = Self::index(feature);
        self.fset[byte] & (1 << bit) != 0
    }

    pub fn set(&mut self, feature: Feature, enabled: bool) -> bool {
        let (byte, bit) = Self::index(feature);
        let Some(b) = self.fset.get_mut(byte) else {
            return false;
        };
        if enabled {
            *b |= 1 << bit;
        } else {
            *b &= !(1 << bit);
        }
        true
    }

    pub fn player_level_limit(&self) -> u8 {
        self.fset[PLAYER_LEVEL_LIMIT_INDEX]
    }
    pub fn set_player_level_limit(&mut self, v: u8) {
        self.fset[PLAYER_LEVEL_LIMIT_INDEX] = v;
    }
    pub fn mate_level_limit(&self) -> u8 {
        self.fset[MATE_LEVEL_LIMIT_INDEX]
    }
    pub fn set_mate_level_limit(&mut self, v: u8) {
        self.fset[MATE_LEVEL_LIMIT_INDEX] = v;
    }

    pub fn bytes(&self) -> &[u8; 11] {
        &self.fset
    }
}

impl Marshal for FeatureSet {
    /// `FeatureSet.Write`: `u16`-length-prefixed byte array.
    fn write(&self, s: &mut PacketStream) {
        s.write_bytes_sized(&self.fset);
    }
}

impl fmt::Display for FeatureSet {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        for b in &self.fset {
            write!(f, "{b:02x} ")?;
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn initialized_bytes() {
        let f = FeatureSet::initialized(55, 50);
        // byte0 0x11 | houseSale(bit3) | nations(bit6) | allowFamilyChanges(bit1) = 0x5B
        // byte4 0x79 | customUiButton(bit7) = 0xF9
        // byte5 0x69 & ~itemSecure(bit5) & ~secondpass(bit6) = 0x09
        // byte6 0xb3 & ~ingameshopSecondpass(bit7) = 0x33
        // byte7 0x8d & ~sensitiveOperation(bit2) = 0x89
        // byte9 0x0c & ~dwarfWarborn(bit2) = 0x08
        assert_eq!(
            f.bytes(),
            &[0x5B, 55, 0x0F, 0x0F, 0xF9, 0x09, 0x33, 0x89, 50, 0x08, 0x1a]
        );
    }
}
