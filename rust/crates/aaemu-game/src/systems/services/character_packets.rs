//! The remaining small character packets (`Core/Packets/C2G` handlers that only touch the
//! character, the connection or the log): active weapon, visual options, appellation, idle
//! status, UI data, action slots, restrict check, LP management, character list refresh and
//! the log-only requests.

use aaemu_commons::{Marshal, PacketStream};
use futures::future::BoxFuture;

use crate::error::Result;
use crate::models::char_data::{ActionSlotType, VisualOptions};
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::{ClientPacket, PacketRegistry};
use crate::opcodes::{cs, sc};
use crate::packets::packet_def;
use crate::packets::world::SCResponseUIDataPacket;
use crate::server::GameServer;
use crate::world::{broadcast, PlayerRef, WorldObject};

/// `BroadcastPacket(packet, self)` of the active character.
fn broadcast_from(
    server: &GameServer,
    p: &PlayerRef,
    packet: &dyn crate::net::packet::ServerPacket,
    include_self: bool,
) {
    let me = WorldObject::from(p);
    match server.world.world_of(&me) {
        Some(world) => broadcast(&world, &me, packet, include_self),
        None if include_self => p.send_dyn(packet),
        None => {}
    }
}

/// `SCActiveWeaponChangedPacket(objId, activeWeapon)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCActiveWeaponChangedPacket {
    pub obj_id: u32,
    pub active_weapon: u8,
}
packet_def!(
    SCActiveWeaponChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCActiveWeaponChangedPacket
);
impl Marshal for SCActiveWeaponChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_u8(self.active_weapon);
    }
}

/// `SCUnitVisualOptionsPacket(id, visualOptions)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCUnitVisualOptionsPacket {
    pub obj_id: u32,
    pub options: VisualOptions,
}
packet_def!(
    SCUnitVisualOptionsPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitVisualOptionsPacket
);
impl Marshal for SCUnitVisualOptionsPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id);
        // `CharacterVisualOptions.Write`: the flag, then the flagged members (a missing `Stp`
        // throws in C#: the packet ends there).
        let o = &self.options;
        s.write_u8(o.flag);
        if o.flag & 1 == 1 {
            match &o.stp {
                Some(stp) => {
                    s.write_raw(stp);
                }
                None => return,
            }
        }
        if o.flag & 2 == 2 {
            s.write_bool(o.helmet);
        }
        if o.flag & 4 == 4 {
            s.write_bool(o.back_holdable);
        }
        if o.flag & 8 == 8 {
            s.write_bool(o.cosplay);
        }
        if o.flag & 16 == 16 {
            s.write_bool(o.cosplay_backpack);
        }
    }
}

/// `SCAppellationChangedPacket(objId, appellationId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCAppellationChangedPacket {
    pub obj_id: u32,
    pub appellation_id: u32,
}
packet_def!(
    SCAppellationChangedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAppellationChangedPacket
);
impl Marshal for SCAppellationChangedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_u32(self.appellation_id);
    }
}

/// `SCUnitIdleStatusPacket(id, status)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCUnitIdleStatusPacket {
    pub obj_id: u32,
    pub status: bool,
}
packet_def!(
    SCUnitIdleStatusPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUnitIdleStatusPacket
);
impl Marshal for SCUnitIdleStatusPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.obj_id).write_bool(self.status);
    }
}

/// `SCRefreshInCharacterListPacket()`: nine `RACE_CONGESTION.LOW` bytes.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCRefreshInCharacterListPacket;
packet_def!(
    SCRefreshInCharacterListPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCRefreshInCharacterListPacket
);
impl Marshal for SCRefreshInCharacterListPacket {
    fn write(&self, s: &mut PacketStream) {
        for _ in 0..9 {
            s.write_u8(0);
        }
    }
}

/// `SCResultRestrictCheckPacket(characterId, code, result)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCResultRestrictCheckPacket {
    pub character_id: u32,
    pub code: u8,
    pub result: u8,
}
packet_def!(
    SCResultRestrictCheckPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCResultRestrictCheckPacket
);
impl Marshal for SCResultRestrictCheckPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id)
            .write_u8(self.code)
            .write_u8(self.result);
    }
}

/// `SCLpManagedPacket(characterId)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SCLpManagedPacket {
    pub character_id: u32,
}
packet_def!(
    SCLpManagedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCLpManagedPacket
);
impl Marshal for SCLpManagedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.character_id);
    }
}

/// Declares a C->S packet with a `Read` that only reads and logs.
macro_rules! log_only_packet {
    ($name:ident, $level:ident, |$s:ident| $read:block) => {
        #[doc = concat!("`", stringify!($name), "` (read and logged only, as in C#).")]
        #[derive(Debug, Clone, PartialEq)]
        pub struct $name(pub String);

        impl ClientPacket for $name {
            const NAME: &'static str = stringify!($name);

            fn read($s: &mut PacketStream) -> Result<Self> {
                Ok(Self($read))
            }

            fn handle<'a>(
                self,
                _server: &'a GameServer,
                _conn: &'a mut GameConnection,
            ) -> BoxFuture<'a, Result<()>> {
                Box::pin(async move {
                    tracing::$level!("{}{}", stringify!($name), self.0);
                    Ok(())
                })
            }
        }
    };
}

#[derive(Debug, Clone, Copy)]
pub struct CSCleanupLogicLinkPacket {
    pub obj_id: u32,
}
impl ClientPacket for CSCleanupLogicLinkPacket {
    const NAME: &'static str = "CSCleanupLogicLinkPacket";
    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            obj_id: s.read_bc()?,
        })
    }
    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            if let Some(player) = &conn.active_char {
                if let Some(world) = server.world.world_of(&WorldObject::from(player)) {
                    if let Some(doodad) = world.doodad(self.obj_id) {
                        if doodad.data().owner_id == player.character_id {
                            let ctx = crate::systems::npcs::doodad::runtime::DoodadCtx::new(
                                server,
                                world.clone(),
                                doodad,
                            );
                            crate::systems::npcs::doodad::logic::clear_links(&ctx);
                        }
                    }
                }
            }
            Ok(())
        })
    }
}
log_only_packet!(CSConsoleCmdUsedPacket, debug, |s| {
    format!(", Cmd: {}", s.read_string()?)
});
log_only_packet!(CSCriminalLockedPacket, warn, |s| {
    format!(", Id: {}", s.read_u32()?)
});
log_only_packet!(CSEditorGameModePacket, debug, |s| {
    let on = s.read_bool()?;
    let _x = s.read_i64()?;
    let _y = s.read_i64()?;
    let _z = s.read_f32()?;
    format!(", On: {on}")
});
/// `CSRankCharacterPacket` (empty body): the client's own ranking rows, answered with
/// `SCRankCharacterPacket` (`systems::social::ranking_manager`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRankCharacterPacket;

impl ClientPacket for CSRankCharacterPacket {
    const NAME: &'static str = "CSRankCharacterPacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            crate::systems::social::ranking_manager::handle_rank_character(server, conn);
            Ok(())
        })
    }
}
log_only_packet!(CSRequestPermissionToPlayCinemaForDirectingMode, warn, |s| {
    let _quest_context_id = s.read_u32()?;
    let _npc = s.read_bc()?;
    let _doodad = s.read_bc()?;
    String::new()
});
log_only_packet!(CSRequestSecondPasswordKeyTablesPacket, debug, |s| {
    let _ = s;
    String::new()
});
log_only_packet!(CSSetupSecondPassword, debug, |s| {
    let _ = s;
    String::new()
});
log_only_packet!(CSResturnAddrsPacket, warn, |s| {
    let code_base = s.read_u32()?;
    let code_size = s.read_u32()?;
    let f = s.read_u32()?;
    format!(", CodeBase: {code_base}, CodeSize: {code_size}, Fn: {f}")
});

/// Public character inspection by name, using the 1.2 character-detail packet.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSCharDetailPacket {
    pub name: String,
}

impl ClientPacket for CSCharDetailPacket {
    const NAME: &'static str = "CSCharDetailPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            name: s.read_string()?,
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
            let Some(target) = server.world.character_by_name(&self.name) else {
                return Ok(());
            };
            let packet = {
                let character = target.data();
                if !character.is_online {
                    return Ok(());
                }
                crate::packets::g2c_misc::SCCharDetailPacket::new(&character, true)
            };
            conn.send(&packet);
            Ok(())
        })
    }
}

/// Character id-to-name/race lookup. The native brief cache sends this request on a miss.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRequestCharBriefPacket {
    pub id: u32,
}

impl ClientPacket for CSRequestCharBriefPacket {
    const NAME: &'static str = "CSRequestCharBriefPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self { id: s.read_u32()? })
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
            let online_packet = server.world.character_by_id(self.id).and_then(|target| {
                let character = target.data();
                character
                    .is_online
                    .then(|| crate::packets::g2c_misc::SCCharBriefPacket {
                        char_id: character.id,
                        name: character.unit.name.clone(),
                        race: character.race,
                    })
            });
            let packet = match online_packet {
                Some(packet) => Some(packet),
                None => crate::systems::housing::dominion::brief::lookup(server, self.id).await?,
            };
            if let Some(packet) = packet {
                conn.send(&packet);
            }
            Ok(())
        })
    }
}

/// `CSActiveWeaponChangedPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSActiveWeaponChangedPacket {
    pub active_weapon: u8,
}

impl ClientPacket for CSActiveWeaponChangedPacket {
    const NAME: &'static str = "CSActiveWeaponChangedPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            active_weapon: s.read_u8()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(p) = conn.active_char.clone() else {
                return Ok(());
            };
            p.data().unit.active_weapon = self.active_weapon;
            broadcast_from(
                server,
                &p,
                &SCActiveWeaponChangedPacket {
                    obj_id: p.obj_id,
                    active_weapon: self.active_weapon,
                },
                true,
            );
            Ok(())
        })
    }
}

/// `CSBroadcastVisualOptionPacket`: `VisualOptions.Read(stream)` updates only the flagged
/// members of the existing options.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSBroadcastVisualOptionPacket {
    pub options: VisualOptions,
}

impl ClientPacket for CSBroadcastVisualOptionPacket {
    const NAME: &'static str = "CSBroadcastVisualOptionPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            options: VisualOptions::read(s)?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(p) = conn.active_char.clone() else {
                return Ok(());
            };
            let options = {
                let mut c = p.data();
                let o = c.visual_options.get_or_insert_with(VisualOptions::default);
                let n = &self.options;
                o.flag = n.flag;
                if n.flag & 1 == 1 {
                    o.stp = n.stp;
                }
                if n.flag & 2 == 2 {
                    o.helmet = n.helmet;
                }
                if n.flag & 4 == 4 {
                    o.back_holdable = n.back_holdable;
                }
                if n.flag & 8 == 8 {
                    o.cosplay = n.cosplay;
                }
                if n.flag & 16 == 16 {
                    o.cosplay_backpack = n.cosplay_backpack;
                }
                o.clone()
            };
            broadcast_from(
                server,
                &p,
                &SCUnitVisualOptionsPacket {
                    obj_id: p.obj_id,
                    options,
                },
                true,
            );
            Ok(())
        })
    }
}

/// `CSChangeAppellationPacket` -> `CharacterAppellations.Change(id)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSChangeAppellationPacket {
    pub id: u32,
}

/// `CharacterAppellations.Change(id)`: swaps the title buff and broadcasts the change.
pub fn change_appellation(server: &GameServer, p: &PlayerRef, id: u32) {
    let me = WorldObject::from(p);
    let buff_of = |t: u32| {
        server
            .game_data
            .characters
            .appellations
            .get(&t)
            .copied()
            .unwrap_or(0)
    };
    let (active, owned, owner_id) = {
        let c = p.data();
        (
            c.appellations.active,
            c.appellations.appellations.contains(&id),
            c.id,
        )
    };
    let remove_current = || {
        if active != 0 {
            let buff = buff_of(active);
            if buff != 0 {
                crate::systems::skills::buffs::remove_buff(server, &me, buff);
            }
            tracing::info!("removing current buff: {buff}");
        }
    };
    if id == 0 {
        remove_current();
        p.data().appellations.active = 0;
    } else if owned {
        remove_current();
        p.data().appellations.active = id;
        let buff = buff_of(id);
        if buff != 0 {
            crate::systems::skills::buffs::add_buff_by_id(server, &me, buff, &me, 0);
        }
        tracing::info!("title: {id} giving buff {buff}");
    } else {
        tracing::warn!("Id {id} doesn't exist, owner {owner_id}");
    }
    let active = p.data().appellations.active;
    broadcast_from(
        server,
        p,
        &SCAppellationChangedPacket {
            obj_id: p.obj_id,
            appellation_id: active,
        },
        true,
    );
}

impl ClientPacket for CSChangeAppellationPacket {
    const NAME: &'static str = "CSChangeAppellationPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self { id: s.read_u32()? })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            if let Some(p) = conn.active_char.clone() {
                change_appellation(server, &p, self.id);
            }
            Ok(())
        })
    }
}

/// `CSIdleStatusPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSIdleStatusPacket {
    pub status: bool,
}

impl ClientPacket for CSIdleStatusPacket {
    const NAME: &'static str = "CSIdleStatusPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            status: s.read_bool()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(p) = conn.active_char.clone() else {
                return Ok(());
            };
            p.data().unit.idle_status = self.status;
            tracing::debug!("IdleStatus: BcId {}, {}", p.obj_id, self.status);
            broadcast_from(
                server,
                &p,
                &SCUnitIdleStatusPacket {
                    obj_id: p.obj_id,
                    status: self.status,
                },
                true,
            );
            Ok(())
        })
    }
}

/// `CSRefreshInCharacterListPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRefreshInCharacterListPacket;

impl ClientPacket for CSRefreshInCharacterListPacket {
    const NAME: &'static str = "CSRefreshInCharacterListPacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!("RefreshInCharacterList");
            conn.send(&SCRefreshInCharacterListPacket);
            Ok(())
        })
    }
}

/// `CSRequestUIDataPacket`: the option of a character of this connection.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRequestUIDataPacket {
    pub ui_data_type: u16,
    pub id: u32,
}

impl ClientPacket for CSRequestUIDataPacket {
    const NAME: &'static str = "CSRequestUIDataPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            ui_data_type: s.read_u16()?,
            id: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            // `Connection.Characters[id]` is the same object as `ActiveChar` in C#.
            let option = match &conn.active_char {
                Some(p) if p.character_id == self.id => {
                    Some(p.data().get_option(self.ui_data_type))
                }
                _ => conn
                    .characters
                    .iter()
                    .find(|c| c.id == self.id)
                    .map(|c| c.get_option(self.ui_data_type)),
            };
            if let Some(data) = option {
                conn.send(&SCResponseUIDataPacket {
                    character_id: self.id,
                    ui_data_type: self.ui_data_type,
                    ui_data: data,
                });
            }
            Ok(())
        })
    }
}

/// `CSSaveUIDataPacket` -> `ActiveChar.SetOption(type, data)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSaveUIDataPacket {
    pub ui_data_type: u16,
    pub id: u32,
    pub data: String,
}

impl ClientPacket for CSSaveUIDataPacket {
    const NAME: &'static str = "CSSaveUIDataPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            ui_data_type: s.read_u16()?,
            id: s.read_u32()?,
            data: s.read_string()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            if let Some(p) = &conn.active_char {
                p.data().options.insert(self.ui_data_type, self.data);
            }
            Ok(())
        })
    }
}

/// `CSUpdateActionSlotPacket` -> `ActiveChar.SetAction(slot, type, id)`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSUpdateActionSlotPacket {
    pub slot: u8,
    pub kind: ActionSlotType,
    pub id: u64,
}

impl ClientPacket for CSUpdateActionSlotPacket {
    const NAME: &'static str = "CSUpdateActionSlotPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        let slot = s.read_u8()?;
        let kind = ActionSlotType::from_u8(s.read_u8()?);
        let id = match kind {
            ActionSlotType::ItemType | ActionSlotType::Spell | ActionSlotType::RidePetSpell => {
                u64::from(s.read_u32()?)
            }
            ActionSlotType::ItemId => s.read_u64()?,
            _ => 0,
        };
        Ok(Self { slot, kind, id })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(p) = &conn.active_char else {
                return Ok(());
            };
            match self.kind {
                ActionSlotType::None
                | ActionSlotType::ItemType
                | ActionSlotType::Spell
                | ActionSlotType::RidePetSpell
                | ActionSlotType::ItemId => {
                    p.data()
                        .set_action(usize::from(self.slot), self.kind, self.id);
                }
                _ => tracing::error!("UpdateActionSlot, Unknown ActionSlotType!"),
            }
            Ok(())
        })
    }
}

/// `CSRestrictCheckPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRestrictCheckPacket {
    pub character_id: u32,
    pub code: u8,
}

impl ClientPacket for CSRestrictCheckPacket {
    const NAME: &'static str = "CSRestrictCheckPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            character_id: s.read_u32()?,
            code: s.read_u8()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            conn.send(&SCResultRestrictCheckPacket {
                character_id: self.character_id,
                code: self.code,
                result: 0,
            });
            Ok(())
        })
    }
}

/// `CSSetLpManageCharacterPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSSetLpManageCharacterPacket {
    pub character_id: u32,
}

impl ClientPacket for CSSetLpManageCharacterPacket {
    const NAME: &'static str = "CSSetLpManageCharacterPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            character_id: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            conn.send(&SCLpManagedPacket {
                character_id: self.character_id,
            });
            Ok(())
        })
    }
}

/// Registers the packets of this module.
pub fn register(reg: &mut PacketRegistry) {
    reg.register::<CSActiveWeaponChangedPacket>(LEVEL_PLAIN, cs::CSActiveWeaponChangedPacket);
    reg.register::<CSBroadcastVisualOptionPacket>(LEVEL_PLAIN, cs::CSBroadcastVisualOptionPacket);
    reg.register::<CSChangeAppellationPacket>(LEVEL_PLAIN, cs::CSChangeAppellationPacket);
    reg.register::<CSIdleStatusPacket>(LEVEL_PLAIN, cs::CSIdleStatusPacket);
    reg.register::<CSRefreshInCharacterListPacket>(LEVEL_PLAIN, cs::CSRefreshInCharacterListPacket);
    reg.register::<CSRequestUIDataPacket>(LEVEL_PLAIN, cs::CSRequestUIDataPacket);
    reg.register::<CSSaveUIDataPacket>(LEVEL_PLAIN, cs::CSSaveUIDataPacket);
    reg.register::<CSUpdateActionSlotPacket>(LEVEL_PLAIN, cs::CSUpdateActionSlotPacket);
    reg.register::<CSRestrictCheckPacket>(LEVEL_PLAIN, cs::CSRestrictCheckPacket);
    reg.register::<CSSetLpManageCharacterPacket>(LEVEL_PLAIN, cs::CSSetLpManageCharacterPacket);
    reg.register::<CSCharDetailPacket>(LEVEL_PLAIN, cs::CSCharDetailPacket);
    reg.register::<CSCleanupLogicLinkPacket>(LEVEL_PLAIN, cs::CSCleanupLogicLinkPacket);
    reg.register::<CSConsoleCmdUsedPacket>(LEVEL_PLAIN, cs::CSConsoleCmdUsedPacket);
    reg.register::<CSCriminalLockedPacket>(LEVEL_PLAIN, cs::CSCriminalLockedPacket);
    reg.register::<CSEditorGameModePacket>(LEVEL_PLAIN, cs::CSEditorGameModePacket);
    reg.register::<CSRankCharacterPacket>(LEVEL_PLAIN, cs::CSRankCharacterPacket);
    reg.register::<CSRequestCharBriefPacket>(LEVEL_PLAIN, cs::CSRequestCharBriefPacket);
    reg.register::<CSRequestPermissionToPlayCinemaForDirectingMode>(
        LEVEL_PLAIN,
        cs::CSRequestPermissionToPlayCinemaForDirectingMode,
    );
    reg.register::<CSRequestSecondPasswordKeyTablesPacket>(
        LEVEL_PLAIN,
        cs::CSRequestSecondPasswordKeyTablesPacket,
    );
    reg.register::<CSSetupSecondPassword>(LEVEL_PLAIN, cs::CSSetupSecondPassword);
    reg.register::<CSResturnAddrsPacket>(LEVEL_PLAIN, cs::CSResturnAddrsPacket);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;
    use crate::test_support::{spawn_player, test_character, test_server, TestClient};

    fn body(p: &dyn crate::net::packet::ServerPacket) -> Vec<u8> {
        encode(p)[6..].to_vec()
    }

    #[test]
    fn packets_are_byte_exact() {
        let mut e = PacketStream::new();
        e.write_bc(5).write_u8(2);
        assert_eq!(
            body(&SCActiveWeaponChangedPacket {
                obj_id: 5,
                active_weapon: 2
            }),
            e.into_bytes()
        );
        let mut e = PacketStream::new();
        e.write_bc(5)
            .write_u8(3)
            .write_raw(&[1, 2, 3, 4, 5, 6])
            .write_bool(true);
        assert_eq!(
            body(&SCUnitVisualOptionsPacket {
                obj_id: 5,
                options: VisualOptions {
                    flag: 3,
                    stp: Some([1, 2, 3, 4, 5, 6]),
                    helmet: true,
                    ..VisualOptions::default()
                }
            }),
            e.into_bytes()
        );
        assert_eq!(body(&SCRefreshInCharacterListPacket), vec![0; 9]);
        let mut e = PacketStream::new();
        e.write_u32(7).write_u8(1).write_u8(0);
        assert_eq!(
            body(&SCResultRestrictCheckPacket {
                character_id: 7,
                code: 1,
                result: 0
            }),
            e.into_bytes()
        );
    }

    #[tokio::test]
    async fn character_queries_return_online_profile_only_to_authenticated_requester() {
        use crate::net::packet::encode_raw;
        let server = test_server();
        let mut a = TestClient::new(&server, 1, 1001);
        let mut b = TestClient::new(&server, 2, 1002);
        let pa = spawn_player(&server, &a, test_character(1, "Al", 10.0, 10.0), 0x100);
        let pb = spawn_player(&server, &b, test_character(2, "Bo", 12.0, 10.0), 0x101);
        a.conn.active_char = Some(pa);
        b.conn.active_char = Some(pb.clone());
        {
            let mut target = pb.data();
            target.is_online = true;
            target.race = 5;
            target.unit.hp = 77;
            target.unit.level = 17;
            target.ability1 = 1;
            target.ability2 = 2;
            target.ability3 = 3;
            target.leave_time = 4242;
        }
        a.drain();
        b.drain();
        let mut brief = PacketStream::new();
        brief.write_u32(2);
        let mut detail = PacketStream::new();
        detail.write_str("bO"); // Existing world lookup is case insensitive.
        for (opcode, request) in [
            (cs::CSRequestCharBriefPacket, &brief),
            (cs::CSCharDetailPacket, &detail),
        ] {
            server
                .handle_frame(&mut a.conn, &encode_raw(1, opcode, request.as_bytes()))
                .await
                .unwrap();
        }
        let mut expected_detail = PacketStream::new();
        expected_detail
            .write_u32(2)
            .write_str("Bo")
            .write_u8(5)
            .write_i32(7700)
            .write_u8(17)
            .write_u8(1)
            .write_u8(2)
            .write_u8(3)
            .write_i64((12i64 * 4096) << 32)
            .write_i64((10i64 * 4096) << 32)
            .write_f32(100.0)
            .write_u32(crate::test_support::TEST_ZONE)
            .write_i64(4242)
            .write_raw(&[0; 28 * 4])
            .write_bool(true);
        assert_eq!(
            a.drain(),
            vec![
                (sc::SCCharBriefPacket, vec![2, 0, 0, 0, 2, 0, b'B', b'o', 5]),
                (sc::SCCharDetailPacket, expected_detail.into_bytes()),
            ]
        );
        assert!(b.drain().is_empty());
        let mut absent_brief = PacketStream::new();
        absent_brief.write_u32(999);
        let mut absent_detail = PacketStream::new();
        absent_detail.write_str("Nobody");
        for (opcode, request) in [
            (cs::CSRequestCharBriefPacket, &absent_brief),
            (cs::CSCharDetailPacket, &absent_detail),
        ] {
            server
                .handle_frame(&mut a.conn, &encode_raw(1, opcode, request.as_bytes()))
                .await
                .unwrap();
        }
        assert!(a.drain().is_empty());
        pb.data().is_online = false;
        for (opcode, request) in [
            (cs::CSRequestCharBriefPacket, &brief),
            (cs::CSCharDetailPacket, &detail),
        ] {
            server
                .handle_frame(&mut a.conn, &encode_raw(1, opcode, request.as_bytes()))
                .await
                .unwrap();
        }
        assert!(a.drain().is_empty());
        pb.data().is_online = true;
        a.conn.active_char = None;
        for (opcode, request) in [
            (cs::CSRequestCharBriefPacket, &brief),
            (cs::CSCharDetailPacket, &detail),
        ] {
            server
                .handle_frame(&mut a.conn, &encode_raw(1, opcode, request.as_bytes()))
                .await
                .unwrap();
        }
        assert!(a.drain().is_empty());
        assert!(b.drain().is_empty());
    }

    #[tokio::test]
    async fn ui_data_action_slots_and_idle_status() {
        let server = test_server();
        let mut a = TestClient::new(&server, 1, 1);
        let mut b = TestClient::new(&server, 2, 2);
        let pa = spawn_player(&server, &a, test_character(1, "Al", 10.0, 10.0), 0x100);
        spawn_player(&server, &b, test_character(2, "Bo", 12.0, 10.0), 0x101);
        a.conn.active_char = Some(pa.clone());
        a.drain();
        b.drain();
        CSSaveUIDataPacket {
            ui_data_type: 5,
            id: 1,
            data: "x=1".into(),
        }
        .handle(&server, &mut a.conn)
        .await
        .unwrap();
        CSRequestUIDataPacket {
            ui_data_type: 5,
            id: 1,
        }
        .handle(&server, &mut a.conn)
        .await
        .unwrap();
        assert_eq!(a.drain_ops(), vec![sc::SCResponseUIDataPacket]);
        CSIdleStatusPacket { status: true }
            .handle(&server, &mut a.conn)
            .await
            .unwrap();
        assert!(pa.data().unit.idle_status);
        assert_eq!(b.drain_ops(), vec![sc::SCUnitIdleStatusPacket]);
        let mut s = PacketStream::new();
        s.write_u8(3).write_u8(4).write_u64(99);
        let p =
            CSUpdateActionSlotPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
        p.handle(&server, &mut a.conn).await.unwrap();
        assert_eq!(pa.data().slots[3].action_id, 99);
        // Appellations: unknown id keeps the current one and still broadcasts.
        change_appellation(&server, &pa, 42);
        assert_eq!(b.drain_ops(), vec![sc::SCAppellationChangedPacket]);
    }
}
