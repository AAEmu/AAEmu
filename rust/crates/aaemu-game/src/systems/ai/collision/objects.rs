//! Colliders of world objects the world server owns (doodads; houses and gimmicks plug in the
//! same way).
//!
//! The zone builds them when the world server pushes the object (`WZCreateDoodad` →
//! x2game `sub_393641A0` → `sub_39453AD0` → holder ctor `sub_39451EC0`):
//!
//! * the func group's model path picks the class by prefix (factory `sub_39451CE0`, table
//!   `off_3A5A20D8`): `prefab://` and `cga://` → `ZdModelPrefab` (`sub_394519E0`: an entity
//!   built from the prefab, static, or rigid when the parent unit is a vehicle, kind 3),
//!   `cgf://` → `ZdModelBrush` (`sub_394514E0`: `CreateSimpleBrush(path, xform, bPhysicalize = 1)`
//!   = a static brush of the CGF), `vegetation://` → `ZdModelVegetation` (`sub_39451840`), anything
//!   else → `ZdModelDummy` (no geometry);
//! * `_UpdateCollisionParams` (`sub_39451190`) then sets `pe_params_part` flags on every part from
//!   the `doodad_almighties` columns: `flagsOR = collide_vehicle·0x08 | collide_ship·0x80`,
//!   `flagsAND` clears 0x80 / 0x08 unless the column is set; `no_collision` → `flagsOR = 0x8000`,
//!   `flagsAND = 0` (rays only). Vegetation doodads with `no_collision` are physicalized without
//!   the call (`Physicalize(0)`); the result is the same ray-only part.
//!
//! AAEmu sends nothing to a zone, so [`sync_world`] diffs the doodads of the loaded cells
//! against the colliders every residency tick (and [`sync_doodad`] can be called directly
//! after a change).

use std::collections::{HashMap, HashSet};
use std::sync::Arc;

use aaemu_physics::math::{Mat33, Quat, Vec3};

use crate::server::GameServer;
use crate::world::{WorldInstance, WorldObject};

use super::cells::normalize_model_path;
use super::models::{ModelCache, ModelPhys};
use super::world::{ColliderKey, ObjKind, PhysWorld};
use super::Files;

/// The class `sub_39451CE0` picks for a model path.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ModelRef {
    /// `cgf://` (prefix stripped, normalised path).
    Brush(String),
    /// `vegetation://`.
    Vegetation(String),
    /// `prefab://library.xml/prefab.name` or `cga://` (full reference).
    Prefab { library: String, name: String },
    /// `cga://path`.
    Cga(String),
    /// No geometry (`ZdModelDummy`).
    Dummy,
}

/// The prefix table `off_3A5A20D8` (`prefab://` 1, `cgf://` 2, `cga://` 3, `cga_loop://` 4,
/// `vegetation://` 5, `entity://` 6); case-insensitive, leading blanks ignored.
pub fn parse_model_ref(model: &str) -> ModelRef {
    let m = model.trim();
    let lower = m.to_ascii_lowercase();
    let strip = |p: &str| m[p.len()..].to_string();
    if lower.starts_with("cgf://") {
        ModelRef::Brush(normalize_model_path(&strip("cgf://")))
    } else if lower.starts_with("vegetation://") {
        ModelRef::Vegetation(normalize_model_path(&strip("vegetation://")))
    } else if lower.starts_with("cga_loop://") {
        ModelRef::Cga(normalize_model_path(&strip("cga_loop://")))
    } else if lower.starts_with("cga://") {
        ModelRef::Cga(normalize_model_path(&strip("cga://")))
    } else if lower.starts_with("prefab://") {
        // prefab://prefabs/interaction_e.xml/tools.lever_left
        let rest = strip("prefab://").replace('\\', "/");
        match rest.to_ascii_lowercase().find(".xml/") {
            Some(i) => ModelRef::Prefab {
                library: normalize_model_path(&rest[..i + 4]),
                name: rest[i + 5..].to_string(),
            },
            None => ModelRef::Dummy,
        }
    } else {
        ModelRef::Dummy
    }
}

/// `_UpdateCollisionParams` (x2game `sub_39451190`): `(flagsOR, flagsAND)` from the template's
/// `collide_ship` (byte +192 bit 2), `collide_vehicle` (bit 3) and `no_collision` (byte +193
/// bit 2).
pub fn collision_flags(
    collide_ship: bool,
    collide_vehicle: bool,
    no_collision: bool,
) -> (u32, u32) {
    if no_collision {
        return (0x8000, 0);
    }
    let b192 = (u32::from(collide_ship) << 2) | (u32::from(collide_vehicle) << 3);
    let or = (b192 & 8) | (32 * (b192 & 4));
    let and = ((32 * b192) | 0xFFFF_FF7F) & (b192 | 0xFFFF_FFF7);
    (or, and)
}

/// One model of a collider shape, placed in the object's frame.
#[derive(Debug, Clone)]
pub struct ShapePart {
    pub model: Arc<ModelPhys>,
    pub pos: Vec3,
    pub q: Quat,
    pub scale: f32,
}

/// The models of an object collider (one for brushes / vegetation, the brushes of a prefab).
#[derive(Debug, Clone, Default)]
pub struct ColliderShape {
    pub parts: Vec<ShapePart>,
}

fn parse_vec(s: &str) -> Vec<f32> {
    s.split(',').filter_map(|v| v.trim().parse().ok()).collect()
}

/// The brushes of a prefab (`<PrefabsLibrary><Prefab Name><Objects><Object Type="Brush"
/// Prefab=… Pos Rotate(w,x,y,z) Scale>`), as model path + local placement.
pub fn prefab_brushes(xml: &str, name: &str) -> Vec<(String, Vec3, Quat, f32)> {
    prefab_brushes_with_prefix(xml, name, "")
}

/// The same local placements, restricted to brush names with a case-insensitive prefix.
pub fn prefab_brushes_with_prefix(
    xml: &str,
    name: &str,
    prefix: &str,
) -> Vec<(String, Vec3, Quat, f32)> {
    prefab_geometry(xml, name, prefix, false)
}

/// Housing prefabs also contain static AnimObject geometry (the 1.2 origin guard towers).
/// Include only explicitly physicalized, non-articulated objects with non-positive mass and
/// density; animated rigid bodies with dynamics keep their owning subsystem's physics.
pub fn prefab_house_geometry(xml: &str, name: &str) -> Vec<(String, Vec3, Quat, f32)> {
    prefab_geometry(xml, name, "", true)
}

fn prefab_geometry(
    xml: &str,
    name: &str,
    prefix: &str,
    static_anim_objects: bool,
) -> Vec<(String, Vec3, Quat, f32)> {
    let Ok(doc) = roxmltree::Document::parse(xml) else {
        return Vec::new();
    };
    let Some(prefab) = doc.descendants().find(|n| {
        n.has_tag_name("Prefab")
            && n.attribute("Name")
                .is_some_and(|v| v.eq_ignore_ascii_case(name))
    }) else {
        return Vec::new();
    };
    let mut out = Vec::new();
    let prefix = prefix.to_ascii_lowercase();
    for o in prefab.descendants().filter(|n| n.has_tag_name("Object")) {
        let model = if o.attribute("Type") == Some("Brush") {
            o.attribute("Prefab")
        } else if static_anim_objects
            && o.attribute("Type") == Some("Entity")
            && o.attribute("EntityClass") == Some("AnimObject")
        {
            let Some(properties) = o.children().find(|n| n.has_tag_name("Properties")) else {
                continue;
            };
            let Some(physics) = properties.children().find(|n| n.has_tag_name("Physics")) else {
                continue;
            };
            let static_body = physics.attribute("bPhysicalize") == Some("1")
                && physics.attribute("bArticulated") == Some("0")
                && physics
                    .attribute("Mass")
                    .and_then(|v| v.parse::<f32>().ok())
                    .is_some_and(|v| v <= 0.0)
                && physics
                    .attribute("Density")
                    .and_then(|v| v.parse::<f32>().ok())
                    .is_some_and(|v| v <= 0.0);
            let delayed = properties
                .children()
                .find(|n| n.has_tag_name("Animation"))
                .is_some_and(|n| n.attribute("bPhysicalizeAfterAnimation") == Some("1"));
            if !static_body || delayed {
                continue;
            }
            properties.attribute("object_Model")
        } else {
            continue;
        };
        if !o
            .attribute("Name")
            .unwrap_or("")
            .to_ascii_lowercase()
            .starts_with(&prefix)
        {
            continue;
        }
        let Some(model) = model.filter(|s| !s.trim().is_empty()) else {
            continue;
        };
        let p = parse_vec(o.attribute("Pos").unwrap_or("0,0,0"));
        let r = parse_vec(o.attribute("Rotate").unwrap_or("1,0,0,0"));
        let s = parse_vec(o.attribute("Scale").unwrap_or("1"));
        let pos = Vec3::new(
            p.first().copied().unwrap_or(0.0),
            p.get(1).copied().unwrap_or(0.0),
            p.get(2).copied().unwrap_or(0.0),
        );
        let q = if r.len() == 4 {
            Quat::new(r[1], r[2], r[3], r[0])
        } else {
            Quat::IDENTITY
        };
        out.push((
            normalize_model_path(model),
            pos,
            q,
            s.first().copied().unwrap_or(1.0),
        ));
    }
    out
}

/// Resolves a model reference to its collider shape (`None` = dummy / no physics).
pub fn resolve_shape(
    files: &Files,
    models: &ModelCache,
    prefab_xml: &mut HashMap<String, Option<String>>,
    r: &ModelRef,
) -> Option<ColliderShape> {
    let single = |path: &str| -> Option<ColliderShape> {
        let m = models.get(path)?;
        Some(ColliderShape {
            parts: vec![ShapePart {
                model: m,
                pos: Vec3::ZERO,
                q: Quat::IDENTITY,
                scale: 1.0,
            }],
        })
    };
    match r {
        ModelRef::Brush(p) | ModelRef::Vegetation(p) | ModelRef::Cga(p) => single(p),
        ModelRef::Prefab { library, name } => {
            let xml = prefab_xml
                .entry(library.clone())
                .or_insert_with(|| {
                    files
                        .read(library)
                        .map(|b| crate::data::client_files::decode_text(&b))
                })
                .clone()?;
            let parts: Vec<ShapePart> = prefab_brushes(&xml, name)
                .into_iter()
                .filter_map(|(path, pos, q, scale)| {
                    Some(ShapePart {
                        model: models.get(&path)?,
                        pos,
                        q,
                        scale,
                    })
                })
                .collect();
            (!parts.is_empty()).then_some(ColliderShape { parts })
        }
        ModelRef::Dummy => None,
    }
}

/// The world placement of a doodad as the zone receives it (position, quaternion, scale).
pub fn object_placement(world: &WorldInstance, obj: &WorldObject, scale: f32) -> (Vec3, Quat, f32) {
    let p = world.world_pos_rot(obj);
    let q = p.to_quaternion();
    (
        Vec3::new(p.x, p.y, p.z),
        Quat::new(q.x, q.y, q.z, q.w),
        scale,
    )
}

/// Builds the collider key and the parts' world placement.
pub fn shape_key(model: &str, pos: Vec3, q: Quat, scale: f32, flags: (u32, u32)) -> ColliderKey {
    ColliderKey {
        model: model.to_string(),
        pos: [pos.x, pos.y, pos.z],
        q: [q.x, q.y, q.z, q.w],
        scale,
        flags_or: flags.0,
        flags_and: flags.1,
    }
}

/// Composes a part's local placement with the object's.
pub fn compose(obj_pos: Vec3, obj_q: Quat, obj_scale: f32, part: &ShapePart) -> (Vec3, Quat, f32) {
    let m = Mat33::from_quat(obj_q);
    let lp = part.pos * obj_scale;
    (
        obj_pos + m.mul_vec(lp),
        obj_q.product(part.q),
        obj_scale * part.scale,
    )
}

/// What the sync needs about one doodad.
#[derive(Debug, Clone)]
pub struct DoodadInfo {
    pub obj_id: u32,
    pub model: String,
    pub pos: Vec3,
    pub q: Quat,
    pub scale: f32,
    pub flags: (u32, u32),
}

/// The doodads of the loaded cells of a world with their collider inputs.
pub fn collect_doodads(
    server: &GameServer,
    world: &WorldInstance,
    cells: &[(i32, i32)],
) -> Vec<DoodadInfo> {
    let data = &server.game_data.systems.npcs.doodad;
    let models = &server.game_data.systems.ai.collision;
    let mut out = Vec::new();
    for &(cx, cy) in cells {
        for o in world.objects_in_cell(cx, cy) {
            let Some(d) = o.as_doodad() else {
                continue;
            };
            let (template_id, func_group, scale) = {
                let dd = d.data();
                (dd.template_id, dd.func_group_id, dd.scale)
            };
            let Some(model) = models.func_group_model(func_group) else {
                continue;
            };
            let Some(t) = data.template(template_id) else {
                continue;
            };
            let (pos, q, scale) = object_placement(world, &o, scale);
            out.push(DoodadInfo {
                obj_id: o.obj_id(),
                model: model.to_string(),
                pos,
                q,
                scale,
                flags: collision_flags(t.collide_ship, t.collide_vehicle, t.no_collision),
            });
        }
    }
    out
}

/// Applies the doodad list to the world's colliders (adds, moves, re-flags, removes).
pub fn apply_doodads(
    pw: &PhysWorld,
    files: &Files,
    models: &ModelCache,
    shapes: &mut HashMap<String, Option<ColliderShape>>,
    doodads: &[DoodadInfo],
) -> (usize, usize) {
    let mut prefab_xml = HashMap::new();
    let mut resolved: Vec<(DoodadInfo, Option<ColliderShape>)> = Vec::new();
    for d in doodads {
        let shape = shapes
            .entry(d.model.clone())
            .or_insert_with(|| {
                resolve_shape(files, models, &mut prefab_xml, &parse_model_ref(&d.model))
            })
            .clone();
        resolved.push((d.clone(), shape));
    }
    let mut st = pw.lock();
    let keep: HashSet<u32> = doodads.iter().map(|d| d.obj_id).collect();
    let stale: Vec<u32> = st
        .objects
        .keys()
        .filter(|(k, id)| *k == ObjKind::Doodad && !keep.contains(id))
        .map(|(_, id)| *id)
        .collect();
    for id in &stale {
        st.remove_object_collider(ObjKind::Doodad, *id);
    }
    let mut added = 0;
    for (d, shape) in resolved {
        let key = shape_key(&d.model, d.pos, d.q, d.scale, d.flags);
        if st.set_object_shape(ObjKind::Doodad, d.obj_id, key, shape.as_ref(), false) {
            added += 1;
        }
    }
    (added, stale.len())
}

/// Housing uses the model of the current construction step. Normal prefab state 1 can
/// contain several elements; all of them are needed for the walls, roof and floor. The zone
/// swaps the unit model in Housing::SetBuildStep (10.x 0x3933ABD0), and records completion in
/// Housing::SetBuildDone (0x3933AB50).
#[derive(Debug, Clone)]
pub struct HouseInfo {
    pub obj_id: u32,
    pub models: Vec<String>,
    pub pos: Vec3,
    pub q: Quat,
    pub scale: f32,
}

fn house_info(server: &GameServer, world: &WorldInstance, obj: &WorldObject) -> Option<HouseInfo> {
    let house = obj.as_house()?;
    let (model_id, scale) = {
        let h = house.data();
        (h.unit.model_id, h.unit.scale)
    };
    let refs = server
        .game_data
        .systems
        .ai
        .collision
        .prefab_models
        .get(&model_id)?;
    let (pos, q, scale) = object_placement(world, obj, scale);
    Some(HouseInfo {
        obj_id: obj.obj_id(),
        models: refs.clone(),
        pos,
        q,
        scale,
    })
}

pub fn resolve_house_shape(
    files: &Files,
    models: &ModelCache,
    prefab_xml: &mut HashMap<String, Option<String>>,
    reference: &ModelRef,
) -> Option<ColliderShape> {
    let ModelRef::Prefab { library, name } = reference else {
        return resolve_shape(files, models, prefab_xml, reference);
    };
    let xml = prefab_xml
        .entry(library.clone())
        .or_insert_with(|| {
            files
                .read(library)
                .map(|bytes| crate::data::client_files::decode_text(&bytes))
        })
        .clone()?;
    let parts = prefab_house_geometry(&xml, name)
        .into_iter()
        .filter_map(|(path, pos, q, scale)| {
            Some(ShapePart {
                model: models.get(&path)?,
                pos,
                q,
                scale,
            })
        })
        .collect::<Vec<_>>();
    (!parts.is_empty()).then_some(ColliderShape { parts })
}

pub fn collect_houses(
    server: &GameServer,
    world: &WorldInstance,
    cells: &[(i32, i32)],
) -> Vec<HouseInfo> {
    cells
        .iter()
        .flat_map(|&(x, y)| world.objects_in_cell(x, y))
        .filter_map(|obj| house_info(server, world, &obj))
        .collect()
}

fn apply_house(
    pw: &PhysWorld,
    files: &Files,
    models: &ModelCache,
    shapes: &mut HashMap<String, Option<ColliderShape>>,
    house: &HouseInfo,
) -> bool {
    let model = format!("house:{}", house.models.join("|"));
    let shape = shapes
        .entry(model.clone())
        .or_insert_with(|| {
            let mut prefab_xml = HashMap::new();
            let parts = house
                .models
                .iter()
                .filter_map(|reference| {
                    resolve_house_shape(files, models, &mut prefab_xml, &parse_model_ref(reference))
                })
                .flat_map(|shape| shape.parts)
                .collect::<Vec<_>>();
            (!parts.is_empty()).then_some(ColliderShape { parts })
        })
        .clone();
    // Houses are static brushes and keep the authored collision-slot flags.
    let key = shape_key(&model, house.pos, house.q, house.scale, (0, u32::MAX));
    pw.lock()
        .set_object_shape(ObjKind::House, house.obj_id, key, shape.as_ref(), false)
}

/// Applies every house in the loaded cells and removes colliders of deleted/unloaded houses.
pub fn apply_houses(
    pw: &PhysWorld,
    files: &Files,
    models: &ModelCache,
    shapes: &mut HashMap<String, Option<ColliderShape>>,
    houses: &[HouseInfo],
) -> (usize, usize) {
    let keep: HashSet<u32> = houses.iter().map(|house| house.obj_id).collect();
    let stale = {
        let st = pw.lock();
        st.objects
            .keys()
            .filter(|(kind, id)| *kind == ObjKind::House && !keep.contains(id))
            .map(|(_, id)| *id)
            .collect::<Vec<_>>()
    };
    for id in &stale {
        pw.lock().remove_object_collider(ObjKind::House, *id);
    }
    let added = houses
        .iter()
        .filter(|house| apply_house(pw, files, models, shapes, house))
        .count();
    (added, stale.len())
}

/// Installs housing geometry when a cell is first loaded by a query, before the next
/// residency tick. This keeps house floors available immediately after entering a cell.
pub fn sync_houses(server: &GameServer, pw: &PhysWorld) {
    if !server.config.physics.doodad_colliders
        && server.systems.ai.collision.test_files.get().is_none()
    {
        return;
    }
    let Some(world) = server.world.world(pw.instance_id) else {
        return;
    };
    let Some(files) = super::files(server) else {
        return;
    };
    let models = super::model_cache(server, &files);
    let cells = pw.lock().cells.keys().copied().collect::<Vec<_>>();
    let houses = collect_houses(server, &world, &cells);
    let mut shapes = server.systems.ai.collision.shape_cache();
    apply_houses(pw, &files, &models, &mut shapes, &houses);
}

/// Syncs doodad and housing colliders of one world (the residency tick).
pub fn sync_world(server: &GameServer, pw: &Arc<PhysWorld>) {
    if !server.config.physics.doodad_colliders
        && server.systems.ai.collision.test_files.get().is_none()
    {
        return;
    }
    let Some(world) = server.world.world(pw.instance_id) else {
        return;
    };
    let Some(files) = super::files(server) else {
        return;
    };
    let models = super::model_cache(server, &files);
    let cells: Vec<(i32, i32)> = pw.lock().cells.keys().copied().collect();
    let doodads = collect_doodads(server, &world, &cells);
    let houses = collect_houses(server, &world, &cells);
    let mut shapes = server.systems.ai.collision.shape_cache();
    apply_doodads(pw, &files, &models, &mut shapes, &doodads);
    apply_houses(pw, &files, &models, &mut shapes, &houses);
    drop(shapes);
}

/// Rebuilds one doodad's collider now (after a phase change / move), if its cell is loaded.
pub fn sync_doodad(server: &GameServer, obj: &WorldObject) {
    let Some(d) = obj.as_doodad() else {
        return;
    };
    let instance_id = d.data().transform.instance_id;
    let Some(pw) = server.systems.ai.collision.existing(instance_id) else {
        return;
    };
    let Some(world) = server.world.world(instance_id) else {
        return;
    };
    let p = world.world_pos_rot(obj);
    let Some(cell) = pw.cell_of(p.x, p.y) else {
        return;
    };
    if !pw.lock().cells.contains_key(&cell) {
        return;
    }
    let Some(files) = super::files(server) else {
        return;
    };
    let models = super::model_cache(server, &files);
    let infos: Vec<DoodadInfo> = collect_doodads(server, &world, &[cell])
        .into_iter()
        .filter(|i| i.obj_id == obj.obj_id())
        .collect();
    let mut shapes = server.systems.ai.collision.shape_cache();
    let mut prefab_xml = HashMap::new();
    match infos.first() {
        Some(i) => {
            let shape = shapes
                .entry(i.model.clone())
                .or_insert_with(|| {
                    resolve_shape(&files, &models, &mut prefab_xml, &parse_model_ref(&i.model))
                })
                .clone();
            let key = shape_key(&i.model, i.pos, i.q, i.scale, i.flags);
            pw.lock()
                .set_object_shape(ObjKind::Doodad, i.obj_id, key, shape.as_ref(), false);
        }
        None => {
            pw.lock()
                .remove_object_collider(ObjKind::Doodad, obj.obj_id());
        }
    }
}

/// Removes a despawned doodad's collider.
pub fn remove_doodad(server: &GameServer, instance_id: u32, obj_id: u32) {
    if let Some(pw) = server.systems.ai.collision.existing(instance_id) {
        pw.lock().remove_object_collider(ObjKind::Doodad, obj_id);
    }
}

/// Immediately replaces a spawned house's collider after a construction step/model change.
pub fn sync_house(server: &GameServer, house: &crate::world::HouseRef) {
    if !server.config.physics.doodad_colliders
        && server.systems.ai.collision.test_files.get().is_none()
    {
        return;
    }
    let instance_id = house.data().unit.transform.instance_id;
    let Some(pw) = server.systems.ai.collision.existing(instance_id) else {
        return;
    };
    let Some(world) = server.world.world(instance_id) else {
        return;
    };
    if world.house(house.obj_id).is_none() {
        return;
    }
    let Some(info) = house_info(server, &world, &WorldObject::from(house)) else {
        pw.lock()
            .remove_object_collider(ObjKind::House, house.obj_id);
        return;
    };
    let Some(cell) = pw.cell_of(info.pos.x, info.pos.y) else {
        return;
    };
    if !pw.lock().cells.contains_key(&cell) {
        return;
    }
    let Some(files) = super::files(server) else {
        return;
    };
    let models = super::model_cache(server, &files);
    let mut shapes = server.systems.ai.collision.shape_cache();
    apply_house(&pw, &files, &models, &mut shapes, &info);
}

pub fn remove_house(server: &GameServer, instance_id: u32, obj_id: u32) {
    if let Some(pw) = server.systems.ai.collision.existing(instance_id) {
        pw.lock().remove_object_collider(ObjKind::House, obj_id);
    }
}

#[cfg(test)]
#[path = "housing_collision_tests.rs"]
mod housing_collision_tests;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn model_prefixes() {
        assert_eq!(
            parse_model_ref("cgf://objects/Env/a.cgf"),
            ModelRef::Brush("game/objects/env/a.cgf".into())
        );
        assert_eq!(
            parse_model_ref(" \tvegetation://Objects/tree.cgf"),
            ModelRef::Vegetation("game/objects/tree.cgf".into())
        );
        assert_eq!(
            parse_model_ref("prefab://prefabs/interaction_e.xml/tools.lever_left"),
            ModelRef::Prefab {
                library: "game/prefabs/interaction_e.xml".into(),
                name: "tools.lever_left".into()
            }
        );
        assert_eq!(parse_model_ref("a://invalid"), ModelRef::Dummy);
        assert_eq!(parse_model_ref(""), ModelRef::Dummy);
    }

    #[test]
    fn update_collision_params_flags() {
        // neither: clear 0x80 and 0x08
        assert_eq!(collision_flags(false, false, false), (0, 0xFFFF_FF77));
        assert_eq!(collision_flags(true, false, false), (0x80, 0xFFFF_FFF7));
        assert_eq!(collision_flags(false, true, false), (0x08, 0xFFFF_FF7F));
        assert_eq!(collision_flags(true, true, false), (0x88, 0xFFFF_FFFF));
        assert_eq!(collision_flags(true, true, true), (0x8000, 0));
        // applied to the brush default 0x18FFF
        let (o, a) = collision_flags(false, false, false);
        assert_eq!((0x18FFF & a) | o, 0x18F77);
    }

    #[test]
    fn prefab_brushes_parse() {
        let xml = r#"<PrefabsLibrary Name="x"><Prefab Name="a.b"><Objects>
            <Object Type="Brush" Prefab="Objects\env\a.cgf" Pos="1,2,3" Rotate="0.70710671,0.70710683,0,0" Scale="2,2,2"/>
            <Object Type="Entity" Name="e"/>
            </Objects></Prefab></PrefabsLibrary>"#;
        let b = prefab_brushes(xml, "a.b");
        assert_eq!(b.len(), 1);
        assert_eq!(b[0].0, "game/objects/env/a.cgf");
        assert_eq!(b[0].1, Vec3::new(1.0, 2.0, 3.0));
        assert_eq!(b[0].2.w, "0.70710671".parse::<f32>().unwrap());
        assert_eq!(b[0].3, 2.0);
        assert!(prefab_brushes(xml, "missing").is_empty());
    }

    #[test]
    fn prefab_buoys_only_include_named_brushes_of_the_selected_prefab() {
        let xml = r#"<PrefabsLibrary><Prefab Name="boat.a"><Objects>
            <Object Type="Brush" Name="hull" Prefab="Objects/hull.cgf"/>
            <Object Type="Brush" Name="Buoy0" Prefab="Objects/box.cgf" Pos="0,-3,0.15" Scale="2.5,2.5,2.5"/>
            <Object Type="Entity" Name="buoy1" Prefab="Objects/box.cgf"/>
            </Objects></Prefab><Prefab Name="boat.b"><Objects>
            <Object Type="Brush" Name="buoy0" Prefab="Objects/other.cgf"/>
            </Objects></Prefab></PrefabsLibrary>"#;
        let parts = prefab_brushes_with_prefix(xml, "BOAT.A", "BUOY");
        assert_eq!(parts.len(), 1);
        assert_eq!(parts[0].0, "game/objects/box.cgf");
        assert_eq!(parts[0].1, Vec3::new(0.0, -3.0, 0.15));
        assert_eq!(parts[0].3, 2.5);
        assert_eq!(prefab_brushes(xml, "boat.a").len(), 2);
        assert!(prefab_brushes_with_prefix(xml, "boat.a", "missing").is_empty());
    }
}
