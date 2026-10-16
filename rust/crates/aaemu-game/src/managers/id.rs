//! Port of `AAEmu.Game.Utils.IdManager` (+ `AAEmu.Commons.Utils.BitSet` / `PrimeFinder`) and the
//! `Core/Managers/Id/*` instances used by this phase (`ObjectIdManager`, `CharacterIdManager`,
//! `ItemIdManager`, `ContainerIdManager`, `ChatIdManager`, `VisitedSubZoneIdManager`,
//! `PrivateBookIdManager`).
//!
//! The allocation algorithm is kept as in C#: a bit set sized from the `PrimeFinder` table,
//! "next clear bit after the last allocated id, wrap to 0, then grow", and `ReleaseId` lowering
//! the next-free cursor.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, OnceLock};

use sqlx::mysql::MySqlPool;
use sqlx::Row;

/// `PrimeFinder.primeCapacities` after `Array.Sort`.
#[rustfmt::skip]
const PRIME_CAPACITIES: &[i64] = &[
    3, 5, 7, 11, 17, 23, 31, 37,
    43, 47, 67, 79, 89, 97, 137, 163,
    179, 197, 277, 311, 331, 359, 379, 397,
    433, 557, 599, 631, 673, 719, 761, 797,
    877, 953, 1039, 1117, 1201, 1277, 1361, 1439,
    1523, 1597, 1759, 1907, 2081, 2237, 2411, 2557,
    2729, 2879, 3049, 3203, 3527, 3821, 4177, 4481,
    4831, 5119, 5471, 5779, 6101, 6421, 7057, 7643,
    8363, 8963, 9677, 10243, 10949, 11579, 12203, 12853,
    14143, 15287, 16729, 17929, 19373, 20507, 21911, 23159,
    24407, 25717, 28289, 30577, 33461, 35863, 38747, 41017,
    43853, 46327, 48817, 51437, 56591, 61169, 66923, 71741,
    77509, 82037, 87719, 92657, 97649, 102877, 113189, 122347,
    133853, 143483, 155027, 164089, 175447, 185323, 195311, 205759,
    226379, 244703, 267713, 286973, 310081, 328213, 350899, 370661,
    390647, 411527, 452759, 489407, 535481, 573953, 620171, 656429,
    701819, 741337, 781301, 823117, 905551, 978821, 1070981, 1147921,
    1240361, 1312867, 1403641, 1482707, 1562611, 1646237, 1811107, 1957651,
    2141977, 2295859, 2480729, 2625761, 2807303, 2965421, 3125257, 3292489,
    3622219, 3915341, 4283963, 4591721, 4961459, 5251529, 5614657, 5930887,
    6250537, 6584983, 7244441, 7830701, 8567929, 9183457, 9922933, 10503061,
    11229331, 11861791, 12501169, 13169977, 14488931, 15661423, 17135863, 18366923,
    19845871, 21006137, 22458671, 23723597, 25002389, 26339969, 28977863, 31322867,
    34271747, 36733847, 39691759, 42012281, 44917381, 47447201, 50004791, 52679969,
    57955739, 62645741, 68543509, 73467739, 79383533, 84024581, 89834777, 94894427,
    100009607, 105359939, 115911563, 125291483, 137087021, 146935499, 158767069, 168049163,
    179669557, 189788857, 200019221, 210719881, 231823147, 250582987, 274174111, 293871013,
    317534141, 336098327, 359339171, 379577741, 400038451, 421439783, 463646329, 501165979,
    548348231, 587742049, 635068283, 672196673, 718678369, 759155483, 800076929, 842879579,
    927292699, 1002331963, 1096696463, 1175484103, 1270136683, 1344393353, 1437356741, 1518310967,
    1600153859, 1685759167, 1854585413, 2004663929, 2147483647,
];

/// `PrimeFinder.NextPrime`: the smallest table prime `>= desired`; an exact match returns the
/// next larger one, as the C# does. (C# throws `OverflowException` past the last entry.)
pub fn next_prime(desired: i64) -> i64 {
    let i = match PRIME_CAPACITIES.binary_search(&desired) {
        Ok(i) => i + 1,
        Err(i) => i,
    };
    PRIME_CAPACITIES[i.min(PRIME_CAPACITIES.len() - 1)]
}

/// `BitSet` (a `BitArray` with a fixed `Count`).
#[derive(Debug, Clone)]
struct BitSet {
    count: usize,
    bits: Vec<u64>,
}

impl BitSet {
    fn new(count: usize) -> Self {
        Self {
            count,
            bits: vec![0; count.div_ceil(64)],
        }
    }
    fn get(&self, i: usize) -> bool {
        self.bits[i / 64] >> (i % 64) & 1 != 0
    }
    fn set(&mut self, i: usize) {
        self.bits[i / 64] |= 1 << (i % 64);
    }
    fn clear(&mut self, i: usize) {
        self.bits[i / 64] &= !(1 << (i % 64));
    }
    /// `NextClear(startFrom)` (`None` = -1).
    fn next_clear(&self, start: usize) -> Option<usize> {
        (start..self.count).find(|&i| !self.get(i))
    }
    /// `new BitSet(count).Or(this)`.
    fn grown(&self, count: usize) -> Self {
        let mut n = BitSet::new(count);
        for i in 0..self.count.min(count) {
            if self.get(i) {
                n.set(i);
            }
        }
        n
    }
}

#[derive(Debug)]
struct State {
    free_ids: BitSet,
    free_id_count: i64,
    next_free_id: usize,
}

/// `GameException("Ran out of valid Id's.")`.
#[derive(Debug, thiserror::Error)]
#[error("{0}: Ran out of valid Id's.")]
pub struct OutOfIds(pub &'static str);

/// Constructor arguments of an `IdManager` subclass.
#[derive(Debug, Clone, Copy)]
pub struct IdManagerSpec {
    pub name: &'static str,
    pub first_id: u32,
    pub last_id: u32,
    pub obj_tables: &'static [(&'static str, &'static str)],
    /// `distinct` (only `FamilyIdManager`): `SELECT DISTINCT`, duplicates allowed.
    pub distinct: bool,
}

/// Observer called with every id [`IdManager::release_id`] gives back (Rust-only: per-id state
/// that lives outside the object, e.g. loot containers, must not outlive the id).
#[derive(Clone)]
pub struct ReleaseHook(pub Arc<dyn Fn(u32) + Send + Sync>);

impl std::fmt::Debug for ReleaseHook {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("ReleaseHook")
    }
}

/// `IdManager`.
#[derive(Debug)]
pub struct IdManager {
    name: &'static str,
    first_id: u32,
    free_id_size: usize,
    obj_tables: &'static [(&'static str, &'static str)],
    distinct: bool,
    state: Mutex<State>,
    /// `_initialized`.
    initialized: AtomicBool,
    /// Observer of released ids, installed once by the subsystem that owns per-id state.
    release_hook: OnceLock<ReleaseHook>,
}

impl IdManager {
    pub fn new(spec: IdManagerSpec) -> Self {
        let free_id_size = (spec.last_id - spec.first_id) as usize;
        let m = Self {
            name: spec.name,
            first_id: spec.first_id,
            free_id_size,
            obj_tables: spec.obj_tables,
            distinct: spec.distinct,
            state: Mutex::new(State {
                free_ids: BitSet::new(0),
                free_id_count: 0,
                next_free_id: 0,
            }),
            initialized: AtomicBool::new(false),
            release_hook: OnceLock::new(),
        };
        // Usable before the DB-backed Initialize (tests, managers without tables); this does
        // not count as `_initialized`.
        m.reset(&[]);
        m
    }

    pub fn name(&self) -> &'static str {
        self.name
    }

    /// Installs the observer of released ids (first installation wins; later ones are ignored).
    pub fn install_release_hook(&self, hook: impl Fn(u32) + Send + Sync + 'static) {
        let _ = self.release_hook.set(ReleaseHook(Arc::new(hook)));
    }

    /// `Initialize()` with the used ids already extracted (`ExtractUsedObjectIdTable`).
    /// Exclusion lists are empty for every manager of this phase.
    pub fn initialize_with(&self, used: &[u32]) {
        self.reset(used);
        self.initialized.store(true, Ordering::Release);
    }

    /// `Initialize(forceReset)` without a database (the C# catches the failed read, logs
    /// "reverting to default" and starts with no used ids). An already initialized manager is
    /// left untouched unless `force_reset`. Returns true (C# result).
    pub fn initialize_without_db(&self, force_reset: bool) -> bool {
        if self.initialized.load(Ordering::Acquire) && !force_reset {
            return true;
        }
        self.initialize_with(&[]);
        true
    }

    /// The body of `Initialize()`: a fresh bit set with `used` marked.
    fn reset(&self, used: &[u32]) {
        let mut st = self.state.lock().expect("id manager poisoned");
        st.free_ids = BitSet::new(next_prime(100_000) as usize);
        st.free_id_count = self.free_id_size as i64;
        for &used_id in used {
            if used_id < self.first_id {
                tracing::warn!(
                    "{}: Object ID {used_id} in DB is less than {}",
                    self.name,
                    self.first_id
                );
                continue;
            }
            let object_id = (used_id - self.first_id) as usize;
            if object_id >= st.free_ids.count {
                let size = (next_prime(object_id as i64 + 1) as usize).min(self.free_id_size);
                st.free_ids = st.free_ids.grown(size);
            }
            if object_id < st.free_ids.count {
                st.free_ids.set(object_id);
            }
            st.free_id_count -= 1;
        }
        st.next_free_id = st.free_ids.next_clear(0).unwrap_or(usize::MAX);
    }

    /// `Initialize()`: reads the used ids from the MySQL tables. On a database error the C# logs
    /// "failed to read from database, reverting to default" and starts empty.
    pub async fn initialize(&self, pool: &MySqlPool) {
        // `Load()` -> `Initialize(forceReset: false)`
        if self.initialized.load(Ordering::Acquire) {
            return;
        }
        let used = match self.extract_used(pool).await {
            Ok(v) => v,
            Err(e) => {
                tracing::warn!(
                    "{} failed to read from database, reverting to default ({e})",
                    self.name
                );
                Vec::new()
            }
        };
        self.initialize_with(&used);
        tracing::info!("{} successfully initialized", self.name);
    }

    /// `ExtractUsedObjectIdTable`.
    async fn extract_used(&self, pool: &MySqlPool) -> crate::Result<Vec<u32>> {
        if self.obj_tables.is_empty() {
            return Ok(Vec::new());
        }
        let (t0, c0) = self.obj_tables[0];
        let d = if self.distinct { "DISTINCT " } else { "" };
        let mut query = format!("SELECT {d}CAST({c0} AS UNSIGNED) AS v, 0 AS i FROM {t0}");
        for (i, (t, c)) in self.obj_tables.iter().enumerate().skip(1) {
            query += &format!(" UNION SELECT {d}CAST({c} AS UNSIGNED), {i} FROM {t}");
        }
        let counts = sqlx::query(&format!(
            "SELECT COUNT(*) AS a, COUNT(DISTINCT v) AS b FROM ( {query} ) AS all_ids"
        ))
        .fetch_one(pool)
        .await?;
        let (a, b): (i64, i64) = (counts.try_get("a")?, counts.try_get("b")?);
        if a != b && !self.distinct {
            return Err(crate::GameError::Protocol(
                "IdManager: there are duplicates in object ids".into(),
            ));
        }
        if a == 0 {
            return Ok(Vec::new());
        }
        tracing::info!(
            "{}: Extracting {a} used id's from data tables...",
            self.name
        );
        let rows = sqlx::query(&query).fetch_all(pool).await?;
        let mut out = Vec::with_capacity(rows.len());
        for r in rows {
            out.push(r.try_get::<u64, _>(0)? as u32);
        }
        tracing::info!(
            "{}: Successfully extracted {} used id's from data tables.",
            self.name,
            out.len()
        );
        Ok(out)
    }

    /// `ReleaseId`. The offset is `(int)(usedObjectId - _firstId)` as in the C#: ids below the
    /// first id, or more than `int.MaxValue` above it, are rejected.
    pub fn release_id(&self, used_id: u32) {
        {
            let mut st = self.state.lock().expect("id manager poisoned");
            let object_id = used_id.wrapping_sub(self.first_id) as i32;
            if object_id > -1 {
                let object_id = object_id as usize;
                if object_id < st.free_ids.count {
                    st.free_ids.clear(object_id);
                }
                if st.next_free_id > object_id {
                    st.next_free_id = object_id;
                }
                st.free_id_count += 1;
            } else {
                tracing::error!("{}: release objectId {used_id} failed", self.name);
                return;
            }
        }
        // Outside the state lock: the observer takes its own locks.
        if let Some(hook) = self.release_hook.get() {
            (hook.0)(used_id);
        }
    }

    /// `GetNextId`.
    pub fn get_next_id(&self) -> Result<u32, OutOfIds> {
        let mut st = self.state.lock().expect("id manager poisoned");
        let new_id = st.next_free_id;
        if new_id >= st.free_ids.count {
            return Err(OutOfIds(self.name));
        }
        st.free_ids.set(new_id);
        st.free_id_count -= 1;
        let mut next = st.free_ids.next_clear(new_id);
        while next.is_none() {
            next = st.free_ids.next_clear(0);
            if next.is_none() {
                if st.free_ids.count < self.free_id_size {
                    let size = (next_prime((st.free_ids.count + self.free_id_size / 10) as i64)
                        as usize)
                        .min(self.free_id_size);
                    st.free_ids = st.free_ids.grown(size);
                } else {
                    return Err(OutOfIds(self.name));
                }
            }
        }
        st.next_free_id = next.unwrap_or(usize::MAX);
        Ok(new_id as u32 + self.first_id)
    }

    /// `GetNextId(count)`.
    pub fn get_next_ids(&self, count: usize) -> Result<Vec<u32>, OutOfIds> {
        (0..count).map(|_| self.get_next_id()).collect()
    }

    /// `ReleaseId(IEnumerable<uint>)`.
    pub fn release_ids(&self, used_ids: impl IntoIterator<Item = u32>) {
        for id in used_ids {
            self.release_id(id);
        }
    }
}

/// `ObjectIdManager` (no tables: object ids are never persisted).
pub const OBJECT_ID: IdManagerSpec = IdManagerSpec {
    name: "ObjectIdManager",
    first_id: 0x0000_0100,
    last_id: 0x00FF_FFFE,
    obj_tables: &[],
    distinct: false,
};
/// `CharacterIdManager`.
pub const CHARACTER_ID: IdManagerSpec = IdManagerSpec {
    name: "CharacterIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("characters", "id"), ("slaves", "id")],
    distinct: false,
};
/// `ItemIdManager`.
pub const ITEM_ID: IdManagerSpec = IdManagerSpec {
    name: "ItemIdManager",
    first_id: 0x0100_0000,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[("items", "id")],
    distinct: false,
};
/// `ContainerIdManager`.
pub const CONTAINER_ID: IdManagerSpec = IdManagerSpec {
    name: "ContainerIdManager",
    first_id: 0x0001_0000,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[("item_containers", "container_id")],
    distinct: false,
};
/// `ChatIdManager`.
pub const CHAT_ID: IdManagerSpec = IdManagerSpec {
    name: "ChatIdManager",
    first_id: 0x0000_0001,
    last_id: 0x0000_FFFE,
    obj_tables: &[],
    distinct: false,
};
/// `VisitedSubZoneIdManager`.
pub const VISITED_SUB_ZONE_ID: IdManagerSpec = IdManagerSpec {
    name: "VisitedSubZoneIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("portal_visited_district", "id")],
    distinct: false,
};
/// `PrivateBookIdManager`.
pub const PRIVATE_BOOK_ID: IdManagerSpec = IdManagerSpec {
    name: "PrivateBookIdManager",
    first_id: 0x0000_1000,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("portal_book_coords", "id")],
    distinct: false,
};

/// `AuctionIdManager`.
pub const AUCTION_ID: IdManagerSpec = IdManagerSpec {
    name: "AuctionIdManager",
    first_id: 0x0000_0001,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[("auction_house", "id")],
    distinct: false,
};
/// `CrimeIdManager`.
pub const CRIME_ID: IdManagerSpec = IdManagerSpec {
    name: "CrimeIdManager",
    first_id: 0x0000_1000,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[("crime", "id")],
    distinct: false,
};
/// `DoodadIdManager`.
pub const DOODAD_ID: IdManagerSpec = IdManagerSpec {
    name: "DoodadIdManager",
    first_id: 0x0000_0001,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[("doodads", "id")],
    distinct: false,
};
/// `ExpeditionIdManager`.
pub const EXPEDITION_ID: IdManagerSpec = IdManagerSpec {
    name: "ExpeditionIdManager",
    first_id: 0x0000_03E8,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[("expeditions", "id")],
    distinct: false,
};
/// `FamilyIdManager`.
pub const FAMILY_ID: IdManagerSpec = IdManagerSpec {
    name: "FamilyIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("characters", "family")],
    distinct: true,
};
/// `FriendIdManager`.
pub const FRIEND_ID: IdManagerSpec = IdManagerSpec {
    name: "FriendIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("friends", "id")],
    distinct: false,
};
/// `GimmickIdManager`.
pub const GIMMICK_ID: IdManagerSpec = IdManagerSpec {
    name: "GimmickIdManager",
    first_id: 0x0000_0001,
    last_id: 0x0000_FFFE,
    obj_tables: &[],
    distinct: false,
};
/// `HousingIdManager`.
pub const HOUSING_ID: IdManagerSpec = IdManagerSpec {
    name: "HousingIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("housings", "id")],
    distinct: false,
};
/// `HousingTldManager`.
pub const HOUSING_TL_ID: IdManagerSpec = IdManagerSpec {
    name: "HousingTldManager",
    first_id: 0x0000_0001,
    last_id: 0x0000_FFFE,
    obj_tables: &[],
    distinct: false,
};
/// `MailIdManager`.
pub const MAIL_ID: IdManagerSpec = IdManagerSpec {
    name: "MailIdManager",
    first_id: 0x0000_2710,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[("mails", "id")],
    distinct: false,
};
/// `MateIdManager`.
pub const MATE_ID: IdManagerSpec = IdManagerSpec {
    name: "MateIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("mates", "id")],
    distinct: false,
};
/// `MusicIdManager`.
pub const MUSIC_ID: IdManagerSpec = IdManagerSpec {
    name: "MusicIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("music", "id")],
    distinct: false,
};
/// `QuestIdManager`.
pub const QUEST_ID: IdManagerSpec = IdManagerSpec {
    name: "QuestIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[("quests", "id")],
    distinct: false,
};
/// `ShipyardIdManager`.
pub const SHIPYARD_ID: IdManagerSpec = IdManagerSpec {
    name: "ShipyardIdManager",
    first_id: 0x0000_0001,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[],
    distinct: false,
};
/// `TaskIdManager`.
pub const TASK_ID: IdManagerSpec = IdManagerSpec {
    name: "TaskIdManager",
    first_id: 0x0000_0001,
    last_id: 0x7FFF_FFFF,
    obj_tables: &[],
    distinct: false,
};
/// `TeamIdManager`.
pub const TEAM_ID: IdManagerSpec = IdManagerSpec {
    name: "TeamIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[],
    distinct: false,
};
/// `TlIdManager`.
pub const TL_ID: IdManagerSpec = IdManagerSpec {
    name: "TlIdManager",
    first_id: 0x0000_0001,
    last_id: 0x0000_FFFE,
    obj_tables: &[],
    distinct: false,
};
/// `TradeIdManager`.
pub const TRADE_ID: IdManagerSpec = IdManagerSpec {
    name: "TradeIdManager",
    first_id: 0x0000_0001,
    last_id: 0x00FF_FFFF,
    obj_tables: &[],
    distinct: false,
};
/// `TrialIdManager`.
pub const TRIAL_ID: IdManagerSpec = IdManagerSpec {
    name: "TrialIdManager",
    first_id: 0x0000_1000,
    last_id: 0xFFFF_FFFF,
    obj_tables: &[],
    distinct: false,
};
/// `WorldIdManager`.
pub const WORLD_ID: IdManagerSpec = IdManagerSpec {
    name: "WorldIdManager",
    first_id: 0x0000_0064,
    last_id: 0xFFFF_FFFE,
    obj_tables: &[],
    distinct: false,
};

/// Every `Core/Managers/Id` singleton (`UccIdManager` lives in `stream::ucc`, the static
/// `SkillTlIdManager` belongs to the skills subsystem).
#[derive(Debug)]
pub struct IdManagers {
    pub object: IdManager,
    pub character: IdManager,
    pub item: IdManager,
    pub container: IdManager,
    pub chat: IdManager,
    pub visited_sub_zone: IdManager,
    pub private_book: IdManager,
    pub auction: IdManager,
    pub crime: IdManager,
    pub doodad: IdManager,
    pub expedition: IdManager,
    pub family: IdManager,
    pub friend: IdManager,
    pub gimmick: IdManager,
    pub housing: IdManager,
    pub housing_tl: IdManager,
    pub mail: IdManager,
    pub mate: IdManager,
    pub music: IdManager,
    pub quest: IdManager,
    pub shipyard: IdManager,
    pub task: IdManager,
    pub team: IdManager,
    pub tl: IdManager,
    pub trade: IdManager,
    pub trial: IdManager,
    pub world: IdManager,
}

impl Default for IdManagers {
    fn default() -> Self {
        Self {
            object: IdManager::new(OBJECT_ID),
            character: IdManager::new(CHARACTER_ID),
            item: IdManager::new(ITEM_ID),
            container: IdManager::new(CONTAINER_ID),
            chat: IdManager::new(CHAT_ID),
            visited_sub_zone: IdManager::new(VISITED_SUB_ZONE_ID),
            private_book: IdManager::new(PRIVATE_BOOK_ID),
            auction: IdManager::new(AUCTION_ID),
            crime: IdManager::new(CRIME_ID),
            doodad: IdManager::new(DOODAD_ID),
            expedition: IdManager::new(EXPEDITION_ID),
            family: IdManager::new(FAMILY_ID),
            friend: IdManager::new(FRIEND_ID),
            gimmick: IdManager::new(GIMMICK_ID),
            housing: IdManager::new(HOUSING_ID),
            housing_tl: IdManager::new(HOUSING_TL_ID),
            mail: IdManager::new(MAIL_ID),
            mate: IdManager::new(MATE_ID),
            music: IdManager::new(MUSIC_ID),
            quest: IdManager::new(QUEST_ID),
            shipyard: IdManager::new(SHIPYARD_ID),
            task: IdManager::new(TASK_ID),
            team: IdManager::new(TEAM_ID),
            tl: IdManager::new(TL_ID),
            trade: IdManager::new(TRADE_ID),
            trial: IdManager::new(TRIAL_ID),
            world: IdManager::new(WORLD_ID),
        }
    }
}

impl IdManagers {
    /// `ManagerOrchestrator` stage 2 (`IdManager.Load()` for each).
    pub async fn initialize(&self, pool: &MySqlPool) {
        for m in [
            &self.object,
            &self.character,
            &self.item,
            &self.container,
            &self.chat,
            &self.visited_sub_zone,
            &self.private_book,
            &self.auction,
            &self.crime,
            &self.doodad,
            &self.expedition,
            &self.family,
            &self.friend,
            &self.gimmick,
            &self.housing,
            &self.housing_tl,
            &self.mail,
            &self.mate,
            &self.music,
            &self.quest,
            &self.shipyard,
            &self.task,
            &self.team,
            &self.tl,
            &self.trade,
            &self.trial,
            &self.world,
        ] {
            m.initialize(pool).await;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn prime_lookup_matches_csharp() {
        assert_eq!(next_prime(100_000), 102_877);
        assert_eq!(next_prime(3), 5);
        assert_eq!(next_prime(4), 5);
        assert_eq!(next_prime(i64::from(i32::MAX)), i64::from(i32::MAX));
    }

    #[test]
    fn allocation_release_and_used_ids() {
        let m = IdManager::new(CHARACTER_ID);
        m.initialize_with(&[1, 2, 4]);
        assert_eq!(m.get_next_id().unwrap(), 3);
        assert_eq!(m.get_next_id().unwrap(), 5);
        m.release_id(2);
        assert_eq!(m.get_next_id().unwrap(), 2);
        // The cursor continues after the released id: 3 is used, 4 is used -> 6.
        assert_eq!(m.get_next_id().unwrap(), 6);
        let o = IdManager::new(OBJECT_ID);
        assert_eq!(o.get_next_id().unwrap(), 0x100);
        assert_eq!(o.get_next_id().unwrap(), 0x101);
    }

    #[test]
    fn used_ids_beyond_capacity_grow_the_set() {
        let m = IdManager::new(ITEM_ID);
        m.initialize_with(&[0x0100_0000, 0x0100_0000 + 200_000]);
        assert_eq!(m.get_next_id().unwrap(), 0x0100_0001);
    }
}

#[cfg(test)]
#[path = "id_tests.rs"]
mod id_tests;
