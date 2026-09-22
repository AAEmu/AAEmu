using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Ucc;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Applies or removes a UCC on one of a house's user-content slots. Authorization, crest stamp
/// consumption and slot persistence are decided by <see cref="UccApplyService"/>;
/// this class only parses the body and reports the outcome.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value. The client omits the placement pair whenever it sends no
/// source item, so those two fields are conditional on a non-zero item id.
/// </remarks>
public class CSHousingUccApplyPacket() : GamePacket(CSOffsets.CSHousingUccApplyPacket, 1)
{
    /// <summary>The crest item the sender applies; zero when the request carries no source item.</summary>
    public long ItemId { get; private set; }

    public sbyte TypeValue { get; private set; }
    public sbyte Index { get; private set; }
    public short Tl { get; private set; }
    public uint Pos { get; private set; }
    public bool IsRemove { get; private set; }

    /// <summary>True when the body carried the placement pair (it is omitted without a source item).</summary>
    public bool HasPlacement { get; private set; }

    public override void Read(PacketStream stream)
    {
        ItemId = stream.ReadInt64();
        HasPlacement = ItemId != 0;
        if (HasPlacement)
        {
            TypeValue = stream.ReadSByte();
            Index = stream.ReadSByte();
        }

        Tl = stream.ReadInt16();
        Pos = stream.ReadUInt32();
        IsRemove = stream.ReadBoolean();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var result = UccApplyService.ForCharacter(character)
            .ApplyToHousing(character, (ushort)Tl, ItemId, TypeValue, Index, (int)Pos, HasPlacement, IsRemove);

        switch (result.Outcome)
        {
            case UccApplyOutcome.Applied:
            {
                // Everyone who has the house loaded sees the crest change, not only the applier.
                var update = new SCUpdateHousingUccPacket(Tl, result.UccId, result.UccKind, result.UccPos, false);
                var house = HousingManager.Instance.GetHouseByTlId((ushort)Tl);
                List<Character> viewers = house != null ? WorldManager.GetAround<Character>(house) : [];
                foreach (var recipient in UccApplyService.HousingUpdateRecipients(character, viewers))
                    recipient.SendPacket(update);
                break;
            }

            case UccApplyOutcome.NoChange:
                // An unchanged repeat is silent.
                break;

            default:
                Logger.Warn("HousingUccApply rejected for character {0}: {1}", character.Id, result.Reason);
                character.SendErrorMessage(ErrorMessageType.Invalid);
                break;
        }
    }
}
