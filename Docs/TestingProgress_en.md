# AAEmu Project Test Implementation Progress

- Audience: Contributors and maintainers
- Last verified against: `develop` on February 28, 2026
- Prerequisites: Access to the latest `AAEmu.UnitTests` test run outputs

<!-- markdownlint-disable-file MD012 MD013 MD022 MD032 MD058 MD060 -->

**Report Date:** February 19, 2026

## Current Statistics

| Metric               | Value            |
| -------------------- | ---------------- |
| **Total Tests**      | 352              |
| **Passed**           | 352 (100%)       |
| **Failed**           | 0                |
| **Execution Time**   | ~1.3 seconds     |
| **Current Coverage** | ~2-5% (estimate) |
| **Target Coverage**  | 50%              |

______________________________________________________________________

## ✅ Completed Tasks

### 1. Testing Infrastructure

Created base classes for tests:

- **`TestBase.cs`** — base class with common fixtures and Moq objects
- **`SqliteTestBase.cs`** — base class for in-memory SQLite DB tests
- **`IntegrationTestBase.cs`** — base class for integration tests
- **`README.md`** — test project documentation (structure, execution, rules)

### 2. Covered Components

#### Core Managers

| Class                  | Tests | Status           |
| ---------------------- | ----- | ---------------- |
| `ExperienceManager`    | 60+   | ✅ Full coverage |
| `NameManager`          | 12    | ✅ Full coverage |
| `AccessLevelManager`   | 4     | ✅ Full coverage |
| `IdManager` (ObjectId) | 3     | ✅ Full coverage |

#### Network Layer

| Class                    | Tests | Status            |
| ------------------------ | ----- | ----------------- |
| `LoginConnectionFactory` | 8     | ✅ Full coverage  |
| `LoginConnectionHandler` | 5     | ✅ Full coverage  |
| `LoginProtocolHandler`   | 7     | ✅ Full coverage  |
| `GameNetwork`            | 3     | ✅ Basic coverage |
| `SimpleIdManager`        | 8     | ✅ Full coverage  |

#### GameData Classes

| Class                 | Tests | Status               |
| --------------------- | ----- | -------------------- |
| `ItemGameData`        | 2     | ⚠️ Basic (singleton) |
| `NpcGameData`         | 2     | ⚠️ Basic (singleton) |
| `BuffGameData`        | 2     | ⚠️ Basic (singleton) |
| `AchievementGameData` | 2     | ⚠️ Basic (singleton) |

#### Packets

| Class                   | Tests | Status           |
| ----------------------- | ----- | ---------------- |
| `CSBuyItemsPacket`      | 1     | ⚠️ Basic         |
| `SCCharacterListPacket` | 3     | ✅ Full coverage |

#### Utils

| Class                        | Tests | Status           |
| ---------------------------- | ----- | ---------------- |
| `MathUtil`                   | 19    | ✅ Full coverage |
| `StringExtensions`           | 16    | ✅ Full coverage |
| `PacketStream`               | 10    | ✅ Full coverage |
| `NumericSubCommandParameter` | 40+   | ✅ Full coverage |
| `SubCommandBase`             | 20+   | ✅ Full coverage |

#### Models

| Class                               | Tests | Status   |
| ----------------------------------- | ----- | -------- |
| `UnitTests` (NoDuplicateAttributes) | 1     | ⚠️ Basic |
| `InventoryTests`                    | 1     | ⚠️ Basic |
| `JsonModelsConverter`               | 2     | ⚠️ Basic |

#### Services

| Class           | Tests | Status           |
| --------------- | ----- | ---------------- |
| `GameService`   | 6     | ✅ Full coverage |
| `WebApiSession` | 8     | ✅ Full coverage |
| `RouteMapper`   | 7     | ✅ Full coverage |

#### Other

| Class                | Tests | Status           |
| -------------------- | ----- | ---------------- |
| `MailTests`          | 2     | ✅ Full coverage |
| `BoatPhysicsManager` | 1     | ⚠️ Basic         |
| `QuestTests`         | 1     | ⚠️ Basic         |

______________________________________________________________________

## ❌ What Still Needs to Be Done (to reach 50% coverage)

### Priority 1: Critical Business Logic (~15-20% coverage)

#### 1.1 Item System

- [ ] **`ItemManager`** — ~50 tests

  - GetTemplate, GetGradeTemplate
  - AcquireDefaultItem, AddOrMoveExistingItem
  - Item conversion, smelting, socketing

- [ ] **`Item` (model)** — ~30 tests

  - Item creation
  - Inventory and slots
  - Item expiration

- [ ] **`Inventory`** — ~40 tests

  - Add/remove items
  - Move between slots
  - Capacity checks

#### 1.2 Skill System

- [ ] **`SkillManager`** — ~30 tests

  - Skill loading
  - Skill application
  - Cooldowns

- [ ] **`Buff` (model)** — ~25 tests

  - Buff application
  - Duration and stacking
  - Triggers

#### 1.3 Quest System

- [ ] **`QuestManager`** — ~40 tests

  - Quest start
  - Quest progress
  - Quest completion

- [ ] **`Quest` (model)** — ~30 tests

  - Quest conditions
  - Rewards
  - Progress tracking

### Priority 2: Packets and Network (~10-15% coverage)

#### 2.1 C2G (Client-to-Game) Packet Handlers

- [ ] **`CSBuyHousePacket`** — ~10 tests
- [ ] **`CSCreateCharacterPacket`** — ~10 tests
- [ ] **`CSDeleteCharacterPacket`** — ~8 tests
- [ ] **`CSSelectCharacterPacket`** — ~8 tests
- [ ] **`CSStartSkillPacket`** — ~10 tests
- [ ] **`CSStopCastingPacket`** — ~8 tests

#### 2.2 G2C (Game-to-Client) Packet Handlers

- [ ] **`SCCharacterStatePacket`** — ~10 tests
- [ ] **`SCExpChangedPacket`** — ~8 tests
- [ ] **`SCLevelChangedPacket`** — ~8 tests
- [ ] **`SCSkillLearnedPacket`** — ~8 tests
- [ ] **`SCQuestContextStartedPacket`** — ~10 tests

### Priority 3: Models and Entities (~10-15% coverage)

#### 3.1 Characters

- [ ] **`Character`** — ~40 tests

  - Character creation
  - Stats
  - Skills and abilities
  - Inventory

- [ ] **`CharacterManager`** — ~20 tests

  - Character loading
  - Progress saving
  - Data validation

#### 3.2 NPCs and Mobs

- [ ] **`Npc`** — ~25 tests

  - AI behavior
  - Aggression
  - Loot

- [ ] **`NpcManager`** — ~15 tests

  - NPC spawn
  - Despawn
  - Interaction

#### 3.3 World and Zones

- [ ] **`WorldManager`** — ~20 tests

  - World loading
  - Zone management
  - Teleports

- [ ] **`ZoneManager`** — ~15 tests

  - Zone boundaries
  - Zone triggers
  - Weather

### Priority 4: Additional Systems (~5-10% coverage)

#### 4.1 Economy

- [ ] **`AuctionManager`** — ~20 tests
- [ ] **`TradeManager`** — ~15 tests
- [ ] **`CashShopManager`** — ~15 tests

#### 4.2 Guilds and Groups

- [ ] **`ExpeditionManager`** — ~20 tests
- [ ] **`FamilyManager`** — ~15 tests
- [ ] **`TeamManager`** — ~15 tests

#### 4.3 Mail and Friends

- [ ] **`MailManager`** — ~20 tests (partial exists)
- [ ] **`FriendManager`** — ~15 tests

______________________________________________________________________

## 📊 50% Coverage Achievement Plan

| Stage       | Components              | Tests     | Coverage | Timeline       |
| ----------- | ----------------------- | --------- | -------- | -------------- |
| **Current** | Scattered tests         | 352       | ~2-5%    | ✅ Done        |
| **Stage 1** | Items + Skills          | ~200      | ~15%     | 2-3 weeks      |
| **Stage 2** | Quests + Packets        | ~200      | ~30%     | 2-3 weeks      |
| **Stage 3** | Models (Character, Npc) | ~150      | ~40%     | 2 weeks        |
| **Stage 4** | World + Economy         | ~150      | ~50%     | 2 weeks        |
| **TOTAL**   | **All critical**        | **~1050** | **~50%** | **8-10 weeks** |

______________________________________________________________________

## 🔧 Technical Recommendations

### 1. Infrastructure

- [ ] Add code coverage (coverlet) to CI/CD
- [ ] Configure coverage reports (HTML/LCov)
- [ ] Add minimum coverage threshold (50%)

### 2. Mocks and Fixtures

- [ ] Create test data factory (`TestDataFactory`)
- [ ] Add mocks for `MySQL` connections
- [ ] Create test configurations

### 3. Integration Tests

- [ ] Set up test DB (in-memory SQLite)
- [ ] Add tests with real DB (compact.sqlite3)
- [ ] Create fixtures for common data

### 4. Performance

- [ ] Optimize test startup time (< 5 minutes)
- [ ] Separate tests into fast (unit) and slow (integration)
- [ ] Add parallel execution

______________________________________________________________________

## 📝 Notes

### Issues

1. **Integration tests with DB** — require `compact.sqlite3` file in correct directory
1. **Static singletons** — difficult to test, require refactoring for dependency injection
1. **Large amount of legacy code** — requires gradual test coverage

### Test Writing Recommendations

1. **Naming**: `Method_Scenario_ExpectedResult`
1. **Structure**: Arrange-Act-Assert (AAA)
1. **Isolation**: one test — one check
1. **Mocks**: use Moq for external dependencies
1. **Documentation**: add XML comments to complex tests

______________________________________________________________________

## 📈 Quality Metrics

| Metric                 | Current | Target  |
| ---------------------- | ------- | ------- |
| Total tests            | 352     | 1000+   |
| Code coverage          | ~2-5%   | 50%     |
| Execution time         | 1.3 sec | < 5 min |
| Successful tests       | 100%    | > 95%   |
| Critical bugs in tests | 0       | 0       |

______________________________________________________________________

**Next step:** Begin Stage 1 — coverage of Item and Skill systems with tests.

## Related

- [Wiki Home](wiki/Home.md)
- [Documentation Maintenance](wiki/Documentation-Maintenance.md)
- [Testing Plan (English)](TestingPlan_en.md)
- [Testing Plan (Russian)](TestingPlan_ru.md)
- [Testing Progress (Russian)](TestingProgress_ru.md)
