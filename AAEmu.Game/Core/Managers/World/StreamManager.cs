using AAEmu.Commons.Utils;
using System.Collections.Concurrent;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.S2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class StreamManager : Singleton<StreamManager>, IStreamManager
{
    // Official 10.0.2.13 streams page cell doodads in 30-record chunks; `next` is the exclusive cursor.
    private const int MaxDoodadsPerPacket = 30;
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<uint, uint> _accounts = [];

    public void AddToken(uint accountId, uint connectionId)
    {
        _accounts[connectionId] = accountId;
    }

    public void RemoveToken(uint token)
    {
        _accounts.TryRemove(token, out _);
    }

    public void Login(StreamConnection connection, long accountId, uint token)
    {
        // Keep the token for the life of the game session (removed in GameProtocolHandler.OnDisconnect).
        // TryRemove here burned it on the first CTJoin, so return-to-character-select (client drops
        // stream, reconnects, CTJoin again) always got Rejected(1) → OpenStream failed → Quit.
        if (!_accounts.TryGetValue(token, out var expectedAccountId)
            || accountId <= 0
            || accountId > uint.MaxValue
            || expectedAccountId != (uint)accountId)
        {
            Logger.Warn("Stream CTJoin rejected: bad token/account token={0} accountId={1}", token, accountId);
            connection.SendPacket(new TCJoinResponsePacket(StreamJoinResponse.Rejected));
            return;
        }

        var gameConnection = GameConnectionTable.Instance.GetConnection(token);
        if (gameConnection is null || gameConnection.AccountId != expectedAccountId)
        {
            Logger.Warn("Stream CTJoin rejected: no game session for token={0} accountId={1}", token, accountId);
            connection.SendPacket(new TCJoinResponsePacket(StreamJoinResponse.Rejected));
            return;
        }

        connection.GameConnection = gameConnection;
        connection.SendPacket(new TCJoinResponsePacket(StreamJoinResponse.Success));
    }

    public static void RequestCell(StreamConnection connection, int instanceId, uint cellX, uint cellY)
    {
        var character = connection?.GameConnection?.ActiveChar;
        if (character is null || instanceId < 0 || character.Transform.InstanceId != (uint)instanceId)
        {
            Logger.Warn("Rejected stream cell request for instance {0} from unauthorised connection {1}",
                instanceId, connection?.Id);
            return;
        }

        var world = WorldManager.Instance.GetWorld((uint)instanceId);
        if (world is null
            || world.Template.CellX <= 0
            || world.Template.CellY <= 0
            || cellX >= (uint)world.Template.CellX
            || cellY >= (uint)world.Template.CellY)
        {
            Logger.Warn("Rejected out-of-range stream cell {0},{1} in instance {2}", cellX, cellY, instanceId);
            return;
        }

        // looks up ClientDoodad — house-bound doors streamed first stay closed forever
        // (SC create is "Doodad duplicated id" no-op). Keep parented/housing doodads on the
        // SC Create path so phase open/close can swap models.
        var doodads = world.GetInCell<Doodad>((int)cellX, (int)cellY)
            .Where(d => d.ParentObjId == 0
                        && d.AttachPoint == AttachPointKind.None
                        && d.OwnerType != DoodadOwnerType.Housing)
            .ToArray();
        var requestId = connection.ReserveRequestId();
        var count = Math.Min(doodads.Length, MaxDoodadsPerPacket);
        var next = checked((uint)count);
        var result = new Doodad[count];
        Array.Copy(doodads, result, count);

        if (count < doodads.Length)
        {
            connection.AddRequest(requestId,
                new StreamConnection.CellRequest(instanceId, cellX, cellY, doodads, next));
        }

        connection.SendPacket(new TCDoodadStreamPacket(requestId, next, result));
    }

    public static void ContinueCell(StreamConnection connection, uint requestId, uint next)
    {
        // Retail TCDoodadStream `next` is the exclusive end index of the chunk just sent
        // (30, 60, 90, …). CTContinue echoes that value as the start of the next chunk.
        // Copying from next-1 re-sent the boundary entry and dropped the cell tail.
        if (connection is null || !connection.TryGetRequest(requestId, out var request))
            return;

        var character = connection.GameConnection?.ActiveChar;
        lock (request)
        {
            if (character is null
                || request.InstanceId < 0
                || character.Transform.InstanceId != (uint)request.InstanceId
                || next != request.Next
                || next >= (uint)request.Doodads.Length)
            {
                connection.RemoveRequest(requestId);
                return;
            }

            var count = Math.Min(request.Doodads.Length - checked((int)next), MaxDoodadsPerPacket);
            var result = new Doodad[count];
            Array.Copy(request.Doodads, checked((int)next), result, 0, count);
            request.Next = checked(next + (uint)count);
            connection.SendPacket(new TCDoodadStreamPacket(requestId, request.Next, result));
            if (request.Next >= (uint)request.Doodads.Length)
                connection.RemoveRequest(requestId);
        }
    }

    public static void CancelCell(StreamConnection connection, int instanceId, uint cellX, uint cellY)
    {
        connection?.CancelCell(instanceId, cellX, cellY);
    }
}
