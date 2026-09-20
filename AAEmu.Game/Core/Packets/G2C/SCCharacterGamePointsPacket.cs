using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The character sheet's game-point table: fourteen slots. Slot 0 honor, slot 1 vocation,
/// slot 11 current-period leadership, slot 12 previous-period leadership (the Hero voter gate).
/// Unused slots are 0.
/// </summary>
public class SCCharacterGamePointsPacket(Character character) : GamePacket(SCOffsets.SCCharacterGamePointsPacket, 1)
{
    private const int SlotCount = 14;
    public const int HonorSlot = 0;
    public const int VocationSlot = 1;
    public const int CurrentLeadershipSlot = 11;
    public const int PeriodLeadershipSlot = 12;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(character.HonorPoint);
        stream.Write(character.VocationPoint);

        for (var i = 2; i < CurrentLeadershipSlot; i++)
            stream.Write(0);

        stream.Write(character.LeadershipPoint);
        stream.Write(character.LeadershipPeriodPoint);

        for (var i = PeriodLeadershipSlot + 1; i < SlotCount; i++)
            stream.Write(0);

        return stream;
    }
}
