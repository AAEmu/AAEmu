using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCItemGradeEnchantResultPacket : GamePacket
    {
        // result :
        //  0 = break, 1 = downgrade, 2 = fail, 3 = success, 4 = great success 
        private readonly byte _result;
        private readonly Item _item;
        private readonly byte _gradeBefore;
        private readonly byte _gradeAfter;
        private readonly uint _breakRewardItemTemplateId;
        private readonly int _breakRewardItemCount;
        private readonly bool _breakRewardByMail;

        public SCItemGradeEnchantResultPacket(byte result, Item item, byte gradeBefore, byte gradeAfter, uint breakRewardItemTemplateId = 0, int breakRewardItemCount = 0, bool breakRewardByMail = false) : base(SCOffsets.SCItemGradeEnchantResultPacket, 5)
        {
            _result = result;
            _item = item;
            _gradeBefore = gradeBefore;
            _gradeAfter = gradeAfter;
            _breakRewardItemTemplateId = breakRewardItemTemplateId;
            _breakRewardItemCount = breakRewardItemCount;
            _breakRewardByMail = breakRewardByMail;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_result);
            stream.Write(_item);
            stream.Write(_gradeBefore);
            stream.Write(_gradeAfter);
            stream.Write(_breakRewardItemTemplateId);
            stream.Write(_breakRewardItemCount);
            stream.Write(_breakRewardByMail);
            return stream;
        }
    }
}
