# GF-E06 specialty-market edge split

## Scope

The specialty market already has the authoritative production path:

- cargo and material state is loaded from the market store;
- a sale is prepared against the current market revision;
- the sale store applies the market write, deletes the claimed pack, updates labor, and persists payout mail in one caller transaction;
- the purchase store applies the market write and the character/inventory write in one transaction;
- a stale revision or an ambiguous commit is refused instead of publishing in-memory state.

This split covers only the evidence-backed edge of the client packet family:

- `CSFakeBuySpecialtyItem` (`0x74`) and `CSFakeSellSpecialtyItem` (`0x73`) are parsed with the protocol's signed count and unsigned type;
- both packets are registered and refused through the same policy;
- malformed bodies are rejected as malformed; valid bodies are rejected as unsupported;
- no fake request can change market revision, cargo, ratios, money, labor, inventory, or mail;
- repeated requests are therefore stateless and safe to retry after a reconnect or restart.

There is no price, fee, request id, or market mutation in the fake body, so the server does not invent one.

## Tests

- `SpecialtyFakeOperationPolicyTests` covers valid, zero, and negative-count shapes.
- `CSFakeSpecialtyItemPacketTests` covers unsigned type and signed count wire parsing for both packets.
- The existing `partial class SpecialtyManagerTests` in `SpecialtyManagerMarketTests.cs` and `SpecialtyPersistenceRestartTests` continue to cover stale revisions, duplicate staged writes, rollback, restart, and the atomic sale/purchase settlement paths.

## Blocked remainder

The remaining question is not a safe implementation detail: the client packet family provides no authoritative fake-operation contract, quote, price, or request identity. A fake operation that could create stock, money, or a sale must not be implemented without a content-backed contract and a client-visible result. Real buy/sell retry semantics likewise have no nonce in their packets; their existing row/revision guards prevent double settlement, but a general request-level idempotency key is intentionally not fabricated.
