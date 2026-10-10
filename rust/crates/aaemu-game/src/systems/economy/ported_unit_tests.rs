//! Ports of the economy-related C# unit tests (`AAEmu.UnitTests`): one submodule per C# test
//! class. The C# `Constructor_DoesNotCallDeps` tests (Moq `VerifyNoOtherCalls` on the injected
//! managers) become "construction has no side effects" tests: the Rust managers take no
//! injected dependencies, so the observable state of a fresh manager is checked instead.

mod item_game_data_tests {
    //! Port of AAEmu.UnitTests/Game/GameData/ItemGameDataTests.cs
    //!
    //! `ItemGameData` is the `item_grade_buffs` part of `systems::economy::items_data::ItemsData`.

    use crate::systems::economy::items_data::ItemsData;

    #[test]
    fn can_create_instance() {
        let instance = ItemsData::default();
        assert!(instance.item_grade_buffs.is_empty());
        assert_eq!(instance.item_grade_buff(1, 0), None);
    }

    #[test]
    fn new_instances_are_independent() {
        let mut instance1 = ItemsData::default();
        let instance2 = ItemsData::default();
        assert!(!std::ptr::eq(&instance1, &instance2));
        // Independent state: filling one leaves the other empty.
        instance1
            .item_grade_buffs
            .entry(10)
            .or_default()
            .insert(2, 99);
        assert_eq!(instance1.item_grade_buff(10, 2), Some(99));
        assert_eq!(instance2.item_grade_buff(10, 2), None);
    }
}

mod cs_buy_items_packet_tests {
    //! Port of AAEmu.UnitTests/Game/Core/Packets/C2G/CSBuyItemsPacketTests.cs
    //!
    //! `new CSBuyItemsPacket()` is `GamePacket(CSOffsets.CSBuyItemsPacket, 1)`: in Rust the
    //! packet's opcode / level live in its registration.

    use crate::net::packet::LEVEL_PLAIN;
    use crate::net::registry::{ClientPacket, PacketRegistry};
    use crate::opcodes::cs;
    use crate::systems::economy::store::{self, CSBuyItemsPacket};

    #[test]
    fn constructor_initializes_packet() {
        let mut reg = PacketRegistry::new();
        store::register(&mut reg);
        assert_eq!(LEVEL_PLAIN, 1);
        let r = reg
            .get(LEVEL_PLAIN, cs::CSBuyItemsPacket)
            .expect("CSBuyItemsPacket registered");
        assert_eq!(r.name, CSBuyItemsPacket::NAME);
        assert_eq!(r.name, "CSBuyItemsPacket");
    }
}

mod mail_tests {
    //! Port of AAEmu.UnitTests/Game/Core/Managers/MailTests.cs
    //!
    //! The C# setup (a `CharacterMock` "tester" with id 1, account 1 and 1000 money, a
    //! `NameManager` that knows it, a `MailManager` with an empty `_allPlayerMails`) is a test
    //! server with that one character spawned and registered; `CharacterMails.SendMailToPlayer`
    //! is `mail_character::send_mail_to_player`.

    use std::sync::Arc;

    use crate::managers::name::normalize_name;
    use crate::server::GameServer;
    use crate::systems::economy::inventory::{Inv, ItemTxn};
    use crate::systems::economy::mail::{mail_result, mail_type};
    use crate::systems::economy::mail_character;
    use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
    use crate::world::PlayerRef;

    struct Setup {
        server: Arc<GameServer>,
        _client: TestClient,
        character: PlayerRef,
    }

    /// `[Before(Test)] Setup`.
    fn setup() -> Setup {
        let server = Arc::new(test_server_with(crate::systems::economy::tests::test_data()));
        server.set_self();
        let mut client = TestClient::new(&server, 1, 1);
        let mut c = test_character(1, "tester", 100.0, 100.0);
        c.account_id = 1;
        c.money = 1000;
        let character = spawn_player(&server, &client, c, 0x100);
        client.conn.active_char = Some(character.clone());
        client.drain();
        server.names.add_character(1, "tester", 1);
        for (_, container) in Inv::of(&character).containers {
            server.items.add_container(container);
        }
        assert_eq!(server.systems.economy.mail.count(), 0);
        Setup {
            server,
            _client: client,
            character,
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn send_mail_to_player(
        s: &Setup,
        mail_type: u8,
        receiver_char_name: &str,
        title: &str,
        text: &str,
        attachments: u8,
        money0: i32,
        money1: i32,
        money2: i32,
        extra: i64,
        item_slots: &[(u8, u8)],
    ) -> u8 {
        let mut txn = ItemTxn::begin(&s.server);
        mail_character::send_mail_to_player(
            &mut txn,
            &s.character,
            mail_type,
            receiver_char_name,
            title,
            text,
            attachments,
            money0,
            money1,
            money2,
            extra,
            item_slots,
        )
    }

    #[tokio::test]
    async fn money_test() {
        let s = setup();
        let receiver_char_name = normalize_name("tester");

        let result = send_mail_to_player(
            &s,
            mail_type::EXPRESS,
            &receiver_char_name,
            "test",
            "test",
            0,
            500,
            0,
            0,
            0,
            &[],
        );

        assert_eq!(result, mail_result::SUCCESS);
        // 500 attached + 100 express fee
        assert_eq!(s.character.data().money, 400);
    }

    #[tokio::test]
    async fn player_not_found_test() {
        let s = setup();

        let result = send_mail_to_player(
            &s,
            mail_type::EXPRESS,
            "bob",
            "test",
            "test",
            0,
            500,
            0,
            0,
            0,
            &[],
        );

        assert_ne!(result, mail_result::SUCCESS);
        assert_eq!(s.character.data().money, 1000);
    }
}

mod mail_manager_tests {
    //! Port of AAEmu.UnitTests/Game/Core/Managers/MailManagerTests.cs

    use crate::systems::economy::mail::MailManager;

    #[test]
    fn constructor_does_not_call_deps() {
        let manager = MailManager::default();
        assert_eq!(manager.count(), 0);
        assert!(manager.state().deleted.is_empty());
        assert!(manager.hooks.housing.get().is_none());
    }
}

mod auction_manager_tests {
    //! Port of AAEmu.UnitTests/Game/Core/Managers/AuctionManagerTests.cs

    use crate::systems::economy::auction::AuctionManager;

    #[test]
    fn constructor_does_not_call_deps() {
        let manager = AuctionManager::default();
        assert!(manager.all_lots().is_empty());
        assert!(manager.deleted_ids().is_empty());
        assert!(manager.mail.get().is_none());
    }
}

mod cash_shop_manager_tests {
    //! Port of AAEmu.UnitTests/Game/Core/Managers/CashShopManagerTests.cs
    //!
    //! `DisableShop_CallsGetAllCharacters` checks (through a mocked `IWorldManager`) that
    //! `DisableShop` walks `GetAllCharacters()` once. The Rust `disable_shop` iterates
    //! `server.world.all_characters()`; the observable effect is checked: the shop is disabled
    //! and every online character gets exactly one `SCICSCheckTimePacket` (with the C# mock's
    //! empty list nothing is sent at all).

    use std::sync::Arc;

    use crate::opcodes::sc;
    use crate::systems::economy::cash_shop::{disable_shop, enable_shop};
    use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};

    #[tokio::test]
    async fn disable_shop_calls_get_all_characters() {
        // GetAllCharacters() returns [] (no online character)
        let server = Arc::new(test_server_with(crate::data::GameData::empty()));
        enable_shop(&server);
        disable_shop(&server);
        assert!(!server.systems.economy.cash_shop.enabled());

        // ... and with characters online, each one is visited exactly once.
        let mut a = TestClient::new(&server, 1, 1001);
        let mut b = TestClient::new(&server, 2, 1002);
        let _pa = spawn_player(&server, &a, test_character(1, "Alice", 100.0, 100.0), 0x100);
        let _pb = spawn_player(&server, &b, test_character(2, "Bob", 101.0, 100.0), 0x101);
        a.drain();
        b.drain();
        enable_shop(&server);
        disable_shop(&server);
        assert!(!server.systems.economy.cash_shop.enabled());
        assert_eq!(a.drain_ops(), vec![sc::SCICSCheckTimePacket]);
        assert_eq!(b.drain_ops(), vec![sc::SCICSCheckTimePacket]);
    }
}

mod trade_manager_tests {
    //! Port of AAEmu.UnitTests/Game/Core/Managers/TradeManagerTests.cs

    use crate::systems::economy::trade::TradeManager;

    #[test]
    fn constructor_does_not_call_deps() {
        let manager = TradeManager::default();
        assert!(manager.trades().is_empty());
    }
}
