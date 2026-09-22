using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Ucc;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Applies the UCC printed by one of the sender's own crest stamps to a batch of the sender's own items.
/// Authorization, stamp consumption and persistence are decided by <see cref="UccApplyService"/>;
/// this class only parses the body and reports the outcome.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSItemUccPacket() : GamePacket(CSOffsets.CSItemUccPacket, 1)
{
    /// <summary>Protocol bound on the target-id array; the client clamps its own list the same way.</summary>
    public const int MaxTargets = 34;

    /// <summary>The request's subject: the carrier item or the UCC it carries.</summary>
    public long SourceRef { get; private set; }

    public uint Num { get; private set; }

    public List<ulong> ItemIds { get; private set; } = [];

    public override void Read(PacketStream stream)
    {
        SourceRef = stream.ReadInt64();
        Num = stream.ReadUInt32();
        ItemIds = [];

        if (Num > MaxTargets)
        {
            Logger.Warn("ItemUcc apply: declared target count {0} exceeds the protocol bound of {1}; request ignored",
                Num, MaxTargets);
            return;
        }

        for (var i = 0; i < Num; i++)
            ItemIds.Add(stream.ReadUInt64());

        if (stream.Overran)
        {
            Logger.Warn("ItemUcc apply: body ended before all {0} target ids were read; request ignored", Num);
            return;
        }

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var result = UccApplyService.ForCharacter(character).ApplyToItems(SourceRef, ItemIds);
        switch (result.Outcome)
        {
            case UccApplyOutcome.Applied:
                foreach (var item in result.ChangedItems)
                    Connection.SendPacket(new SCItemUccDataChangedPacket(result.UccId, character.Id, item.Id));

                List<ItemTask> bits = [];
                foreach (var item in result.ChangedItems)
                    bits.Add(new ItemUpdateBits(item));
                Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.GainItemWithUcc, bits, []));
                break;

            case UccApplyOutcome.NoChange:
                // An unchanged repeat request is silent.
                break;

            default:
                Logger.Warn("ItemUcc apply rejected for character {0}: {1}", character.Id, result.Reason);
                character.SendErrorMessage(ErrorMessageType.Invalid);
                break;
        }
    }
}
