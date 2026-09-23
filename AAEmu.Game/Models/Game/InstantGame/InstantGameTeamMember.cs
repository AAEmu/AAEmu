using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Models.Game.InstantGame;

/// <summary>
/// One participant's scoreboard line. It carries the player's identity as plain values so the
/// result can still be built (and the character object graph released) after the player has left
/// the match; <see cref="Present"/> distinguishes a member who stayed until the finish from one
/// who left mid-match, which is what the expedition history's Started/Finished status reads.
/// </summary>
public class InstantGameTeamMember : PacketMarshaler
{
    public uint CharacterId { get; set; }
    public string CharacterName { get; set; }
    /// <summary>Still inside the match; cleared when the member is released (leave/disconnect).</summary>
    public bool Present { get; set; } = true;

    public int Score { get; set; }
    public int Bonus { get; set; }
    public uint BonusSet { get; set; }
    public ushort Killstreak { get; set; }
    public ushort Kills { get; set; }
    public ushort Assists { get; set; }
    public ushort Deaths { get; set; }

    /// <summary>content: instances.expedition instance this member belongs to, 0 for none.</summary>
    public uint ExpeditionId { get; set; }

    public InstantGameTeamResult Corps { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(CharacterId);
        stream.Write(CharacterName);
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
