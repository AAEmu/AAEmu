//! Port of AAEmu.UnitTests/Game/Core/Managers/PortalManagerTests.cs,
//! AAEmu.UnitTests/Game/Core/Managers/UnitManagers/NpcManagerTests.cs,
//! AAEmu.UnitTests/Game/Core/Managers/SubZoneManagerTests.cs and
//! AAEmu.UnitTests/Game/Core/Managers/World/ExampleTests.cs (`SpecialtyManager.GetValueOfOne`).
//!
//! The C# `Constructor_DoesNotCallDeps` tests check that the DI constructors touch none of
//! their injected managers. The Rust managers have no injected dependencies, so these assert
//! that construction has no side effects: the new manager's observable state is empty.

use super::npc::data::NpcTemplate;
use super::npc::manager::{NpcManager, TemplateLook};
use super::portal::PortalManager;
use super::specialty::SpecialtyManager;
use super::subzone::SubZoneData;
use crate::data::client_files::ClientFileManager;
use crate::data::world_data::ZoneData;
use crate::data::world_templates::WorldTemplates;

/// `PortalManagerTests.Constructor_DoesNotCallDeps`.
#[test]
fn portal_manager_constructor_does_not_call_deps() {
    let manager = PortalManager::default();
    assert!(manager.pending_removals(1).is_none());
}

/// `NpcManagerTests.Constructor_DoesNotCallDeps`: no `LoadCustom` look was written back, so a
/// template's look is the template's own.
#[test]
fn npc_manager_constructor_does_not_call_deps() {
    let manager = NpcManager::default();
    let template = NpcTemplate {
        id: 7,
        hair_id: 3,
        ..NpcTemplate::default()
    };
    assert_eq!(
        manager.template_look(&template),
        TemplateLook {
            hair_id: template.hair_id,
            model_params: template.model_params.clone(),
            body_items: template.body_items,
        }
    );
}

/// `SubZoneManagerTests.Load_CallsGetWorlds`: `Load` walks `GetAllWorldTemplates()` (here: no
/// templates) and produces no areas.
#[test]
fn load_calls_get_worlds() {
    let worlds = WorldTemplates::default();
    let d = SubZoneData::load_from(&ClientFileManager::new(), &worlds, &ZoneData::default())
        .expect("load");
    assert!(d.sub_zones.is_empty());
    assert!(d.housing_zones.is_empty());
}

/// `ExampleTests.SampleTest`.
#[test]
fn sample_test() {
    assert_eq!(SpecialtyManager::get_value_of_one(), 1);
}
