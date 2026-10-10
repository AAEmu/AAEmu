//! `Core/Managers/MusicManager` (+ `Models/Game/Music/SongData`) and the music packets
//! (`CSSaveUserMusicNotesPacket`, `CSSendUserMusicPacket`, `CSRequestMusicNotesPacket`,
//! `CSEndMusicPacket`, `SCUserNoteLoadedPacket`).
//!
//! The sheet-music item work of `CreateSheetMusic` (ownership, bag space, the new
//! `MusicSheetItem`, consuming the music paper) goes through [`SheetMusicInventory`], which
//! the economy subsystem implements (it also implements the skills `ItemSkillHooks`
//! `midi_cache` / `create_sheet_music` by calling this manager).

use std::collections::HashMap;
use std::sync::Mutex;

use aaemu_commons::{Marshal, PacketStream};
use futures::future::BoxFuture;
use sqlx::Row;

use crate::error::Result;
use crate::net::connection::GameConnection;
use crate::net::packet::LEVEL_PLAIN;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::packets::packet_def;
use crate::packets::world::SCErrorMsgPacket;
use crate::server::GameServer;
use crate::world::{PlayerRef, WorldObject};

/// `BuffConstants.ScoreMemorized`.
pub const BUFF_SCORE_MEMORIZED: u32 = 6010;
/// `TagsEnum.PlaySong`.
pub const TAG_PLAY_SONG: u32 = 1155;
/// `ErrorMessageType.BagFull`.
const ERROR_BAG_FULL: i16 = 25;

/// `SongData`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct SongData {
    pub id: u32,
    pub author_id: u32,
    pub title: String,
    pub song: String,
    /// Item id of the source item (upload validation only).
    pub source_item_id: u64,
}

/// The inventory side of `CreateSheetMusic` (economy subsystem).
pub trait SheetMusicInventory {
    /// `sourceItem._holdingContainer.OwnerId` and `TemplateId` (`None` = no such item).
    fn source_item(&self, player: &PlayerRef, item_id: u64) -> Option<(u32, u32)>;
    /// `player.Inventory.Bag.FreeSlotCount`.
    fn bag_free_slots(&self, player: &PlayerRef) -> i32;
    /// `ItemManager.Create(Item.SheetMusic, 1, 0, true)` with `OwnerId` / `MadeUnitId` = the
    /// player and `SongId`, then `Bag.AddOrMoveExistingItem(SaveMusicNotes, sheet)`.
    fn add_sheet_music(&self, player: &PlayerRef, song_id: u32) -> bool;
    /// `Bag.ConsumeItem(SaveMusicNotes, templateId, 1, sourceItem)` (consumed count).
    fn consume_source(&self, player: &PlayerRef, item_id: u64, template_id: u32) -> i32;
}

#[derive(Debug, Default)]
struct State {
    /// `_uploadQueue` (player id -> song).
    upload_queue: HashMap<u32, SongData>,
    /// `_allSongs`.
    all_songs: HashMap<u32, SongData>,
    /// `_midiCache` (player id -> MIDI data).
    midi_cache: HashMap<u32, Vec<u8>>,
}

/// `MusicManager`.
#[derive(Debug, Default)]
pub struct MusicManager {
    state: Mutex<State>,
}

impl MusicManager {
    fn st(&self) -> std::sync::MutexGuard<'_, State> {
        self.state.lock().expect("music poisoned")
    }

    /// `Load()`: every song of the `music` table.
    pub async fn load(&self, server: &GameServer) -> Result<()> {
        let rows = sqlx::query(
            "SELECT CAST(id AS SIGNED) AS id, CAST(author AS SIGNED) AS author, title, song FROM music",
        )
        .fetch_all(&server.db)
        .await?;
        let mut st = self.st();
        st.upload_queue.clear();
        st.all_songs.clear();
        st.midi_cache.clear();
        for r in rows {
            let song = SongData {
                id: r.try_get::<i64, _>("id")? as u32,
                author_id: r.try_get::<i64, _>("author")? as u32,
                title: r.try_get("title")?,
                song: r.try_get("song")?,
                source_item_id: 0,
            };
            // `Dictionary.Add`: ids are the primary key, never duplicated.
            st.all_songs.insert(song.id, song);
        }
        Ok(())
    }

    /// `Save(songData)`: assigns a new id and writes the row.
    pub async fn save(&self, server: &GameServer, song: &mut SongData) -> bool {
        song.id = match server.ids.music.get_next_id() {
            Ok(id) => id,
            Err(e) => {
                tracing::error!("{e}");
                return false;
            }
        };
        let res =
            sqlx::query("REPLACE INTO music (`id`,`author`,`title`,`song` ) VALUES ( ?, ?, ?, ? )")
                .bind(song.id)
                .bind(song.author_id)
                .bind(&song.title)
                .bind(&song.song)
                .execute(&server.db)
                .await;
        if !matches!(res, Ok(ref r) if r.rows_affected() == 1) {
            tracing::warn!("Error saving song to DB for {} ({})", song.title, song.id);
            return false;
        }
        self.st().all_songs.insert(song.id, song.clone());
        true
    }

    /// `UploadSong(charId, title, song, itemId)`.
    pub fn upload_song(&self, char_id: u32, title: &str, song: &str, item_id: u64) {
        let mut st = self.st();
        let q = st.upload_queue.entry(char_id).or_default();
        q.author_id = char_id;
        q.title = title.to_string();
        q.song = song.to_string();
        q.source_item_id = item_id;
    }

    /// The queued upload of a player (tests / economy).
    pub fn queued_upload(&self, char_id: u32) -> Option<SongData> {
        self.st().upload_queue.get(&char_id).cloned()
    }

    /// `CreateSheetMusic(player, sourceItem)`.
    pub async fn create_sheet_music(
        &self,
        server: &GameServer,
        player: &PlayerRef,
        source_item_id: u64,
        inventory: &(dyn SheetMusicInventory + Sync),
    ) -> bool {
        let (pid, name) = {
            let c = player.data();
            (c.id, c.unit.name.clone())
        };
        let source = inventory.source_item(player, source_item_id);
        let Some((_, source_template)) = source.filter(|(owner, _)| *owner == pid) else {
            tracing::warn!("Player {name} ({pid}) does not own the used source item");
            return false;
        };
        let Some(mut sud) = self.queued_upload(pid) else {
            tracing::warn!("Player {name} ({pid}) did not upload any music yet.");
            return false;
        };
        if inventory.bag_free_slots(player) < 1 {
            player.send(SCErrorMsgPacket::new(ERROR_BAG_FULL, 0, true));
            tracing::warn!(
                "Player {name} ({pid}) did not have enough space to aquire new music sheet {} ({})",
                sud.title,
                sud.id
            );
            return false;
        }
        let saved = self.save(server, &mut sud).await;
        // `Save` assigned the id to the queued object itself.
        if let Some(q) = self.st().upload_queue.get_mut(&pid) {
            q.id = sud.id;
        }
        if saved {
            if !inventory.add_sheet_music(player, sud.id) {
                tracing::warn!(
                    "Player {name} ({pid}) had a unknown error when adding Sheet Music to inventory {} ({})",
                    sud.title,
                    sud.id
                );
                return false;
            }
            if inventory.consume_source(player, source_item_id, source_template) <= 0 {
                tracing::warn!(
                    "Failed to consume source item while creating music for Player {name} ({pid}) item {source_item_id} ({source_template})"
                );
            }
        }
        true
    }

    /// `GetSongById(songId)`.
    pub fn get_song_by_id(&self, song_id: u32) -> Option<SongData> {
        self.st().all_songs.get(&song_id).cloned()
    }

    /// `CacheMidi(playerId, midiData)`.
    pub fn cache_midi(&self, player_id: u32, midi: Vec<u8>) {
        self.st().midi_cache.insert(player_id, midi);
    }

    /// `GetMidiCache(playerId)` (empty when nothing is cached).
    pub fn get_midi_cache(&self, player_id: u32) -> Vec<u8> {
        self.st()
            .midi_cache
            .get(&player_id)
            .cloned()
            .unwrap_or_default()
    }
}

/// `SCUserNotesLoadedPacket(songId)` (`SCUserNoteLoadedPacket`): empty body for an unknown song.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCUserNoteLoadedPacket {
    pub song: Option<SongData>,
}
packet_def!(
    SCUserNoteLoadedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCUserNoteLoadedPacket
);
impl Marshal for SCUserNoteLoadedPacket {
    fn write(&self, s: &mut PacketStream) {
        if let Some(song) = &self.song {
            s.write_u32(song.id)
                .write_u8(1)
                .write_i32(song.song.encode_utf16().count() as i32)
                .write_str(&song.title)
                .write_str(&format!("{}\0", song.song));
        }
    }
}

/// `CSSaveUserMusicNotesPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSaveUserMusicNotesPacket {
    pub song_size: u32,
    pub item_id: u64,
    pub value2: u16,
    pub title: String,
    pub song: String,
}

impl ClientPacket for CSSaveUserMusicNotesPacket {
    const NAME: &'static str = "CSSaveUserMusicNotesPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            song_size: s.read_u32()?,
            item_id: s.read_u64()?,
            value2: s.read_u16()?,
            title: s.read_string()?,
            song: s.read_string()?,
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
            server.systems.services.music.upload_song(
                p.character_id,
                &self.title,
                &self.song,
                self.item_id,
            );
            tracing::debug!(
                "Uploading song, title: {}, songsize: {}, value2:{}",
                self.title,
                self.song_size,
                self.value2
            );
            tracing::trace!("Song data: {}", self.song);
            Ok(())
        })
    }
}

/// `CSSendUserMusicPacket`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CSSendUserMusicPacket {
    pub song_size: u32,
    pub data: Vec<u8>,
}

impl ClientPacket for CSSendUserMusicPacket {
    const NAME: &'static str = "CSSendUserMusicPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        let song_size = s.read_u32()?;
        let block = s.read_u16()?;
        Ok(Self {
            song_size,
            data: s.read_bytes(usize::from(block))?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::debug!(
                "Caching MIDI data size: {}/{}",
                self.data.len(),
                self.song_size
            );
            if let Some(p) = &conn.active_char {
                server
                    .systems
                    .services
                    .music
                    .cache_midi(p.character_id, self.data);
            }
            Ok(())
        })
    }
}

/// `CSRequestMusicNotesPacket`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSRequestMusicNotesPacket {
    pub id64: u64,
    pub value2: u16,
}

impl ClientPacket for CSRequestMusicNotesPacket {
    const NAME: &'static str = "CSRequestMusicNotesPacket";

    fn read(s: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            id64: s.read_u64()?,
            value2: s.read_u16()?,
        })
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("Request Song, id: {}, value2:{}", self.id64, self.value2);
            let Some(p) = conn.active_char.clone() else {
                return Ok(());
            };
            match self.value2 {
                1 => {
                    let song = server
                        .systems
                        .services
                        .music
                        .get_song_by_id(self.id64 as u32);
                    p.send(SCUserNoteLoadedPacket { song });
                }
                256 => {
                    let me = WorldObject::from(&p);
                    crate::systems::skills::buffs::add_buff_by_id(
                        server,
                        &me,
                        BUFF_SCORE_MEMORIZED,
                        &me,
                        0,
                    );
                }
                _ => tracing::warn!(
                    "Unknown song request type id: {}, value2:{}",
                    self.id64,
                    self.value2
                ),
            }
            Ok(())
        })
    }
}

/// `CSEndMusicPacket`: ends the play-song buffs once the score memorization is over.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct CSEndMusicPacket;

impl ClientPacket for CSEndMusicPacket {
    const NAME: &'static str = "CSEndMusicPacket";

    fn read(_s: &mut PacketStream) -> Result<Self> {
        Ok(Self)
    }

    fn handle<'a>(
        self,
        server: &'a GameServer,
        conn: &'a mut GameConnection,
    ) -> BoxFuture<'a, Result<()>> {
        Box::pin(async move {
            tracing::warn!("CSEndMusicPacket");
            let Some(p) = conn.active_char.clone() else {
                return Ok(());
            };
            let me = WorldObject::from(&p);
            if me.with_base_unit(|b| b.buffs().check_buff(BUFF_SCORE_MEMORIZED)) {
                return Ok(());
            }
            let ids = server
                .game_data
                .systems
                .skills
                .buffs_by_tag(TAG_PLAY_SONG)
                .map(<[u32]>::to_vec)
                .unwrap_or_default();
            for id in ids {
                if me.with_base_unit(|b| b.buffs().check_buff(id)) {
                    crate::systems::skills::buffs::remove_buff(server, &me, id);
                }
            }
            Ok(())
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::net::packet::encode;

    #[test]
    fn note_loaded_packet() {
        let song = SongData {
            id: 7,
            author_id: 1,
            title: "T".into(),
            song: "abc".into(),
            source_item_id: 0,
        };
        let mut e = PacketStream::new();
        e.write_u32(7)
            .write_u8(1)
            .write_i32(3)
            .write_str("T")
            .write_str("abc\0");
        assert_eq!(
            encode(&SCUserNoteLoadedPacket { song: Some(song) })[6..].to_vec(),
            e.into_bytes()
        );
        assert!(encode(&SCUserNoteLoadedPacket { song: None })[6..].is_empty());
    }

    #[test]
    fn queue_and_cache() {
        let m = MusicManager::default();
        assert!(m.get_midi_cache(1).is_empty());
        m.cache_midi(1, vec![1, 2]);
        m.cache_midi(1, vec![3]);
        assert_eq!(m.get_midi_cache(1), vec![3]);
        m.upload_song(5, "a", "b", 9);
        m.upload_song(5, "c", "d", 10);
        let q = m.queued_upload(5).unwrap();
        assert_eq!(
            (q.author_id, q.title.as_str(), q.source_item_id),
            (5, "c", 10)
        );
        assert!(m.get_song_by_id(1).is_none());
    }

    #[test]
    fn client_packets_decode() {
        let mut s = PacketStream::new();
        s.write_u32(3)
            .write_u64(77)
            .write_u16(5)
            .write_str("t")
            .write_str("s");
        let p = CSSaveUserMusicNotesPacket::read(&mut PacketStream::from_bytes(s.into_bytes()))
            .unwrap();
        assert_eq!(
            (p.item_id, p.title.as_str(), p.song.as_str()),
            (77, "t", "s")
        );
        let mut s = PacketStream::new();
        s.write_u32(10).write_u16(2).write_u8(1).write_u8(2);
        let p = CSSendUserMusicPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
        assert_eq!(p.data, vec![1, 2]);
        let mut s = PacketStream::new();
        s.write_u64(5).write_u16(256);
        let p =
            CSRequestMusicNotesPacket::read(&mut PacketStream::from_bytes(s.into_bytes())).unwrap();
        assert_eq!((p.id64, p.value2), (5, 256));
    }
}
