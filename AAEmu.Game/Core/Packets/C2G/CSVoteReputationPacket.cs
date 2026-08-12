using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// A peer rating - the button on the target unit frame in x2ui/components/reputation.lua.
/// </summary>
/// <remarks>
/// The client sends +1 for "Rate" and -1 for "Don't Rate" (X2Hero:VoteReputation(1) / (-1)), and hides
/// its own button immediately without waiting for a reply, so a refusal has to be reported rather than
/// signalled by silence.
///
/// TypeValue is the target. It arrives as a u64 and is resolved as an object id first, since the button
/// rates whoever is targeted, falling back to a character id.
///
/// Add is declared u32 by the client's serializer but carries a signed delta, so -1 comes across as
/// 0xFFFFFFFF; it is read back through int and clamped to a sign in ReputationManager.
/// </remarks>
public class CSVoteReputationPacket() : GamePacket(CSOffsets.CSVoteReputationPacket, 1)
{
    public ulong TypeValue { get; private set; }
    public uint Add { get; private set; }
    public bool ByGm { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt64();
        Add = stream.ReadUInt32();
        ByGm = stream.ReadBoolean();

        var voter = Connection?.ActiveChar;
        if (voter == null)
            return;

        var target = WorldManager.Instance.GetCharacterByObjId((uint)TypeValue)
                     ?? WorldManager.Instance.GetCharacterById((uint)TypeValue);

        if (target == null)
        {
            Logger.Warn("VoteReputation from {0}: no character for id {1}", voter.Name, TypeValue);
            return;
        }

        var result = ReputationManager.Instance.Vote(voter, target, (int)Add);
        if (result != ReputationVoteResult.Ok)
            voter.SendMessage(ReputationManager.Explain(result));
    }
}
