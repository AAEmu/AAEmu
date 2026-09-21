using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNpcInteractionSkillListPacket : GamePacket
{
    /// <summary>
    /// The client keeps at most ten entries of this list; anything past that is dropped by its reader
    /// before the bar is built, so sending more would hide an action without saying so.
    /// </summary>
    private const int MaxSkillCount = 10;

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
        _skillList = [skillId];
    }

    public override PacketStream Write(PacketStream stream)
    {
        if (_skillList.Length > MaxSkillCount)
            Logger.Warn(
                "NPC {0} offers {1} interaction skills; the client only reads the first {2}",
                _npcObjId, _skillList.Length, MaxSkillCount);

        stream.WriteBc(_npcObjId);
        stream.WriteBc(_objId);
        stream.Write(_extraInfo);
        stream.Write(_pickId);
        stream.Write(_mouseButton);
        stream.Write(Math.Min(_skillList.Length, MaxSkillCount));
        foreach (var skillId in _skillList.Take(MaxSkillCount))
        {
            stream.Write(skillId);
        }

        stream.Write((byte)1); // interactable
        stream.Write(_modifierKeys);

        return stream;
    }
}
