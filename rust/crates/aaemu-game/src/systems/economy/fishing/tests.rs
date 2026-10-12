use std::sync::Arc;

use crate::data::items::{ItemTemplate, LootPackConvertFish, TemplateKind};
use crate::data::GameData;
use crate::models::container::{equip_slot, slot_type};
use crate::models::doodad::Doodad;
use crate::models::item::{Item, ItemClass};
use crate::models::item_task::task_type;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::economy::inventory::Inv;
use crate::systems::economy::items_data::FishDetails;
use crate::systems::economy::loot_data::{Loot, LootPack};
use crate::systems::npcs::doodad::phase;
use crate::systems::npcs::doodad::runtime::DoodadCtx;
use crate::systems::npcs::doodad::templates::{
    DoodadFunc, DoodadFuncBuyFish, DoodadFuncBuyFishItem, DoodadFuncConvertFish, FuncTemplate,
};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::{DoodadRef, Entity, PlayerRef, WorldObject};

const FISH: u32 = 27602;
const TROPHY: u32 = 28004;
const OTHER_PACK: u32 = 99991;
const FILLER: u32 = 99992;
const REFUND: i32 = 400000;

fn data() -> GameData {
    let mut data = GameData::empty();
    for id in [FISH, OTHER_PACK] {
        let mut template = ItemTemplate::plain(id, 1);
        template.kind = TemplateKind::Backpack { backpack_type: 6 };
        template.refund = REFUND;
        data.items.templates.insert(id, template);
    }
    data.items
        .templates
        .insert(TROPHY, ItemTemplate::plain(TROPHY, 1));
    data.items
        .templates
        .insert(FILLER, ItemTemplate::plain(FILLER, 1));
    data.items.loot_pack_convert_fish.insert(
        FISH,
        vec![LootPackConvertFish {
            id: 11,
            item_id: FISH,
            loot_pack_id: 7866,
            doodad_func_convert_fish_id: 2,
        }],
    );
    data.systems.economy.items.fish_details.insert(
        FISH,
        FishDetails {
            id: 10,
            name: "Large Blue Marlin".into(),
            item_id: FISH,
            min_weight: 515,
            max_weight: 644,
            min_length: 286,
            max_length: 358,
        },
    );
    data.systems.economy.loot.packs.insert(
        7866,
        LootPack {
            id: 7866,
            loots: vec![Loot {
                item_id: TROPHY,
                drop_rate: 10000000,
                min_amount: 1,
                max_amount: 1,
                ..Default::default()
            }],
            ..Default::default()
        },
    );
    let doodad = &mut data.systems.npcs.doodad;
    doodad
        .phase_func_templates
        .entry("DoodadFuncBuyFishItem")
        .or_default()
        .insert(
            10,
            FuncTemplate::BuyFishItem(DoodadFuncBuyFishItem {
                id: 10,
                doodad_func_buy_fish_id: 1,
                item_id: FISH,
            }),
        );
    doodad
        .func_templates
        .entry("DoodadFuncBuyFish")
        .or_default()
        .insert(
            1,
            FuncTemplate::BuyFish(DoodadFuncBuyFish { id: 1, item_id: 0 }),
        );
    doodad
        .func_templates
        .entry("DoodadFuncConvertFish")
        .or_default()
        .insert(
            2,
            FuncTemplate::ConvertFish(DoodadFuncConvertFish { id: 2 }),
        );
    data
}

struct Fixture {
    server: Arc<GameServer>,
    client: TestClient,
    player: PlayerRef,
    stand: DoodadRef,
}

impl Fixture {
    fn new(data: GameData) -> Self {
        let server = Arc::new(test_server_with(data));
        server.set_self();
        super::super::bridges::install(&server);
        let mut client = TestClient::new(&server, 1, 1001);
        let player = spawn_player(
            &server,
            &client,
            test_character(1, "Fisher", 100.0, 100.0),
            0x100,
        );
        client.conn.active_char = Some(player.clone());
        player.data().money = 500;
        let stand = Entity::new(0x200, Doodad::new(6507));
        {
            let mut doodad = stand.data();
            doodad.func_group_id = 17262;
            doodad.item_template_id = 777;
            doodad.transform.set_xyz(102.0, 100.0, 100.0);
        }
        server
            .world
            .world(0)
            .unwrap()
            .spawn(&stand, &server.view_ctx());
        client.drain();
        Self {
            server,
            client,
            player,
            stand,
        }
    }

    fn give(&self, template: u32, equipment: bool, slot: i32) -> u64 {
        let id = self.server.items.new_id(&self.server.ids.item).unwrap();
        let item = Item::create(
            id,
            self.server.game_data.items.template(template).unwrap(),
            1,
            0,
            self.server.config.id,
            &self.server.game_data.items,
            77,
        );
        let inv = Inv::of(&self.player);
        let container = if equipment {
            inv.equipment()
        } else {
            inv.bag()
        };
        assert!(container
            .lock()
            .unwrap()
            .add_new_item(item, slot, &self.server.game_data.items)
            .is_ok());
        id
    }

    fn backpack(&self) -> Option<Item> {
        Inv::of(&self.player).item_at(slot_type::EQUIPMENT, equip_slot::BACKPACK as u8)
    }

    fn use_stand(&self, convert: bool) {
        let ctx = DoodadCtx::of(&self.server, &self.stand).unwrap();
        let caster = WorldObject::from(&self.player);
        let func = DoodadFunc {
            group_id: 17262,
            func_id: if convert { 2 } else { 1 },
            func_key: if convert { 14417 } else { 14073 },
            func_type: if convert {
                "DoodadFuncConvertFish"
            } else {
                "DoodadFuncBuyFish"
            }
            .into(),
            next_phase: if convert { -1 } else { 17263 },
            skill_id: if convert { 22201 } else { 21904 },
            ..Default::default()
        };
        phase::do_func(&ctx, Some(&caster), func.skill_id, Some(&func));
    }
}

#[tokio::test]
async fn fish_sale_credits_one_static_refund_and_replay_cannot_pay_again() {
    let mut fixture = Fixture::new(data());
    fixture.give(FISH, true, equip_slot::BACKPACK);
    fixture.use_stand(false);
    assert!(fixture.backpack().is_none());
    assert_eq!(fixture.player.data().money, 500 + i64::from(REFUND));
    assert_eq!(fixture.stand.data().func_group_id, 17263);
    assert_eq!(fixture.stand.data().item_template_id, FISH);
    let frames = fixture.client.drain();
    let deposits: Vec<_> = frames
        .iter()
        .filter(|(op, bytes)| {
            *op == sc::SCItemTaskSuccessPacket && bytes.first() == Some(&task_type::DEPOSIT_MONEY)
        })
        .collect();
    let mut expected = vec![83, 1, 1]; // deposit action, one task, MoneyChange
    expected.extend_from_slice(&REFUND.to_le_bytes());
    expected.extend_from_slice(&[0, 0, 0, 0, 0]); // no forced removal, type(id)
    assert_eq!(deposits.len(), 1);
    assert_eq!(deposits[0].1, expected);
    fixture.use_stand(false);
    assert_eq!(fixture.player.data().money, 500 + i64::from(REFUND));
    assert!(!fixture.stand.data().phase.to_next_phase);
    assert!(!fixture
        .client
        .drain_ops()
        .contains(&sc::SCItemTaskSuccessPacket));
}

#[tokio::test]
async fn fish_sale_rejects_arbitrary_pack_foreign_owner_and_balance_overflow() {
    for case in 0..3 {
        let mut fixture = Fixture::new(data());
        fixture.give(
            if case == 0 { OTHER_PACK } else { FISH },
            true,
            equip_slot::BACKPACK,
        );
        if case == 1 {
            Inv::of(&fixture.player).equipment().lock().unwrap().items[0].owner_id = 2;
        } else if case == 2 {
            fixture.player.data().money = i64::MAX;
        }
        let before = fixture.backpack().unwrap();
        let money = fixture.player.data().money;
        fixture.stand.data().phase.to_next_phase = true;
        fixture.use_stand(false);
        assert_eq!(fixture.backpack().unwrap(), before);
        assert_eq!(fixture.player.data().money, money);
        assert_eq!(fixture.stand.data().func_group_id, 17262);
        assert_eq!(fixture.stand.data().item_template_id, 777);
        assert!(!fixture.stand.data().phase.to_next_phase);
        assert!(fixture.client.drain().is_empty());
    }
}

#[tokio::test]
async fn fish_conversion_delivers_sized_trophy_once_and_preserves_stand() {
    let mut fixture = Fixture::new(data());
    fixture.give(FISH, true, equip_slot::BACKPACK);
    fixture.use_stand(true);
    assert!(fixture.backpack().is_none());
    assert_eq!(fixture.player.data().money, 500);
    let trophies = Inv::of(&fixture.player).bag().lock().unwrap().items.clone();
    assert_eq!(trophies.len(), 1);
    let trophy = &trophies[0];
    assert_eq!(
        (trophy.template_id, trophy.class, trophy.count),
        (TROPHY, ItemClass::BigFish, 1)
    );
    assert!((286.0..358.0).contains(&trophy.details.fish_length));
    let expected_weight = 515.0 + (644.0 - 515.0) * (trophy.details.fish_length / 358.0);
    assert_eq!(trophy.details.fish_weight, expected_weight);
    assert_eq!(fixture.stand.data().func_group_id, 17262);
    assert!(!fixture.stand.data().phase.deleted);
    assert!(fixture
        .server
        .world
        .world(0)
        .unwrap()
        .doodad(fixture.stand.obj_id)
        .is_some());
    assert!(fixture
        .client
        .drain_ops()
        .contains(&sc::SCItemTaskSuccessPacket));
    fixture.use_stand(true);
    assert_eq!(
        Inv::of(&fixture.player).bag().lock().unwrap().items,
        trophies
    );
    assert!(!fixture
        .client
        .drain_ops()
        .contains(&sc::SCItemTaskSuccessPacket));
}

#[tokio::test]
async fn fish_conversion_lottery_allocates_only_the_selected_reward() {
    use crate::systems::economy::loot::{loot_convert_fish_from_pack, ScriptedRng};
    let mut game_data = data();
    let pack = game_data.systems.economy.loot.packs.get_mut(&7866).unwrap();
    pack.loots.push(Loot {
        item_id: FILLER,
        drop_rate: 10000000,
        min_amount: 1,
        max_amount: 1,
        ..Default::default()
    });
    let fixture = Fixture::new(game_data);
    let first_id = fixture
        .server
        .items
        .new_id(&fixture.server.ids.item)
        .unwrap();
    for (index, (roll, selected_template)) in
        [(0.0, TROPHY), (0.75, FILLER)].into_iter().enumerate()
    {
        let mut rng = ScriptedRng::new(&[], &[roll]);
        let selected = loot_convert_fish_from_pack(&fixture.server, FISH, 7866, &mut rng).unwrap();
        assert_eq!(selected.len(), 1);
        assert_eq!(selected[0].template_id, selected_template);
        assert_eq!(selected[0].id, first_id + 1 + index as u64);
    }
    assert_eq!(
        fixture
            .server
            .items
            .new_id(&fixture.server.ids.item)
            .unwrap(),
        first_id + 3
    );
}

#[tokio::test]
async fn fish_conversion_rejections_preserve_source_and_emit_no_success() {
    for case in 0..6 {
        let mut game_data = data();
        match case {
            1 => {
                game_data.items.loot_pack_convert_fish.clear();
            }
            2 => {
                game_data.systems.economy.loot.packs.clear();
            }
            3 => {
                game_data.items.templates.remove(&TROPHY);
            }
            4 => {
                game_data.systems.economy.items.fish_details.clear();
            }
            5 => {
                game_data
                    .items
                    .loot_pack_convert_fish
                    .get_mut(&FISH)
                    .unwrap()[0]
                    .doodad_func_convert_fish_id = 4;
            }
            _ => {}
        }
        let mut fixture = Fixture::new(game_data);
        fixture.give(FISH, true, equip_slot::BACKPACK);
        if case == 0 {
            for slot in 0..50 {
                fixture.give(FILLER, false, slot);
            }
        }
        let source = fixture.backpack().unwrap();
        let bag_before = Inv::of(&fixture.player).bag().lock().unwrap().items.clone();
        fixture.stand.data().phase.to_next_phase = true;
        fixture.use_stand(true);
        assert_eq!(fixture.backpack().unwrap(), source);
        assert_eq!(
            Inv::of(&fixture.player).bag().lock().unwrap().items,
            bag_before
        );
        assert_eq!(fixture.player.data().money, 500);
        assert_eq!(fixture.stand.data().func_group_id, 17262);
        assert_eq!(fixture.stand.data().item_template_id, 777);
        assert!(!fixture.stand.data().phase.to_next_phase);
        assert!(fixture.client.drain().is_empty());
    }
    let mut fixture = Fixture::new(data());
    fixture.give(OTHER_PACK, true, equip_slot::BACKPACK);
    let source = fixture.backpack().unwrap();
    fixture.use_stand(true);
    assert_eq!(fixture.backpack().unwrap(), source);
    assert!(fixture.client.drain().is_empty());
}

#[tokio::test]
async fn normal_chum_skill_hit_activates_only_matching_school_and_replaces_expiry_timer() {
    use crate::systems::npcs::doodad::phase::do_change_phase;
    use crate::systems::npcs::doodad::templates::{
        DoodadFuncFishSchool, DoodadFuncGroups, DoodadFuncSkillHit, DoodadFuncTimer,
        DoodadPhaseFunc, DoodadTemplate,
    };
    use crate::systems::skills::data::SkillTemplate;
    use crate::systems::skills::skill::use_skill;
    use crate::systems::skills::state::SkillRun;
    use crate::systems::skills::types::{skill_result, SkillCastTarget, SkillCaster};

    let mut game_data = data();
    // Exact relevant 1.2 skill fields. Casting at the caster's position makes the
    // real 15 m/s projectile delay zero without changing its template or scheduling.
    for (item_id, skill_id) in [(27313, 21693), (27314, 21744)] {
        let mut item = ItemTemplate::plain(item_id, 100);
        item.use_skill_id = skill_id;
        item.use_skill_as_reagent = true;
        game_data.items.templates.insert(item_id, item);
        game_data.systems.skills.skills.insert(
            skill_id,
            Arc::new(SkillTemplate {
                id: skill_id,
                category_id: 3,
                target_type: 6,
                target_selection: 2,
                target_relation: 0,
                target_area_count: 100,
                target_area_radius: 25,
                max_range: 30,
                effect_speed: 15.0,
                ..Default::default()
            }),
        );
    }
    let doodad = &mut game_data.systems.npcs.doodad;
    let groups: Vec<_> = [(16995, 1), (17243, 2), (17245, 2), (17140, 2)]
        .into_iter()
        .map(|(id, kind)| DoodadFuncGroups {
            id,
            almighty: 6447,
            group_kind_id: kind,
            ..Default::default()
        })
        .collect();
    doodad.templates.insert(
        6447,
        DoodadTemplate {
            id: 6447,
            group_id: 65,
            func_groups: groups.clone(),
            ..Default::default()
        },
    );
    for group in groups {
        doodad
            .all_func_groups_index
            .insert(group.id, doodad.all_func_groups.len());
        doodad.all_func_groups.push(group);
    }
    let hit = DoodadFunc {
        group_id: 17243,
        func_id: 709,
        func_key: 14057,
        func_type: "DoodadFuncSkillHit".into(),
        next_phase: 17245,
        ..Default::default()
    };
    doodad.funcs_by_id.insert(hit.func_key, hit.clone());
    doodad.funcs_by_groups.insert(17243, vec![hit]);
    doodad
        .func_templates
        .entry("DoodadFuncSkillHit")
        .or_default()
        .insert(
            709,
            FuncTemplate::SkillHit(DoodadFuncSkillHit {
                id: 709,
                skill_id: 21693,
            }),
        );
    for (group, timer_id, delay) in [(17243, 7477, 1800000), (17245, 7480, 2700000)] {
        doodad
            .phase_func_templates
            .entry("DoodadFuncTimer")
            .or_default()
            .insert(
                timer_id,
                FuncTemplate::Timer(DoodadFuncTimer {
                    id: timer_id,
                    delay,
                    next_phase: 17140,
                    ..Default::default()
                }),
            );
        doodad
            .phase_funcs
            .entry(group)
            .or_default()
            .push(DoodadPhaseFunc {
                group_id: group,
                func_id: timer_id,
                func_type: "DoodadFuncTimer".into(),
            });
    }
    doodad
        .phase_func_templates
        .entry("DoodadFuncFishSchool")
        .or_default()
        .insert(
            12,
            FuncTemplate::FishSchool(DoodadFuncFishSchool {
                id: 12,
                npc_spawner_id: 14475,
            }),
        );
    doodad.phase_funcs.get_mut(&17245).unwrap().insert(
        0,
        DoodadPhaseFunc {
            group_id: 17245,
            func_id: 12,
            func_type: "DoodadFuncFishSchool".into(),
        },
    );
    let mut fixture = Fixture::new(game_data);
    crate::systems::npcs::bridges::install(&fixture.server);
    crate::systems::skills::bridges::install(&fixture.server);
    fixture.server.systems.npcs.doodads.tasks.set_manual(true);
    fixture
        .server
        .items
        .add_container(Inv::of(&fixture.player).bag());
    let school = Entity::new(0x300, Doodad::new(6447));
    school.data().transform.set_xyz(110.0, 100.0, 100.0);
    fixture
        .server
        .world
        .world(0)
        .unwrap()
        .spawn(&school, &fixture.server.view_ctx());
    let ctx = DoodadCtx::of(&fixture.server, &school).unwrap();
    let caster = WorldObject::from(&fixture.player);
    do_change_phase(&ctx, Some(&caster), 17243);
    fixture.client.drain();
    for (item_id, skill_id, expected_phase) in [(27314, 21744, 17243), (27313, 21693, 17245)] {
        let bait_id = fixture.give(item_id, false, 0);
        let template = fixture
            .server
            .game_data
            .systems
            .skills
            .skill(skill_id)
            .unwrap()
            .clone();
        assert!(template.effects.is_empty());
        assert!(template.plot_id.is_none());
        let skill = SkillRun::new(template, 1);
        let mut target = SkillCastTarget::Position {
            obj_id: 0,
            x: 100.0,
            y: 100.0,
            z: 100.0,
            rot: 0.0,
            obj_id1: 0,
            obj_id2: 0,
        };
        let result = use_skill(
            &fixture.server,
            &skill,
            &caster,
            SkillCaster::Item {
                obj_id: fixture.player.obj_id,
                item_id: bait_id,
                item_template_id: item_id,
                type1: 0,
                type2: 0,
            },
            &mut target,
            None,
            true,
        );
        assert_eq!(result, (skill_result::SUCCESS, 0));
        assert_eq!(school.data().func_group_id, expected_phase);
        assert!(Inv::of(&fixture.player)
            .bag()
            .lock()
            .unwrap()
            .items
            .iter()
            .all(|item| item.id != bait_id));
        assert!(fixture
            .client
            .drain_ops()
            .contains(&sc::SCDoodadPhaseChangedPacket));
    }
    let fish_spawner = |group| {
        fixture
            .server
            .game_data
            .systems
            .npcs
            .doodad
            .phase_funcs(group)
            .iter()
            .filter_map(|func| {
                fixture
                    .server
                    .game_data
                    .systems
                    .npcs
                    .doodad
                    .phase_func_template(func.func_id, &func.func_type)
            })
            .find_map(|template| {
                if let FuncTemplate::FishSchool(f) = template {
                    Some(f.npc_spawner_id)
                } else {
                    None
                }
            })
    };
    assert_eq!(fish_spawner(school.data().func_group_id), Some(14475));
    fixture
        .server
        .systems
        .npcs
        .doodads
        .tasks
        .advance(&fixture.server, 1800000);
    assert_eq!(school.data().func_group_id, 17245); // cancelled inactive expiry cannot win
    fixture
        .server
        .systems
        .npcs
        .doodads
        .tasks
        .advance(&fixture.server, 900000);
    assert_eq!(school.data().func_group_id, 17140);
    assert_eq!(fish_spawner(school.data().func_group_id), None);
}

#[tokio::test]
#[ignore = "requires AAEMU_COMPACT_DB"]
async fn real_compact_db_sport_fishing_rules_and_plot_consumption() {
    use crate::models::char_data::Actability;
    use crate::systems::skills::data::{templates::load_skills, SkillItemAmount};
    use crate::systems::skills::effects::{special, EffectArgs, EffectSource};
    use crate::systems::skills::geometry::Target;
    use crate::systems::skills::state::SkillRun;
    use crate::systems::skills::types::{CastAction, SkillCastTarget, SkillCaster, SkillObject};
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let mut game_data = GameData::empty();
    game_data.items = crate::data::items::ItemData::load(&conn).unwrap();
    game_data.systems.economy.items =
        crate::systems::economy::items_data::ItemsData::load(&conn, "en_us").unwrap();
    game_data.systems.npcs.doodad =
        crate::systems::npcs::doodad::data::DoodadData::load_from(&conn).unwrap();
    assert_eq!(game_data.items.template(FISH).unwrap().refund, REFUND);
    assert!(game_data.systems.npcs.doodad.phase_func_templates["DoodadFuncBuyFishItem"].values().any(|row|
        matches!(row,FuncTemplate::BuyFishItem(f) if f.doodad_func_buy_fish_id==1 && f.item_id==FISH)));
    assert!(game_data
        .items
        .loot_packs_by_fish(FISH)
        .iter()
        .any(|row| row.doodad_func_convert_fish_id == 2 && row.loot_pack_id == 7866));
    let skills = load_skills(&conn, &|_| true, &|_| true).unwrap();
    let sport = Arc::new(skills.into_iter().find(|s| s.id == 21578).unwrap());
    assert_eq!(
        (
            sport.min_range,
            sport.max_range,
            sport.consume_labor_power,
            sport.actability_group_id
        ),
        (8, 30, 100, 7)
    );
    assert_eq!((sport.plot_id, sport.plot_only), (Some(821), true));
    let reagent = conn
        .query_row(
            "SELECT id,skill_id,item_id,amount FROM skill_reagents WHERE skill_id=21578",
            [],
            |row| {
                Ok(SkillItemAmount {
                    id: row.get(0)?,
                    skill_id: row.get(1)?,
                    item_id: row.get(2)?,
                    amount: row.get(3)?,
                })
            },
        )
        .unwrap();
    assert_eq!((reagent.item_id, reagent.amount), (27319, 1));
    game_data.systems.skills.skill_reagents.push(reagent);
    // Use the actual plot's effects rather than the normal end-skill path: 21578 is plot-only.
    for (effect_id, kind) in [(10951, 68), (11130, 86)] {
        let count:i64=conn.query_row("SELECT COUNT(*) FROM plot_effects p JOIN plot_events e ON e.id=p.event_id JOIN special_effects s ON s.id=p.actual_id WHERE e.plot_id=821 AND p.actual_type='SpecialEffect' AND s.id=? AND s.special_effect_type_id=?",[effect_id,kind],|row|row.get(0)).unwrap();
        assert_eq!(count, 1);
    }
    let mut fixture = Fixture::new(game_data);
    fixture.give(27319, false, 0);
    fixture.give(27319, false, 1);
    fixture.player.data().labor_power = 1000;
    fixture
        .player
        .data()
        .actability
        .actabilities
        .push(Actability {
            id: 7,
            point: 0,
            step: 0,
        });
    let caster = WorldObject::from(&fixture.player);
    let skill = SkillRun::new(sport, 1);
    let skill_object = SkillObject::None;
    let args = EffectArgs {
        caster: Some(caster.clone()),
        caster_caster: SkillCaster::Unit(fixture.player.obj_id),
        target: Target::Obj(caster),
        target_caster: SkillCastTarget::Unit(fixture.player.obj_id),
        cast: CastAction::Skill {
            skill_id: 21578,
            tl_id: 0,
        },
        source: EffectSource::from_skill(skill),
        skill_object: &skill_object,
    };
    special::execute(&fixture.server, 68, &args, [0; 4]);
    assert_eq!(
        Inv::of(&fixture.player)
            .bag()
            .lock()
            .unwrap()
            .items
            .iter()
            .filter(|item| item.template_id == 27319)
            .map(|item| item.count)
            .sum::<i32>(),
        1
    );
    special::execute(&fixture.server, 86, &args, [0; 4]);
    assert_eq!(fixture.player.data().labor_power, 900);
    assert_eq!(
        fixture
            .client
            .drain_ops()
            .iter()
            .filter(|&&op| op == sc::SCCharacterLaborPowerChangedPacket)
            .count(),
        1
    );
}
