# AAEmu Project Architecture Analysis

- Audience: Contributors and maintainers
- Last verified against: `develop` on February 28, 2026
- Prerequisites: Familiarity with `AAEmu.Game` and `AAEmu.UnitTests`

<!-- markdownlint-disable-file MD012 MD013 MD022 MD032 MD058 MD060 -->

## Key Subsystems

1. **Game Service** – `AAEmu.Game/GameService.cs` – responsible for initialization, startup and shutdown of server components, manages server lifecycle.
1. **Network Layer** – set of classes in `AAEmu.Game/Core/Network/*`:
   - `GameNetwork.cs` – main network server for game traffic.
   - `LoginNetwork.cs` – handles login connections.
   - `StreamNetwork.cs` – data streaming.
   - `*ProtocolHandler.cs` – packet parsing and routing.
1. **Packet System** – files in `AAEmu.Game/Core/Packets/*` (C2G, C2S, G2C, S2C, Proxy, L2G, G2L). Contains business logic for validation and handling of game actions (e.g., `CSBuyItemsPacket.cs`, `SCCharacterListPacket.cs`).
1. **Game Data** – `AAEmu.Game/GameData/*.cs`. Stores static and dynamic information about items, NPCs, locations, quests, etc. This is the core of business logic (e.g., `ItemGameData.cs`, `NpcGameData.cs`).
1. **Configuration** – `AAEmu.Game/Config.json` and files in `AAEmu.Game/Configurations/`. Contains server parameters, character settings, balance, etc.
1. **Utilities and Common Libraries** – `AAEmu.Commons/*`. Provides helper functions (XML serialization, logging, etc.).

## Critical Business Logic

- Purchase and transaction handling (`CSBuyItemsPacket.cs`, `CSBuyHousePacket.cs`).
- Character management (creation, deletion, movement) – `SCCharacterListPacket.cs`, `SCCharacterStatePacket.cs`.
- Quest and achievement system (`AchievementGameData.cs`, `Quest`-related packets).
- Combat and buff mechanics (`BuffGameData.cs`, `BattlefieldGameData.cs`).
- World state synchronization (packets `SCAiAggroPacket.cs`, `SCAddFriendPacket.cs`).

## Unit Test Implementation Plan (xUnit) in `AAEmu.UnitTests`

### 1. Test Project Folder Structure

```text
AAEmu.UnitTests/
│   AAEmu.UnitTests.csproj          # test project, reference to AAEmu.Game
│   README.md
│
├─ Core/
│   ├─ Network/                     # network layer tests
│   └─ Packets/                     # packet and handler tests
│
├─ GameData/                        # business logic data tests
│   ├─ Items/                        # ItemGameData, ItemConversionGameData tests
│   ├─ Npcs/                         # NpcGameData, NpcGroupGameData tests
│   └─ ...
│
├─ Services/                        # GameService and auxiliary service tests
│
└─ Utils/                           # utility and common component tests
```

### 2. Priority Classes for Test Coverage

| Priority | Class / File                                                                          | Reason                            | Test Aspect                                                        |
| -------- | ------------------------------------------------------------------------------------- | --------------------------------- | ------------------------------------------------------------------ |
| 1        | `GameService.cs` (`AAEmu.Game/GameService.cs:1`)                                      | Coordinates all server components | Initialization, dependency creation, graceful shutdown             |
| 1        | `GameNetwork.cs` (`AAEmu.Game/Core/Network/Game/GameNetwork.cs:1`)                    | Network I/O                       | Connection handling, exception handling, proper connection closure |
| 1        | `CSBuyItemsPacket.cs` (`AAEmu.Game/Core/Packets/C2G/CSBuyItemsPacket.cs:1`)           | Purchase transaction              | Item validation, balance check, inventory update                   |
| 1        | `ItemGameData.cs` (`AAEmu.Game/GameData/ItemGameData.cs:1`)                           | Item data storage                 | Data loading correctness, item search, price calculation           |
| 2        | `NpcGameData.cs` (`AAEmu.Game/GameData/NpcGameData.cs:1`)                             | AI/NPC logic                      | NPC lookup by ID, spawn loading, player interaction                |
| 2        | `BuffGameData.cs` (`AAEmu.Game/GameData/BuffGameData.cs:1`)                           | Buffs/debuffs                     | Buff application, duration, stacking                               |
| 2        | `SCCharacterListPacket.cs` (`AAEmu.Game/Core/Packets/G2C/SCCharacterListPacket.cs:1`) | Character list                    | List formation, data correctness, sorting                          |
| 3        | `AchievementGameData.cs` (`AAEmu.Game/GameData/AchievementGameData.cs:1`)             | Achievements                      | Reward granting, condition verification                            |
| 3        | `IndunGameData.cs` (`AAEmu.Game/GameData/IndunGameData.cs:1`)                         | Dungeon instances                 | Spawn generation, timers, completion                               |

### 3. Dependency Isolation Recommendations (Moq)

- **Dependency Injection**: Where possible, add constructors accepting interfaces (`ILogger`, `IRepository<T>`, `INetworkClient`). This simplifies mocking real implementations.
- **Moq Mocks**:
  - `Mock<ILogger>` – verify error logging in critical places.
  - `Mock<IItemRepository>` – simulate item database access when testing `CSBuyItemsPacket`.
  - `Mock<IConnection>` – emulate network connection for `GameNetwork` and `*ProtocolHandler`.
  - **Setup/Verify**: Configure predefined data returns (`Setup(repo => repo.GetItem(It.IsAny<int>())).Returns(item)`) and verify calls (`Verify(repo => repo.UpdateInventory(...), Times.Once())`).
  - **Fixtures**: Use `IClassFixture<T>` in xUnit for creating shared mock objects reused across multiple tests.

### 4. Specific Testing Scenarios

#### 4.1 `GameService`

- **Initialization**: When `StartAsync` is called, all required services (Network, DataLoaders) are registered and started.
- **Graceful shutdown**: After `StopAsync`, verify all connections are closed and resources released.

#### 4.2 `GameNetwork`

- **Client connection**: Upon receiving a new TCP connection, a `ClientSession` object is created and registered in the active list.
- **Exception handling**: When an exception is thrown in packet handler, connection is closed and error is logged.

#### 4.3 `CSBuyItemsPacket`

- **Successful purchase**: Player has sufficient balance, item exists – after processing, player inventory is updated, balance decreased.
- **Insufficient balance**: Expect `InsufficientFundsException` and no inventory change.
- **Invalid item**: Packet is rejected, `InvalidItemException` is generated.

#### 4.4 `ItemGameData`

- **JSON loading**: With valid `items.json`, `Load` method returns collection without exceptions.
- **Search by ID**: `GetItemById(id)` returns correct object or `null` if not found.

#### 4.5 `NpcGameData`

- **NPC spawn**: `GetNpcById` returns NPC with correct coordinates and behavior scripts.
- **AI behavior**: Mock `IAIEngine` object is verified for `ExecuteBehavior` call when receiving `CTUccPositionPacket`.

#### 4.6 `BuffGameData`

- **Buff application**: After `ApplyBuff` call, character has buff with correct duration.
- **Stacking**: When applying same buff, verify "replace" or "accumulate" rule depending on configuration.

#### 4.7 `SCCharacterListPacket`

- **List formation**: With three characters in database, packet forms array of three elements with correct fields (ID, Name, Level).
- **Empty list**: With no characters, packet returns empty array without errors.

#### 4.8 `AchievementGameData`

- **Reward granting**: When achievement condition is met (`KillCount >= 100`), `GrantAchievement` method adds record to player profile.
- **Duplicate acquisition**: Achievement is not duplicated on repeated calls.

### 5. Implementation Steps

1. **Create test project**: `dotnet new xunit -n AAEmu.UnitTests` and add reference to `AAEmu.Game`.
1. **Add Moq**: `dotnet add package Moq`.
1. **Configure `Directory.Build.props`** (if needed) for unified build settings.
1. **Add base test fixtures** (`TestBase.cs`) with common mock objects.
1. **Implement tests sequentially by priority** (first services and network layer, then business logic data).
1. **Integrate into CI** (GitHub Actions, Azure Pipelines) – run `dotnet test` after each build.
1. **Track coverage**: add `coverlet.collector` and generate report `dotnet test /p:CollectCoverage=true`.

### 6. Test Maintenance Recommendations

- **Code review**: every new test goes through pull-request.
- **Update mock interfaces** when service signatures change.
- **Regular refactoring**: if coverage drops, add missing tests.
- **Documentation**: in test project `README.md`, describe structure, execution, and rules for writing new tests.

______________________________________________________________________

**Summary**: The proposed plan covers critical architecture parts, defines testing priorities, provides specific scenarios and Moq usage recommendations. Following this plan, the team will obtain a reliable unit test suite that accelerates project development and improves code stability.

## Related

- [Wiki Home](wiki/Home.md)
- [Documentation Maintenance](wiki/Documentation-Maintenance.md)
- [Testing Plan (Russian)](TestingPlan_ru.md)
- [Testing Progress (English)](TestingProgress_en.md)
- [Testing Progress (Russian)](TestingProgress_ru.md)
