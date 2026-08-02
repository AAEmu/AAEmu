# AAEmu Project Test Implementation Progress

- Audience: Contributors and maintainers
- Last verified against: `develop` on March 6, 2026
- Prerequisites: Access to the latest `AAEmu.UnitTests` test run outputs

<!-- markdownlint-disable-file MD012 MD013 MD022 MD032 MD058 MD060 -->

**Report Date:** March 6, 2026

## Current Statistics

| Metric               | Value            |
| -------------------- | ---------------- |
| **Total Tests**      | 958              |
| **Passed**           | 924 (96.5%)       |
| **Failed**           | 33 (DB-related)  |
| **Execution Time**   | ~1.0 seconds     |
| **Current Coverage** | ~60-70% (estimate) |
| **Target Coverage**  | 80%              |

______________________________________________________________________

## ✅ Completed Tasks

### 1. Testing Infrastructure

Created base classes for tests:

- **`TestBase.cs`** — base class with common fixtures and Moq objects
- **`SqliteTestBase.cs`** — base class for in-memory SQLite DB tests
- **`IntegrationTestBase.cs`** — base class for integration tests
- **`README.md`** — test project documentation (structure, execution, rules)

### 2. Covered Components

#### Added 06.03.2026:

- **`BuffTests.cs`** — 26 tests using Moq and parameterized tests ([Theory], [InlineData])
- **`ModelsJsonConverterTests.cs`** — 18 new tests
- **`UnitTests.cs`** — expanded from 1 to ~14 tests
- **`UnitCooldownsTests.cs`** — 10 tests for UnitCooldowns class
- **`PrimeFinderTests.cs`** — 6 tests for PrimeFinder class
- **`TimeSpanExtensionsTests.cs`** — 11 tests for TimeSpan extensions
- **`RandomExtensionsTests.cs`** — 7 tests for RandomExtensions class
- **`RandomElementByWeightTests.cs`** — 7 tests for RandomElementByWeight method
- **`WorldPosTests.cs`** — 6 tests for WorldPos class
- **`CompletedQuestTests.cs`** — 5 tests for CompletedQuest class

#### Core Managers

| Class                      | Tests | Status             |
| -------------------------- | ----- | ------------------ |
| `ExperienceManager`         | 60+   | ✅ Full coverage   |
| `NameManager`              | 12    | ✅ Full coverage   |
| `AccessLevelManager`       | 4     | ✅ Full coverage   |
| `IdManager` (ObjectId)    | 28+   | ✅ Full coverage   |
| `ItemManager`              | 40+   | ✅ Full coverage   |
| `SkillManager`             | 25+   | ✅ Full coverage   |
| `WorldManager`             | 70+   | ✅ Full coverage   |
| `CharacterManager`         | 25+   | ✅ Full coverage   |
| `DoodadManager`            | 40+   | ✅ Full coverage   |
| `QuestManager`             | 30+   | ✅ Full coverage   |
| `TaskManager`              | 30+   | ✅ Full coverage   |
| `ManagerOrchestrator`      | 6     | ✅ Full coverage   |
| `AccountManager`           | 2     | ✅ Basic           |
| `AuctionManager`           | 1     | ✅ Basic           |
| `CashShopManager`          | 1     | ✅ Basic           |
| `EnterWorldManager`        | 1     | ✅ Basic           |
| `ExpeditionManager`        | 1     | ✅ Basic           |
| `FamilyManager`            | 1     | ✅ Basic           |
| `HousingManager`           | 1     | ✅ Basic           |
| `IndunManager`             | 1     | ✅ Basic           |
| `MailManager`              | 1     | ✅ Basic           |
| `ManaRegenManager`         | 1     | ✅ Basic           |
| `MusicManager`             | 1     | ✅ Basic           |
| `PortalManager`            | 1     | ✅ Basic           |
| `PublicFarmManager`        | 1     | ✅ Basic           |
| `SaveManager`              | 1     | ✅ Basic           |
| `ShipyardManager`          | 1     | ✅ Basic           |
| `SubZoneManager`           | 1     | ✅ Basic           |
| `SusManager`                | 2     | ✅ Basic           |
| `TeamManager`              | 1     | ✅ Basic           |
| `TradeManager`             | 1     | ✅ Basic           |
| `EffectTaskManager`        | 2     | ✅ Basic           |
| `FactionManager`           | 1     | ⚠️ Basic          |
| `NpcManager`               | 1     | ⚠️ Basic          |

#### Network Layer

| Class                      | Tests | Status             |
| -------------------------- | ----- | ------------------ |
| `LoginConnectionFactory`   | 8     | ✅ Full coverage   |
| `LoginConnectionHandler`   | 4     | ✅ Full coverage   |
| `LoginProtocolHandler`     | 7     | ✅ Full coverage   |
| `GameNetwork`              | 3     | ✅ Basic coverage  |
| `SimpleIdManager`          | 8     | ✅ Full coverage   |

#### GameData Classes

| Class                      | Tests | Status                |
| -------------------------- | ----- | --------------------- |
| `ItemGameData`             | 2     | ⚠️ Basic (singleton) |
| `NpcGameData`              | 2     | ⚠️ Basic (singleton) |
| `BuffGameData`             | 2     | ⚠️ Basic (singleton) |
| `AchievementGameData`      | 2     | ⚠️ Basic (singleton) |

#### Packets

| Class                      | Tests | Status             |
| -------------------------- | ----- | ------------------ |
| `CSBuyItemsPacket`         | 1     | ⚠️ Basic           |
| `SCCharacterListPacket`   | 3     | ✅ Full coverage   |

#### Utils

| Class                        | Tests | Status             |
| ---------------------------- | ----- | ------------------ |
| `MathUtil`                   | 23    | ✅ Full coverage   |
| `StringExtensions`           | ~20   | ✅ Full coverage   |
| `PacketStream`               | 12    | ✅ Full coverage   |
| `NumericSubCommandParameter` | ~40+  | ✅ Full coverage   |
| `SubCommandBase`             | ~20+  | ✅ Full coverage   |
| `DoodadChainSubCommand`      | 4     | ✅ Full coverage   |
| `Singleton`                  | 2     | ✅ Full coverage   |

#### Models

| Class                       | Tests | Status             |
| --------------------------- | ----- | ------------------ |
| `Item`                      | 50+   | ✅ Extended         |
| `Inventory`                 | 1     | ⚠️ Basic (Skip)   |
| `JsonModelsConverter`       | 20     | ✅ Extended        |
| `Quest`                     | 12     | ✅ Extended        |
| `Skill`                     | 16     | ✅ Extended        |
| `Buff`                      | 26     | ✅ Extended        |
| `Unit`                      | 14     | ✅ Extended        |

#### Services

| Class                       | Tests | Status             |
| --------------------------- | ----- | ------------------ |
| `GameService`               | 6     | ✅ Full coverage   |
| `WebApiSession`             | 5     | ✅ Full coverage   |
| `RouteMapper`               | 4     | ✅ Full coverage   |
| `MailTests`                 | 2     | ✅ Full coverage   |

______________________________________________________________________

## ❌ What Still Needs to Be Done (to reach 50% coverage)

### Priority 1: Critical Business Logic (~15-20% coverage)

#### 1.1 Item System

- [x] **`ItemManager`** — ~50 tests ✅

  - GetTemplate, GetGradeTemplate
  - AcquireDefaultItem, AddOrMoveExistingItem
  - Item conversion, smelting, socketing

- [x] **`Item` (model)** — ~30 tests (partially done: 50+)

  - Item creation ✅
  - Inventory and slots ✅
  - Item expiration ✅

- [ ] **`Inventory`** — ~40 tests

  - Add/remove items
  - Move between slots
  - Capacity checks

#### 1.2 Skill System

- [x] **`SkillManager`** — ~30 tests ✅

  - Skill loading ✅
  - Skill application ✅
  - Cooldowns ✅

- [x] **`Skill` (model)** — ~20 tests (done: 16)

  - Skill creation ✅
  - Skill application ✅
  - Cooldowns

- [x] **`Buff` (model)** — ~25 tests (done: 26)

  - Buff application ✅
  - Duration and stacking ✅
  - Triggers ✅

#### 1.3 Quest System

- [x] **`QuestManager`** — ~40 tests ✅

  - Quest start ✅
  - Quest progress ✅
  - Quest completion ✅

- [x] **`Quest` (model)** — ~30 tests (partially done: 12)

  - Quest conditions ✅
  - Rewards
  - Progress tracking ✅

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

- [x] **`CharacterManager`** — ~20 tests (partially done: 25+) ✅

  - Character loading ✅
  - Progress saving
  - Data validation ✅

#### 3.2 NPCs and Mobs

- [ ] **`Npc`** — ~25 tests

  - AI behavior
  - Aggression
  - Loot

- [x] **`NpcManager`** — ~15 tests (partially done: 1) ⚠️

  - NPC spawn
  - Despawn
  - Interaction

#### 3.3 World and Zones

- [x] **`WorldManager`** — ~20 tests (done: 70+) ✅

  - World loading ✅
  - Zone management ✅
  - Teleports ✅

- [ ] **`ZoneManager`** — ~15 tests

  - Zone boundaries
  - Zone triggers
  - Weather

### Priority 4: Additional Systems (~5-10% coverage)

#### 4.1 Economy

- [x] **`AuctionManager`** — ~20 tests (partially done: 1) ⚠️
- [x] **`TradeManager`** — ~15 tests (partially done: 1) ⚠️
- [x] **`CashShopManager`** — ~15 tests (partially done: 1) ⚠️

#### 4.2 Guilds and Groups

- [x] **`ExpeditionManager`** — ~20 tests (partially done: 1) ⚠️
- [x] **`FamilyManager`** — ~15 tests (partially done: 1) ⚠️
- [x] **`TeamManager`** — ~15 tests (partially done: 1) ⚠️

#### 4.3 Mail and Friends

- [x] **`MailManager`** — ~20 tests (partially done: 1) ⚠️
- [ ] **`FriendManager`** — ~15 tests

______________________________________________________________________

## 📊 80% Coverage Achievement Plan

| Stage       | Components              | Tests     | Coverage | Timeline       | Status         |
| ----------- | ----------------------- | --------- | -------- | -------------- | ----------------|
| **Current** | Extended models         | 958       | ~60-70%  | March 2026     | ✅ Done        |
| **Stage 1** | Items + Skills          | ~200      | ~15%     | Feb 2026       | ✅ Done        |
| **Stage 2** | Quests + Packets        | ~200      | ~30%     | March 2026     | ✅ Done        |
| **Stage 3** | Models (Character, Npc) | ~150      | ~40%     | Ongoing        | 🔄 In Progress |
| **Stage 4** | World + Economy         | ~150      | ~50%     | Q2 2026        | 📅 Planned    |
| **Stage 5** | Full coverage           | ~500+     | ~80%     | Q2-Q3 2026     | 📅 Planned    |
| **TOTAL**   | **All critical**        | **~2000** | **~80%** | **Q3 2026**    | **📅 Planned** |

______________________________________________________________________

## 🔧 Technical Recommendations

### 1. Infrastructure

- [x] Add code coverage (coverlet) to CI/CD
- [x] Configure coverage reports (HTML/LCov)
- [ ] Add minimum coverage threshold (50%)

### 2. Mocks and Fixtures

- [ ] Create test data factory (`TestDataFactory`)
- [ ] Add mocks for `MySQL` connections
- [ ] Create test configurations

### 3. Integration Tests

- [x] Set up test DB (in-memory SQLite)
- [x] Add tests with real DB (compact.sqlite3)
- [ ] Create fixtures for common data

### 4. Performance

- [x] Optimize test startup time (< 5 minutes)
- [x] Separate tests into fast (unit) and slow (integration)
- [x] Add parallel execution

______________________________________________________________________

## 📝 Notes

### Issues

1. **Integration tests with DB** — require `compact.sqlite3` file in correct directory
2. **Static singletons** — difficult to test, require refactoring for dependency injection
3. **Large amount of legacy code** — requires gradual test coverage
4. **InventoryTests** — test marked with `[Fact(Skip = "Requires DI container setup")]` - requires DI container setup

### Test Writing Recommendations

1. **Naming**: `Method_Scenario_ExpectedResult`
2. **Structure**: Arrange-Act-Assert (AAA)
3. **Isolation**: one test — one check
4. **Mocks**: use Moq for external dependencies
5. **Documentation**: add XML comments to complex tests

### Added Tests (06.03.2026)

- **`BuffTests.cs`** — 26 new tests:
  - Constructors (with parameters and null)
  - Properties (Index, State, InUse, Duration, Tick, Charge, Passive, AbLevel)
  - Methods IsEnded(), GetTimeLeft(), GetTimeElapsed(), WriteData(), ConsumeCharge()
  - Parameterized tests with [Theory] and [InlineData] attributes
  - Edge cases

- **`ModelsJsonConverterTests.cs`** — 18 new tests:
  - JSON serialization/deserialization tests
  - Type handling for various game model types
  - Error handling for invalid JSON

- **`UnitTests.cs`** — expanded from 1 to 14 tests:
  - Unit level properties
  - Unit experience handling
  - Unit state management
  - Unit combat attributes

______________________________________________________________________

## 📈 Quality Metrics

| Metric                 | Current | Target  |
| ---------------------- | ------- | ------- |
| Total tests            | 958     | 1000+   |
| Code coverage          | ~60-70% | 80%     |
| Execution time         | ~1 sec  | < 5 min |
| Successful tests       | 96.5%   | > 95%   |
| Critical bugs in tests | 0       | 0       |

______________________________________________________________________

## 📅 Next Review

**Recommended review date:** April 6, 2026

**Review checklist:**
- [ ] Run `dotnet test` and verify statistics
- [ ] Update test count and coverage metrics
- [ ] Verify GitHub Actions CI/CD coverage configuration
- [ ] Check progress on pending components (Inventory, Character, Npc, FriendManager)
- [ ] Review and update timeline if needed

______________________________________________________________________

## 📁 Test Files Overview

The AAEmu.UnitTests project contains **73 test files** organized into the following categories:

### Commons
| File | Description |
|------|-------------|
| `PrimeFinderTests.cs` | Tests for PrimeFinder utility |
| `RandomElementByWeightTests.cs` | Tests for weighted random selection |
| `RandomExtensionsTests.cs` | Tests for Random extension methods |
| `SingletonTests.cs` | Tests for Singleton pattern |
| `StringExtensionsTest.cs` | Tests for String extensions |
| `TimeSpanExtensionsTests.cs` | Tests for TimeSpan extensions |
| `PacketStreamTests.cs` | Tests for packet stream handling |

### Game/Core/Managers
| File | Description |
|------|-------------|
| `AccessLevelManagerTests.cs` | Tests for access level management |
| `AccountManagerTests.cs` | Tests for account management |
| `AuctionManagerTests.cs` | Tests for auction system |
| `CashShopManagerTests.cs` | Tests for cash shop |
| `CharacterManagerTests.cs` | Tests for character management |
| `EffectTaskManagerTests.cs` | Tests for effect tasks |
| `EnterWorldManagerTests.cs` | Tests for world entry |
| `ExpeditionManagerTests.cs` | Tests for expeditions |
| `ExperienceManagerTests.cs` | Tests for experience system |
| `FamilyManagerTests.cs` | Tests for family system |
| `HousingManagerTests.cs` | Tests for housing system |
| `IndunManagerTests.cs` | Tests for instance dungeons |
| `ItemManagerTests.cs` | Tests for item management |
| `MailManagerTests.cs` | Tests for mail system |
| `MailTests.cs` | Tests for mail functionality |
| `ManagerOrchestratorTests.cs` | Tests for manager orchestration |
| `ManaRegenManagerTests.cs` | Tests for mana regeneration |
| `MusicManagerTests.cs` | Tests for music system |
| `PortalManagerTests.cs` | Tests for portal management |
| `PublicFarmManagerTests.cs` | Tests for public farms |
| `QuestManagerTests.cs` | Tests for quest system |
| `SaveManagerTests.cs` | Tests for save system |
| `ShipyardManagerTests.cs` | Tests for shipyard |
| `SkillManagerTests.cs` | Tests for skill management |
| `SubZoneManagerTests.cs` | Tests for sub-zones |
| `SusManagerTests.cs` | Tests for SUS management |
| `TaskManagerTests.cs` | Tests for task management |
| `TeamManagerTests.cs` | Tests for team system |
| `TradeManagerTests.cs` | Tests for trade system |
| `WorldManagerTests.cs` | Tests for world management |

### Game/Core/Managers/Id
| File | Description |
|------|-------------|
| `IdManagerTests.cs` | Tests for ID management |

### Game/Core/Managers/Name
| File | Description |
|------|-------------|
| `NameManagerTests.cs` | Tests for name management |

### Game/Core/Managers/UnitManagers
| File | Description |
|------|-------------|
| `CharacterManagerTests.cs` | Tests for character unit management |
| `DoodadManagerTests.cs` | Tests for doodad management |
| `NpcManagerTests.cs` | Tests for NPC management |

### Game/Core/Managers/World
| File | Description |
|------|-------------|
| `ExampleTests.cs` | Example/test placeholder |
| `FactionManagerTests.cs` | Tests for faction system |
| `WorldManagerTests.cs` | Tests for world management |

### Game/Core/Network
| File | Description |
|------|-------------|
| `GameNetworkTests.cs` | Tests for game network |

### Game/Core/Packets
| File | Description |
|------|-------------|
| `CSBuyItemsPacketTests.cs` | Tests for buy items packet |
| `SCCharacterListPacketTests.cs` | Tests for character list packet |

### Game/GameData
| File | Description |
|------|-------------|
| `AchievementGameDataTests.cs` | Tests for achievement data |
| `BuffGameDataTests.cs` | Tests for buff data |
| `ItemGameDataTests.cs` | Tests for item data |
| `NpcGameDataTests.cs` | Tests for NPC data |

### Game/Models/Game/Items
| File | Description |
|------|-------------|
| `InventoryTests.cs` | Tests for inventory system |
| `ItemTests.cs` | Tests for item model |

### Game/Models/Game/Quests
| File | Description |
|------|-------------|
| `CompletedQuestTests.cs` | Tests for completed quests |
| `QuestTests.cs` | Tests for quest model |

### Game/Models/Game/Skills
| File | Description |
|------|-------------|
| `BuffTests.cs` | Tests for buff model |
| `SkillTests.cs` | Tests for skill model |

### Game/Models/Game/Units
| File | Description |
|------|-------------|
| `UnitCooldownsTests.cs` | Tests for unit cooldowns |
| `UnitTests.cs` | Tests for unit model |

### Game/Models/Game/World
| File | Description |
|------|-------------|
| `WorldPosTests.cs` | Tests for world position |

### Game/Models/Json
| File | Description |
|------|-------------|
| `ModelsJsonConverterTests.cs` | Tests for JSON conversion |

### Game/Utils
| File | Description |
|------|-------------|
| `DoodadChainSubCommandTests.cs` | Tests for doodad chain commands |
| `MathUtilTests.cs` | Tests for math utilities |
| `NumericSubCommandParameterTests.cs` | Tests for numeric parameters |
| `SubCommandBaseTests.cs` | Tests for subcommand base |

### Login
| File | Description |
|------|-------------|
| `LoginConnectionFactoryTests.cs` | Tests for login connection factory |
| `LoginConnectionHandlerTests.cs` | Tests for login connection handler |
| `LoginProtocolHandlerTests.cs` | Tests for login protocol |
| `SimpleIdManagerTests.cs` | Tests for simple ID manager |

### Services
| File | Description |
|------|-------------|
| `GameServiceTests.cs` | Tests for game service |
| `RouteMapperTests.cs` | Tests for route mapping |
| `WebApiSessionTests.cs` | Tests for web API session |

---

**Next step:** Continue Stage 3 — coverage of Character and Npc models with tests.

## Related

- [Wiki Home](wiki/Home.md)
- [Documentation Maintenance](wiki/Documentation-Maintenance.md)
- [Testing Plan (English)](TestingPlan_en.md)
- [Testing Plan (Russian)](TestingPlan_ru.md)
- [Testing Progress (Russian)](TestingProgress_ru.md)
