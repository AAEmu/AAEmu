#nullable enable

using System.Net;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
namespace AAEmu.UnitTests.Login.Core.Network.Login;

public class LoginConnectionHandlerTests
{
    private readonly Mock<ILoginConnectionFactory> _mockFactory = Mock.Of<ILoginConnectionFactory>();
    private readonly Mock<ILoginConnectionTable> _mockTable = Mock.Of<ILoginConnectionTable>();
    private readonly Mock<ILogger<LoginConnectionHandler>> _mockLogger = Mock.Of<ILogger<LoginConnectionHandler>>();
    private readonly LoginConnectionHandler _cut;

    public LoginConnectionHandlerTests()
    {
        _cut = new LoginConnectionHandler(
            _mockFactory.Object,
            _mockTable.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task OnConnectedAsync_NormalConnection_AddsAndRemovesConnection()
    {
        // Arrange
        var connectionId = new ConnectionId(42);
        var mockConnectionContext = CreateMockConnectionContext();
        var mockConnection = CreateMockLoginConnection(connectionId);

        _mockFactory.Create(mockConnectionContext.Object).Returns(mockConnection.Object);
        _mockTable.RemoveConnection(connectionId).Returns(mockConnection.Object);

        // Act
        await _cut.OnConnectedAsync(mockConnectionContext.Object);

        // Assert
        _mockFactory.Create(mockConnectionContext.Object).WasCalled(Times.Once);
        _mockTable.AddConnection(Any<ILoginConnection>()).WasCalled(Times.Once);
        _mockTable.RemoveConnection(connectionId).WasCalled(Times.Once);
    }

    [Test]
    public async Task OnConnectedAsync_ConnectionThrows_StillRemovesConnection()
    {
        // Arrange
        var connectionId = new ConnectionId(42);
        var mockConnectionContext = CreateMockConnectionContext();
        var mockConnection = CreateMockLoginConnection(connectionId, throwOnConnect: true);

        _mockFactory.Create(mockConnectionContext.Object).Returns(mockConnection.Object);
        _mockTable.RemoveConnection(connectionId).Returns(mockConnection.Object);

        // Act
        await _cut.OnConnectedAsync(mockConnectionContext.Object);

        // Assert - connection should still be removed even after exception
        _mockTable.RemoveConnection(connectionId).WasCalled(Times.Once);
    }

    [Test]
    public async Task OnConnectedAsync_DisposesConnectionContext()
    {
        // Arrange
        var connectionId = new ConnectionId(42);
        var mockConnectionContext = CreateMockConnectionContext();
        var mockConnection = CreateMockLoginConnection(connectionId);

        _mockFactory.Create(mockConnectionContext.Object).Returns(mockConnection.Object);

        // Act
        await _cut.OnConnectedAsync(mockConnectionContext.Object);

        // Assert
        mockConnectionContext.DisposeAsync().WasCalled(Times.Once);
    }

    [Test]
    public async Task OnConnectedAsync_DisposesConnection()
    {
        // Arrange
        var connectionId = new ConnectionId(42);
        var mockConnectionContext = CreateMockConnectionContext();
        var mockConnection = CreateMockLoginConnection(connectionId);

        _mockFactory.Create(mockConnectionContext.Object).Returns(mockConnection.Object);

        // Act
        await _cut.OnConnectedAsync(mockConnectionContext.Object);

        // Assert
        mockConnection.DisposeAsync().WasCalled(Times.Once);
    }

    private static Mock<ConnectionContext> CreateMockConnectionContext()
    {
        var mockContext = Mock.Of<ConnectionContext>();
        mockContext.RemoteEndPoint.Returns(new IPEndPoint(IPAddress.Loopback, 12345));
        mockContext.ConnectionId.Returns("test-connection-id");
        // DisposeAsync() returns ValueTask by default — no .Returns() needed
        return mockContext;
    }

    private static Mock<ILoginConnectionOwner> CreateMockLoginConnection(ConnectionId connectionId,
        bool throwOnConnect = false)
    {
        var mockConnection = Mock.Of<ILoginConnectionOwner>();
        mockConnection.Id.Returns(connectionId);
        // DisposeAsync() returns ValueTask by default — no .Returns() needed

        if (throwOnConnect)
        {
            mockConnection.OnConnectedAsync().Throws(new InvalidOperationException("Test exception"));
        }
        // OnConnectedAsync() returns Task.CompletedTask by default — no .Returns() needed

        return mockConnection;
    }
}
