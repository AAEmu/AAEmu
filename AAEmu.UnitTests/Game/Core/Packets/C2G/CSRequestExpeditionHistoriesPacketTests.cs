using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

[NotInParallel]
public sealed class CSRequestExpeditionHistoriesPacketTests
{
    [After(Test)]
    public void TearDown() => SingletonContainer.ServiceProvider = null;

    [Test]
    [Arguments((sbyte)1)]
    [Arguments((sbyte)2)]
    [Arguments((sbyte)3)]
    [Arguments((sbyte)4)]
    public async Task Read_DispatchesEveryDefinedSBytePageWithoutEnumTypeMismatch(sbyte historyType)
    {
        var world = Mock.Of<IWorldManager>();
        var expeditionManager = new ExpeditionManager(
            Mock.Of<IExpeditionIdManager>().Object,
            Mock.Of<ITeamManager>().Object,
            world.Object,
            Mock.Of<IChatManager>().Object);
        var service = new ExpeditionActivityService(
            Mock.Of<IExpeditionActivityRepository>().Object,
            world.Object,
            expeditionManager,
            Mock.Of<IItemManager>().Object,
            Mock.Of<IExpeditionActivityConnectionFactory>().Object,
            Mock.Of<IFactionManager>().Object);
        SingletonContainer.ServiceProvider = new ServiceCollection()
            .AddSingleton(service)
            .BuildServiceProvider();

        var connection = new GameConnection(Mock.Of<ISession>().Object);
        connection.ActiveChar = new Character(new UnitCustomModelParams()) { Id = 42 };
        var packet = new CSRequestExpeditionHistoriesPacket { Connection = connection };
        var stream = new PacketStream().Write(historyType);

        packet.Read(stream);

        await Assert.That(packet.HistoryType).IsEqualTo(historyType);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
