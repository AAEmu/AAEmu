using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks the craft order board for one page of orders: which actability group to list, how to sort
/// them, which page, and whether to keep only the orders this character can fill.
/// </summary>
public class CSSearchCraftOrderPacket() : GamePacket(CSOffsets.CSSearchCraftOrderPacket, 1)
{
    public int Type { get; private set; }
    public sbyte Kind { get; private set; }
    public sbyte Order { get; private set; }
    public uint Page { get; private set; }
    public bool Possible { get; private set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadInt32();
        Kind = stream.ReadSByte();
        Order = stream.ReadSByte();
        Page = stream.ReadUInt32();
        Possible = stream.ReadBoolean();

        if (Connection?.ActiveChar is { } character)
        {
            // Search is what the board's first tab sends. My List is a different store and
            // a Load does not fire INSERT, so push own rows before the search page lands.
            CraftOrderManager.Instance.SendOwnEntries(character);
            CraftOrderManager.Instance.SendSearch(character,
                new CraftOrderQuery((uint)Math.Max(0, Type), Kind, Order, Page, Possible));
        }
    }
}
