using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Greater-dungeon difficulty pick from the H-window. Client waits for
/// <see cref="SCSelectedInstanceDifficultPacket"/> before Enter is enabled.
/// </summary>
/// <remarks>Wire: u8 difficult, u8 invalidCheck.</remarks>
public class CSSelectInstanceDifficultPacket() : GamePacket(CSOffsets.CSSelectInstanceDifficultPacket, 1)
{
    public byte Difficult { get; private set; }
    public byte InvalidCheck { get; private set; }

    public override void Read(PacketStream stream)
    {
        Difficult = stream.ReadByte();
        InvalidCheck = stream.ReadByte();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        Logger.Debug(
            "CSSelectInstanceDifficult char={0} difficult={1} invalidCheck={2}",
            character.Name, Difficult, InvalidCheck);

        // A difficulty-doodad response may mutate only the copy whose permission-checked interaction
        // reserved this character. An ordinary H-window pick remains pending for the next copy.
        var dungeon = character.ParentWorld?.DungeonInstance;
        var selectedForReply = Difficult;
        if (dungeon?.HasDifficultySelection(character) == true)
        {
            if (dungeon.SetDifficult(character, Difficult))
                selectedForReply = dungeon.Difficult ?? Difficult;
            else
                selectedForReply = dungeon.Difficult ?? 0;
        }
        else
        {
            IndunManager.Instance.RememberSelectedDifficult(character.Id, Difficult);
        }

        character.SendPacket(new SCSelectedInstanceDifficultPacket((sbyte)selectedForReply, showUi: true));
    }
}
