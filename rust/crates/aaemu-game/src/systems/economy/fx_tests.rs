//! Persistence effects must become visible before deferred callbacks can start another save.

use std::sync::{Arc, Mutex};

use crate::data::items::ItemTemplate;
use crate::models::item::Item;
use crate::models::item_task::{ItemFx, ItemFxList};
use crate::server::GameServer;
use crate::systems::economy::hooks::QuestItemHooks;
use crate::systems::economy::inventory::ItemTxn;
use crate::test_support::{spawn_player, test_character, test_server, TestClient};
use crate::world::PlayerRef;

struct ObservePersistence {
    removed: u64,
    loose: u64,
    events: Mutex<Vec<(bool, i32)>>,
}

impl ObservePersistence {
    fn observe(&self, server: &GameServer, acquired: bool, count: i32) {
        // A callback can begin another economy operation, so the original guard must be gone.
        let _guard = server.systems.economy.ops_lock();
        assert!(server.items.removed_items().contains(&self.removed));
        assert!(server.items.item_by_id(self.loose).is_some());
        self.events.lock().unwrap().push((acquired, count));
    }
}

impl QuestItemHooks for ObservePersistence {
    fn on_items_acquired(&self, server: &GameServer, _: &PlayerRef, _: u32, count: i32) {
        self.observe(server, true, count);
    }

    fn on_items_consumed(&self, server: &GameServer, _: &PlayerRef, _: u32, count: i32) {
        self.observe(server, false, count);
    }
}

fn loose_item(server: &GameServer, id: u64) -> Item {
    Item::create(
        id,
        &ItemTemplate::plain(500, 10),
        1,
        0,
        1,
        &server.game_data.items,
        77,
    )
}

fn queue_effects(txn: &mut ItemTxn<'_>, removed: u64, loose: u64) {
    txn.env.fx.push(ItemFx::Acquired {
        owner: 1,
        template_id: 500,
        count: 3,
    });
    txn.env.fx.push(ItemFx::ReleaseId(removed));
    txn.env.fx.push(ItemFx::Consumed {
        owner: 1,
        template_id: 500,
        count: 2,
    });
    txn.env
        .fx
        .push(ItemFx::Orphaned(Box::new(loose_item(txn.server, loose))));
}

#[tokio::test]
async fn deferred_transaction_persists_items_before_callbacks_and_does_not_release_twice() {
    let server = Arc::new(test_server());
    server.set_self();
    let client = TestClient::new(&server, 1, 1001);
    let _player = spawn_player(
        &server,
        &client,
        test_character(1, "Persistence", 100.0, 100.0),
        0x100,
    );
    let removed = u64::from(server.ids.item.get_next_id().unwrap());
    let loose = u64::from(server.ids.item.get_next_id().unwrap());
    let observer = Arc::new(ObservePersistence {
        removed,
        loose,
        events: Mutex::new(Vec::new()),
    });
    server
        .systems
        .economy
        .hooks
        .quests
        .install(observer.clone());
    let mut txn = ItemTxn::begin(&server);
    queue_effects(&mut txn, removed, loose);
    let fx = txn.into_fx();

    // This is the state a financial snapshot sees after the operation lock is released,
    // even when its caller deliberately delays notification/quest/world effects.
    let guard = server.systems.economy.ops_lock();
    assert_eq!(server.items.removed_items(), vec![removed]);
    assert_eq!(server.items.item_by_id(loose).unwrap().id, loose);
    assert!(observer.events.lock().unwrap().is_empty());
    assert_eq!(fx.len(), 2);
    drop(guard);
    // Re-reserve the freed ID before flushing callbacks. Replaying ReleaseId would free it.
    assert_eq!(u64::from(server.ids.item.get_next_id().unwrap()), removed);
    super::flush(&server, fx);
    assert_ne!(u64::from(server.ids.item.get_next_id().unwrap()), removed);
    assert_eq!(
        *observer.events.lock().unwrap(),
        vec![(true, 3), (false, 2)]
    );
    assert_eq!(server.items.removed_items(), vec![removed]);
    assert!(server.items.take_loose(loose).is_some());
    assert!(server.items.take_loose(loose).is_none());
}

#[tokio::test]
async fn transaction_drop_applies_persistence_before_reentrant_callbacks() {
    let server = Arc::new(test_server());
    server.set_self();
    let client = TestClient::new(&server, 1, 1001);
    let _player = spawn_player(
        &server,
        &client,
        test_character(1, "Persistence", 100.0, 100.0),
        0x100,
    );
    let removed = u64::from(server.ids.item.get_next_id().unwrap());
    let loose = u64::from(server.ids.item.get_next_id().unwrap());
    let observer = Arc::new(ObservePersistence {
        removed,
        loose,
        events: Mutex::new(Vec::new()),
    });
    server
        .systems
        .economy
        .hooks
        .quests
        .install(observer.clone());
    let mut txn = ItemTxn::begin(&server);
    queue_effects(&mut txn, removed, loose);
    drop(txn);
    assert_eq!(
        *observer.events.lock().unwrap(),
        vec![(true, 3), (false, 2)]
    );
}

#[tokio::test]
async fn persistence_flush_preserves_release_and_orphan_order() {
    let server = test_server();
    let first = u64::from(server.ids.item.get_next_id().unwrap());
    let second = u64::from(server.ids.item.get_next_id().unwrap());
    let mut fx = ItemFxList(vec![
        ItemFx::Orphaned(Box::new(loose_item(&server, first))),
        ItemFx::ReleaseId(first),
        ItemFx::ReleaseId(second),
        ItemFx::Orphaned(Box::new(loose_item(&server, second))),
    ]);
    super::flush_persistence(&server, &mut fx);
    assert!(fx.is_empty());
    assert!(server.items.item_by_id(first).is_none());
    assert_eq!(server.items.item_by_id(second).unwrap().id, second);
    assert_eq!(server.items.removed_items(), vec![first, second]);
    super::flush_persistence(&server, &mut fx);
    assert_eq!(server.items.removed_items(), vec![first, second]);
}
