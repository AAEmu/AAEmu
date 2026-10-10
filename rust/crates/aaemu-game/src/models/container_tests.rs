//! Port of AAEmu.UnitTests/Game/Models/Game/Items/Containers/InventoryTests.cs
//!
//! Includes the test helpers it uses: `Utils/InventoryTestUtils.cs` (`MockItem`),
//! `Utils/Mocks/ItemMock.cs` and `Utils/Mocks/CharacterMock.cs`. The C# mocks carry their
//! `ItemTemplate` on the item; the Rust container resolves templates through `ItemData`, so the
//! mock template is registered there.

use crate::data::items::{bind_type, ItemData, ItemTemplate};
use crate::models::container::{slot_type, ContainerKind, ItemContainer};
use crate::models::item::Item;
use crate::models::item_task::{task_type, ItemFxList};

/// `CharacterMock` (a `Character` with no connection; only its id is used here).
struct CharacterMock {
    id: u32,
}

impl CharacterMock {
    fn new() -> Self {
        // `new CharacterMock()`: `Character(null)` leaves `Id` at 0.
        Self { id: 0 }
    }
}

/// `ItemMock(id, template, count = 1)`: a base `Item` with the given template.
fn item_mock(id: u64, template: &ItemTemplate, count: i32) -> Item {
    Item::with_template(1, id, template, count)
}

/// `InventoryTestUtils.MockItem(id, templateId)`: a `BindType.Normal` template. Returns the
/// template too (registered in `ItemData` by the caller).
fn mock_item(id: u64, template_id: u32) -> (Item, ItemTemplate) {
    let template = ItemTemplate {
        bind_type: bind_type::NORMAL,
        ..ItemTemplate::new(template_id, crate::data::items::TemplateKind::Item)
    };
    (item_mock(id, &template, 1), template)
}

#[test]
fn inventory_adds_item() {
    let mock_character = CharacterMock::new();
    // new ItemContainer(mockCharacter.Id, SlotType.Inventory, false, mockCharacter)
    let mut container = ItemContainer::new(
        ContainerKind::Item,
        mock_character.id,
        slot_type::INVENTORY,
        0,
    );
    container.has_owner = true;
    let (item, template) = mock_item(1, 1);
    let mut data = ItemData::default();
    data.templates.insert(template.id, template);

    let mut fx = ItemFxList::default();
    assert_eq!(
        container.add_item(task_type::GM, item, 1, &data, &mut fx),
        Ok(true)
    );

    let found: Vec<&Item> = container
        .items
        .iter()
        .filter(|it| it.template_id == 1)
        .collect();
    assert_eq!(found.len(), 1, "SingleOrDefault");
}
