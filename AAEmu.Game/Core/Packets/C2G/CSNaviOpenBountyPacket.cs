using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Crime;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNaviOpenBountyPacket() : GamePacket(CSOffsets.CSNaviOpenBountyPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();

        // Should be a ObjId of a bounty board doodad (6166)
        Logger.Warn($"NaviOpenBounty, ObjId: {objId}");
        
        // Generate dummy data
        /*
        var list = new List<BountyDescription>();
        var allChars = WorldManager.Instance.GetAllCharacters().Where(x => x.Id == 1);
        foreach (var c in allChars)
        {
            list.Add(new BountyDescription()
            {
                PlayerId = c.Id,
                PlayerName = c.Name,
                PlayerLevel = c.Level,
                ZoneId = c.Transform.ZoneId,
                Pos = c.Transform.World.Position,
                BountyAmount = 1000000,
                CrimePoints = c.CrimePoint,
                IsLive = true,
                PlayerRace = c.Race,
                Type184 = 1,
                ArrestCount = 5,
                AcceptGuiltyCount = 6,
                AcceptTrialCount = 7,
                GuiltyCount = 8,
                NotGuiltyCount = 9,
                Seconds = 99,
            });
        }

        Connection.ActiveChar.SendPacket(new SCBountyListPacket(list.Count, (byte)list.Count, list));
        */
        // Having no entries make it open the UI
        // However, if you start filling in the data, the client runs into LUA-related issues
        // TODO: Figure out what is broken in the LUA, en_us and kr give different types of errors on the client
        Connection.ActiveChar.SendPacket(new SCBountyListPacket(0, 0, []));
    }
}
