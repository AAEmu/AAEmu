//! World policy for territory property tax rules absent from the available 1.2 server source.

use serde::Deserialize;

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum DominionPropertyTaxBase {
    /// Charge the territory percentage on the weekly amount after multiple-property tax.
    #[default]
    AfterHeavy,
    /// Charge the territory percentage only on the template's ordinary base tax.
    Base,
}

impl crate::config::ConfigEnum for DominionPropertyTaxBase {
    fn parse_config(value: &str) -> Option<Self> {
        if value.eq_ignore_ascii_case("AfterHeavy") || value == "0" {
            Some(Self::AfterHeavy)
        } else if value.eq_ignore_ascii_case("Base") || value == "1" {
            Some(Self::Base)
        } else {
            None
        }
    }
}

#[derive(Debug, Clone, Default, Deserialize)]
#[serde(default)]
pub struct HousingConfig {
    #[serde(
        rename = "dominionpropertytaxbase",
        deserialize_with = "crate::config::lenient_enum"
    )]
    pub dominion_property_tax_base: DominionPropertyTaxBase,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_and_explicit_property_tax_policy_follow_the_configuration_binder() {
        let default = crate::config::AppConfig::from_value(serde_json::json!({})).unwrap();
        assert_eq!(
            default.housing.dominion_property_tax_base,
            DominionPropertyTaxBase::AfterHeavy
        );
        for value in [serde_json::json!("base"), serde_json::json!(1)] {
            let config = crate::config::AppConfig::from_value(serde_json::json!({
                "Housing": { "DominionPropertyTaxBase": value }
            }))
            .unwrap();
            assert_eq!(
                config.housing.dominion_property_tax_base,
                DominionPropertyTaxBase::Base
            );
        }
        assert!(crate::config::AppConfig::from_value(serde_json::json!({
            "Housing": { "DominionPropertyTaxBase": "unknown" }
        }))
        .is_err());
    }
}
