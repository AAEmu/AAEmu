using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.InstantGame;

public class InstantGameTeamMember : PacketMarshaler
{
    public Character Character { get; set; }
    public int Score { get; set; }
    public int Bonus { get; set; }
    public uint BonusSet { get; set; }
    public ushort Killstreak { get; set; }
    public ushort Kills { get; set; }
    public ushort Assists { get; set; }
    public ushort Deaths { get; set; }

    public InstantGameTeamResult Corps { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Character.Id);
        stream.Write(Character.Name);
        stream.Write(Score);
        stream.Write(Bonus);
        stream.Write(BonusSet);
        stream.Write(Killstreak);
        stream.Write(Kills);
        stream.Write(Assists);
        stream.Write(Deaths);
        return stream;
    }
}