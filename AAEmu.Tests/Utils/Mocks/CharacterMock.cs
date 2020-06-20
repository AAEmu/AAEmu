using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Tests.Utils
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
