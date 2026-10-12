//! C->S packets of the skills subsystem: `CSStartSkillPacket`, `CSStopCastingPacket`,
//! `CSRemoveBuffPacket`, `CSLearnSkillPacket`, `CSLearnBuffPacket`, `CSResetSkillsPacket`,
//! `CSSwapAbilityPacket`, `CSSkillControllerStatePacket`, `CSCreateSkillControllerPacket`,
//! `CSSetForceAttackPacket`, `CSChangeTargetPacket`, `CSBuyPriestBuffPacket`.

use aaemu_commons::PacketStream;
use futures::future::BoxFuture;

use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::registry::ClientPacket;
use crate::packets::world_objects::SCTargetChangedPacket;
use crate::server::GameServer;
use crate::world::WorldObject;

use super::packets::{
    SCPlotCastingStoppedPacket, SCPlotChannelingStoppedPacket, SCSkillStartedPacket,
};
use super::state::{SkillRef, SkillRun, SkillTaskKind};
use super::types::{skill_result, SkillCastTarget, SkillCaster, SkillObject};
use super::{broadcast, buffs, combat, progress, skill};

/// `CSStartSkillPacket`.
#[derive(Debug, Clone, PartialEq)]
pub struct CSStartSkillPacket {
    pub skill_id: u32,
    pub caster: SkillCaster,
    pub target: SkillCastTarget,
    pub flag: u8,
    pub skill_object: SkillObject,
}

impl ClientPacket for CSStartSkillPacket {
    const NAME: &'static str = "CSStartSkillPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        let skill_id = s.read_u32()?;
        let ct = s.read_u8()?;
        let caster = SkillCaster::read(ct, s)?;
        let tt = s.read_u8()?;
        let target = SkillCastTarget::read(tt, s)?;
        let flag = s.read_u8()?;
        let flag_type = flag & 15;
        let skill_object = if flag_type > 0 {
            SkillObject::read(flag_type, s)?
        } else {
            SkillObject::None
        };
        Ok(Self {
            skill_id,
            caster,
            target,
            flag,
            skill_object,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            if conn.active_char.is_none() {
                return Ok(());
            }
            // "Will delay for 150 Milliseconds to eliminate the hanging of the skill" (100 ms)
            tokio::time::sleep(std::time::Duration::from_millis(100)).await;
            start_skill(server, conn, self);
            Ok(())
        })
    }
}

fn template_skill(server: &GameServer, id: u32) -> Option<SkillRef> {
    match server.game_data.systems.skills.skill(id).cloned() {
        Some(t) => Some(SkillRun::new(t, 1)),
        None => {
            tracing::error!("StartSkill: skill {id} has no template (NullReferenceException)");
            None
        }
    }
}

/// `CSStartSkillPacket.Read` behaviour.
pub fn start_skill(server: &GameServer, conn: &mut GameConnection, p: CSStartSkillPacket) {
    let Some(player) = conn.active_char.clone() else {
        return;
    };
    let me = WorldObject::from(&player);
    let mut target = p.target;
    let skill_id = p.skill_id;
    tracing::info!(
        "StartSkill: Id {skill_id}, flag {}, caster={}, target={}",
        p.flag,
        p.caster.obj_id(),
        target.obj_id()
    );
    let result;
    let mut value = 0u32;
    let data = &server.game_data;
    let is_default = data.skills.is_default_skill(skill_id);
    let is_common = data.skills.is_common_skill(skill_id);
    let auto = player.data().unit.skill_state.auto_attack.clone();
    let is_auto = player.data().unit.is_auto_attack;
    if let SkillCaster::Mount { obj_id, .. } = p.caster {
        // Mount / slave skill
        let Some(skill) = template_skill(server, skill_id) else {
            return;
        };
        let Some(caster) = super::geometry::world_of(server, &me).and_then(|w| w.object(obj_id))
        else {
            return;
        };
        if caster
            .as_mate()
            .is_some_and(|mate| !super::mount_authority::may_use_mate(&player, mate))
        {
            return;
        }
        // StormShipLogic.TryBlockMountOrSlaveSkill (vehicles + ai storm buff)
        if let Some(slave) = caster.as_slave() {
            if crate::systems::vehicles::ship_physics::storm_blocks_slave_skill(
                server, slave, skill_id,
            ) {
                // Otherwise the skill press can feel like "nothing happens" client-side.
                player.send(crate::packets::world::SCErrorMsgPacket::new(
                    crate::systems::economy::error_message::NOT_READY,
                    0,
                    true,
                ));
                let mut pkt =
                    SCSkillStartedPacket::new(skill_id, 0, p.caster, target, p.skill_object);
                pkt.set_skill_result(skill_result::NO_PERM)
                    .set_result_uint(0);
                player.send(pkt);
                return;
            }
        }
        let (r, _v) = skill::use_skill(
            server,
            &skill,
            &caster,
            p.caster,
            &mut target,
            Some(p.skill_object.clone()),
            false,
        );
        // Harpoon rope controller + MateGameData.GetMountAttachedSkills (vehicles subsystem)
        let mount_attached_skill = crate::systems::vehicles::on_mount_skill_used(
            server,
            &player,
            &caster,
            skill_id,
            r == skill_result::SUCCESS,
            &target,
        );
        // If no rider/operator skill is linked, we can stop here
        if mount_attached_skill == 0 {
            return;
        }
        // Use player's currently selected target for the rider/operator skill
        let current = player.data().unit.current_target;
        let rider_target = super::geometry::world_of(server, &me)
            .and_then(|w| w.object(current))
            .filter(WorldObject::is_unit)
            .unwrap_or_else(|| me.clone());
        result = skill::unit_use_skill(server, &me, mount_attached_skill, &rider_target);
    } else if is_auto
        && auto
            .as_ref()
            .is_some_and(|a| a.skill.template.id == skill_id)
    {
        result = skill_result::SUCCESS;
    } else if is_default || (is_common && !p.caster.is_item()) {
        let Some(skill) = template_skill(server, skill_id) else {
            return;
        };
        let (r, v) = skill::use_skill(
            server,
            &skill,
            &me,
            p.caster,
            &mut target,
            Some(p.skill_object.clone()),
            false,
        );
        result = r;
        value = v;
        if result == skill_result::SUCCESS && skill_id < 5000 && p.caster.obj_id() == player.obj_id
        {
            player.data().unit.is_auto_attack = true;
            skill::start_auto_skill(server, &me, &skill);
        }
    } else if let SkillCaster::Item { item_id, .. } = p.caster {
        let items = server.systems.skills.ext.items();
        let Some((template_id, _)) = items.owned_item(server, &player, item_id) else {
            return;
        };
        let Some((use_skill_id, bind_on_pickup)) = items.item_use_skill(server, template_id) else {
            return;
        };
        if skill_id != use_skill_id && !bind_on_pickup {
            return;
        }
        let Some(skill) = template_skill(server, skill_id) else {
            return;
        };
        // The legacy bind-on-pickup exception supports fireplace portal actions, but
        // binding alone cannot authorize imprinting or bleaching a UCC. Require the
        // actual item's configured use skill at this client boundary; internal casts
        // and plot children continue through the server-driven skill APIs.
        if skill_id != use_skill_id
            && skill.template.effects.iter().any(|effect| {
                matches!(
                    effect.template,
                    Some((
                        super::data::EffectKind::ImprintUccEffect
                            | super::data::EffectKind::CleanupUccEffect,
                        _
                    ))
                )
            })
        {
            return;
        }
        let (r, v) = skill::use_skill(
            server,
            &skill,
            &me,
            p.caster,
            &mut target,
            Some(p.skill_object.clone()),
            false,
        );
        result = r;
        value = v;
    } else if player.data().skills.skills.iter().any(|s| s.id == skill_id) {
        let Some(t) = server.game_data.systems.skills.skill(skill_id).cloned() else {
            return;
        };
        let skill = skill::new_skill_for(server, t, Some(&me));
        let (r, v) = skill::use_skill(
            server,
            &skill,
            &me,
            p.caster,
            &mut target,
            Some(p.skill_object.clone()),
            false,
        );
        result = r;
        value = v;
    } else if skill_id > 0 && is_variant_of_skill(server, &player, skill_id) {
        let Some(skill) = template_skill(server, skill_id) else {
            return;
        };
        let (r, v) = skill::use_skill(
            server,
            &skill,
            &me,
            p.caster,
            &mut target,
            Some(p.skill_object.clone()),
            false,
        );
        result = r;
        value = v;
    } else {
        tracing::warn!("StartSkill: Id {skill_id}, undefined use type");
        let Some(skill) = template_skill(server, skill_id) else {
            return;
        };
        let (r, v) = skill::use_skill(
            server,
            &skill,
            &me,
            p.caster,
            &mut target,
            Some(p.skill_object.clone()),
            false,
        );
        result = r;
        value = v;
    }
    if result != skill_result::SUCCESS {
        let mut pkt = SCSkillStartedPacket::new(skill_id, 0, p.caster, target, p.skill_object);
        pkt.set_skill_result(result).set_result_uint(value);
        player.send(pkt);
    }
}

#[cfg(test)]
#[path = "ucc_item_authority_tests.rs"]
mod ucc_item_authority_tests;

/// `CharacterSkills.IsVariantOfSkill(skillId)`.
fn is_variant_of_skill(
    server: &GameServer,
    player: &crate::world::PlayerRef,
    skill_id: u32,
) -> bool {
    let data = &server.game_data.systems.skills;
    let Some(t) = data.skill(skill_id) else {
        return false;
    };
    let learned: Vec<u32> = player.data().skills.skills.iter().map(|s| s.id).collect();
    learned.into_iter().any(|id| {
        data.skill(id)
            .is_some_and(|s| s.ability_id == t.ability_id && s.ability_level == t.ability_level)
    })
}

/// `CSStopCastingPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSStopCastingPacket {
    pub tl_id: u16,
    pub plot_tl_id: u16,
    pub obj_id: u32,
}

impl ClientPacket for CSStopCastingPacket {
    const NAME: &'static str = "CSStopCastingPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            tl_id: s.read_u16()?,
            plot_tl_id: s.read_u16()?,
            obj_id: s.read_bc()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            stop_casting(server, conn, self);
            Ok(())
        })
    }
}

fn stop_casting(server: &GameServer, conn: &mut GameConnection, p: CSStopCastingPacket) {
    let Some(player) = conn.active_char.clone() else {
        tracing::error!("CSStopCastingPacket without an active character (NullReferenceException)");
        return;
    };
    if player.obj_id != p.obj_id {
        tracing::warn!(
            "Player (ObjId {}) is trying to stop casting a skill on object {} using TlId {} and plotTlId {}",
            player.obj_id,
            p.obj_id,
            p.tl_id,
            p.plot_tl_id
        );
        return;
    }
    let me = WorldObject::from(&player);
    let mut plot_cancel = false;
    let plot = player.data().unit.skill_state.active_plot.clone();
    if p.plot_tl_id != 0 {
        if let Some(plot) = plot {
            if plot.0.skill.tl_id() == p.plot_tl_id {
                plot.0.request_cancellation();
                plot_cancel = true;
            } else {
                conn.send(&SCPlotCastingStoppedPacket {
                    tl: p.plot_tl_id,
                    duration: 0,
                    last_event: 1,
                });
                conn.send(&SCPlotChannelingStoppedPacket {
                    tl: p.plot_tl_id,
                    duration: 0,
                    last_event: 1,
                });
            }
        }
    }
    let task = player.data().unit.skill_state.skill_task.clone();
    let Some(task) = task else {
        if !plot_cancel {
            tracing::warn!(
                "Stop requested, but no skill active? Tl: {}, Pid: {}, objId: {}",
                p.tl_id,
                p.plot_tl_id,
                p.obj_id
            );
        }
        return;
    };
    let active = task.skill.tl_id();
    let matches = if p.tl_id != 0 {
        active == p.tl_id || active == p.plot_tl_id
    } else {
        p.plot_tl_id != 0 && active == p.plot_tl_id
    };
    if !matches {
        tracing::warn!(
            "Stop requested for another skill? Tl: {}, Pid: {}, ActiveTl: {active}",
            p.tl_id,
            p.plot_tl_id
        );
        return;
    }
    task.cancel.cancel();
    match task.kind {
        SkillTaskKind::EndChanneling { channel_doodad } => {
            skill::stop(server, &task.skill, &me, channel_doodad)
        }
        SkillTaskKind::Cast => skill::stop(server, &task.skill, &me, 0),
    }
}

/// `CSRemoveBuffPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRemoveBuffPacket {
    pub obj_id: u32,
    pub buff_index: u32,
    pub reason: u8,
}

impl ClientPacket for CSRemoveBuffPacket {
    const NAME: &'static str = "CSRemoveBuffPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
            buff_index: s.read_u32()?,
            reason: s.read_u8()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(player) = conn.active_char.clone() else {
                return Ok(());
            };
            let me = WorldObject::from(&player);
            let remove = |o: &WorldObject| -> bool {
                match buffs::get_buff(o, self.buff_index) {
                    None => false,
                    Some(b) => {
                        if buffs::full_template(server, &b.template).kind == 1 {
                            buffs::exit(server, o, b.index, false);
                        }
                        true
                    }
                }
            };
            if player.obj_id == self.obj_id && remove(&me) {
                return Ok(());
            }
            // Pets (`MateManager.GetActiveMates(...).FirstOrDefault(x => x.Id == objId)`) and
            // vehicles (`SlaveManager.GetSlaveByObjId`): owned mates / slaves in the world.
            if let Some(o) =
                super::geometry::world_of(server, &me).and_then(|w| w.object(self.obj_id))
            {
                if o.as_mate()
                    .is_some_and(|m| m.data().owner_obj_id == player.obj_id)
                    || o.as_slave()
                        .is_some_and(|s| s.data().summoner_id == player.character_id)
                {
                    remove(&o);
                }
            }
            Ok(())
        })
    }
}

macro_rules! simple_handler {
    ($(#[$m:meta])* $name:ident { $($field:ident : $ty:ty = $read:ident),* $(,)? } => |$server:ident, $conn:ident, $p:ident| $body:block) => {
        $(#[$m])*
        #[derive(Debug, Clone, Copy, PartialEq)]
        pub struct $name {
            $(pub $field: $ty,)*
        }
        impl ClientPacket for $name {
            const NAME: &'static str = stringify!($name);
            fn read(s: &mut PacketStream) -> Result<Self> {
                Ok(Self { $($field: s.$read()?,)* })
            }
            fn handle<'a>(self, $server: &'a GameServer, $conn: &'a mut GameConnection) -> BoxFuture<'a, Result<()>> {
                let $p = self;
                Box::pin(async move { $body Ok(()) })
            }
        }
    };
}

simple_handler!(
    /// `CSLearnSkillPacket`.
    CSLearnSkillPacket { skill_id: u32 = read_u32 } => |server, conn, p| {
        if let Some(c) = conn.active_char.clone() {
            progress::learn_skill(server, &c, p.skill_id);
        }
    }
);

simple_handler!(
    /// `CSLearnBuffPacket`.
    CSLearnBuffPacket { buff_id: u32 = read_u32 } => |server, conn, p| {
        if let Some(c) = conn.active_char.clone() {
            progress::learn_buff(server, &c, p.buff_id);
        }
    }
);

simple_handler!(
    /// `CSResetSkillsPacket`.
    CSResetSkillsPacket { ability_id: u8 = read_u8, ausp: bool = read_bool } => |server, conn, p| {
        if let Some(c) = conn.active_char.clone() {
            progress::reset_skills(server, &c, p.ability_id);
        }
    }
);

simple_handler!(
    /// `CSSwapAbilityPacket`.
    CSSwapAbilityPacket { obj_id: u32 = read_bc, old_ability: u8 = read_u8, ability: u8 = read_u8, auap: bool = read_bool } => |server, conn, p| {
        if let Some(c) = conn.active_char.clone() {
            progress::swap_ability(server, &c, p.old_ability, p.ability);
        }
    }
);

simple_handler!(
    /// `CSSetForceAttackPacket`.
    CSSetForceAttackPacket { on: bool = read_bool } => |server, conn, p| {
        if let Some(c) = conn.active_char.clone() {
            combat::set_force_attack(server, &WorldObject::from(&c), p.on);
        }
    }
);

simple_handler!(
    /// `CSCreateSkillControllerPacket` (logged only, as in the C#).
    CSCreateSkillControllerPacket { obj_id: u32 = read_bc, sc_type: u8 = read_u8, fall_damage_immune: bool = read_bool } => |_server, _conn, p| {
        tracing::debug!("CSCreateSkillController objId={} scType={} fallDamageImmune={}", p.obj_id, p.sc_type, p.fall_damage_immune);
    }
);

simple_handler!(
    /// `CSBuyPriestBuffPacket` (logged only, as in the C#).
    CSBuyPriestBuffPacket { priest_buff_id: u32 = read_u32, npc_unit_id: u32 = read_bc } => |_server, _conn, p| {
        tracing::warn!("BuyPriestBuff, PriestBuffId: {}, NpcUnitId: {}", p.priest_buff_id, p.npc_unit_id);
    }
);

/// `CSSkillControllerStatePacket` (`scType == 0`: rope length / teared / cutouted).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CSSkillControllerStatePacket {
    pub obj_id: u32,
    pub sc_type: u8,
    pub rope: Option<(f32, bool, bool)>,
}

impl ClientPacket for CSSkillControllerStatePacket {
    const NAME: &'static str = "CSSkillControllerStatePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        let obj_id = s.read_bc()?;
        let sc_type = s.read_u8()?;
        let rope = if sc_type == 0 {
            Some((s.read_f32()?, s.read_bool()?, s.read_bool()?))
        } else {
            None
        };
        Ok(Self {
            obj_id,
            sc_type,
            rope,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!(
                "CSSkillControllerState objId={} scType={} {:?}",
                self.obj_id,
                self.sc_type,
                self.rope
            );
            // ShipHarpoonRopeController.TryApplySkillControllerState (vehicles subsystem)
            if let (Some((len, teared, cutouted)), Some(p)) = (self.rope, _conn.active_char.clone())
            {
                crate::systems::vehicles::harpoon::try_apply_skill_controller_state(
                    _server,
                    &p,
                    self.obj_id,
                    len,
                    teared,
                    cutouted,
                );
            }
            Ok(())
        })
    }
}

/// `CSChangeTargetPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSChangeTargetPacket {
    pub target_id: u32,
}

impl ClientPacket for CSChangeTargetPacket {
    const NAME: &'static str = "CSChangeTargetPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            target_id: s.read_bc()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(c) = conn.active_char.clone() else {
                return Ok(());
            };
            let me = WorldObject::from(&c);
            let target = if self.target_id > 0 {
                super::geometry::world_of(server, &me)
                    .and_then(|w| w.unit(self.target_id))
                    .map_or(0, |u| u.obj_id())
            } else {
                0
            };
            c.data().unit.current_target = target;
            broadcast(
                server,
                &me,
                &SCTargetChangedPacket {
                    obj_id: c.obj_id,
                    target_obj_id: target,
                },
                true,
            );
            Ok(())
        })
    }
}
