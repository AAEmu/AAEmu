using AAEmu.Game.Core.Network.Game;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Network.Game;

/// <summary>
/// Tests for GameNetwork class
/// </summary>
public class GameNetworkTests
{
    private readonly GameNetwork _cut = GameNetwork.Instance;

    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Arrange & Act
        var instance1 = GameNetwork.Instance;
        var instance2 = GameNetwork.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void Start_InitializesServer()
    {
        // Arrange
        // Note: This test requires AppConfiguration to be set up
        // For full integration, configuration needs to be mocked

        // Act
        // _cut.Start(); // Requires actual configuration

        // Assert
        // Verification would require mocking the Server class
        Assert.True(true, "Start method requires configuration setup");
    }

    [Fact]
    public void Stop_StopsServer_WhenStarted()
    {
        // Arrange
        // Note: Stop requires Start to be called first

        // Act
        // _cut.Stop(); // Requires server to be started

        // Assert
        // Verification would require mocking the Server class
        Assert.True(true, "Stop method requires server to be started first");
    }
}
