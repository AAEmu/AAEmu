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

            if (Connection.ActiveChar.CurrentTarget == null)
                return;
            if (Connection.ActiveChar.CurrentTarget is Npc npc)
                Connection.ActiveChar.SendMessage("ObjId: {0}, TemplateId: {1}, Ai: {2}, @{3}", targetId, npc.TemplateId, npc.Ai?.GetType().Name.Replace("AiCharacter", ""), npc.Ai?.GetCurrentBehavior()?.GetType().Name.Replace("Behavior", ""));
            else if (Connection.ActiveChar.CurrentTarget is House house)
                Connection.ActiveChar.SendMessage("ObjId: {0}, HouseId: {1}, Pos: {2}", targetId, house.Id, house.Transform.ToString());
            else if (Connection.ActiveChar.CurrentTarget is Transfer transfer)
                Connection.ActiveChar.SendMessage("ObjId: {0}, Transfer TemplateId: {1}\nPos: {2}", targetId, transfer.TemplateId, transfer.Transform.ToString());
            else if (Connection.ActiveChar.CurrentTarget is Character character)
                Connection.ActiveChar.SendMessage("ObjId: {0}, CharacterId: {1}, \nPos: {2}", targetId, character.Id, character.Transform.ToFullString(true,true));
            else
                Connection.ActiveChar.SendMessage("ObjId: {0}, Pos: {1}, {2}", targetId, Connection.ActiveChar.CurrentTarget.Transform.ToString(),Connection.ActiveChar.CurrentTarget.Name);
        }
    }
}
