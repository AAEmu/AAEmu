#!/usr/bin/env python3
"""S->C opcode usage audit: which Game->Client packets the 1.2 game crate *defines* but never *sends*.

The C->S side is already covered elsewhere (every 1.2 C->S opcode has a registered handler).  The
interesting gap is the other direction: `opcodes::sc` lists 517 1.2 S->C opcodes and the crate
declares a Rust type for 402 of them, but a *declared* type is not a *sent* packet.  Packets that
are only defined (their `Marshal` impl exists, nothing ever builds them) are exactly the client
features that have no server implementation yet.

Outputs (both regenerated on every run, next to this script):

  * ``sc_sent.json`` -- machine readable: every S->C packet type with bucket, opcode, file, line,
    evidence sites and (for defined-only) a feature guess; plus the opcodes that have no type at
    all and the type names that are declared twice.
  * ``sc_sent.md``   -- human readable: counted summary, the three bucket tables, the appendix of
    opcodes without any type, and notes.

Heuristics (deliberately simple text analysis; no cargo, no macro expansion)
---------------------------------------------------------------------------
1. *Definition* of an S->C packet type is one of
     - ``packet_def!(Type, level = .., opcode = <mod>::<CONST>)`` with ``mod == sc``,
     - ``raw_body_packet!(Type, sc::CONST)``,
     - an invocation of one of the local generator macros that expand to a `packet_def!`:
       ``sc_packet!`` / ``simple_packet!`` (opcode is ``sc::<Type>`` by construction),
       ``bc_packet!`` / ``skill_level_packet!`` (opcode is the macro's second argument),
     - a hand written ``impl PacketDef for Type { const OPCODE: u16 = 0x...; }`` (two of those
       exist in ``packets/world.rs`` for the C# "empty faction list" constructors); the literal
       opcode is mapped back to the ``sc`` constant name.
   Macro *bodies* are skipped by rejecting a first argument containing ``$`` (``$ty``/``$name``).
   Proxy (``pp::``), stream (``TC*``) and login (``GL*``/``LG*``) packets are not S->C game
   packets and are ignored.

2. *Test code* is excluded globally.  A file counts as test-only when its name ends in
   ``_tests.rs`` / is ``test_support.rs`` / ``tests.rs``, when it lives under a ``tests/``
   directory, or when it is pulled in by a ``#[cfg(test)] mod x;`` declaration.  Inside any file,
   the body following ``#[cfg(test)]`` / ``#[test]`` / ``#[tokio::test]`` is brace matched and
   treated as test-only too (covers ``#[cfg(test)] mod tests { .. }`` and ``#[cfg(test)] fn ..``).

3. Comments and string/char literals are blanked out before searching, so a name that only shows
   up in a doc comment, a log message or the ``sc::NAMES`` opcode table is not mistaken for code.
   Occurrences on the definition statement, ``use`` lines, ``struct`` declaration lines and
   ``impl ... for Type`` header lines are ignored as well -- they are declaration, not use.

4. An occurrence is *strong evidence* that the packet is sent when the code builds a value:
   ``Type { .. }`` (struct literal / raw-body construction, also when path qualified as
   ``crate::packets::g2c::Type { .. }``), ``Type::new(..)`` / ``Type::default()`` /
   ``Type::from(..)`` / another associated constructor call, a unit struct used as a value
   (``conn.send(&SCLeaveWorldCanceledPacket)``, ``p.send(SCICSCheckTimePacket)``) or the token
   sitting in the argument list of a send helper (``.send(..)``/``broadcast(..)``).  A bare type
   position (``-> Option<Type>``, ``Vec<Type>``, ``Option<(Type, u32)>``, an enum variant field)
   or a lone ``sc::Type`` opcode-constant reference is only *medium* evidence.

5. Buckets: ``sent`` (>= 1 strong evidence outside test code), ``defined_only`` (appears nowhere
   outside its own definition), ``unclear`` (only medium evidence -- reported together with the
   reason, never silently folded into one of the other two).  For this revision every occurrence
   of every ``defined_only`` packet outside test code was additionally printed and eyeballed:
   they are only the `opcodes.rs` constant, the `struct` declaration and the `impl Marshal`
   header, and the handful of weak-evidence ``sent`` rows were checked by hand (unit structs sent
   as ``&Packet``, ``SCCharacterListPacket::chunked(..)``).  The ``unclear`` bucket is therefore
   empty right now but stays in the report for future runs.

Known limitations (stated honestly, they are why ``unclear`` exists):
   * no call-graph analysis: a packet constructed only inside a function that is itself never
     called is still classified ``sent``;
   * macro expansion is not performed: a packet type forwarded through a macro is only detected
     through the argument's own syntax (fine for the generator macros above and for
     ``sync_client_packet!``, which only wraps C->S packets);
   * a packet assembled by hand into a raw byte buffer would not be detected (no such case is
     known in this crate: the ``raw_body_packet!`` types are still built as struct literals);
   * three type names are declared twice (two modules, same opcode); all copies are analysed
     together and the extra declarations are reported in ``duplicate_types``.

Ratchet (CI)
------------
``sc_baseline.json`` pins the two gap lists (opcodes without any Rust type = ``typeless``; types
defined but never sent outside tests = ``never_sent``) with their counts.  ``--check`` re-analyses
the sources (needs only the checked-in Rust files: no client data, no compact.sqlite3, no network),
writes nothing and exits 1 when either list gains a member.  Shrinking is allowed; the check then
hints to refresh the baseline with ``--update-baseline``.  ``cargo xtask ci`` runs ``--check``
(skipped with a notice when python is missing).

Usage: python rust/tools/opcode_coverage/sc_usage.py [--md out.md] [--json out.json]
       python rust/tools/opcode_coverage/sc_usage.py --check | --update-baseline
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]  # <repo>/rust/tools/opcode_coverage -> <repo>
GAME = REPO / "rust" / "crates" / "aaemu-game"
SRC = GAME / "src"
OPCODES = SRC / "opcodes.rs"
DEFAULT_JSON = Path(__file__).resolve().parent / "sc_sent.json"
DEFAULT_MD = Path(__file__).resolve().parent / "sc_sent.md"
DEFAULT_BASELINE = Path(__file__).resolve().parent / "sc_baseline.json"

# Generator macros of this crate that declare an S->C packet.  ``implicit`` means the opcode is
# ``sc::<TypeName>`` (baked into the macro body), otherwise the opcode is an explicit argument.
GENERATORS = {
    "sc_packet": {"implicit": True, "name_follow": "{"},
    "simple_packet": {"implicit": True, "name_follow": "{"},
    "bc_packet": {"implicit": False, "name_follow": ","},
    "skill_level_packet": {"implicit": False, "name_follow": ","},
}

CALL_RE = re.compile(
    r"\b(packet_def|raw_body_packet|sc_packet|simple_packet|bc_packet|skill_level_packet)!\s*\("
)
MOD_RE = re.compile(r"^\s*pub mod\s+(\w+)")
SC_CONST_RE = re.compile(r"^\s*pub const\s+(\w+)\s*:\s*u16\s*=\s*(0x[0-9a-fA-F]+)")

TEST_ATTR_RE = re.compile(r"#\[(cfg\(test\)|test|tokio::test[^\]]*|async_std::test[^\]]*)\]")
CFG_TEST_MOD_RE = re.compile(r"#\[cfg\(test\)\]\s*(?:#\[[^\]]*\]\s*)*mod\s+(\w+)\s*;")
IMPL_PACKETDEF_RE = re.compile(
    r"impl\s+(?:crate::net::packet::)?PacketDef\s+for\s+(\w+)\s*\{([^}]*)\}", re.S
)
OPCODE_LIT_RE = re.compile(r"const\s+OPCODE\s*:\s*u16\s*=\s*(0x[0-9a-fA-F]+|\d+)")
# `&Type` in expression position: a unit struct used as a value
# (``conn.send(&SCLeaveWorldCanceledPacket)``).  A `&` that follows `:`/`-`/`<` is a reference
# *type* (``p: &SCFooPacket``), so it is excluded.
UNIT_VALUE_RE = re.compile(r"(?:^|[(,=\[])\s*&\s*$")
# the token sits in the argument list of one of the crate's send helpers
SEND_CALL_RE = re.compile(
    r"(?:\.\s*)?(?:send|send_packet|send_packet_and_close|broadcast|send_to|send_self)\s*\([^()]*$"
)
# an opening `(` directly preceded by something callable (`foo(`) -- as opposed to a tuple type
CALL_PAREN_RE = re.compile(r"[\w.)\]]\s*\($")
STRONG_ROLES = {
    "struct_literal",
    "construct_call",
    "associated_call",
    "unit_value",
    "send_arg",
    "value_position",
}

# --- feature guess: CamelCase keyword of the packet name -> client feature ----------------------
# Longest keyword wins, and a keyword only matches at a CamelCase word boundary, so that e.g.
# ``SC...ReQuest...`` does not match "Quest".
FEATURES: list[tuple[str, str]] = [
    ("BeautySalon", "beauty salon / appearance change UI"),
    ("DominionDeleted", "castle dominion (siege) UI"),
    ("DominionOwnerChanged", "castle dominion (siege) UI"),
    ("DominionTaxBalanced", "castle dominion tax UI"),
    ("DominionTaxRate", "castle dominion tax UI"),
    ("DominionData", "castle dominion (siege) UI"),
    ("Dominion", "castle dominion (siege) UI"),
    ("NationalTaxRate", "nation tax rate UI"),
    ("NationalMonumentChanged", "national monument UI"),
    ("FactionImmigrateToOrigin", "faction immigration UI"),
    ("FactionImmigrateInviteResult", "faction immigration UI"),
    ("FactionImmigrateInvite", "faction immigration UI"),
    ("FactionKickToOrigin", "faction exile UI"),
    ("FactionDeclareHostile", "faction diplomacy/war UI"),
    ("FactionSetRelationState", "faction relation UI"),
    ("FactionRelationExpired", "faction relation UI"),
    ("FactionRelationList", "faction relation UI"),
    ("FactionRenamed", "faction rename UI"),
    ("FactionRetryRename", "faction rename UI"),
    ("FactionIndependence", "faction independence UI"),
    ("FactionOwnerChanged", "faction owner UI"),
    ("FactionList", "faction UI"),
    ("Faction", "faction UI"),
    ("ExpeditionSponsorChanged", "expedition (guild) sponsor UI"),
    ("ExpeditionDismissed", "expedition disband"),
    ("ExpeditionRolePolicy", "expedition rank/permission UI"),
    ("ExpeditionRoleChanged", "expedition rank/permission UI"),
    ("ExpeditionOwnerChanged", "expedition leader change"),
    ("ExpeditionMemberStatusChanged", "expedition member list"),
    ("ExpeditionMemberList", "expedition member list"),
    ("ExpeditionInvitation", "expedition invite popup"),
    ("ExpeditionShowRename", "expedition rename UI"),
    ("Expedition", "expedition (guild) UI"),
    ("FamilyMemberNameChanged", "family UI"),
    ("FamilyMemberOnline", "family member online status"),
    ("FamilyMember", "family UI"),
    ("FamilyTitle", "family title UI"),
    ("FamilyOwnerChanged", "family UI"),
    ("Family", "family UI"),
    ("SlaveEquipmentChanged", "vehicle/mercenary equipment UI"),
    ("SlaveCreated", "vehicle (slave) spawn"),
    ("SlaveRemoved", "vehicle (slave) despawn"),
    ("SlaveDespawn", "vehicle (slave) despawn"),
    ("SlaveBound", "vehicle bound point UI"),
    ("EscapeSlave", "vehicle escape/eject"),
    ("MySlave", "vehicle (slave) UI"),
    ("Mate", "pet/mount UI"),
    ("TrialCanceled", "jury/trial UI"),
    ("Trial", "jury/trial UI"),
    ("Jury", "jury/trial UI"),
    ("SuspectGoingBotTrial", "bot trial / jury UI"),
    ("BotSuspectArrested", "bot trial / jury UI"),
    ("BotSuspectReported", "bot trial / jury UI"),
    ("CrimeChanged", "crime/infamy UI"),
    ("TeamAreaInvited", "party/raid area UI"),
    ("AskToJoinTeamArea", "party/raid area UI"),
    ("Team", "party/raid UI"),
    ("RankAlarm", "ranking UI"),
    ("RankSnapshot", "ranking UI"),
    ("RankCharacter", "ranking UI"),
    ("Rank", "ranking UI"),
    ("Achievement", "achievement UI"),
    ("PremiumPointChanged", "premium/credits UI"),
    ("UpdatePremiumPoint", "premium/credits UI"),
    ("BmPoint", "cash shop points"),
    ("CashShop", "cash shop"),
    ("Ics", "in-game cash shop (ICS)"),
    ("IcsCheckTime", "in-game cash shop (ICS)"),
    ("IcsSyncGood", "in-game cash shop (ICS)"),
    ("IcsExchangeRatio", "in-game cash shop (ICS)"),
    ("Quest", "quest UI"),
    ("MailRemoved", "mail UI"),
    ("MailReceiverOpened", "mail UI"),
    ("Mail", "mail UI"),
    ("Auction", "auction house UI"),
    ("Coffer", "housing storage coffer"),
    ("HouseBuildPayChanged", "housing build tax UI"),
    ("HouseBuildProgress", "housing build progress"),
    ("HouseFarm", "housing farm UI"),
    ("HouseOwnerNameChanged", "housing UI"),
    ("HouseState", "housing UI"),
    ("MyHouse", "housing UI"),
    ("House", "housing UI"),
    ("Housing", "housing UI"),
    ("Tax", "tax UI"),
    ("Specialty", "specialty (trade pack) vendor"),
    ("Trade", "trading (pack runs)"),
    ("CraftFailed", "crafting UI"),
    ("Craft", "crafting UI"),
    ("ScheduleItemUpdate", "item update scheduling"),
    ("ItemTask", "item task/progress UI"),
    ("Item", "item/inventory UI"),
    ("Inven", "inventory UI"),
    ("Cooldowns", "cooldown UI"),
    ("Bank", "bank UI"),
    ("SkillUpgraded", "skills UI"),
    ("SkillLearned", "skills UI"),
    ("Skill", "skills UI"),
    ("AbilityExpChanged", "skill tree (ability) exp UI"),
    ("Ability", "skill tree UI"),
    ("Buff", "buff UI"),
    ("CombatCleared", "combat state UI"),
    ("CombatEngaged", "combat state UI"),
    ("CombatFirstHit", "combat UI"),
    ("CvFCombatRelationship", "combat relationship (faction) UI"),
    ("FvFCombatRelationship", "combat relationship (faction) UI"),
    ("Aggro", "NPC aggro UI"),
    ("PortalDeleted", "portal UI"),
    ("UnitPortalUsed", "portal used"),
    ("PortalInfoSaved", "portal UI"),
    ("Portal", "portal/teleport UI"),
    ("NaviTeleport", "navigation teleport"),
    ("AboxTeleport", "abox (item box) teleport"),
    ("TeleportUnit", "teleport"),
    ("ReturnDistricts", "return district UI"),
    ("Resurrect", "resurrection UI"),
    ("LaborPowerChanged", "labor power UI"),
    ("LpManaged", "labor power UI"),
    ("Playtime", "playtime/anti-addiction UI"),
    ("PvPPointsChanged", "PvP points UI"),
    ("HonorPointSum", "conflict-zone honor points UI"),
    ("BountyMoney", "bounty UI"),
    ("Mileage", "mileage points UI"),
    ("ActionPoint", "action point (combo) UI"),
    ("GamePointChanged", "game points (honor/vocation) UI"),
    ("GmModeChanged", "GM mode UI"),
    ("IdleKick", "idle kick"),
    ("LevelRestriction", "level restriction config"),
    ("KnockBack", "knockback movement"),
    ("Offline", "unit offline state"),
    ("LootingState", "looting UI"),
    ("MineAmount", "mining"),
    ("ShowCommonFarm", "public farm UI"),
    ("Farm", "public farm UI"),
    ("DoodadOriginator", "doodad ownership UI"),
    ("Doodad", "doodad (world object) UI"),
    ("Gimmick", "gimmick (elevator/trap)"),
    ("Music", "music instrument UI"),
    ("Radar", "radar UI"),
    ("ExpressText", "express text / emote UI"),
    ("ChatBubble", "chat bubble above a unit"),
    ("ChatSpamDelay", "chat spam throttle"),
    ("Chat", "chat UI"),
    ("Friends", "friend list UI"),
    ("AddFriend", "friend list UI"),
    ("DeleteFriend", "friend list UI"),
    ("FriendStatusChanged", "friend list UI"),
    ("Friend", "friend list UI"),
    ("BlockedUser", "block list UI"),
    ("SearchList", "player search UI"),
    ("CharacterBound", "character bind point UI"),
    ("CharacterDeleted", "character deletion"),
    ("CharacterRenamed", "character rename"),
    ("CharacterList", "character list (login)"),
    ("Character", "character UI"),
    ("Unit", "unit/world state"),
    ("Duel", "duel UI"),
    ("FishSchool", "fishing"),
    ("Fish", "fishing"),
    ("Vehicle", "vehicle UI"),
    ("Shipyard", "shipyard UI"),
    ("Transfer", "transfer (carriage/airship) UI"),
    ("Account", "account/patron UI"),
    ("Trion", "Trion account config"),
    ("HackGuard", "hack-guard handshake"),
    ("WorldQueue", "login world queue"),
    ("WorldMessage", "world/announcement message"),
    ("Reconnect", "reconnect handshake"),
    ("LeaveWorld", "leave-world flow"),
    ("InstantGameRemainTime", "instant game (mini-game) timer"),
    ("InstantGame", "instant game (mini-game) UI"),
    ("UnlockCurrencySlot", "currency slot unlock"),
    ("SiegeReinforce", "castle siege reinforcement"),
    ("SpamReported", "spam report feedback"),
    ("Unknown", "unknown/legacy opcode, no known client feature"),
    ("ConflictZone", "conflict zone UI"),
    ("Escape", "escape/eject"),
]


def camel_words(name: str) -> list[str]:
    """``SCUnitPvPPointsChangedPacket`` -> ``['Unit','Pv','P','Points','Changed','Packet']``."""
    body = re.sub(r"^(SC|X2)", "", name)
    return re.findall(r"[A-Z]+(?![a-z])|[A-Z][a-z0-9]*|[a-z0-9]+", body)


def feature_guess(name: str, doc: str) -> str:
    """Map an S->C packet name onto the client feature it belongs to (longest keyword wins)."""
    words = camel_words(name)
    for key, feature in sorted(FEATURES, key=lambda kv: -len(kv[0])):
        low = key.lower()
        for i in range(len(words)):
            if "".join(words[i:]).lower().startswith(low):
                return feature
    doc = re.sub(r"^`+|\s+", " ", doc).strip()
    if doc:
        return f"unclassified -- C# doc: {doc[:70]}"
    return "unclassified (no keyword match)"


# ------------------------------------------------------------------------------------------------
# lexical helpers
# ------------------------------------------------------------------------------------------------
def mask_code(text: str) -> str:
    """Blank out comments and string/char literal bodies (newlines are preserved).

    The result has exactly the same length and line structure as ``text`` so offsets and line
    numbers stay comparable; anything that disappears was inside a comment or a literal.
    """
    out = list(text)
    i, n = 0, len(text)

    def blank(a: int, b: int) -> None:
        for k in range(a, min(b, n)):
            if out[k] != "\n":
                out[k] = " "

    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            blank(i, j)
            i = j
        elif c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            blank(i, j)
            i = j
        elif (c == "r" or (c == "b" and i + 1 < n and text[i + 1] == "r")):
            # raw string: r"..." / r#"..."# / br#"..."#
            k = i + 1 if c == "r" else i + 2
            hashes = 0
            while k < n and text[k] == "#":
                hashes += 1
                k += 1
            if k < n and text[k] == '"':
                close = '"' + "#" * hashes
                j = text.find(close, k + 1)
                j = n if j < 0 else j + len(close)
                blank(i, j)
                i = j
            else:
                i += 1
        elif c == '"':
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    j += 1
                    break
                j += 1
            blank(i, j)
            i = j
        elif c == "'":
            # char literal vs lifetime
            if text[i + 1 : i + 2] == "\\":
                j = i + 2
                while j < n and text[j] != "'":
                    j += 2 if text[j] == "\\" else 1
                j = min(j + 1, n)
                blank(i, j)
                i = j
            elif text[i + 2 : i + 3] == "'" and text[i + 1] != "'":
                blank(i, i + 3)
                i += 3
            else:
                i += 1  # lifetime
        else:
            i += 1
    return "".join(out)


def balanced(text: str, open_paren: int) -> tuple[str, int] | None:
    """Contents of the balanced ``( ... )`` starting at ``open_paren`` plus its end offset."""
    depth, i, n = 0, open_paren, len(text)
    while i < n:
        ch = text[i]
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return text[open_paren + 1 : i], i + 1
        i += 1
    return None


def split_top_level(args: str) -> list[str]:
    out, depth, cur = [], 0, ""
    for ch in args:
        if ch in "(<{[":
            depth += 1
        elif ch in ")>}]":
            depth -= 1
        if ch == "," and depth == 0:
            out.append(cur)
            cur = ""
        else:
            cur += ch
    out.append(cur)
    return [p.strip() for p in out]


def line_index(text: str) -> list[int]:
    """Offset of the start of every line (index 0 == line 1)."""
    starts, pos = [0], 0
    while True:
        pos = text.find("\n", pos)
        if pos < 0:
            break
        pos += 1
        starts.append(pos)
    return starts


def line_of(starts: list[int], offset: int) -> int:
    lo, hi = 0, len(starts) - 1
    while lo < hi:
        mid = (lo + hi + 1) // 2
        if starts[mid] <= offset:
            lo = mid
        else:
            hi = mid - 1
    return lo + 1


# ------------------------------------------------------------------------------------------------
# opcode table
# ------------------------------------------------------------------------------------------------
def load_opcodes() -> dict[str, dict[str, int]]:
    tables: dict[str, dict[str, int]] = {}
    current = None
    for line in OPCODES.read_text(encoding="utf-8").splitlines():
        m = MOD_RE.match(line)
        if m:
            current = m.group(1)
            tables.setdefault(current, {})
            continue
        m = SC_CONST_RE.match(line)
        if m and current:
            tables[current][m.group(1)] = int(m.group(2), 16)
    return tables


# ------------------------------------------------------------------------------------------------
# source scan
# ------------------------------------------------------------------------------------------------
class Source:
    """One Rust file with its masked text and derived line sets."""

    def __init__(self, path: Path, repo: Path, test_only: bool, text: str | None = None):
        self.path = path
        self.rel = str(path.relative_to(repo)).replace("\\", "/")
        self.text = path.read_text(encoding="utf-8", errors="replace") if text is None else text
        self.masked = mask_code(self.text)
        self.starts = line_index(self.text)
        self.lines = self.text.splitlines()
        self.masked_lines = self.masked.splitlines()
        self.test_lines: set[int] = set()
        if test_only:
            self.test_lines.update(range(1, len(self.lines) + 1))
        self._find_test_regions()
        self.use_ranges = [
            (m.start(), m.end())
            for m in re.finditer(
                r"(?:^|\n)\s*(?:pub(?:\s*\([^)]*\))?\s+)?use\b[^;]*;", self.masked
            )
        ]

    def in_use(self, offset: int) -> bool:
        """True when ``offset`` sits inside a (possibly multi line) ``use`` statement."""
        for start, end in self.use_ranges:
            if start <= offset <= end:
                return True
        return False

    def _find_test_regions(self) -> None:
        """Brace match the item that follows a ``#[cfg(test)]`` / ``#[test]`` attribute."""
        for m in TEST_ATTR_RE.finditer(self.masked):
            i, depth = m.end(), 0
            end = None
            limit = min(len(self.masked), m.end() + 4000)
            while i < limit:
                ch = self.masked[i]
                if ch in "([<":
                    depth += 1
                elif ch in ")]>":
                    depth -= 1
                elif ch == "{" and depth <= 0:
                    j, d = i, 0
                    while j < len(self.masked):
                        if self.masked[j] == "{":
                            d += 1
                        elif self.masked[j] == "}":
                            d -= 1
                            if d == 0:
                                break
                        j += 1
                    end = j
                    break
                elif ch == ";" and depth <= 0:
                    end = i  # e.g. `#[cfg(test)] mod tests;`
                    break
                elif ch == "}" and depth <= 0:
                    break  # attribute with no block after it
                i += 1
            if end is None:
                continue
            first = line_of(self.starts, m.start())
            last = line_of(self.starts, end)
            self.test_lines.update(range(first, last + 1))


def discover_sources() -> dict[str, Source]:
    """Read every ``.rs`` file of the game crate, flagging test-only files."""
    paths = sorted(SRC.rglob("*.rs"))
    texts: dict[str, str] = {}
    for p in paths:
        texts[str(p)] = p.read_text(encoding="utf-8", errors="replace")
    test_only: set[str] = set()
    for p in paths:
        name = p.name
        if name.endswith("_tests.rs") or name in ("test_support.rs", "tests.rs"):
            test_only.add(str(p))
        if "tests" in p.relative_to(SRC).parts[:-1]:
            test_only.add(str(p))
    # `#[cfg(test)] mod x;` declarations pull further files into the test build
    for p in paths:
        for m in CFG_TEST_MOD_RE.finditer(mask_code(texts[str(p)])):
            mod = m.group(1)
            for cand in (p.parent / f"{mod}.rs", p.parent / mod / "mod.rs"):
                if cand.exists():
                    test_only.add(str(cand))
    out: dict[str, Source] = {}
    for p in paths:
        src = Source(p, REPO, str(p) in test_only, texts[str(p)])
        out[src.rel] = src
    return out


def index_identifiers(sources: dict[str, Source], names: set[str]) -> dict[str, list[tuple[str, int]]]:
    """Single pass over every file: offset of each occurrence of the packet names we care about."""
    index: dict[str, list[tuple[str, int]]] = {n: [] for n in names}
    token = re.compile(r"[A-Za-z_]\w*")
    for src in sources.values():
        for m in token.finditer(src.masked):
            word = m.group(0)
            if word in index:
                index[word].append((src.rel, m.start()))
    return index


# ------------------------------------------------------------------------------------------------
# definitions
# ------------------------------------------------------------------------------------------------
def find_definitions(sources: dict[str, Source], sc: dict[str, int]) -> list[dict]:
    defs: list[dict] = []
    for src in sources.values():
        text, masked = src.text, src.masked
        for m in CALL_RE.finditer(masked):
            macro = m.group(1)
            parsed = balanced(masked, m.end() - 1)
            if parsed is None:
                continue
            args, end = parsed
            first_range = (m.end(), end)
            first_line = line_of(src.starts, m.start())
            # doc comments inside the invocation are masked, so `raw` args (from the original
            # text) are only needed for the name; identifiers live in `args` already.
            if macro in ("packet_def", "raw_body_packet"):
                parts = split_top_level(args)
                if not parts or "$" in parts[0] or not re.fullmatch(r"\w+", parts[0]):
                    continue  # macro body ($ty) or something unexpected
                name = parts[0]
                op_match = re.search(r"opcode\s*=\s*([\w:]+)", args)
                if macro == "raw_body_packet":
                    if len(parts) < 2:
                        continue
                    op_path = parts[1]
                elif op_match:
                    op_path = op_match.group(1)
                else:
                    continue
                op_path = op_path.strip()
                if "::" not in op_path:
                    continue
                mod, _, const = op_path.rpartition("::")
                mod = mod.split("::")[-1]
                if mod != "sc":
                    continue
                kind = "raw_body_packet" if macro == "raw_body_packet" else "packet_def"
            elif macro in GENERATORS:
                spec = GENERATORS[macro]
                stripped = re.sub(r"///[^\n]*", "", text[m.end() : end])
                nm = re.search(r"\b([A-Z]\w*)\s*(?=\s*[" + re.escape(spec["name_follow"]) + r"])", stripped)
                if not nm:
                    continue
                name = nm.group(1)
                if spec["implicit"]:
                    mod, const = "sc", name
                else:
                    parts = split_top_level(args)
                    if len(parts) < 2 or "::" not in parts[1]:
                        continue
                    mod, _, const = parts[1].rpartition("::")
                    mod = mod.split("::")[-1]
                    if mod != "sc":
                        continue
                kind = macro
            else:  # pragma: no cover - CALL_RE has no other alternatives
                continue
            if const not in sc:
                continue
            # the first *code* occurrence of the name inside the declaration (doc comments are
            # masked, so this lands on `Name,` / `Name {`, not on the `///` line above it)
            name_off = masked.find(name, m.start(), end)
            if name_off < 0:
                name_off = text.find(name, m.start(), end)
            defs.append(
                {
                    "name": name,
                    "opcode": sc[const],
                    "opcode_const": const,
                    "kind": kind,
                    "file": src.rel,
                    # `line` points at the type name itself, `stmt_line`..`end_line` is the whole
                    # declaration statement (used to ignore it when looking for uses)
                    "line": line_of(src.starts, name_off if name_off >= 0 else m.start()),
                    "stmt_line": first_line,
                    "end_line": line_of(src.starts, end),
                }
            )
        # hand written `impl PacketDef for X { const OPCODE = 0x..; }` (not the test dummies)
        for m in IMPL_PACKETDEF_RE.finditer(masked):
            name, body = m.group(1), m.group(2)
            if not re.fullmatch(r"(SC|X2)\w*Packet", name):
                continue
            stmt_line = line_of(src.starts, m.start())
            if stmt_line in src.test_lines:
                continue
            lit = OPCODE_LIT_RE.search(body)
            if not lit:
                continue
            value = int(lit.group(1), 16) if lit.group(1).startswith("0x") else int(lit.group(1))
            consts = [c for c, v in sc.items() if v == value]
            if not consts:
                continue  # not a 1.2 S->C opcode (e.g. the 0x1234 test dummy)
            defs.append(
                {
                    "name": name,
                    "opcode": value,
                    "opcode_const": consts[0],
                    "kind": "impl PacketDef",
                    "file": src.rel,
                    "line": line_of(src.starts, m.start(1)),
                    "stmt_line": stmt_line,
                    "end_line": line_of(src.starts, m.end()),
                }
            )
    # de-duplicate (3 names are defined twice, in two modules, with the same opcode); keep the
    # first definition as the reported one but remember every site so no copy is counted as use
    seen: dict[str, dict] = {}
    for d in defs:
        site = {"file": d["file"], "line": d["stmt_line"], "end_line": d["end_line"]}
        if d["name"] in seen:
            seen[d["name"]].setdefault("also_defined_at", []).append(site)
            continue
        d["def_sites"] = [site]
        seen[d["name"]] = d
    return sorted(seen.values(), key=lambda d: (d["opcode"], d["name"]))


# ------------------------------------------------------------------------------------------------
# usage classification
# ------------------------------------------------------------------------------------------------
def doc_hint(src: Source, defn: dict) -> str:
    """The nearest `///` block above the definition (the C# signature), for the feature guess."""
    idx = defn["stmt_line"] - 2  # 0-based index of the line above the declaration statement
    floor = max(-1, idx - 40)
    while idx > floor and not src.lines[idx].strip().startswith("///"):
        idx -= 1
    if idx <= floor or not src.lines[idx].strip().startswith("///"):
        return ""
    out: list[str] = []
    while idx >= 0 and src.lines[idx].strip().startswith("///"):
        out.append(src.lines[idx].strip()[3:].strip())
        idx -= 1
    return " ".join(reversed(out))


def classify(defn: dict, sources: dict[str, Source], sites: dict[str, list[tuple[str, int]]]) -> dict:
    name = defn["name"]
    # every copy of the type (a few names are defined in two modules) -- all of their
    # declaration/impl lines are declaration, never use
    def_ranges = [
        (d["file"], d["line"], d["end_line"])
        for d in [*defn["def_sites"], *defn.get("also_defined_at", [])]
    ]
    evidence: list[dict] = []
    ignored: list[dict] = []
    for rel, offset in sites.get(name, []):
        src = sources[rel]
        line = line_of(src.starts, offset)
        text_line = src.lines[line - 1]
        col = offset - src.starts[line - 1]
        before = text_line[:col]
        after = src.masked[offset + len(name) : offset + len(name) + 60]

        def is_def_site() -> bool:
            return any(rel == f and lo <= line <= hi for f, lo, hi in def_ranges)

        kind = "code"
        if is_def_site():
            kind = "definition"
        elif line in src.test_lines:
            kind = "test"
        elif rel == "rust/crates/aaemu-game/src/opcodes.rs":
            # the opcode table itself: `pub const SCFooPacket: u16 = ..` / the `sc::NAMES` rows.
            # Neither is a send path, so the whole file is ignored as evidence.
            kind = "opcode_table"
        elif re.match(r"\s*(pub\s+)?const\s+" + re.escape(name) + r"\b", text_line):
            kind = "const_decl"
        elif src.in_use(offset):
            kind = "use"
        elif re.search(r"\bimpl\b", before) and "{" not in before and ";" not in before:
            kind = "impl_header"
        elif re.match(r"\s*(pub\s+)?struct\s+" + re.escape(name), text_line):
            kind = "struct_decl"
        elif re.search(r"\bimpl\b[^{;]*$", "\n".join(src.masked_lines[max(0, line - 4) : line - 1])):
            # wrapped `impl ...` header: the name sits on a continuation line
            kind = "impl_header"

        site = {"file": rel, "line": line}
        if kind != "code":
            if kind not in ("definition", "struct_decl"):
                ignored.append({**site, "kind": kind})
            continue

        # ---- code occurrence: which syntactic role? ----
        nxt = after.lstrip()
        prev = re.sub(r"\s+$", "", before)
        prev_line_txt = src.lines[line - 2] if line >= 2 else ""
        wrapped_ret = bool(re.search(r"->\s*$", prev_line_txt)) and not prev
        # `a: Type`, `-> Type`, `Vec<Type>`, `crate::path::Type {` must not be read as a
        # constructor call: the `:` of a path separator is not a type ascription.
        type_ascription = bool(re.search(r"(?<!:)(->|:)\s*$", prev))
        if nxt.startswith("::"):
            ident = re.match(r"::\s*(\w+)", nxt)
            ident = ident.group(1) if ident else ""
            if ident in ("new", "default", "from", "with_body", "new_empty"):
                role = "construct_call"
            elif re.fullmatch(r"[A-Z][A-Z0-9_]*", ident):
                role = "const_path"
            else:
                role = "associated_call"
        elif nxt.startswith("{"):
            if type_ascription or wrapped_ret:
                role = "type_position"
            else:
                role = "struct_literal"
        elif re.search(r"(?:^|[^\w:])sc\s*::$", prev):
            role = "opcode_path"
        elif UNIT_VALUE_RE.search(prev):
            # a unit struct can only appear as a value: `conn.send(&SCLeaveWorldCanceledPacket)`
            role = "unit_value"
        elif SEND_CALL_RE.search(prev):
            role = "send_arg"
        elif (not prev or (prev[-1] in ",=[" or (prev[-1] == "(" and CALL_PAREN_RE.search(prev)))) and (
            nxt[:1] not in (">", "]")
        ):
            # a bare value on its own line / argument position: `send(\n  SCFooPacket,\n)`.
            # `(` only counts as a call when something callable precedes it, so a tuple *type*
            # (`-> Option<(SCFooPacket, u32)>`) is not mistaken for an argument list.
            role = "value_position"
        elif nxt[:1] in (">", ",", ")", ";", "]"):
            role = "type_position"
        else:
            role = "type_position"

        evidence.append({**site, "role": role, "text": text_line.strip()[:160]})

    strong = [e for e in evidence if e["role"] in STRONG_ROLES]
    medium = [e for e in evidence if e["role"] not in STRONG_ROLES]

    if strong:
        bucket = "sent"
        reason = ""
    elif medium:
        bucket = "unclear"
        roles = sorted({e["role"] for e in medium})
        if roles == ["type_position"]:
            reason = "referenced only in type position (signature/enum field); no construction found"
        elif "opcode_path" in roles:
            reason = "only the sc:: opcode constant is referenced outside its definition; no construction found"
        else:
            reason = "only weak references found: " + ", ".join(roles)
    else:
        bucket = "defined_only"
        reason = ""

    src = sources[defn["file"]]
    return {
        **defn,
        "bucket": bucket,
        "reason": reason,
        "doc": doc_hint(src, defn),
        "feature": feature_guess(name, doc_hint(src, defn)) if bucket == "defined_only" else "",
        "evidence": (strong or medium)[:4],
        "evidence_count": len(evidence),
        "ignored_sites": len(ignored),
        "ignored_kinds": sorted({i["kind"] for i in ignored}),
    }


# ------------------------------------------------------------------------------------------------
# reporting
# ------------------------------------------------------------------------------------------------
def render_md(report: dict) -> str:
    s = report["summary"]
    out: list[str] = []
    out.append("# S->C packets: defined vs actually sent (aaemu-game, 1.2 protocol)\n")
    out.append(
        "Generated by [`rust/tools/opcode_coverage/sc_usage.py`](sc_usage.py) -- "
        "`python rust/tools/opcode_coverage/sc_usage.py`. Text analysis only, no cargo.\n"
    )
    out.append("## Counted summary\n")
    out.append("| bucket | S->C packet types | meaning |")
    out.append("| --- | --- | --- |")
    out.append(
        f"| `sent` | {s['sent']} | constructed outside test code -- the server builds and can send it |"
    )
    out.append(
        f"| `defined_only` | {s['defined_only']} | declared (struct + `Marshal`) but never constructed anywhere: no server implementation |"
    )
    out.append(
        f"| `unclear` | {s['unclear']} | only weak references (type position / opcode constant); see the table |"
    )
    out.append(f"| **total S->C types defined** | **{s['total']}** | |")
    out.append("")
    out.append(
        f"`opcodes::sc` declares {s['sc_opcodes']} S->C opcode constants; "
        f"{s['sc_opcodes_with_type']} of them have a Rust packet type in this crate, "
        f"{s['sc_opcodes_without_type']} have none at all."
    )
    out.append("")
    if s["by_area"]:
        out.append("### `defined_only` by source area\n")
        out.append("| area | count |")
        out.append("| --- | --- |")
        for area, n in sorted(s["by_area"].items(), key=lambda kv: (-kv[1], kv[0])):
            out.append(f"| `{area}` | {n} |")
        out.append("")

    out.append(f"## defined_only -- {s['defined_only']} packets, sorted by opcode\n")
    out.append("| opcode | Rust type | defined in | guessed client feature |")
    out.append("| --- | --- | --- | --- |")
    for p in report["defined_only"]:
        out.append(
            f"| `0x{p['opcode']:03x}` | `{p['name']}` | `{p['file']}:{p['line']}` | {p['feature']} |"
        )
    out.append("")

    out.append(f"## unclear -- {s['unclear']} packets\n")
    if report["unclear"]:
        out.append("| opcode | Rust type | defined in | evidence | reason |")
        out.append("| --- | --- | --- | --- | --- |")
        for p in report["unclear"]:
            ev = "; ".join(f"`{e['file']}:{e['line']}` ({e['role']})" for e in p["evidence"])
            out.append(
                f"| `0x{p['opcode']:03x}` | `{p['name']}` | `{p['file']}:{p['line']}` | {ev} | {p['reason']} |"
            )
    else:
        out.append("_none_")
    out.append("")

    out.append(f"## sent -- {s['sent']} packets (first construction site)\n")
    out.append("| opcode | Rust type | defined in | first construction site |")
    out.append("| --- | --- | --- | --- |")
    for p in report["sent"]:
        ev = p["evidence"][0] if p["evidence"] else {}
        out.append(
            f"| `0x{p['opcode']:03x}` | `{p['name']}` | `{p['file']}:{p['line']}` | "
            f"`{ev.get('file', '?')}:{ev.get('line', '?')}` ({ev.get('role', '?')}) |"
        )
    out.append("")

    no_type = report["opcodes_without_any_type"]
    unknown = [p for p in no_type if "Unk" in p["name"]]
    out.append(
        f"## Appendix: {len(no_type)} `sc` opcodes with no Rust type at all\n"
    )
    out.append(
        "These opcodes exist in the 1.2 table but the crate does not declare a packet type for "
        "them at all, so nothing is ever sent with them either (a body writer can still exist "
        "without a packet type, e.g. `models/movement.rs::write_sc_unit_movements`, but it is "
        "dead code). "
        f"{len(unknown)} of them are `SCUnk*` placeholders with no known meaning.\n"
    )
    out.append("| opcode | opcode constant |")
    out.append("| --- | --- |")
    for p in no_type:
        out.append(f"| `0x{p['opcode']:03x}` | `{p['name']}` |")
    out.append("")

    if report["duplicate_types"]:
        out.append("## Notes: type names defined more than once\n")
        out.append(
            "The same type name (and opcode) is declared in two modules; all copies are analysed "
            "together, `defined in` reports the first one.\n"
        )
        out.append("| type | definitions |")
        out.append("| --- | --- |")
        for name, where in report["duplicate_types"].items():
            out.append(f"| `{name}` | {', '.join('`' + w + '`' for w in where)} |")
        out.append("")
    return "\n".join(out)


BASELINE_LISTS = ("typeless", "never_sent")
BASELINE_KEYS = ("counts", *BASELINE_LISTS)


def baseline_from_report(report: dict) -> dict:
    """The ratcheted part of a report: gap counts plus the sorted opcode/type names behind them."""
    typeless = sorted(o["name"] for o in report["opcodes_without_any_type"])
    never_sent = sorted(p["name"] for p in report["defined_only"] + report["unclear"])
    return {
        "counts": {
            "sc_opcodes": report["summary"]["sc_opcodes"],
            "typeless": len(typeless),
            "never_sent": len(never_sent),
        },
        "typeless": typeless,
        "never_sent": never_sent,
    }


def format_counts(counts: dict) -> str:
    return (
        f"  S->C opcodes: {counts['sc_opcodes']}, without a Rust type (typeless): "
        f"{counts['typeless']}, typed but never sent: {counts['never_sent']}"
    )


def check_against_baseline(baseline: dict, current: dict) -> tuple[list[str], list[str]]:
    """Compare two baseline-shaped dicts.  Returns (errors, hints); errors fail the ratchet.

    A gap list may only shrink: every name that is in ``current`` but not in ``baseline`` is an
    error.  Names that left a list produce a hint to refresh the baseline.
    """
    errors: list[str] = []
    hints: list[str] = []
    labels = {
        "typeless": "S->C opcode has no Rust packet type",
        "never_sent": "S->C packet type is defined but never sent outside tests",
    }
    for key in BASELINE_LISTS:
        old, new = set(baseline.get(key, [])), set(current[key])
        for name in sorted(new - old):
            errors.append(f"regression ({key}): {name}: {labels[key]} (not in baseline)")
        gone = sorted(old - new)
        if gone:
            hints.append(
                f"improved ({key}): {len(gone)} fewer ({', '.join(gone[:5])}"
                f"{', ...' if len(gone) > 5 else ''}); run --update-baseline to lock it in"
            )
    return errors, hints


def build_report() -> dict:
    """Analyse the game crate sources and return the report dict (no files are written)."""
    tables = load_opcodes()
    sc = tables["sc"]
    sources = discover_sources()
    defs = find_definitions(sources, sc)
    sites = index_identifiers(sources, {d["name"] for d in defs})
    results = [classify(d, sources, sites) for d in defs]

    typed_values = {d["opcode"] for d in defs}

    def sort_key(p: dict) -> tuple[int, str]:
        return (p["opcode"], p["name"])

    sent = sorted([p for p in results if p["bucket"] == "sent"], key=sort_key)
    defined_only = sorted([p for p in results if p["bucket"] == "defined_only"], key=sort_key)
    unclear = sorted([p for p in results if p["bucket"] == "unclear"], key=sort_key)

    by_area: dict[str, int] = {}
    for p in defined_only:
        area = p["file"].split("src/", 1)[-1]
        area = area.rsplit("/", 1)[0] if "/" in area else area
        by_area[area] = by_area.get(area, 0) + 1

    duplicate_types = {
        d["name"]: [f"{s['file']}:{s['line']}" for s in d["def_sites"] + d.get("also_defined_at", [])]
        for d in defs
        if d.get("also_defined_at")
    }

    report = {
        "summary": {
            "total": len(results),
            "sent": len(sent),
            "defined_only": len(defined_only),
            "unclear": len(unclear),
            "sc_opcodes": len(sc),
            "sc_opcodes_with_type": len(typed_values),
            "sc_opcodes_without_type": len(set(sc.values()) - typed_values),
            "by_area": by_area,
            "generated_by": "rust/tools/opcode_coverage/sc_usage.py",
            "extracted_from": "rust/crates/aaemu-game/src",
        },
        "sent": sent,
        "defined_only": defined_only,
        "unclear": unclear,
        "duplicate_types": duplicate_types,
        "opcodes_without_any_type": sorted(
            (
                {"name": n, "opcode": v}
                for n, v in sc.items()
                if v not in typed_values
            ),
            key=lambda d: (d["opcode"], d["name"]),
        ),
    }
    return report


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--json", default=str(DEFAULT_JSON))
    ap.add_argument("--md", default=str(DEFAULT_MD))
    ap.add_argument(
        "--check",
        action="store_true",
        help="ratchet: exit 1 if a gap list grew vs the baseline; writes no report files",
    )
    ap.add_argument(
        "--update-baseline",
        action="store_true",
        help="rewrite the baseline file from the current analysis (writes no report files)",
    )
    ap.add_argument("--baseline", default=str(DEFAULT_BASELINE))
    args = ap.parse_args()

    report = build_report()
    defined_only = report["defined_only"]
    unclear = report["unclear"]

    if args.update_baseline:
        current = baseline_from_report(report)
        Path(args.baseline).write_text(json.dumps(current, indent=2) + "\n", encoding="utf-8")
        print(f"wrote {args.baseline}")
        print(format_counts(current["counts"]))
        return 0
    if args.check:
        baseline = json.loads(Path(args.baseline).read_text(encoding="utf-8"))
        errors, hints = check_against_baseline(baseline, baseline_from_report(report))
        for h in hints:
            print(h)
        for e in errors:
            print(e, file=sys.stderr)
        if errors:
            print(
                "S->C coverage ratchet FAILED: send/implement the new packets, or (only if the "
                "growth is intended) run `python rust/tools/opcode_coverage/sc_usage.py "
                "--update-baseline` and justify it in review.",
                file=sys.stderr,
            )
            return 1
        print("S->C coverage ratchet OK")
        print(format_counts(baseline_from_report(report)["counts"]))
        return 0

    Path(args.json).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    Path(args.md).write_text(render_md(report), encoding="utf-8")

    s = report["summary"]
    print(f"S->C packet types defined in aaemu-game : {s['total']}")
    print(f"  sent (constructed in non-test code)   : {s['sent']}")
    print(f"  defined_only (never constructed)      : {s['defined_only']}")
    print(f"  unclear                               : {s['unclear']}")
    print(f"opcodes::sc constants                   : {s['sc_opcodes']}")
    print(f"  with a Rust packet type               : {s['sc_opcodes_with_type']}")
    print(f"  without any Rust packet type          : {s['sc_opcodes_without_type']}")
    print()
    print(f"defined_only ({len(defined_only)}):")
    for p in defined_only:
        print(f"  0x{p['opcode']:03x}  {p['name']:52s} {p['file']}:{p['line']}")
    if unclear:
        print()
        print(f"unclear ({len(unclear)}):")
        for p in unclear:
            ev = ", ".join(f"{e['file']}:{e['line']}" for e in p["evidence"][:2])
            print(f"  0x{p['opcode']:03x}  {p['name']:52s} {p['reason']} [{ev}]")
    print(f"\nwrote {args.json}\nwrote {args.md}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
