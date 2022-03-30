using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAbilitySwappedPacket : GamePacket
    {
        private readonly Character _character;
        private readonly List<AbilityType> _oldAbilities;
        private readonly List<AbilityType> _newAbilities;

        public SCAbilitySwappedPacket(Character character, List<AbilityType> oldAbilities) : base(SCOffsets.SCAbilitySwappedPacket, 5)
        {
            _character = character;
            _oldAbilities = oldAbilities;
            _newAbilities = new List<AbilityType>(3);
            _newAbilities.Add(_character.Ability1);
            _newAbilities.Add(_character.Ability2);
            _newAbilities.Add(_character.Ability3);
        }

        public SCAbilitySwappedPacket(Character character, List<AbilityType> oldAbilities, List<AbilityType> newAbilities) : base(SCOffsets.SCAbilitySwappedPacket, 5)
        {
            _character = character;
            _oldAbilities = oldAbilities;
            _newAbilities = newAbilities;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_character.ObjId);
            for (int i = 0; i < 3; i++) {
                stream.Write((byte) _oldAbilities[i]);
                stream.Write((byte) _newAbilities[i]);
            }
            return stream;
        }
    }
}
