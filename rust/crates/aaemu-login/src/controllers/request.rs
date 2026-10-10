//! `RequestController`: request ids for login -> game server round-trips (`LGRequestInfo`),
//! each with a completion signal and a timeout.

use std::future::Future;
use std::sync::Mutex;
use std::time::Duration;

use dashmap::DashMap;
use tokio::sync::oneshot;

use crate::id_manager::{IdError, IdManager};

const FIRST_ID: u32 = 0x0000_0001;
const LAST_ID: u32 = 0x00FF_FFFF;

pub struct RequestController {
    ids: Mutex<IdManager>,
    requests: DashMap<u32, oneshot::Sender<()>>,
}

impl Default for RequestController {
    fn default() -> Self {
        Self::new()
    }
}

impl RequestController {
    pub fn new() -> Self {
        Self {
            ids: Mutex::new(IdManager::new(
                "RequestController",
                FIRST_ID,
                LAST_ID,
                Vec::new(),
            )),
            requests: DashMap::new(),
        }
    }

    /// No id tables to scan for this manager.
    pub fn initialize(&self) -> bool {
        self.ids.lock().unwrap().initialize(std::iter::empty());
        true
    }

    pub fn get_next_id(&self) -> Result<u32, IdError> {
        self.ids.lock().unwrap().get_next_id()
    }

    pub fn get_next_ids(&self, count: usize) -> Result<Vec<u32>, IdError> {
        self.ids.lock().unwrap().get_next_ids(count)
    }

    /// Allocates `count` request ids. The returned future completes once every request has been
    /// released or has individually timed out after `timeout`.
    ///
    /// Note (as in C#): ids of requests that time out are never released.
    pub fn create(
        &self,
        count: usize,
        timeout: Duration,
    ) -> Result<(Vec<u32>, impl Future<Output = ()> + Send + 'static), IdError> {
        let ids = self.get_next_ids(count)?;
        let mut waits = Vec::with_capacity(count);
        for &id in &ids {
            let (tx, rx) = oneshot::channel();
            self.requests.insert(id, tx);
            waits.push(async move {
                let _ = tokio::time::timeout(timeout, rx).await;
            });
        }
        Ok((ids, async move {
            futures::future::join_all(waits).await;
        }))
    }

    /// Completes the pending request (if any) and frees the id.
    pub fn release_id(&self, id: u32) {
        if let Some((_, tx)) = self.requests.remove(&id) {
            let _ = tx.send(());
        }
        self.ids.lock().unwrap().release_id(id);
    }

    pub fn release_ids(&self, ids: impl IntoIterator<Item = u32>) {
        for id in ids {
            self.release_id(id);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn completes_on_release_or_timeout() {
        let rc = RequestController::new();
        rc.initialize();
        let (ids, wait) = rc.create(2, Duration::from_secs(30)).unwrap();
        assert_eq!(ids, vec![1, 2]);
        rc.release_ids(ids.clone());
        tokio::time::timeout(Duration::from_secs(1), wait)
            .await
            .expect("released requests complete");

        let (ids, wait) = rc.create(1, Duration::from_millis(20)).unwrap();
        assert_eq!(ids, vec![1]); // lowest free id is reused
        tokio::time::timeout(Duration::from_secs(1), wait)
            .await
            .expect("timeout completes the wait");
    }
}
