# AAEmu Unit Tests

This project contains unit tests for the AAEmu server implementation.

## Structure

```
AAEmu.UnitTests/
├── Commons/           # Tests for common utilities
├── Game/              # Tests for game server components
│   ├── Core/          # Core game systems
│   │   ├── Managers/  # Manager classes tests
│   │   ├── Network/   # Network layer tests
│   │   └── Packets/   # Packet handling tests
│   ├── GameData/      # Game data tests
│   ├── Models/        # Model classes tests
│   └── Utils/         # Utility classes tests
├── Login/             # Tests for login server components
├── Services/          # Service layer tests
└── Utils/             # Test utilities and mocks
```

## Running Tests

### All Tests
```bash
dotnet test
```

### Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~GameNetworkTests"
```

### With Coverage
```bash
dotnet test /p:CollectCoverage=true
```

## Test Categories

### Priority 1 (Critical Systems)
- `GameService` - Server lifecycle management
- `GameNetwork` - Network connections
- `CSBuyItemsPacket` - Item purchase transactions
- `ItemGameData` - Item data management

### Priority 2 (Core Features)
- `NpcGameData` - NPC spawning and AI
- `BuffGameData` - Buff/debuff system
- `SCCharacterListPacket` - Character list

### Priority 3 (Additional Features)
- `AchievementGameData` - Achievement system
- `IndunGameData` - Dungeon instances

## Writing Tests

### Test Naming Convention
```csharp
[Fact]
public void MethodName_Scenario_ExpectedResult()
{
    // Arrange
    // Act
    // Assert
}
```

### Using Mocks (Moq)
```csharp
var mockRepository = new Mock<IItemRepository>();
mockRepository.Setup(r => r.GetItem(It.IsAny<uint>())).Returns(item);
```

### Test Data
Use `[Theory]` and `[InlineData]` for parameterized tests:
```csharp
[Theory]
[InlineData(1, 2, 3)]
[InlineData(5, 5, 10)]
public void Add_Numbers_ReturnsSum(int a, int b, int expected)
{
    Assert.Equal(expected, a + b);
}
```

## Coverage Report

Generate HTML coverage report:
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=html
```

## CI Integration

Tests are automatically run on pull requests via GitHub Actions.

## Guidelines

1. **Isolation**: Tests should be independent and not rely on external state
2. **Naming**: Use descriptive names that explain the test scenario
3. **Arrange-Act-Assert**: Follow the AAA pattern for test structure
4. **Mocking**: Use Moq for external dependencies
5. **Coverage**: Aim for high coverage on critical business logic
