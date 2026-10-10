//! `Scripts/Commands/TestHouse.cs`.

use super::prelude::*;
use crate::systems::housing::{manager as housing, ops as housing_ops};
use crate::world::HouseRef;

const DAY: i64 = 24 * 3600;

/// `character.CurrentTarget is House house`.
pub(super) fn target_house(server: &GameServer, player: &PlayerRef) -> Option<HouseRef> {
    current_target(server, player).and_then(|t| t.as_house().cloned())
}

/// `TestHouse`.
pub struct TestHouse;

impl TestHouse {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }

    /// The `try { ... }` body: `Err` = the message of a caught exception.
    fn run(
        &self,
        server: &GameServer,
        house: &HouseRef,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> Result<(), String> {
        const NRE: &str = "Object reference not set to an instance of an object.";
        let name = house.data().unit.name.clone();
        let a0 = args[0].to_lowercase();
        let set_protection = |secs: i64| {
            {
                let mut h = house.data();
                h.protection_end_date = housing::now() + secs;
                h.is_dirty = true;
            }
            housing::update_tax_info(server, house);
        };
        match a0.as_str() {
            "info" => {
                let (tl, id, template_id, model_id, account_id) = {
                    let h = house.data();
                    (
                        h.unit.tl_id,
                        h.id,
                        h.unit.template_id,
                        h.unit.model_id,
                        h.account_id,
                    )
                };
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!(
                        "ObjId: {} - TlId: {tl} - HouseId: {id} - TemplateId: {template_id} - ModelId: {model_id} - {name}",
                        house.obj_id
                    ),
                );
                let template = housing::template_of(server, house).ok_or(NRE)?;
                let (_, info) =
                    housing::calculate_building_tax_info(server, account_id, template, false)
                        .map_err(|_| NRE.to_string())?;
                let total_tax_amount_due = info.total_tax_to_pay;
                let heavy = if template.heavy_tax {
                    ""
                } else {
                    "(no heavy tax)"
                };
                let (tax_due_date, protection_end_date) = {
                    let h = house.data();
                    (housing::tax_due_date(&h), h.protection_end_date)
                };
                let now = housing::now();
                if now >= tax_due_date {
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "Tax Due: {total_tax_amount_due}{heavy} by {}",
                            dotnet_datetime(tax_due_date)
                        ),
                    );
                } else if now >= tax_due_date + 7 * DAY {
                    // C#: unreachable (the first branch already covers it).
                    CommandManager::send_normal_text(
                        self,
                        out,
                        &format!(
                            "Tax Overdue: {}{heavy}, demolition at {}",
                            total_tax_amount_due.wrapping_mul(2),
                            dotnet_datetime(protection_end_date)
                        ),
                    );
                }
            }
            "taxmail" => {
                let mail = housing::new_tax_mail(server, house);
                server.systems.housing.hooks.get().send_mail(server, mail);
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("Created tax mail for {name}"),
                );
            }
            "settaxpaid" => {
                set_protection(21 * DAY);
                CommandManager::send_normal_text(self, out, &format!("Set {name} as tax paid"));
            }
            "settaxdue" => {
                set_protection(14 * DAY);
                CommandManager::send_normal_text(self, out, &format!("Set {name} as tax due"));
            }
            "settaxoverdue" => {
                set_protection(7 * DAY);
                CommandManager::send_normal_text(self, out, &format!("Set {name} as tax overdue"));
            }
            "setdemosoon" => {
                set_protection(20);
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("Set {name} to demolished in about 20 seconds"),
                );
            }
            "setforsale" => {
                let mut price = 0u32;
                let mut buyer = String::new();
                let mut buyer_id = 0u32;
                if let Some(a) = args.get(1) {
                    match try_parse::<u32>(a) {
                        Some(v) => price = v,
                        None => {
                            CommandManager::send_error_text(self, out, "Parse error, price");
                            return Ok(());
                        }
                    }
                }
                if let Some(a) = args.get(2) {
                    buyer = a.clone();
                    buyer_id = server.names.get_character_id(&buyer);
                    if buyer_id > 0 {
                        buyer = server
                            .names
                            .get_character_name(buyer_id)
                            .unwrap_or_default();
                    } else {
                        CommandManager::send_error_text(
                            self,
                            out,
                            &format!("Invalid buyer name {a}"),
                        );
                        return Ok(());
                    }
                }
                if buyer_id == 0 {
                    buyer = "anyone".into();
                }
                let sell_price = house.data().sell_price;
                if price > 0 {
                    // Set for sale
                    if sell_price > 0 {
                        CommandManager::send_error_text(
                            self,
                            out,
                            "Is already for sale, clear first to assign a new value",
                        );
                        return Ok(());
                    }
                    if housing_ops::set_for_sale(server, house, price, buyer_id, None) {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!(
                                "Setting {name} for sale with a price of {price} to buy for {buyer}."
                            ),
                        );
                    } else {
                        CommandManager::send_error_text(
                            self,
                            out,
                            &format!("Failed to set {name} for sale !"),
                        );
                    }
                } else {
                    if sell_price == 0 {
                        CommandManager::send_error_text(self, out, "This house wasn't for sale.");
                        return Ok(());
                    }
                    // Remove sale (for GM commands we don't return certificates)
                    if housing_ops::cancel_for_sale(server, house, false) {
                        CommandManager::send_normal_text(
                            self,
                            out,
                            &format!("{name} is no longer for sale"),
                        );
                    } else {
                        CommandManager::send_error_text(
                            self,
                            out,
                            &format!("Failed to remove sale from {name} !"),
                        );
                    }
                }
            }
            "forcedemo" => {
                housing_ops::demolish(server, None, house, false, true);
                CommandManager::send_normal_text(
                    self,
                    out,
                    &format!("{name} demolished with full restore"),
                );
            }
            _ => CommandManager::send_error_text(self, out, &format!("Unknown command: {a0}")),
        }
        Ok(())
    }
}

impl Command for TestHouse {
    fn command_names(&self) -> Vec<String> {
        names(&["house", "test_house", "testhouse"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.is_empty() {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        let server = ctx.server()?;
        let player = ctx.player()?;
        let Some(house) = target_house(server, &player) else {
            CommandManager::send_error_text(self, out, "No building selected");
            return Ok(());
        };
        if let Err(e) = self.run(server, &house, args, out) {
            CommandManager::send_error_text(self, out, &format!("Exception: {e}"));
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "[command] [options]".into()
    }

    fn get_command_help_text(&self) -> String {
        concat!(
            "Available commands;\n",
            "|cFFFFFFFFinfo|r -> Shows various house info\n",
            "|cFFFFFFFFtaxmail|r -> Creates a tax due mail for the house owner\n",
            "|cFFFFFFFFsetforsale <money> [buyer]|r -> Forces a house for sale to be set, with optional [buyer] specified. Specify 0 money to clear the sale. (does not return any certificates)\n",
            "|cFFFFFFFFsettaxpaid|r -> Sets the house's taxdue date as if just build and paid for.\n",
            "|cFFFFFFFFsettaxdue|r -> Sets the house's taxdue date to now.\n",
            "|cFFFFFFFFsettaxduesoon|r -> Sets the house's taxdue date to 20 seconds from now.\n",
            "|cFFFFFFFFsetdemosoon|r -> Sets the house's demolition date to 20 seconds from now.\n",
            "|cFFFFFFFFforcedemo|r -> Forcefully demolishes a house right now, but returns all furniture regardless if it is normally returned or not.\n",
        )
        .into()
    }
}

/// A house fixture shared by the house command tests (`build`, `house_binding_move`, `house`).
#[cfg(test)]
pub(super) mod fixture {
    use std::collections::BTreeMap;

    use crate::data::world_data::Zone;
    use crate::data::GameData;
    use crate::server::GameServer;
    use crate::systems::housing::data::{
        HousingBindingDoodad, HousingBuildStep, HousingItemHousings, HousingTemplate, Taxation,
        WorldSpawnPosition,
    };
    use crate::systems::housing::manager;
    use crate::systems::npcs::doodad::templates::DoodadTemplate;
    use crate::test_support::TEST_ZONE;
    use crate::world::{HouseRef, PlayerRef};

    pub const DESIGN: u32 = 300;
    pub const BOUND_DOODAD: u32 = 4001;
    pub const ATTACH_POINT: u8 = 9;
    pub const DESIGN_ITEM: u32 = 9000;

    fn step(step: i16, model_id: u32, num_actions: i32) -> HousingBuildStep {
        HousingBuildStep {
            id: u32::from(step as u16) + 1,
            housing_id: DESIGN,
            step,
            model_id,
            skill_id: 11000 + u32::from(step as u16),
            num_actions,
        }
    }

    /// A design with two build steps (2 + 1 actions), one bound doodad and a 100000 tax.
    pub fn data() -> GameData {
        let mut data = GameData::empty();
        let h = &mut data.systems.housing;
        let taxation = Taxation {
            id: 2,
            tax: 100_000,
            show: true,
        };
        h.taxations.insert(2, taxation);
        let mut build_steps = BTreeMap::new();
        build_steps.insert(0, step(0, 710, 2));
        build_steps.insert(1, step(1, 711, 1));
        h.add_template(HousingTemplate {
            id: DESIGN,
            name: "Cottage Design".into(),
            main_model_id: 700,
            hp: 1600,
            garden_radius: 8.0,
            taxation: Some(taxation),
            taxation_id: 2,
            is_sellable: true,
            heavy_tax: true,
            build_steps,
            binding_doodads: vec![HousingBindingDoodad {
                attach_point_id: ATTACH_POINT,
                doodad_id: BOUND_DOODAD,
                position: WorldSpawnPosition {
                    x: 1.0,
                    y: 2.0,
                    z: 3.0,
                    ..Default::default()
                },
            }],
            ..Default::default()
        });
        h.add_item_housing(HousingItemHousings {
            id: 1,
            item_id: DESIGN_ITEM,
            design_id: DESIGN,
        });
        for (id, max) in [(DESIGN_ITEM, 1), (manager::BOUND_TAX_CERTIFICATE, 1000)] {
            data.items
                .templates
                .insert(id, crate::data::items::ItemTemplate::plain(id, max));
        }
        for id in [BOUND_DOODAD, manager::FOR_SALE_MARKER_DOODAD_ID] {
            data.systems.npcs.doodad.templates.insert(
                id,
                DoodadTemplate {
                    id,
                    ..Default::default()
                },
            );
        }
        data.zones.zones.insert(
            TEST_ZONE,
            Zone {
                id: 1,
                name: "zone".into(),
                zone_key: TEST_ZONE,
                group_id: 25,
                ..Default::default()
            },
        );
        for id in [manager::BUFF_UNTOUCHABLE, manager::BUFF_REMOVAL_DEBUFF] {
            data.systems
                .skills
                .add_buff(crate::systems::skills::data::BuffTemplate {
                    id,
                    ..Default::default()
                });
        }
        data
    }

    /// A registered, spawned house of `owner` at (x, y) at build step `step` (-1 = finished).
    pub fn place_house(
        server: &GameServer,
        owner: &PlayerRef,
        x: f32,
        y: f32,
        step: i32,
    ) -> HouseRef {
        let world = server.world.world(0).unwrap();
        // Keep the house / doodad obj ids clear of the harness players' fixed ids (0x100 + n).
        while server.ids.object.get_next_id().unwrap() < 0x200 {}
        let (cid, acc) = {
            let c = owner.data();
            (c.id, c.account_id)
        };
        let house = manager::create(server, DESIGN, 101, &world, 0, 0).unwrap();
        {
            let mut h = house.data();
            h.id = server.ids.housing.get_next_id().unwrap();
            h.unit.transform.set_xyz(x, y, 100.0);
            h.unit.transform.zone_id = TEST_ZONE;
        }
        manager::set_current_step(server, &house, step, false);
        {
            let mut h = house.data();
            h.unit.owner_id = cid;
            h.co_owner_id = cid;
            h.account_id = acc;
            h.place_date = manager::now() - 100;
            h.protection_end_date = manager::now() + 7 * 24 * 3600 + 3600;
        }
        assert!(server.systems.housing.manager.insert(&house));
        manager::spawn_house(server, &house);
        house
    }

    /// Targets `house` (`CurrentTarget`).
    pub fn target(p: &PlayerRef, house: &HouseRef) {
        p.data().unit.current_target = house.obj_id;
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use super::fixture;
    use crate::opcodes::sc;
    use crate::systems::economy::mail::mail_type;
    use crate::systems::housing::manager;

    #[tokio::test]
    async fn house_without_target_and_help() {
        let server = harness::server_with(fixture::data());
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "house info").await;
        assert_eq!(out, [harness::error("house", "No building selected")]);
        let (_, out) = harness::run(&server, &gm.p, "testhouse").await;
        assert!(out[0].starts_with(
            "Help for |cFFFFFFFF/house|r |cFFEEEEAA[command] [options]|r\n|cFF888888Available commands;\n|cFFFFFFFFinfo|r -> Shows various house info\n"
        ));
        assert!(out[0].ends_with("returned or not.\n|r"));
    }

    #[tokio::test]
    async fn house_info_tax_dates_and_unknown() {
        let server = harness::server_with(fixture::data());
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        server.names.add_character(1, "Gm", 1001);
        let house = fixture::place_house(&server, &gm.p, 120.0, 120.0, -1);
        fixture::target(&gm.p, &house);
        let (tl, id) = {
            let h = house.data();
            (h.unit.tl_id, h.id)
        };
        let obj = house.obj_id;
        let name = house.data().unit.name.clone();
        // Tax not yet due: only the first line.
        let (_, out) = harness::run(&server, &gm.p, "house info").await;
        assert_eq!(
            out,
            [harness::normal(
                "house",
                &format!(
                    "ObjId: {obj} - TlId: {tl} - HouseId: {id} - TemplateId: 300 - ModelId: 700 - {name}"
                )
            )]
        );
        // settaxdue: protection end in 14 days (tax due in 7 days: still not due).
        let (_, out) = harness::run(&server, &gm.p, "house settaxdue").await;
        assert_eq!(
            out,
            [harness::normal("house", &format!("Set {name} as tax due"))]
        );
        let end = house.data().protection_end_date;
        assert!((end - manager::now() - 14 * 24 * 3600).abs() <= 2);
        // setdemosoon: tax due date in the past -> "Tax Due" line with the date.
        let (_, out) = harness::run(&server, &gm.p, "house SetDemoSoon").await;
        assert_eq!(
            out,
            [harness::normal(
                "house",
                &format!("Set {name} to demolished in about 20 seconds")
            )]
        );
        let due = manager::tax_due_date(&house.data());
        let (_, out) = harness::run(&server, &gm.p, "house info").await;
        assert_eq!(out.len(), 2);
        assert_eq!(
            out[1],
            harness::normal(
                "house",
                &format!("Tax Due: 100000 by {}", super::dotnet_datetime(due))
            )
        );
        for (cmd, days, text) in [
            ("settaxpaid", 21, "as tax paid"),
            ("settaxoverdue", 7, "as tax overdue"),
        ] {
            let (_, out) = harness::run(&server, &gm.p, &format!("house {cmd}")).await;
            assert_eq!(
                out,
                [harness::normal("house", &format!("Set {name} {text}"))]
            );
            let end = house.data().protection_end_date;
            assert!((end - manager::now() - days * 24 * 3600).abs() <= 2);
        }
        let (_, out) = harness::run(&server, &gm.p, "house Foo").await;
        assert_eq!(out, [harness::error("house", "Unknown command: foo")]);
    }

    #[tokio::test]
    async fn house_tax_mail_sale_and_demolition() {
        let server = harness::server_with(fixture::data());
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        server.names.add_character(1, "Gm", 1001);
        server.names.add_character(2, "Buyer", 1002);
        let house = fixture::place_house(&server, &gm.p, 120.0, 120.0, -1);
        fixture::target(&gm.p, &house);
        let name = house.data().unit.name.clone();
        gm.frames();

        // taxmail: a billing mail to the owner.
        let (_, out) = harness::run(&server, &gm.p, "house taxmail").await;
        assert_eq!(
            out,
            [harness::normal(
                "house",
                &format!("Created tax mail for {name}")
            )]
        );
        let mails: Vec<_> = server
            .systems
            .economy
            .mail
            .state()
            .all
            .iter()
            .map(|(_, m)| {
                (
                    m.mail_type,
                    m.header.receiver_id,
                    m.header.sender_name.clone(),
                )
            })
            .collect();
        assert_eq!(
            mails,
            vec![(mail_type::BILLING, 1, ".houseTax".to_string())]
        );

        // setforsale
        let (_, out) = harness::run(&server, &gm.p, "house setforsale x").await;
        assert_eq!(out, [harness::error("house", "Parse error, price")]);
        let (_, out) = harness::run(&server, &gm.p, "house setforsale 0").await;
        assert_eq!(
            out,
            [harness::error("house", "This house wasn't for sale.")]
        );
        let (_, out) = harness::run(&server, &gm.p, "house setforsale 5000 nobody").await;
        assert_eq!(out, [harness::error("house", "Invalid buyer name nobody")]);
        gm.frames();
        let (_, out) = harness::run(&server, &gm.p, "house setforsale 5000 Buyer").await;
        assert_eq!(
            out,
            [harness::normal(
                "house",
                &format!("Setting {name} for sale with a price of 5000 to buy for Buyer.")
            )]
        );
        assert_eq!(house.data().sell_price, 5000);
        assert_eq!(house.data().sell_to_player_id, 2);
        assert!(gm
            .packets()
            .iter()
            .any(|(op, _)| *op == sc::SCHouseSetForSalePacket));
        let (_, out) = harness::run(&server, &gm.p, "house setforsale 10").await;
        assert_eq!(
            out,
            [harness::error(
                "house",
                "Is already for sale, clear first to assign a new value"
            )]
        );
        let (_, out) = harness::run(&server, &gm.p, "house setforsale").await;
        assert_eq!(
            out,
            [harness::normal(
                "house",
                &format!("{name} is no longer for sale")
            )]
        );
        assert_eq!(house.data().sell_price, 0);

        // forcedemo
        let id = house.data().id;
        let (_, out) = harness::run(&server, &gm.p, "house forcedemo").await;
        assert_eq!(
            out,
            [harness::normal(
                "house",
                &format!("{name} demolished with full restore")
            )]
        );
        // The house loses its owner and is queued for removal (`MarkRemoved`).
        assert_eq!(house.data().unit.owner_id, 0);
        assert!(server.systems.housing.manager.removed_ids().contains(&id));
    }
}
