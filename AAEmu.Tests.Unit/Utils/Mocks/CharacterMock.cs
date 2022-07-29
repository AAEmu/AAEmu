using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Tests.Unit.Utils.Mocks
{
    public class CharacterMock : Character
    {
        public CharacterMock() : base(null)
        {
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            return;
        }
    }
}
