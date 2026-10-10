//! **P3 — the deferred command queue** (`world+20872 …`, lock `+22496`): calls made while the
//! world cannot take them immediately are recorded and replayed at the start of the next
//! `TimeStep` (0x3508BD10, step 2 of `world.md` §4).
//!
//! Record ops (`{int op; int size; entity*; payload}`, `-1` terminated pages of 8 KB):
//! 0 SetParams (entity slot 7), 1 Action (slot 10), 2 AddGeometry (slot 11), 3 RemoveGeometry
//! (slot 12), 4 grid reposition (0x35080A00 + pool growth), 5 Destroy (world slot 24).
//!
//! In the single-threaded port the only producer is entity creation from a non-physics
//! thread (op 4: every entity created before the first `TimeStep`), plus whatever a caller
//! queues explicitly. Ropes/soft entities (simclass 7, or 5 with deletion state 2) only take
//! op 1; their other records are dropped.

use super::{EntIdx, World};

/// One queued call.
#[derive(Clone, Debug)]
pub enum Command {
    /// op 4: `RepositionEntity(ent, flags, NULL)` and the pool growth.
    Reposition { ent: EntIdx, flags: u32 },
    /// op 5: `DestroyPhysicalEntity(ent, mode, 1)`.
    Destroy { ent: EntIdx, mode: i32 },
}

/// The double-buffered queue.
#[derive(Clone, Debug, Default)]
pub struct CommandQueue {
    pub cmds: Vec<Command>,
}

impl CommandQueue {
    /// Append a record.
    pub fn push(&mut self, c: Command) {
        self.cmds.push(c);
    }
}

impl World {
    /// Execute and clear the queue (the loop at the top of TimeStep).
    pub(crate) fn flush_command_queue(&mut self) {
        let cmds = std::mem::take(&mut self.queue.cmds);
        for c in cmds {
            match c {
                Command::Reposition { ent, flags } => {
                    if self.ents[ent.0 as usize].is_none() {
                        continue;
                    }
                    let sc = self.ent(ent).sim_class();
                    if sc == 7 || (sc == 5 && self.ent(ent).deleted == 2) {
                        continue;
                    }
                    self.ent_mut(ent).flags &= !0x8000_0000;
                    self.reposition_entity(ent, flags, None);
                    self.n_ents_pool += 1;
                    if self.n_ents_pool > self.pool_size - 1 {
                        self.grow_pools();
                    }
                }
                Command::Destroy { ent, mode } => {
                    if self.ents[ent.0 as usize].is_some() {
                        self.destroy_physical_entity(ent, mode);
                    }
                }
            }
        }
    }
}
