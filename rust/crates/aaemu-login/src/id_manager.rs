//! Identifier allocation: `Utils/IdManager.cs`, `Utils/SimpleIdManager.cs` and
//! `ConnectionIdLease`.

use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::{Arc, Mutex};

use sqlx::MySqlPool;

use crate::models::ConnectionId;

#[derive(Debug, thiserror::Error, PartialEq, Eq)]
pub enum IdError {
    #[error("{0}: ran out of valid ids")]
    Exhausted(String),
    #[error("ID space exhausted.")]
    SpaceExhausted,
    #[error("id must be non-zero")]
    Zero,
}

/// `IdManager`: hands out the lowest free id in `[first_id, last_id)`, backed by a bitset
/// (the C# version uses `BitSet`/`PrimeFinder` sizing; only the allocation order matters).
#[derive(Debug)]
pub struct IdManager {
    name: String,
    first_id: u32,
    exclude: Vec<u32>,
    free_id_size: usize,
    used: Vec<u64>,
    next_free: usize,
}

impl IdManager {
    pub fn new(name: impl Into<String>, first_id: u32, last_id: u32, exclude: Vec<u32>) -> Self {
        Self {
            name: name.into(),
            first_id,
            exclude,
            free_id_size: (last_id - first_id) as usize,
            used: Vec::new(),
            next_free: 0,
        }
    }

    fn is_set(&self, idx: usize) -> bool {
        self.used
            .get(idx / 64)
            .is_some_and(|w| w & (1 << (idx % 64)) != 0)
    }

    fn set(&mut self, idx: usize) {
        let word = idx / 64;
        if word >= self.used.len() {
            self.used.resize(word + 1, 0);
        }
        self.used[word] |= 1 << (idx % 64);
    }

    fn clear(&mut self, idx: usize) {
        if let Some(w) = self.used.get_mut(idx / 64) {
            *w &= !(1 << (idx % 64));
        }
    }

    fn next_clear(&self, from: usize) -> Option<usize> {
        (from..self.free_id_size).find(|&i| !self.is_set(i))
    }

    /// `Initialize`: marks the given already-used ids (e.g. from [`extract_used_ids`]).
    pub fn initialize(&mut self, used_ids: impl IntoIterator<Item = u32>) {
        self.used.clear();
        for id in used_ids {
            if self.exclude.contains(&id) {
                continue;
            }
            if id < self.first_id {
                tracing::warn!(
                    "{}: Object ID {} in DB is less than {}",
                    self.name,
                    id,
                    self.first_id
                );
                continue;
            }
            self.set((id - self.first_id) as usize);
        }
        self.next_free = self.next_clear(0).unwrap_or(self.free_id_size);
        tracing::info!("{} successfully initialized", self.name);
    }

    pub fn get_next_id(&mut self) -> Result<u32, IdError> {
        let id = self.next_free;
        if id >= self.free_id_size || self.is_set(id) {
            return Err(IdError::Exhausted(self.name.clone()));
        }
        self.set(id);
        self.next_free = self
            .next_clear(id)
            .or_else(|| self.next_clear(0))
            .unwrap_or(self.free_id_size);
        Ok(id as u32 + self.first_id)
    }

    pub fn get_next_ids(&mut self, count: usize) -> Result<Vec<u32>, IdError> {
        (0..count).map(|_| self.get_next_id()).collect()
    }

    pub fn release_id(&mut self, id: u32) {
        // C#: `(int)(usedObjectId - _firstId) > -1`
        let idx = id.wrapping_sub(self.first_id) as i32;
        if idx < 0 {
            tracing::warn!("{}: release objectId {} failed", self.name, id);
            return;
        }
        let idx = idx as usize;
        self.clear(idx);
        if self.next_free > idx {
            self.next_free = idx;
        }
    }
}

/// `IdManager.ExtractUsedObjectIdTable`: collects used ids from `(table, column)` pairs.
pub async fn extract_used_ids(
    pool: &MySqlPool,
    tables: &[(&str, &str)],
    distinct: bool,
) -> anyhow::Result<Vec<u32>> {
    let Some(((first_table, first_col), rest)) = tables.split_first() else {
        return Ok(Vec::new());
    };
    let d = if distinct { "DISTINCT " } else { "" };
    let mut query = format!("SELECT {d}{first_col}, 0 AS i FROM {first_table}");
    for (i, (table, col)) in rest.iter().enumerate() {
        query += &format!(" UNION SELECT {d}{col}, {} FROM {table}", i + 1);
    }
    let (count, distinct_count): (i64, i64) = sqlx::query_as(&format!(
        "SELECT COUNT(*), COUNT(DISTINCT {first_col}) FROM ( {query} ) AS all_ids"
    ))
    .fetch_one(pool)
    .await?;
    if count != distinct_count && !distinct {
        anyhow::bail!("IdManager: there are duplicates in object ids");
    }
    if count == 0 {
        return Ok(Vec::new());
    }
    let rows: Vec<(i64,)> = sqlx::query_as(&format!(
        "SELECT CAST({first_col} AS SIGNED) FROM ( {query} ) AS all_ids"
    ))
    .fetch_all(pool)
    .await?;
    Ok(rows.into_iter().map(|r| r.0 as u32).collect())
}

/// `SimpleIdManager<ConnectionId>`: reuses freed ids (LIFO) before minting new ones.
///
/// The free list may grow almost unbounded under heavy load or DDoS; ideally rate limiting would
/// be applied at a higher level.
#[derive(Debug, Default)]
pub struct SimpleIdManager {
    next_id: AtomicU32,
    free_ids: Mutex<Vec<u32>>,
}

impl SimpleIdManager {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn rent(&self) -> Result<u32, IdError> {
        // Try to reuse a freed ID first
        if let Some(id) = self.free_ids.lock().unwrap().pop() {
            return Ok(id);
        }
        // Otherwise, generate a new ID atomically; guard against overflow
        match self.next_id.fetch_add(1, Ordering::SeqCst).wrapping_add(1) {
            0 => Err(IdError::SpaceExhausted),
            id => Ok(id),
        }
    }

    pub fn give_back(&self, id: u32) -> Result<(), IdError> {
        if id == 0 {
            return Err(IdError::Zero);
        }
        self.free_ids.lock().unwrap().push(id);
        Ok(())
    }
}

/// A leased connection id, returned to the pool when dropped.
#[derive(Debug)]
pub struct ConnectionIdLease {
    id: ConnectionId,
    pool: Arc<SimpleIdManager>,
}

impl ConnectionIdLease {
    pub fn rent(pool: &Arc<SimpleIdManager>) -> Result<Self, IdError> {
        Ok(Self {
            id: ConnectionId(pool.rent()?),
            pool: pool.clone(),
        })
    }

    pub fn id(&self) -> ConnectionId {
        self.id
    }
}

impl Drop for ConnectionIdLease {
    fn drop(&mut self) {
        let _ = self.pool.give_back(self.id.0);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn id_manager_lowest_free() {
        let mut m = IdManager::new("t", 1, 0x00FF_FFFF, vec![]);
        m.initialize([2u32, 3]);
        assert_eq!(m.get_next_ids(3).unwrap(), vec![1, 4, 5]);
        m.release_id(2);
        assert_eq!(m.get_next_id().unwrap(), 2);
        assert_eq!(m.get_next_id().unwrap(), 6);
        m.release_id(0); // below first id: warning only
    }

    #[test]
    fn id_manager_exhaustion() {
        let mut m = IdManager::new("t", 1, 3, vec![]);
        m.initialize([]);
        assert_eq!(m.get_next_id().unwrap(), 1);
        assert_eq!(m.get_next_id().unwrap(), 2);
        assert!(m.get_next_id().is_err());
        m.release_id(1);
        assert_eq!(m.get_next_id().unwrap(), 1);
    }

    #[test]
    fn simple_id_manager() {
        let m = Arc::new(SimpleIdManager::new());
        assert_eq!(m.rent().unwrap(), 1);
        assert_eq!(m.rent().unwrap(), 2);
        m.give_back(1).unwrap();
        assert_eq!(m.rent().unwrap(), 1);
        assert_eq!(m.give_back(0), Err(IdError::Zero));
        {
            let lease = ConnectionIdLease::rent(&m).unwrap();
            assert_eq!(lease.id(), ConnectionId(3));
        }
        assert_eq!(m.rent().unwrap(), 3);
    }
}
