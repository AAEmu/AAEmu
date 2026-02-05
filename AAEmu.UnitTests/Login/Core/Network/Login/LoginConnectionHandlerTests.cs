#nullable enable

using System.Net;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Login.Core.Network.Login;

public class LoginConnectionHandlerTests
{
    private readonly Mock<ILoginConnectionFactory> _mockFactory;
    private readonly Mock<ILoginConnectionTable> _mockTable;
    private readonly Mock<ILogger<LoginConnectionHandler>> _mockLogger;
    private readonly LoginConnectionHandler _cut;

    public LoginConnectionHandlerTests()
    {
        _mockFactory = new Mock<ILoginConnectionFactory>();
        _mockTable = new Mock<ILoginConnectionTable>();
        _mockLogger = new Mock<ILogger<LoginConnectionHandler>>();

        _cut = new LoginConnectionHandler(
            _mockFactory.Object,
            _mockTable.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task OnConnectedAsync_NormalConnection_AddsAndRemovesConnection()
    {
        // Arrange
        var connectionId = new ConnectionId(42);
        var mockConnectionContext = CreateMockConnectionContext();
        var mockConnection = CreateMockLoginConnection(connectionId);

        _mockFactory.Setup(f => f.Create(mockConnectionContext.Object))
            .Returns(mockConnection.Object);

        _mockTable.Setup(t => t.AddConnection(mockConnection.Object));
        _mockTable.Setup(t => t.RemoveConnection(connectionId))
            .Returns(mockConnection.Object);

        // Act
        await _cut.OnConnectedAsync(mockConnectionContext.Object);

        // Assert
        _mockFactory.Verify(f => f.Create(mockConnectionContext.Object), Times.Once);
        _mockTable.Verify(t => t.AddConnection(mockConnection.Object), Times.Once);
        _mockTable.Verify(t => t.RemoveConnection(connectionId), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_ConnectionThrows_StillRemovesConnection()
    {
        // Arrange
        var connectionId = new ConnectionId(42);
        var mockConnectionContext = CreateMockConnectionContext();
        var mockConnection = CreateMockLoginConnection(connectionId, throwOnConnect: true);

        _mockFactory.Setup(f => f.Create(mockConnectionContext.Object))
            .Returns(mockConnection.Object);

        _mockTable.Setup(t => t.AddConnection(mockConnection.Object));
        _mockTable.Setup(t => t.RemoveConnection(connectionId))
            .Returns(mockConnection.Object);

        // Act
        await _cut.OnConnectedAsync(mockConnectionContext.Object);

        // Assert - connection should still be removed even after exception
        _mockTable.Verify(t => t.RemoveConnection(connectionId), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_DisposesConnectionContext()
    {
        // Arrange
        var connectionId = new ConnectionId(42);
        var mockConnectionContext = CreateMockConnectionContext();
        var mockConnection = CreateMockLoginConnection(connectionId);

        _mockFactory.Setup(f => f.Create(mockConnectionContext.Object))
            .Returns(mockConnection.Object);

        // Act
        await _cut.OnConnectedAsync(mockConnectionContext.Object);

        // Assert
        mockConnectionContext.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_DisposesConnection()
    {
        // Arrange
        var connectionId = new ConnectionId(42);
        var mockConnectionContext = CreateMockConnectionContext();
        var mockConnection = CreateMockLoginConnection(connectionId);

        _mockFactory.Setup(f => f.Create(mockConnectionContext.Object))
            .Returns(mockConnection.Object);

        // Act
        await _cut.OnConnectedAsync(mockConnectionContext.Object);

        // Assert
        mockConnection.Verify(c => c.DisposeAsync(), Times.Once);
    }

    private static Mock<ConnectionContext> CreateMockConnectionContext()
    {
        var mockContext = new Mock<ConnectionContext>();
        mockContext.Setup(c => c.RemoteEndPoint)
            .Returns(new IPEndPoint(IPAddress.Loopback, 12345));
        mockContext.Setup(c => c.ConnectionId)
            .Returns("test-connection-id");
        mockContext.Setup(c => c.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        return mockContext;
    }

    private static Mock<ILoginConnectionOwner> CreateMockLoginConnection(ConnectionId connectionId,
        bool throwOnConnect = false)
    {
        var mockConnection = new Mock<ILoginConnectionOwner>();
        mockConnection.Setup(c => c.Id).Returns(connectionId);
        mockConnection.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        if (throwOnConnect)
        {
            mockConnection.Setup(c => c.OnConnectedAsync())
                .ThrowsAsync(new InvalidOperationException("Test exception"));
        }
        else
        {
            mockConnection.Setup(c => c.OnConnectedAsync())
                .Returns(Task.CompletedTask);
        }

        return mockConnection;
    }
}
