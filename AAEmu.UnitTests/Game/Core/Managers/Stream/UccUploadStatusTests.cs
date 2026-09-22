using System.Net;
using System.Net.Sockets;

using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Stream;

namespace AAEmu.UnitTests.Game.Core.Managers.Stream;

/// <summary>
/// Upload-status handling: an upload that reports failure is acknowledged, its queued upload is
/// discarded, and the granting confirm path is never reached for it.
/// </summary>
public sealed class UccUploadStatusTests
{
    private sealed class RecordingSession(uint sessionId) : ISession
    {
        public List<byte[]> Sent { get; } = [];

        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => sessionId;
        public Socket Socket => null!;

        public void SendPacket(byte[] packet) => Sent.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    /// <summary>
    /// A connection whose active character has no inventory: entering the granting confirm path
    /// with a pending upload would dereference that missing inventory and throw, so a clean run is
    /// proof that the failure handling never reached it.
    /// </summary>
    private static (UccManager Manager, StreamConnection Connection, RecordingSession Session)
        CreatePendingUpload()
    {
        var manager = new UccManager(Mock.Of<IUccIdManager>().Object);
        var session = new RecordingSession(77);
        var connection = new StreamConnection(session)
        {
            GameConnection = new GameConnection(session)
            {
                ActiveChar = new Character(new UnitCustomModelParams()) { Id = 10 },
            },
        };

        manager.AddDefaultUcc(new DefaultUcc { UploaderId = 10 }, connection);
        return (manager, connection, session);
    }

    [Test]
    public async Task FailedUploadStatus_DiscardsQueuedUploadAcknowledgesAndGrantsNothing()
    {
        var (manager, connection, session) = CreatePendingUpload();
        await Assert.That(manager.HasPendingUpload(connection)).IsTrue();
        await Assert.That(session.Sent.Count).IsEqualTo(1); // the readiness acknowledgement only

        var action = manager.HandleUploadStatus(connection, 5);

        await Assert.That(action).IsEqualTo(UccManager.UccUploadStatusAction.UploadFailed);
        await Assert.That(manager.HasPendingUpload(connection)).IsFalse();
        await Assert.That(session.Sent.Count).IsEqualTo(2); // failure is acknowledged too

        // With the queue empty there is nothing left for a later completion status to grant, and
        // confirming an empty queue sends nothing at all.
        var late = manager.HandleUploadStatus(connection, UccManager.UploadStatusTransferComplete);
        await Assert.That(late).IsEqualTo(UccManager.UccUploadStatusAction.ConfirmUpload);
        await Assert.That(manager.HasPendingUpload(connection)).IsFalse();
        await Assert.That(session.Sent.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ReadyUploadStatus_KeepsUploadPendingWithoutGranting()
    {
        var (manager, connection, session) = CreatePendingUpload();

        var action = manager.HandleUploadStatus(connection, UccManager.UploadStatusTransferReady);

        await Assert.That(action).IsEqualTo(UccManager.UccUploadStatusAction.TransferInProgress);
        await Assert.That(manager.HasPendingUpload(connection)).IsTrue();
        await Assert.That(session.Sent.Count).IsEqualTo(2); // readiness ack plus a progress ack
    }

    [Test]
    public async Task UploadStatus_Classification_OnlyCompletionReachesTheGrantingAction()
    {
        await Assert.That(UccManager.ClassifyUploadStatus(UccManager.UploadStatusTransferComplete))
            .IsEqualTo(UccManager.UccUploadStatusAction.ConfirmUpload);
        await Assert.That(UccManager.ClassifyUploadStatus(UccManager.UploadStatusTransferReady))
            .IsEqualTo(UccManager.UccUploadStatusAction.TransferInProgress);

        foreach (var status in new byte[] { 0, 1, 2, 5, 255 })
        {
            await Assert.That(UccManager.ClassifyUploadStatus(status))
                .IsEqualTo(UccManager.UccUploadStatusAction.UploadFailed);
        }
    }
}
