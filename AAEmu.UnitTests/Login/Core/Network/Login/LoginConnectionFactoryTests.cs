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
namespace AAEmu.UnitTests.Login.Core.Network.Login;

public class LoginConnectionFactoryTests
{
    private readonly Mock<ILoginProtocolHandler> _mockProtocolHandler = Mock.Of<ILoginProtocolHandler>();
    private readonly Mock<IConnectionIdLeaseFactory> _mockLeaseFactory = Mock.Of<IConnectionIdLeaseFactory>();
    private readonly Mock<ILoginSessionFactory> _mockSessionFactory = Mock.Of<ILoginSessionFactory>();
    private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    [Test]
    public async Task Create_WithValidContext_ReturnsLoginConnection()
    {
        // Arrange
        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Rent().Returns(lease);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _mockSessionFactory.Object,
            _loggerFactory);

        var mockContext = CreateMockConnectionContext();

        // Act
        var connection = factory.Create(mockContext.Object);

        // Assert
        await Assert.That(connection).IsNotNull();
        await Assert.That(connection.Id).IsEqualTo(connectionId);
    }

    [Test]
    public void Create_RentsConnectionIdLease()
    {
        // Arrange
        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Rent().Returns(lease);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _mockSessionFactory.Object,
            _loggerFactory);

        var mockContext = CreateMockConnectionContext();

        // Act
        _ = factory.Create(mockContext.Object);

        // Assert
        _mockLeaseFactory.Rent().WasCalled(Times.Once);
    }

    [Test]
    public async Task Create_MultipleCalls_RentsNewLeaseEachTime()
    {
        // Arrange
        var connectionId1 = new ConnectionId(1);
        var connectionId2 = new ConnectionId(2);
        var lease1 = CreateConnectionIdLease(connectionId1);
        var lease2 = CreateConnectionIdLease(connectionId2);

        _mockLeaseFactory.Rent().ReturnsSequentially(lease1, lease2);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _mockSessionFactory.Object,
            _loggerFactory);

        var mockContext1 = CreateMockConnectionContext();
        var mockContext2 = CreateMockConnectionContext();

        // Act
        var connection1 = factory.Create(mockContext1.Object);
        var connection2 = factory.Create(mockContext2.Object);

        // Assert
        await Assert.That(connection1.Id).IsEqualTo(connectionId1);
        await Assert.That(connection2.Id).IsEqualTo(connectionId2);
        _mockLeaseFactory.Rent().WasCalled(Times.Exactly(2));
    }

    [Test]
    public async Task Create_WithPacketDescriptors_IncludesThemInConnection()
    {
        // Arrange
        var mockDescriptor1 = Mock.Of<ILoginPacketDescriptor>();
        mockDescriptor1.TypeId.Returns(1);

        var mockDescriptor2 = Mock.Of<ILoginPacketDescriptor>();
        mockDescriptor2.TypeId.Returns(2);

        var packetDescriptors = new List<ILoginPacketDescriptor> { mockDescriptor1.Object, mockDescriptor2.Object };

        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Rent().Returns(lease);

        var factory = new LoginConnectionFactory(
            packetDescriptors,
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _mockSessionFactory.Object,
            _loggerFactory);

        var mockContext = CreateMockConnectionContext();

        // Act
        var connection = factory.Create(mockContext.Object);

        // Assert - connection was created successfully with the descriptors
        await Assert.That(connection).IsNotNull();
    }

    [Test]
    public void Create_WithDuplicatePacketDescriptorTypeIds_ThrowsArgumentException()
    {
        // Arrange
        var mockDescriptor1 = Mock.Of<ILoginPacketDescriptor>();
        mockDescriptor1.TypeId.Returns(1);

        var mockDescriptor2 = Mock.Of<ILoginPacketDescriptor>();
        mockDescriptor2.TypeId.Returns(1); // Duplicate TypeId

        var packetDescriptors = new List<ILoginPacketDescriptor> { mockDescriptor1.Object, mockDescriptor2.Object };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new LoginConnectionFactory(
            packetDescriptors,
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _mockSessionFactory.Object,
            _loggerFactory));
    }

    [Test]
    public async Task Create_ConnectionIsLocalWhenSameEndpoint()
    {
        // Arrange
        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Rent().Returns(lease);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _mockSessionFactory.Object,
            _loggerFactory);

        // Create context where local and remote are the same IP
        var mockContext = Mock.Of<ConnectionContext>();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 12345);
        mockContext.Object.LocalEndPoint = endpoint;
        mockContext.Object.RemoteEndPoint = endpoint;
        mockContext.Object.Transport = CreateMockDuplexPipe().Object;

        // Act
        var connection = factory.Create(mockContext.Object);

        // Assert
        await Assert.That(connection.IsLocallyConnected).IsTrue();
    }

    [Test]
    public async Task Create_ConnectionIsNotLocalWhenDifferentEndpoint()
    {
        // Arrange
        var connectionId = new ConnectionId(1);
        var lease = CreateConnectionIdLease(connectionId);

        _mockLeaseFactory.Rent().Returns(lease);

        var factory = new LoginConnectionFactory(
            [],
            _mockProtocolHandler.Object,
            _mockLeaseFactory.Object,
            _mockSessionFactory.Object,
            _loggerFactory);

        // Create context where local and remote are different IPs
        var mockContext = Mock.Of<ConnectionContext>();
        mockContext.Object.LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 12345);
        mockContext.Object.RemoteEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 54321);
        mockContext.Object.Transport = CreateMockDuplexPipe().Object;

        // Act
        var connection = factory.Create(mockContext.Object);

        // Assert
        await Assert.That(connection.IsLocallyConnected).IsFalse();
    }

    private static Mock<ConnectionContext> CreateMockConnectionContext()
    {
        var mockContext = Mock.Of<ConnectionContext>();
        mockContext.Object.RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 12345);
        mockContext.Object.LocalEndPoint = new IPEndPoint(IPAddress.Loopback, 1234);
        mockContext.Object.Transport = CreateMockDuplexPipe().Object;
        return mockContext;
    }

    private static Mock<IDuplexPipe> CreateMockDuplexPipe()
    {
        var mockPipe = Mock.Of<IDuplexPipe>();
        mockPipe.Input.Returns(PipeReader.Create(Stream.Null));
        mockPipe.Output.Returns(PipeWriter.Create(Stream.Null));
        return mockPipe;
    }

    private static ConnectionIdLease CreateConnectionIdLease(ConnectionId connectionId)
    {
        var mockIdManager = Mock.Of<IIdManager<ConnectionId>>();
        mockIdManager.Rent().Returns(connectionId);
        var leaseFactory = new ConnectionIdLeaseFactory(mockIdManager.Object);
        return leaseFactory.Rent();
    }
}
