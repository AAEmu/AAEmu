using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSetPingPosPacket : GamePacket
{
    public CSSetPingPosPacket() : base(CSOffsets.CSSetPingPosPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var teamId = stream.ReadUInt32();
        var setPingType = stream.ReadByte();
        var flag = stream.ReadByte();
        Logger.Warn($"SetPingPos: teamId={teamId}, setPingType={setPingType}, flag={flag}");
        return;

        //var positions = new WorldSpawnPosition[6];
        //for (var i = 0; i < 6; i++)
        //{
        //    stream.ReadByte();
        //    //positions[i] = new WorldSpawnPosition();
        //    //positions[i].X = stream.ReadSingle();
        //    //positions[i].Y = stream.ReadSingle();
        //    //positions[i].Z = stream.ReadSingle();
        //}

        //var Pisc = new uint[6];

        //// --- begin pish
        //var mTmps = stream.ReadPisc(4);
        //// --- end pish
        //Pisc[0] = (uint)mTmps[0];
        //Pisc[1] = (uint)mTmps[1];
        //Pisc[2] = (uint)mTmps[2];
        //Pisc[3] = (uint)mTmps[3];

        //// --- begin pish
        //mTmps = stream.ReadPisc(2);
        //// --- end pish
        //Pisc[4] = (uint)mTmps[0];
        //Pisc[5] = (uint)mTmps[1];

        //var lineCount = stream.ReadByte();
        //if (lineCount > 0)
        //{
        //    var linePositions = new WorldSpawnPosition[lineCount];
        //    for (var i = 0; i < lineCount; i++) // не больше 20 точек
        //    {
        //        linePositions[i] = new WorldSpawnPosition();
        //        linePositions[i].X = stream.ReadSingle();
        //        linePositions[i].Y = stream.ReadSingle();
        //        linePositions[i].Z = stream.ReadSingle();
        //    }
        //}

        //Logger.Warn($"SetPingPos: teamId={teamId}, setPingType={setPingType}, flag={flag}");
        //var owner = Connection.ActiveChar;
        //owner.LocalPingPosition = positions[0];
        //if (teamId > 0)
        //{
        //    TeamManager.Instance.SetPingPos(owner, teamId, hasPing, position, insId);
        //}
        //else
        //{
        //    owner.SendPacket(new SCTeamPingPosPacket(hasPing, position, insId));
        //}
    }
}
