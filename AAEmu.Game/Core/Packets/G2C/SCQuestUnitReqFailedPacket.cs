using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the client which unit requirement refused a quest accept.
/// </summary>
/// <remarks>
/// Body serializer: <c>type</c> (the quest context id) as a 4-byte int,
/// <c>bc</c> as the three-byte object id, then the result through the skill-cast tail: a flag
/// byte followed by <c>c</c> (result u8), <c>e</c> (u16), <c>p</c> (u32) and <c>d</c> (the
/// display gate), each written only when it differs from its default of 0, 0, 0 and true.
/// The handler hands it to a resolver, which acts only when <c>bc</c> is the local player:
/// it formats the result through the skill-result display path (which drops results above
/// 0x49 when the gate is false) and raises QUEST_QUICK_CLOSE_EVENT with the quest id, which
/// quest_context_directing.lua answers by ending directing mode for that quest.
/// </remarks>
public class SCQuestUnitReqFailedPacket(uint questId, uint objId, UnitReqsValidationResult result)
    : GamePacket(SCOffsets.SCQuestUnitReqFailedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(questId);
        stream.WriteBc(objId);
        stream.WriteSkillCastTail(
            (byte)QuestStartRequirementRules.WireResult(result),
            result.ResultUShort,
            result.ResultUInt,
            result.DisplayMessage);
        return stream;
    }

    public override string Verbose()
    {
        return $" - Quest {questId}, ObjId {objId}, Result {QuestStartRequirementRules.WireResult(result)}, " +
               $"UShort {result.ResultUShort}, UInt {result.ResultUInt}, Display {result.DisplayMessage}";
    }
}
