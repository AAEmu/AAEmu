//! Packet capture: every frame a bot sends or receives, in order, as JSON lines.

use std::io::Write;
use std::path::Path;
use std::sync::{Arc, Mutex};
use std::time::Instant;

use serde::{Deserialize, Serialize};

/// Which socket a frame travelled on.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Channel {
    Login,
    Game,
    Stream,
}

/// Direction of a frame.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum Dir {
    C2S,
    S2C,
}

/// One captured frame. `level` is the game frame level (1 = game, 2 = proxy, 0 = login/stream).
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct Event {
    pub t_ms: u64,
    pub channel: Channel,
    pub dir: Dir,
    pub level: u8,
    pub opcode: u16,
    pub name: String,
    /// Scenario step this frame belongs to (the last [`Recorder::step`] marker).
    pub step: String,
    /// Body after the opcode, hex encoded.
    pub body: String,
}

impl Event {
    pub fn body_bytes(&self) -> Vec<u8> {
        hex::decode(&self.body).unwrap_or_default()
    }
}

#[derive(Debug)]
struct Inner {
    start: Instant,
    step: String,
    events: Vec<Event>,
    enabled: bool,
}

/// Shared, cheaply cloneable capture buffer.
#[derive(Clone, Debug)]
pub struct Recorder(Arc<Mutex<Inner>>);

impl Default for Recorder {
    fn default() -> Self {
        Self::new(true)
    }
}

impl Recorder {
    pub fn new(enabled: bool) -> Self {
        Self(Arc::new(Mutex::new(Inner {
            start: Instant::now(),
            step: "connect".into(),
            events: Vec::new(),
            enabled,
        })))
    }

    fn inner(&self) -> std::sync::MutexGuard<'_, Inner> {
        self.0.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Starts a new scenario step; later frames are tagged with it.
    pub fn step(&self, name: &str) {
        self.inner().step = name.to_string();
    }

    pub fn current_step(&self) -> String {
        self.inner().step.clone()
    }

    pub fn push(
        &self,
        channel: Channel,
        dir: Dir,
        level: u8,
        opcode: u16,
        name: &str,
        body: &[u8],
    ) {
        let mut i = self.inner();
        if !i.enabled {
            return;
        }
        let ev = Event {
            t_ms: i.start.elapsed().as_millis() as u64,
            channel,
            dir,
            level,
            opcode,
            name: name.to_string(),
            step: i.step.clone(),
            body: hex::encode(body),
        };
        i.events.push(ev);
    }

    pub fn events(&self) -> Vec<Event> {
        self.inner().events.clone()
    }

    pub fn save(&self, path: &Path) -> anyhow::Result<()> {
        let mut f = std::io::BufWriter::new(std::fs::File::create(path)?);
        for e in self.inner().events.iter() {
            serde_json::to_writer(&mut f, e)?;
            f.write_all(b"\n")?;
        }
        f.flush()?;
        Ok(())
    }
}

/// Reads a capture written by [`Recorder::save`].
pub fn load(path: &Path) -> anyhow::Result<Vec<Event>> {
    let text = std::fs::read_to_string(path)?;
    text.lines()
        .filter(|l| !l.trim().is_empty())
        .map(|l| Ok(serde_json::from_str(l)?))
        .collect()
}
