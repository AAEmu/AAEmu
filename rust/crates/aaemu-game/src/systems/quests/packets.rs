//! Quest packets: `CSStartQuestContextPacket`, `CSCompleteQuestContextPacket`,
//! `CSDropQuestContextPacket`, `CSQuestTalkMadePacket`, `CSQuestStartWithPacket`,
//! `CSTryQuestCompleteAsLetItDonePacket`, `CSRestartMainQuestPacket`, `CSSaveTutorialPacket`,
//! `CSStartedCinemaPacket`, `CSCompletedCinemaPacket` and the quest S->C packets.

use aaemu_commons::{Marshal, PacketStream};
use futures::future::BoxFuture;

use super::events;
use crate::error::Result;
use crate::models::char_data::ActiveQuest;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::packets::packet_def;
use crate::server::GameServer;

// ----- S->C -----

/// `SCQuestContextStartedPacket(quest, componentId)`.
#[derive(Debug, Clone)]
pub struct SCQuestContextStartedPacket {
    pub quest: ActiveQuest,
    pub component_id: u32,
    /// `DateTime.UtcNow` at write time (unix ms) for `Quest.Write`.
    pub now_ms: i64,
}
packet_def!(
    SCQuestContextStartedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCQuestContextStartedPacket
);
impl Marshal for SCQuestContextStartedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.quest.write(s, self.now_ms);
        s.write_u32(self.component_id);
    }
}

/// `SCQuestContextUpdatedPacket(quest, componentId, para1..4)`.
#[derive(Debug, Clone)]
pub struct SCQuestContextUpdatedPacket {
    pub quest: ActiveQuest,
    pub component_id: u32,
    pub para: [i32; 4],
    pub now_ms: i64,
}
impl SCQuestContextUpdatedPacket {
    pub fn new(quest: ActiveQuest, component_id: u32, now_ms: i64) -> Self {
        Self {
            quest,
            component_id,
            para: [0; 4],
            now_ms,
        }
    }
}
packet_def!(
    SCQuestContextUpdatedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCQuestContextUpdatedPacket
);
impl Marshal for SCQuestContextUpdatedPacket {
    fn write(&self, s: &mut PacketStream) {
        self.quest.write(s, self.now_ms);
        s.write_u32(self.component_id);
        for p in self.para {
            s.write_i32(p);
        }
    }
}

/// `SCQuestContextCompletedPacket(questId, body, componentId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCQuestContextCompletedPacket {
    pub quest_id: u32,
    pub body: [u8; 8],
    pub component_id: u32,
}
packet_def!(
    SCQuestContextCompletedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCQuestContextCompletedPacket
);
impl Marshal for SCQuestContextCompletedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.quest_id)
            .write_raw(&self.body)
            .write_u32(self.component_id);
    }
}

/// `SCQuestContextResetPacket(questId, body, componentId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCQuestContextResetPacket {
    pub quest_id: u32,
    pub body: [u8; 8],
    pub component_id: u32,
}
packet_def!(
    SCQuestContextResetPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCQuestContextResetPacket
);
impl Marshal for SCQuestContextResetPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.quest_id)
            .write_raw(&self.body)
            .write_u32(self.component_id);
    }
}

/// `SCQuestRewardedByMailPacket(questList)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCQuestRewardedByMailPacket {
    pub quests: Vec<u32>,
}
packet_def!(
    SCQuestRewardedByMailPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCQuestRewardedByMailPacket
);
impl Marshal for SCQuestRewardedByMailPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.quests.len() as u8);
        for q in &self.quests {
            s.write_u32(*q);
        }
    }
}

/// `SCDoodadQuestAcceptPacket(doodadObjId, questContextId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCDoodadQuestAcceptPacket {
    pub doodad_obj_id: u32,
    pub quest_id: u32,
}
packet_def!(
    SCDoodadQuestAcceptPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCDoodadQuestAcceptPacket
);
impl Marshal for SCDoodadQuestAcceptPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_bc(self.doodad_obj_id).write_u32(self.quest_id);
    }
}

/// `SCTutorialSavedPacket(id, body)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCTutorialSavedPacket {
    pub id: u32,
    pub body: [u8; 8],
}
packet_def!(
    SCTutorialSavedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCTutorialSavedPacket
);
impl Marshal for SCTutorialSavedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.id).write_raw(&self.body);
    }
}

/// `SCAppellationGainedPacket(appellationId)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCAppellationGainedPacket {
    pub appellation_id: u32,
}
packet_def!(
    SCAppellationGainedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCAppellationGainedPacket
);
impl Marshal for SCAppellationGainedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.appellation_id);
    }
}

// ----- C->S -----

fn active(conn: &GameConnection) -> Option<crate::world::PlayerRef> {
    conn.active_char.clone()
}

/// `CSStartQuestContextPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSStartQuestContextPacket {
    pub quest_context_id: u32,
    pub npc_obj_id: u32,
    pub doodad_obj_id: u32,
    pub sphere_id: u32,
}

impl ClientPacket for CSStartQuestContextPacket {
    const NAME: &'static str = "CSStartQuestContextPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            quest_context_id: s.read_u32()?,
            npc_obj_id: s.read_bc()?,
            doodad_obj_id: s.read_bc()?,
            sphere_id: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            let Some(p) = active(conn) else {
                return Ok(());
            };
            if self.npc_obj_id > 0 {
                events::add_quest_from_npc(server, &p, self.quest_context_id, self.npc_obj_id);
            } else if self.doodad_obj_id > 0 {
                events::add_quest_from_doodad(
                    server,
                    &p,
                    self.quest_context_id,
                    self.doodad_obj_id,
                );
            } else if self.sphere_id > 0 {
                events::add_quest_from_sphere(server, &p, self.quest_context_id, self.sphere_id);
            } else {
                events::add_quest(server, &p, self.quest_context_id, false);
            }
            Ok(())
        })
    }
}

/// `CSCompleteQuestContextPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSCompleteQuestContextPacket {
    pub quest_context_id: u32,
    pub npc_obj_id: u32,
    pub doodad_obj_id: u32,
    pub selected: i32,
}

impl ClientPacket for CSCompleteQuestContextPacket {
    const NAME: &'static str = "CSCompleteQuestContextPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            quest_context_id: s.read_u32()?,
            npc_obj_id: s.read_bc()?,
            doodad_obj_id: s.read_bc()?,
            selected: s.read_i32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            if let Some(p) = active(conn) {
                events::on_report(
                    server,
                    &p,
                    self.quest_context_id,
                    self.npc_obj_id,
                    self.doodad_obj_id,
                    self.selected,
                );
            }
            Ok(())
        })
    }
}

/// `CSDropQuestContextPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSDropQuestContextPacket {
    pub quest_id: u32,
}

impl ClientPacket for CSDropQuestContextPacket {
    const NAME: &'static str = "CSDropQuestContextPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            quest_id: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            if let Some(p) = active(conn) {
                events::drop_quest(server, &p, self.quest_id, true);
            }
            Ok(())
        })
    }
}

/// `CSQuestTalkMadePacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSQuestTalkMadePacket {
    pub npc_obj_id: u32,
    pub quest_context_id: u32,
    pub quest_comp_id: u32,
    pub quest_act_id: u32,
}

impl ClientPacket for CSQuestTalkMadePacket {
    const NAME: &'static str = "CSQuestTalkMadePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            npc_obj_id: s.read_bc()?,
            quest_context_id: s.read_u32()?,
            quest_comp_id: s.read_u32()?,
            quest_act_id: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "QuestTalkMade: npcObjId {}, questContextId {}, questCompId {}, questActId {}",
                self.npc_obj_id,
                self.quest_context_id,
                self.quest_comp_id,
                self.quest_act_id
            );
            if let Some(p) = active(conn) {
                events::on_talk_made(
                    server,
                    &p,
                    &p,
                    self.npc_obj_id,
                    self.quest_context_id,
                    self.quest_comp_id,
                    self.quest_act_id,
                );
            }
            Ok(())
        })
    }
}

/// `CSQuestStartWithPacket` (logged only).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSQuestStartWithPacket {
    pub unk_id: u32,
    pub unk_id2: u32,
    pub unk_id3: u32,
}

impl ClientPacket for CSQuestStartWithPacket {
    const NAME: &'static str = "CSQuestStartWithPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            unk_id: s.read_u32()?,
            unk_id2: s.read_u32()?,
            unk_id3: s.read_u32()?,
        })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "QuestStartWith, UnkId: {}, UnkId2: {}, UnkId3: {}",
                self.unk_id,
                self.unk_id2,
                self.unk_id3
            );
            Ok(())
        })
    }
}

/// `CSTryQuestCompleteAsLetItDonePacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSTryQuestCompleteAsLetItDonePacket {
    pub id: u32,
    pub obj_id: u32,
    pub selected: i32,
}

impl ClientPacket for CSTryQuestCompleteAsLetItDonePacket {
    const NAME: &'static str = "CSTryQuestCompleteAsLetItDonePacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            id: s.read_u32()?,
            obj_id: s.read_bc()?,
            selected: s.read_i32()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!(
                "TryQuestCompleteAsLetItDone, Id: {}, ObjId: {}, Selected: {}",
                self.id,
                self.obj_id,
                self.selected
            );
            let Some(p) = active(conn) else {
                return Ok(());
            };
            // Check if player is actually targeting the NPC
            let target = p.data().unit.current_target;
            if self.obj_id > 0 && target != 0 && target != self.obj_id {
                return Ok(());
            }
            let (id, selected) = (self.id, self.selected);
            events::with_quests(server, &p, None, move |ctx| {
                ctx.try_complete_quest_as_let_it_done(id, selected)
            });
            Ok(())
        })
    }
}

/// `CSRestartMainQuestPacket` (logged only).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSRestartMainQuestPacket {
    pub id: u32,
}

impl ClientPacket for CSRestartMainQuestPacket {
    const NAME: &'static str = "CSRestartMainQuestPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self { id: s.read_u32()? })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        _conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("RestartMainQuest, Id: {}", self.id);
            Ok(())
        })
    }
}

/// `CSSaveTutorialPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSaveTutorialPacket {
    pub id: u32,
}

impl ClientPacket for CSSaveTutorialPacket {
    const NAME: &'static str = "CSSaveTutorialPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self { id: s.read_u32()? })
    }

    fn handle<'a>(
        self,
        _server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!("SaveTutorial, Id: {}", self.id);
            let Some(p) = active(conn) else {
                return Ok(());
            };
            let block = {
                let mut c = p.data();
                super::engine::set_completed_quest_flag(&mut c.quests.completed, self.id, true)
            };
            conn.send(&SCTutorialSavedPacket {
                id: self.id,
                body: block.body8(),
            });
            Ok(())
        })
    }
}

/// `CSStartedCinemaPacket` (empty).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSStartedCinemaPacket;

impl ClientPacket for CSStartedCinemaPacket {
    const NAME: &'static str = "CSStartedCinemaPacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("StartedCinema");
            if let Some(p) = active(conn) {
                events::on_cinema_started(server, &p);
            }
            Ok(())
        })
    }
}

/// `CSCompletedCinemaPacket` (empty): restore neighbouring objects before quest events.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSCompletedCinemaPacket;

impl ClientPacket for CSCompletedCinemaPacket {
    const NAME: &'static str = "CSCompletedCinemaPacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("CompletedCinema");
            if let Some(p) = active(conn) {
                crate::systems::npcs::portal::resend_visible_objects_to_character(server, &p);
                events::on_cinema_ended(server, &p);
            }
            Ok(())
        })
    }
}
