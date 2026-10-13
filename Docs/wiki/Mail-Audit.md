# Mail Technical Audit

- Scope: ArcheAge 1.2 server mail, quests, auction, player mail and persistence
- Audited: October 13, 2026, rust-rewrite working tree

## Corrected behavior

Player mail rejects negative money, unsupported attached currencies, invalid mail
types, duplicate slots, bound items and postage overflow before moving inventory
or charging the wallet. Registered player mail pays postage. Receivers cannot
read, claim, delete or return mail before delivery. Sent headers do not increment
the sender's received count.

Every mailed item moves into the recipient's persistent Mail container. Loading
legacy mail repairs sender/auction escrow ownership. Ordinary inventory move and
destroy packets cannot bypass Mail/Auction escrow. Returns preserve the original
mail and item IDs, move escrow back to the original sender, charge no new fee, and
cannot be replayed.

Quest overflow rewards are created and stored while the quest and economy
operation locks protect the completion and wallet/item snapshot. The engine
receives failures before completing the quest. Invalid item definitions do not
silently yield empty successful reward mail. Supply acts do not generate their
side effects again on another evaluation.

Auction cancellation returns the original listed item. Listings reject foreign
or bound inventory, invalid prices and duration; a failed move does not charge a
fee or retain a lot ID. Bids reject the seller, expired lots, invalid prices and
insufficient funds before reserving money under the economy lock.

Auction rows, deletion versions, mails, items and character wallets are captured
at the same financial operation boundary and written in one MySQL transaction.
Dirty rows and deletions are acknowledged after commit; rollback retains work
for retry. An old acknowledgement cannot clear a newer auction mutation.

## Verification matrix

| Flow | Evidence |
| --- | --- |
| Player send, normal delay, express delivery, read, claims, delete | Packet-flow regressions; delivery/authentication and invalid input cases |
| Returns with 0, 1 and 10 attachments | Original IDs and money preserved; no fee, correct unread counts, replay rejected |
| Quest overflow rewards | Actual economy bridge test observes mail before completion; 14 items split into two mails |
| Auction bid, refund, buyout, cancel and expiry | Packet-flow tests plus actual mail bridge settlement |
| Commercial, speciality and housing mail | Existing generation, item claims and tax-payment regressions |
| SQL save, rollback, retry and fresh reload | Private MySQL regression for quest deadlines/progress, player and reward mail, auction escrow, items and wallets |
| Real 1.2 client | Local test character reads sender/body/25 copper/item, claims both, disconnects and reconnects; inbox remains empty and claimed item/wallet persist |

The targeted economy suite passes **187 tests**, with eight data/MySQL-gated tests
excluded from that count. The quest suite passes **81 tests**, with 11 gated tests
excluded. The private MySQL regression passes separately, as do both actual compact/client
geometry accounting tests. Final cargo xtask ci passes formatting, Clippy with
warnings denied and workspace tests: **2,857 passed, zero failed, 118 ignored**.
Results are recorded in the local logs below.

## Live test conditions and limits

Only private MySQL on 127.0.0.1:3317 was used. The isolated schemas are
quest_mail_audit_game/login; Login/Game use ports 23237/23239. Client and server
assets were reused without downloading or modifying the installed client.

The real-client test uses disposable characters Questaudit and Mailauditbob.
Existing GM commands supplied levels, timed quests and a mail fixture; this is
a protocol/save/reconnect test, not a retail quest playthrough or a player-send
mailbox UI certification. Quest 2684 counted down and failed at its deadline.
Quest 350 retained its saved absolute deadline through reconnect and resumed
counting down after its blocked supply phase was advanced. The claimed 25 copper
persisted once, the original attachment is owned by Questaudit in Inventory, and
the emptied mail was deleted from MySQL. On the final binary, quest 350 was then
given a past saved deadline while offline. Reconnect immediately displayed
Failed in the real client; disconnect persisted step Fail and status Failed. The
fixture and screenshot are saved alongside the evidence.

The full catalogue of quests remains subject to the [Quest Technical Audit](Quest-Audit).
Absent quest areas/components are not repaired by mail fixes. Scheduled mail
expiry/return policy is still unimplemented. Auction delivery failure handling
still needs review: AuctionManager::send_mail ignores the mail hook's failure
result, so settlement can remove a lot without delivering its item/refund and
without retaining a delivery retry. Successful delivery tests do not cover this
failure path.
Failed-bid refunds use the offer-failed mail type instead of bid-failed; recipient
name lookup is case-sensitive. These are recorded audit findings, not certified
mail behavior.
The successful nominal flows above do not certify every failure or every
mailbox UI path.

## Local evidence and reproduction

Evidence is under D:/aaemu-audits/: quest-mail-economy-verified.log,
quest-mail-quests-verified.log, quest-mail-mysql-verified.log,
quest-mail-ci-verified.log, and quest-mail-live/live-reconnect-evidence.json.
The live folder also contains screenshots, isolated server logs and headless
packet captures. Configs and test credentials are local only.

Run the database regression with AAEMU_TEST_MYSQL pointing at a loopback 3317
test schema whose user supports SQLx authentication:

    cargo test -p aaemu-game --lib private_mysql_quest_mail_auction_rollback_retry_and_reconnect -- --ignored --nocapture

It shadows financial tables with connection-local temporary tables and uses the
production save/load paths. No persistent gameplay rows in the configured
schema are changed by that regression.
