# GF-C12 seat topology — verification boundary

The implementation derives rider seats from the shipped mount-skill joins. It does not derive a
seat list from a capacity field and it does not promote mast, sail, equipment, or presentation
attach points into rider seats.

## Covered locally

- One-seat, multi-seat, duplicate, and invalid-row catalog behavior.
- Both SQLite join queries against synthetic schema fixtures.
- Mate and slave model initialization from explicit seat sets.
- Rejection of a slave attach point absent from the catalog.
- Stable all-rider release helper used by death/despawn paths.

## Live gaps

The following require a running World with content-loaded game data and a real client session;
they are not represented by synthetic manager fixtures:

- `CharacterMates.SpawnMount` and `SlaveManager.Create` applying the catalog during actual summoning.
- Real owner/non-owner driver-seat rejection and client-visible board refusal.
- Per-frame board/unboard packets and the World↔Zone attach/detach relay during a live mount.
- Client-visible release of every rider when a mate/slave dies or is despawned.

A live pass should record the template, packet order, server log, persisted state, and a clean
client restart. No template, seat, or client identifier is hardcoded in this branch.
