#nullable enable

using System.IO.Pipelines;
using System.Net;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Login.Core.Network.Login;

public class LoginConnectionFactoryTests
{
    private readonly Mock<ILoginProtocolHandler> _mockProtocolHandler = new();
    private readonly Mock<IConnectionIdLeaseFactory> _mockLeaseFactory = new();
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    [Fact]
    public void Create_WithValidContext_ReturnsLoginConnection()
    {
        // Arrange
        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Setup(f => f.Rent()).Returns(lease);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _loggerFactory);

        var mockContext = CreateMockConnectionContext();

        // Act
        var connection = factory.Create(mockContext.Object);

        // Assert
        Assert.NotNull(connection);
        Assert.Equal(connectionId, connection.Id);
    }

    [Fact]
    public void Create_RentsConnectionIdLease()
    {
        // Arrange
        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Setup(f => f.Rent()).Returns(lease);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _loggerFactory);

        var mockContext = CreateMockConnectionContext();

        // Act
        _ = factory.Create(mockContext.Object);

        // Assert
        _mockLeaseFactory.Verify(f => f.Rent(), Times.Once);
    }

    [Fact]
    public void Create_MultipleCalls_RentsNewLeaseEachTime()
    {
        // Arrange
        var connectionId1 = new ConnectionId(1);
        var connectionId2 = new ConnectionId(2);
        var lease1 = CreateConnectionIdLease(connectionId1);
        var lease2 = CreateConnectionIdLease(connectionId2);

        _mockLeaseFactory.SetupSequence(f => f.Rent())
            .Returns(lease1)
            .Returns(lease2);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _loggerFactory);

        var mockContext1 = CreateMockConnectionContext();
        var mockContext2 = CreateMockConnectionContext();

        // Act
        var connection1 = factory.Create(mockContext1.Object);
        var connection2 = factory.Create(mockContext2.Object);

        // Assert
        Assert.Equal(connectionId1, connection1.Id);
        Assert.Equal(connectionId2, connection2.Id);
        _mockLeaseFactory.Verify(f => f.Rent(), Times.Exactly(2));
    }

    [Fact]
    public void Create_WithPacketDescriptors_IncludesThemInConnection()
    {
        // Arrange
        var mockDescriptor1 = new Mock<ILoginPacketDescriptor>();
        mockDescriptor1.Setup(d => d.TypeId).Returns(1);

        var mockDescriptor2 = new Mock<ILoginPacketDescriptor>();
        mockDescriptor2.Setup(d => d.TypeId).Returns(2);

        var packetDescriptors = new List<ILoginPacketDescriptor> { mockDescriptor1.Object, mockDescriptor2.Object };

        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Setup(f => f.Rent()).Returns(lease);

        var factory = new LoginConnectionFactory(
            packetDescriptors,
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _loggerFactory);

        var mockContext = CreateMockConnectionContext();

        // Act
        var connection = factory.Create(mockContext.Object);

        // Assert - connection was created successfully with the descriptors
        Assert.NotNull(connection);
    }

    [Fact]
    public void Create_WithDuplicatePacketDescriptorTypeIds_ThrowsArgumentException()
    {
        // Arrange
        var mockDescriptor1 = new Mock<ILoginPacketDescriptor>();
        mockDescriptor1.Setup(d => d.TypeId).Returns(1);

        var mockDescriptor2 = new Mock<ILoginPacketDescriptor>();
        mockDescriptor2.Setup(d => d.TypeId).Returns(1); // Duplicate TypeId

        var packetDescriptors = new List<ILoginPacketDescriptor> { mockDescriptor1.Object, mockDescriptor2.Object };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new LoginConnectionFactory(
            packetDescriptors,
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _loggerFactory));
    }

    [Fact]
    public void Create_ConnectionIsLocalWhenSameEndpoint()
    {
        // Arrange
        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Setup(f => f.Rent()).Returns(lease);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _loggerFactory);

        // Create context where local and remote are the same IP
        var mockContext = new Mock<ConnectionContext>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);
        mockContext.Setup(c => c.LocalEndPoint).Returns(endpoint);
        mockContext.Setup(c => c.RemoteEndPoint).Returns(endpoint);
        mockContext.Setup(c => c.Transport).Returns(CreateMockDuplexPipe().Object);

        // Act
        var connection = factory.Create(mockContext.Object);

        // Assert
        Assert.True(connection.IsLocallyConnected);
    }

    [Fact]
    public void Create_ConnectionIsNotLocalWhenDifferentEndpoint()
    {
        // Arrange
        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Setup(f => f.Rent()).Returns(lease);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _loggerFactory);

        // Create context where local and remote are different IPs
        var mockContext = new Mock<ConnectionContext>();
        mockContext.Setup(c => c.LocalEndPoint).Returns(new IPEndPoint(IPAddress.Loopback, 12345));
        mockContext.Setup(c => c.RemoteEndPoint).Returns(new IPEndPoint(IPAddress.Parse("192.168.1.1"), 54321));
        mockContext.Setup(c => c.Transport).Returns(CreateMockDuplexPipe().Object);

        // Act
        var connection = factory.Create(mockContext.Object);

        // Assert
        Assert.False(connection.IsLocallyConnected);
    }

    private static Mock<ConnectionContext> CreateMockConnectionContext()
    {
        var mockContext = new Mock<ConnectionContext>();
        mockContext.Setup(c => c.RemoteEndPoint)
            .Returns(new IPEndPoint(IPAddress.Loopback, 12345));
        mockContext.Setup(c => c.LocalEndPoint)
            .Returns(new IPEndPoint(IPAddress.Loopback, 1234));
        mockContext.Setup(c => c.Transport)
            .Returns(CreateMockDuplexPipe().Object);
        return mockContext;
    }

    private static Mock<IDuplexPipe> CreateMockDuplexPipe()
    {
        var mockPipe = new Mock<IDuplexPipe>();
        mockPipe.Setup(p => p.Input).Returns(Mock.Of<PipeReader>());
        mockPipe.Setup(p => p.Output).Returns(Mock.Of<PipeWriter>());
        return mockPipe;
    }

    private static ConnectionIdLease CreateConnectionIdLease(ConnectionId connectionId)
    {
        var mockIdManager = new Mock<IIdManager<ConnectionId>>();
        mockIdManager.Setup(m => m.Rent()).Returns(connectionId);
        var leaseFactory = new ConnectionIdLeaseFactory(mockIdManager.Object);
        return leaseFactory.Rent();
    }
}
