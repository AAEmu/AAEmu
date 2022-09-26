using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTodayAssignmentPacket : GamePacket
    {
        public CSTodayAssignmentPacket() : base(CSOffsets.CSTodayAssignmentPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var realStep = stream.ReadInt32(); // realStep
            var request = stream.ReadByte();  // request
            _log.Debug($"CSTodayAssignmentPacket, realStep: {realStep}, request: {request}");

            //Connection.ActiveChar.Skills.Reset((AbilityType) abilityId);
            // данные из таблицы today_quest_group_quest
            //                                                                                     V--today_quest_group_id
            //                                                                                                V--quest_context_id
            //Connection.ActiveChar.BroadcastPacket(new SCTodayAssignmentChangedPacket(1u, 13u, 6907u, 2, true), true);
            //Connection.ActiveChar.BroadcastPacket(new SCTodayAssignmentChangedPacket(9u, 33u, 7205u, 2, true), true);
            //Connection.ActiveChar.BroadcastPacket(new SCTodayAssignmentChangedPacket(8u, 22u, 7240u, 2, true), true);
            //Connection.ActiveChar.BroadcastPacket(new SCTodayAssignmentItemSentPacket((uint)realStep, request), true);

        }
    }
}
