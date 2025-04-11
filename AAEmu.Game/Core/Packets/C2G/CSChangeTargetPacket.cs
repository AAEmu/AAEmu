using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeTargetPacket : GamePacket
{
    public CSChangeTargetPacket() : base(CSOffsets.CSChangeTargetPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var targetId = stream.ReadBc();
        Connection
                .ActiveChar
                .CurrentTarget = targetId > 0 ? WorldManager.Instance.GetUnit(targetId) : null;

        Connection
            .ActiveChar
            .BroadcastPacket(
                new SCTargetChangedPacket(Connection.ActiveChar.ObjId,
                    Connection.ActiveChar.CurrentTarget?.ObjId ?? 0), true);

        if (targetId == 0)
        {
            Connection.ActiveChar.SendDebugMessage("Selected nothing");
            return;
        }
        if (Connection.ActiveChar.CurrentTarget == null)
        {
            Connection.ActiveChar.SendDebugMessage($"ObjId: {targetId}, TemplateId: not found in Db");
            WorldManager.Instance.RemoveObject(targetId); // trying to delete the missing object
            return;
        }
        if (Connection.ActiveChar.CurrentTarget is Portal portal)
            Connection.ActiveChar.SendDebugMessage($"ObjId: {targetId}, TemplateId: {portal.TemplateId}\nPos: {portal.Transform}");
        else if (Connection.ActiveChar.CurrentTarget is Npc npc)
        {
            var spawnerId = npc.Spawner != null && npc.Spawner.NpcSpawnerIds.Count > 0
                ? npc.Spawner.NpcSpawnerIds[0]
                : 0u;

            Connection.ActiveChar.SendDebugMessage(string.Format("ObjId: {0}, TemplateId: {1}, Ai: {2}, @{3} SpawnerId: {4} Stance: {6}, Speed: {7:F1}\nPos: {5}",
                targetId,
                npc.TemplateId,
                npc.Ai?.GetType().Name.Replace("AiCharacter", ""),
                npc.Ai?.GetCurrentBehavior()?.GetType().Name.Replace("Behavior", ""), spawnerId,
                npc.Transform,
                npc.CurrentGameStance, npc.BaseMoveSpeed));
        }
        else if (Connection.ActiveChar.CurrentTarget is House house)
            Connection.ActiveChar.SendDebugMessage($"ObjId: {targetId}, HouseId: {house.Id}, Pos: {house.Transform}");
        else if (Connection.ActiveChar.CurrentTarget is Transfer transfer)
            Connection.ActiveChar.SendDebugMessage($"ObjId: {targetId}, Transfer TemplateId: {transfer.TemplateId}\nPos: {transfer.Transform}");
        else if (Connection.ActiveChar.CurrentTarget is Slave slave)
            Connection.ActiveChar.SendDebugMessage($"ObjId: {slave.ObjId}, Slave TemplateId: {slave.TemplateId}, Id: {slave.Id}, Owner: {slave.Summoner?.Name}\nPos: {slave.Transform}");
        else if (Connection.ActiveChar.CurrentTarget is Character character)
            Connection.ActiveChar.SendDebugMessage($"ObjId: {targetId}, CharacterId: {character.Id}, \nPos: {character.Transform.ToFullString(true, true)}");
        else
            Connection.ActiveChar.SendDebugMessage($"ObjId: {targetId}, Pos: {Connection.ActiveChar.CurrentTarget.Transform}, {Connection.ActiveChar.CurrentTarget.Name}");
    }
}
