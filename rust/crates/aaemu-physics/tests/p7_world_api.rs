//! The convenience API ([`aaemu_physics::world::api`]) reproduces the scenario goldens.

use aaemu_physics::geometry::primitives::{BoxPrim, Primitive};
use aaemu_physics::math::{Quat, Vec3};
use aaemu_physics::oracle::check_scenario_golden;
use aaemu_physics::trace::Trace;
use aaemu_physics::world::api::PartDesc;
use aaemu_physics::world::World;

fn boxp(h: f32) -> Primitive {
    Primitive::Box(BoxPrim {
        size: Vec3::new(h, h, h),
        ..BoxPrim::default()
    })
}

#[test]
fn box_on_ground_through_the_api() {
    let mut w = World::with_grid(Vec3::new(0.0, 0.0, -100.0), 64, 4.0);
    let ground = w
        .geometry(&Primitive::Box(BoxPrim {
            size: Vec3::new(50.0, 50.0, 0.5),
            ..BoxPrim::default()
        }))
        .unwrap();
    let cube = w.geometry(&boxp(0.5)).unwrap();
    w.add_static(
        Vec3::new(32.0, 32.0, -0.5),
        Quat::IDENTITY,
        &[PartDesc::new(ground, 0.0)],
        -1,
    );
    let b = w
        .add_rigid(
            Vec3::new(32.0, 32.0, 5.0),
            Quat::IDENTITY,
            &[PartDesc::new(cube, 10.0)],
            -1,
        )
        .unwrap();
    let dt = f32::from_bits(0x3d08_8889);
    let golden = Trace::parse(&check_scenario_golden("box_on_ground").unwrap()).unwrap();
    for n in 1..=90u32 {
        w.step(dt);
        let s = w.body_state(b);
        let g = golden
            .entity_states("box")
            .into_iter()
            .find(|(k, _)| *k == n)
            .unwrap()
            .1
            .clone();
        assert_eq!(s.pos.to_bits(), g.pos_f().to_bits(), "step {n}");
        assert_eq!(s.q.to_bits(), g.q_f().to_bits(), "step {n}");
    }
}

#[test]
fn floating_box_through_the_api() {
    let mut w = World::with_grid(Vec3::new(0.0, 0.0, -100.0), 64, 4.0);
    let ground = w
        .geometry(&Primitive::Box(BoxPrim {
            size: Vec3::new(50.0, 50.0, 0.5),
            ..BoxPrim::default()
        }))
        .unwrap();
    let cube = w.geometry(&boxp(0.5)).unwrap();
    w.add_static(
        Vec3::new(32.0, 32.0, -20.0),
        Quat::IDENTITY,
        &[PartDesc::new(ground, 0.0)],
        -1,
    );
    let b = w
        .add_rigid(
            Vec3::new(32.0, 32.0, 5.0),
            Quat::IDENTITY,
            &[PartDesc::new(cube, 300.0)],
            -1,
        )
        .unwrap();
    w.set_ocean(2.0, 1000.0, 1000.0);
    w.set_body_buoyancy(b, 1000.0, 1000.0);
    let golden = Trace::parse(&check_scenario_golden("box_float_300kg").unwrap()).unwrap();
    for n in 1..=300u32 {
        w.step(f32::from_bits(0x3d08_8889));
        let s = w.body_state(b);
        let g = golden
            .entity_states("box")
            .into_iter()
            .find(|(k, _)| *k == n)
            .unwrap()
            .1
            .clone();
        assert_eq!(s.pos.to_bits(), g.pos_f().to_bits(), "step {n}");
    }
}
