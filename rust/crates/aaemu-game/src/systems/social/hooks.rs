//! Behaviour the social subsystem needs from other subsystems (installed by their owners in
//! their `System::start` through `server.systems.social.hooks.install(..)`). Every method
//! defaults to the "not ported yet" behaviour documented on it, so the social flows run on
//! their own.

use std::sync::Arc;

use crate::server::GameServer;
use crate::systems::HookSlot;
use crate::world::{DoodadRef, PlayerRef};

/// What `DoodadFuncEvidenceItemLoot` (a phase func of a crime evidence doodad) contributes to
/// `CrimeManager.ReportCrime`.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct EvidenceFunc {
    /// `CrimeKindId` (`CrimeKind`).
    pub crime_kind: u8,
    pub crime_value: i16,
    /// The doodad func's `NextPhase`.
    pub next_phase: i32,
}

/// Cross-subsystem services used by teams / duels / crimes / trials.
pub trait SocialHooks: Send + Sync {
    /// npcs: `new DoodadSpawner { UnitId = templateId, Position = .. }.Spawn(0)` in the world
    /// instance (the duel's combat flag). Default: no doodad subsystem -> `None` (the duel runs
    /// without a flag; distance checks then use the challenger's start position).
    fn spawn_doodad(
        &self,
        _server: &GameServer,
        _instance_id: u32,
        _template_id: u32,
        _pos: (f32, f32, f32),
        _rot_z: f32,
    ) -> Option<DoodadRef> {
        None
    }

    /// npcs: `WorldInstance.Template.GeoData.GetHeight(x, y)`. Default: no height maps.
    fn terrain_height(
        &self,
        _server: &GameServer,
        _instance_id: u32,
        _x: f32,
        _y: f32,
    ) -> Option<f32> {
        None
    }

    /// npcs: `DoodadManager.CreatePlayerDoodad(criminal, templateId, x, y, z, zRot, 1, 0,
    /// FarmGroupKind.Invalid, itemTemplateId, customData, true)` (crime evidence). Default:
    /// `None` (no evidence is created).
    #[allow(clippy::too_many_arguments)]
    fn create_player_doodad(
        &self,
        _server: &GameServer,
        _owner: &PlayerRef,
        _template_id: u32,
        _pos: (f32, f32, f32),
        _rot_z: f32,
        _item_template_id: u32,
        _custom_data: i32,
    ) -> Option<DoodadRef> {
        None
    }

    /// npcs: the `DoodadFuncEvidenceItemLoot` of the doodad's current funcs
    /// (`DoodadManager.GetFuncTemplate`). Default: `None` (`CrimeKind.Invalid`, 0 points).
    fn evidence_func(&self, _server: &GameServer, _doodad: &DoodadRef) -> Option<EvidenceFunc> {
        None
    }

    /// npcs: `Doodad.DoChangePhase(caster, nextPhase)`. Default: no-op.
    fn doodad_change_phase(
        &self,
        _server: &GameServer,
        _doodad: &DoodadRef,
        _caster: &PlayerRef,
        _next_phase: i32,
    ) {
    }

    /// housing: `HousingManager.UpdateOwnedHousingFaction(characterId, factionId)`. Default:
    /// the housing subsystem.
    fn update_owned_housing_faction(
        &self,
        server: &GameServer,
        character_id: u32,
        faction_id: u32,
    ) {
        crate::systems::housing::update_owned_housing_faction(server, character_id, faction_id);
    }

    /// npcs (mates / slaves): `Character.ForceDismount()`.
    fn force_dismount(&self, _server: &GameServer, _player: &PlayerRef) {}

    /// skills: `Character.PostUpdateCurrentHp(attacker, oldHp, newHp, KillReason)` after the
    /// trial resurrects a defendant. Default: no-op.
    fn post_update_current_hp(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _old_hp: i32,
        _new_hp: i32,
    ) {
    }

    /// The `NpcSpawner.SpawnedNpcs.First().ObjId` of a court room's judge (npcs spawners with
    /// `SpecialLink = "justice.<court>.judge"`). Default: 0.
    fn judge_obj_id(&self, _server: &GameServer, _court_room_id: u32) -> u32 {
        0
    }

    /// skills: `Buffs.CheckBuffTag(tagId)` of a player (buff tags need the skill tag tables,
    /// which are not ported). Called without the player's data lock held, possibly while the
    /// trial / crime manager holds its own state lock: must not call back into them.
    /// Default: `false` (no buff carries a tag).
    fn check_buff_tag(&self, _server: &GameServer, _player: &PlayerRef, _tag: u32) -> bool {
        false
    }

    /// skills: `Buffs.RemoveBuffs(kind, count, tagId)` of a player. Called without any lock
    /// held. Default: no-op (no buff carries a tag).
    fn remove_buffs_by_tag(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        _kind: crate::data::skills::BuffKind,
        _count: i32,
        _tag: u32,
    ) {
    }
}

/// `server.systems.social.hooks`.
#[derive(Debug, Default)]
pub struct SocialHookSlot(HookSlot<dyn SocialHooks>);

/// The defaults of every hook.
struct NoHooks;
impl SocialHooks for NoHooks {}

impl SocialHookSlot {
    pub fn install(&self, hooks: Arc<dyn SocialHooks>) {
        self.0.install(hooks);
    }

    /// The installed hooks or the defaults.
    pub fn get(&self) -> Arc<dyn SocialHooks> {
        self.0.get().unwrap_or_else(|| Arc::new(NoHooks))
    }
}
