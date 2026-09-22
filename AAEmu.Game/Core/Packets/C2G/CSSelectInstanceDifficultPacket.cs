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

        // Inside a copy the pick is that copy's difficulty (zone group 146 opens its lock on it through
        // indun_event_difficult_changeds); outside, it waits for the next copy this character enters.
        if (character.ParentWorld?.DungeonInstance is { } dungeon)
            dungeon.SetDifficult(Difficult);
        else
            IndunManager.Instance.RememberSelectedDifficult(character.Id, Difficult);

        character.SendPacket(new SCSelectedInstanceDifficultPacket((sbyte)Difficult, showUi: true));
    }
}
