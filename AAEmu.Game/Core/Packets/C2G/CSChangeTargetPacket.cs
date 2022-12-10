using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
namespace AAEmu.Game.Core.Packets.C2G
{
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
                Connection.ActiveChar.SendMessage("Selected nothing");
                return;
            }
            if (Connection.ActiveChar.CurrentTarget == null)
            {
                Connection.ActiveChar.SendMessage("ObjId: {0}, TemplateId: not found in Db", targetId);
                return;
            }
            if (Connection.ActiveChar.CurrentTarget is Portal portal)
                Connection.ActiveChar.SendMessage("ObjId: {0}, TemplateId: {1}\nPos: {2}", targetId, portal.TemplateId, portal.Transform.ToString());
            else if (Connection.ActiveChar.CurrentTarget is Npc npc)
            {
                var spawnerId = npc.Spawner != null && npc.Spawner.NpcSpawnerIds.Count > 0
                    ? npc.Spawner.NpcSpawnerIds[0]
                    : 0u;

                Connection.ActiveChar.SendMessage("ObjId: {0}, TemplateId: {1}, Ai: {2}, @{3} SpawnerId: {4}\nPos: {5}", targetId, npc.TemplateId, npc.Ai?.GetType().Name.Replace("AiCharacter", ""), npc.Ai?.GetCurrentBehavior()?.GetType().Name.Replace("Behavior", ""), spawnerId, npc.Transform.ToString());
            }
            else if (Connection.ActiveChar.CurrentTarget is House house)
                Connection.ActiveChar.SendMessage("ObjId: {0}, HouseId: {1}, Pos: {2}", targetId, house.Id, house.Transform.ToString());
            else if (Connection.ActiveChar.CurrentTarget is Transfer transfer)
                Connection.ActiveChar.SendMessage("ObjId: {0}, Transfer TemplateId: {1}\nPos: {2}", targetId, transfer.TemplateId, transfer.Transform.ToString());
            else if (Connection.ActiveChar.CurrentTarget is Character character)
                Connection.ActiveChar.SendMessage("ObjId: {0}, CharacterId: {1}, \nPos: {2}", targetId, character.Id, character.Transform.ToFullString(true, true));
            else
                Connection.ActiveChar.SendMessage("ObjId: {0}, Pos: {1}, {2}", targetId, Connection.ActiveChar.CurrentTarget.Transform.ToString(), Connection.ActiveChar.CurrentTarget.Name);
        }
    }
}
