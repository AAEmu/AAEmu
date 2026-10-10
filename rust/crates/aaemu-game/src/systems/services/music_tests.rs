//! Port of AAEmu.UnitTests/Game/Core/Managers/MusicManagerTests.cs
//!
//! `Constructor_DoesNotCallDeps` verifies (Moq) that the constructor touches neither the
//! `IMusicIdManager` nor the `IItemManager`. The Rust `MusicManager` has no injected
//! dependencies (both are passed per call); construction has no side effects: no songs, no
//! queued uploads, no MIDI cache.

use super::MusicManager;

#[test]
fn constructor_does_not_call_deps() {
    let manager = MusicManager::default();
    assert!(manager.get_song_by_id(1).is_none());
    assert!(manager.queued_upload(1).is_none());
    assert!(manager.get_midi_cache(1).is_empty());
}
