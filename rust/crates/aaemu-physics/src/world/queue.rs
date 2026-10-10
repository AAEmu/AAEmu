//! **P3 — the deferred command queue** (`world+20872 …`, lock `+22496`): calls made while the
//! world cannot take them immediately are recorded and replayed at the start of the next
//! `TimeStep` (0x3508BD10, step 2 of `world.md` §4).
//!
//! Record ops (`{int op; int size; entity*; payload}`, `-1` terminated pages of 8 KB):
//! 0 SetParams (entity slot 7), 1 Action (slot 10), 2 AddGeometry (slot 11), 3 RemoveGeometry
//! (slot 12), 4 grid reposition (0x35080A00 + pool growth), 5 Destroy (world slot 24).
//!
//! In the single-threaded port the producers are entity creation from a non-physics
//! thread (op 4: every entity created before the first `TimeStep`), SetParams / Action calls
//! that the lock-or-queue helpers defer (ops 0 / 1, [`crate::entities::interface`]), plus
//! whatever a caller queues explicitly. Ropes/soft entities (simclass 7, or 5 with deletion state 2) only take
//! op 1; their other records are dropped.

use super::{EntIdx, World};

/// One queued call.
#[derive(Clone, Debug)]
pub enum Command {
    /// op 0: `SetParams(params, bThreadSafe = 1)` (entity slot 7) with the queued byte image.
    SetParams { ent: EntIdx, buf: Vec<u8> },
    /// op 1: `Action(action, bThreadSafe = 1)` (entity slot 10) with the queued byte image.
    Action { ent: EntIdx, buf: Vec<u8> },
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
                Command::SetParams { ent, buf } => self.replay_call(ent, buf, false),
                Command::Action { ent, buf } => self.replay_call(ent, buf, true),
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

    /// op 0 / op 1: call entity slot 7 / 10 with the queued image and `bThreadSafe = 1`,
    /// then `processed -= 0x20000`. Ropes / soft bodies (simclass 7, or 5 while being deleted)
    /// only take action id 16 (an AddRef of its target; neither is ported), so their records
    /// are dropped (the counter is still decremented).
    fn replay_call(&mut self, ent: EntIdx, mut buf: Vec<u8>, is_action: bool) {
        if self.ents[ent.0 as usize].is_none() {
            return;
        }
        let sc = self.ent(ent).sim_class();
        if !(sc == 7 || (sc == 5 && self.ent(ent).deleted == 2)) && buf.len() >= 4 {
            if is_action {
                crate::entities::interface::action(self, ent, &mut buf, 1);
            } else {
                crate::entities::interface::set_params(self, ent, &mut buf, 1);
            }
        }
        let e = self.ent_mut(ent);
        e.processed = e.processed.wrapping_add(0xFFFE_0000);
    }
}
