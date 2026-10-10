//! `Scripts/SubCommands/Doodads/{DoodadRemoveSubCommand, DoodadRemovesSubCommand}.cs`.

use super::super::super::prelude::*;
use super::{choice_param, sub_base, uint_param};
use crate::world::DoodadRef;

/// `Vector3.Distance` of two world positions.
fn distance_3d(
    a: &crate::models::transform::PositionAndRotation,
    b: &crate::models::transform::PositionAndRotation,
) -> f32 {
    let (dx, dy, dz) = (a.x - b.x, a.y - b.y, a.z - b.z);
    (dx * dx + dy * dy + dz * dz).sqrt()
}

/// `GetNearestDoodad(character, radius)`.
fn get_nearest_doodad(
    server: &GameServer,
    character: &PlayerRef,
    radius: f32,
) -> Option<DoodadRef> {
    // Получаем список объектов Doodad
    let doodads = around_of(server, character, radius, |o| o.as_doodad().cloned());
    let me = world_pos(server, &WorldObject::from(character));
    let mut nearest = None;
    let mut min_distance = f32::MAX;
    for doodad in doodads {
        let distance = distance_3d(&me, &world_pos(server, &WorldObject::from(&doodad)));
        if distance.partial_cmp(&min_distance) != Some(std::cmp::Ordering::Less) {
            continue;
        }
        nearest = Some(doodad);
        min_distance = distance;
    }
    nearest
}

/// Remove Doodad: `doodad.Spawner.Id = 0xffffffff; doodad.Hide();` and the report.
fn remove_doodad(
    base: &SubCommandBase,
    server: &GameServer,
    doodad: &DoodadRef,
    out: &mut dyn MessageOutput,
) -> anyhow::Result<()> {
    // removed from the game manually (not saved to doodad_spawns_new.json by /save all);
    // `doodad.Spawner` is null for doodads without a spawner (NullReferenceException).
    if server
        .systems
        .npcs
        .spawns
        .doodad_spawner_of(doodad.obj_id)
        .is_none()
    {
        return Err(nre());
    }
    let template_id = {
        let mut d = doodad.data();
        d.spawner_id = 0xffff_ffff;
        d.template_id
    };
    if let Some(world) = world_of_obj(server, &WorldObject::from(doodad)) {
        world.hide(doodad);
    }
    let msg = format!(
        "Doodad @DOODAD_NAME({template_id}), ObjId: {}, TemplateId:{template_id} removed successfully",
        doodad.obj_id
    );
    base.send_message(out, &msg);
    tracing::warn!("{msg}");
    Ok(())
}

/// The `Task.Run(() => { try { .. } catch (Exception ex) { Logger.Error(..) } })` wrapper
/// (run synchronously here).
fn run_logged(f: impl FnOnce() -> anyhow::Result<()>) {
    if let Err(ex) = f() {
        tracing::error!("Ошибка при выполнении метода: {ex}");
    }
}

/// `DoodadRemoveSubCommand`.
pub struct DoodadRemoveSubCommand {
    base: SubCommandBase,
}

impl Default for DoodadRemoveSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad Remove]",
                "Remove using an Doodad <ObjId> or using ObjId = 0 for the nearest Doodad",
                &format!("{COMMAND_PREFIX}remove"),
                vec![
                    choice_param("target", &["all", "id"]),
                    uint_param("ObjId", "object id", false),
                ],
            ),
        }
    }
}

impl DoodadRemoveSubCommand {
    fn remove_all(
        &self,
        ctx: &CommandContext<'_>,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let Some(nearest) = get_nearest_doodad(server, &character, 5.0) else {
            self.base
                .send_color_message(out, Color::RED, "Nearest Doodad does not exist");
            tracing::warn!("Nearest Doodad does not exist");
            return Ok(());
        };
        let instance = character.data().unit.transform.instance_id;
        let current_world = server.world.world(instance).ok_or_else(nre)?;
        let template_id = nearest.data().template_id;
        let mut doodads = current_world.all_doodads();
        doodads.sort_by_key(|d| d.obj_id);
        for doodad in doodads
            .iter()
            .filter(|d| d.data().template_id == template_id)
        {
            remove_doodad(&self.base, server, doodad, out)?;
        }
        Ok(())
    }

    fn remove_by_id(
        &self,
        ctx: &CommandContext<'_>,
        doodad_obj_id: u32,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let world = parent_world(server, &character).ok_or_else(nre)?;
        let doodad = match world
            .doodad(doodad_obj_id)
            .or_else(|| get_nearest_doodad(server, &character, 5.0))
        {
            Some(d) => d,
            None => {
                self.base.send_color_message(
                    out,
                    Color::RED,
                    &format!("Doodad with objId {doodad_obj_id} does not exist"),
                );
                tracing::warn!("Doodad with objId {doodad_obj_id} does not exist");
                return Ok(());
            }
        };
        remove_doodad(&self.base, server, &doodad, out)
    }
}

impl SubCommand for DoodadRemoveSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // C#: runs on a background task; exceptions are only logged.
        run_logged(|| match parameters.get("ObjId") {
            Some(v) => self.remove_by_id(ctx, v.as_u32()?, out),
            None => self.remove_all(ctx, out),
        });
        Ok(())
    }
}

/// `DoodadRemovesSubCommand`.
pub struct DoodadRemovesSubCommand {
    base: SubCommandBase,
}

impl Default for DoodadRemovesSubCommand {
    fn default() -> Self {
        Self {
            base: sub_base(
                "[Doodad Removes]",
                "Remove the nearest Doodads around a character within a <r> radius",
                &format!("{COMMAND_PREFIX}removes"),
                vec![uint_param("Radius", "radius", true)],
            ),
        }
    }
}

impl DoodadRemovesSubCommand {
    fn remove_all(
        &self,
        ctx: &CommandContext<'_>,
        radius: u32,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        let server = ctx.server()?;
        let character = ctx.player()?;
        let Some(nearest) = get_nearest_doodad(server, &character, 5.0) else {
            self.base
                .send_color_message(out, Color::RED, "Nearest Doodad does not exist");
            tracing::warn!("Nearest Doodad does not exist");
            return Ok(());
        };

        // GetDoodadsInRadius(character, radius, templateId)
        let template_id = nearest.data().template_id;
        let radius = radius as f32;
        let me = world_pos(server, &WorldObject::from(&character));
        let doodads_in_radius: Vec<DoodadRef> =
            around_of(server, &character, radius, |o| o.as_doodad().cloned())
                .into_iter()
                .filter(|d| {
                    distance_3d(&me, &world_pos(server, &WorldObject::from(d))) <= radius
                        && d.data().template_id == template_id
                })
                .collect();
        for doodad in &doodads_in_radius {
            remove_doodad(&self.base, server, doodad, out)?;
        }
        Ok(())
    }
}

impl SubCommand for DoodadRemovesSubCommand {
    fn base(&self) -> &SubCommandBase {
        &self.base
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _trigger: &str,
        parameters: &Parameters,
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // C#: runs on a background task; exceptions are only logged.
        run_logged(|| match parameters.get("Radius") {
            Some(v) => self.remove_all(ctx, v.as_u32()?, out),
            None => Ok(()),
        });
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::super::super::super::harness;
    use super::super::super::super::util::objects_test_support as ts;

    #[tokio::test]
    async fn doodad_remove_and_removes_hide_spawned_doodads() {
        let server = ts::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let world = server.world.world(0).unwrap();
        let a = ts::spawn_doodad_with_spawner(&server, ts::DOODAD, 101.0, 100.0, world.id);
        let b = ts::spawn_doodad_with_spawner(&server, ts::DOODAD, 104.0, 100.0, world.id);
        let c = ts::spawn_doodad_with_spawner(&server, ts::DOODAD, 120.0, 100.0, world.id);

        let (_, out) =
            harness::run(&server, &gm.p, &format!("doodad remove id {}", a.obj_id)).await;
        assert_eq!(
            out,
            [format!(
                "[Doodad Remove] Doodad @DOODAD_NAME({0}), ObjId: {1}, TemplateId:{0} removed successfully",
                ts::DOODAD,
                a.obj_id
            )]
        );
        assert!(!a.is_visible());
        assert_eq!(a.data().spawner_id, 0xffff_ffff);

        let (_, out) = harness::run(&server, &gm.p, "doodad removes 10").await;
        // Hidden doodads leave their region: only `b` is found.
        assert_eq!(out.len(), 1);
        assert!(!b.is_visible());
        assert!(c.is_visible());

        let (_, out) = harness::run(&server, &gm.p, "doodad remove all").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Doodad Remove] Nearest Doodad does not exist|r"]
        );
        let e = ts::spawn_doodad_with_spawner(&server, ts::DOODAD, 101.0, 100.0, world.id);
        let (_, out) = harness::run(&server, &gm.p, "doodad remove all").await;
        // Every doodad of the template in the world (hidden ones included).
        assert_eq!(out.len(), 4);
        assert!(!c.is_visible() && !e.is_visible());

        let (_, out) = harness::run(&server, &gm.p, "doodad remove bad").await;
        assert_eq!(
            out,
            ["|cFFFF0000[Doodad Remove] Parameter [target] only accepts: all||id|r"]
        );
    }
}
