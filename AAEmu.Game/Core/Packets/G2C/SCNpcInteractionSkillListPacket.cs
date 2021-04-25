using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNpcInteractionSkillListPacket : GamePacket
    {
        private readonly uint _npcObjId;
        private readonly uint _objId;
        private readonly int _extraInfo;
        private readonly int _pickId;
        private readonly byte _mouseButton;
        private readonly int _modifierKeys;
        private readonly uint[] _skillList;
        
        public SCNpcInteractionSkillListPacket(uint npcObjId, uint objId, int extraInfo, int pickId, byte mouseButton, int modifierKeys, uint[] skillList) : base(SCOffsets.SCNpcInteractionSkillListPacket, 1)
        {
            _npcObjId = npcObjId;
            _objId = objId;
            _extraInfo = extraInfo;
            _pickId = pickId;
            _mouseButton = mouseButton;
            _modifierKeys = modifierKeys;
            _skillList = skillList;
        }

        public SCNpcInteractionSkillListPacket(uint npcObjId, uint objId, int extraInfo, int pickId, byte mouseButton, int modifierKeys, uint skillId) : base(SCOffsets.SCNpcInteractionSkillListPacket, 1)
        {
            _npcObjId = npcObjId;
            _objId = objId;
            _extraInfo = extraInfo;
            _pickId = pickId;
            _mouseButton = mouseButton;
            _modifierKeys = modifierKeys;
            _skillList = new uint[] { skillId };
        }
        
        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_npcObjId);
            stream.WriteBc(_objId);
            stream.Write(_extraInfo);
            stream.Write(_pickId);
            stream.Write(_mouseButton);
            stream.Write(_skillList.Length);
            foreach (var skillId in _skillList)
            {
                stream.Write(skillId);
            }
            stream.Write((byte)1); // interactable, doesn't seem to matter what we set here
            stream.Write(_modifierKeys);

            return stream;
        }
    }
}
