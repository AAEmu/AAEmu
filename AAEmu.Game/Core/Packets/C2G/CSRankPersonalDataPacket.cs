using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for the character's own lines in the rankings, which is what the ranking window opens with.
/// </summary>
/// <remarks>
/// The packet has no body. Every parameterless C2S type folds onto that one function, so a shared
/// serializer here is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSRankPersonalDataPacket() : GamePacket(CSOffsets.CSRankPersonalDataPacket, 1)
{
    public override void Read(PacketStream stream)
    {
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var entries = new List<RankingEntryLine>();

        // The boards measured by the character's whole gear score, which the World already computes for
        // instance entry and squad limits.
        foreach (var board in RankingGameData.Instance.BoardsMeasuring(RankingGameData.GearScoreDetailType))
            entries.Add(Line(character, board.Id, character.GearScore));

        // The item boards measure one equipped weapon each. A character wearing nothing of a board's kind
        // gets no line for it rather than a zero.
        var weapons = new List<(ulong ItemId, byte SlotTypeId, int Score)>();
        foreach (var item in character.Inventory?.Equipment?.Items ?? [])
        {
            if (item is not EquipItem equip || equip.Template is not WeaponTemplate weapon)
                continue;

            var holdable = ItemManager.Instance.GetHoldable(weapon.HoldableTemplate?.Id ?? 0);
            if (holdable == null)
                continue;

            weapons.Add((equip.Id, (byte)holdable.SlotTypeId, (int)Math.Round(GearScoreCalculator.EvaluateItem(equip))));
        }

        foreach (var board in RankingGameData.Instance.BoardsMeasuring(RankingGameData.ItemDetailType))
        {
            var slots = RankingRules.ItemBoardSlots(board.Id);
            if (slots == null)
                continue;

            var best = RankingRules.BestItem(weapons, slots);
            if (best != null)
                entries.Add(Line(character, board.Id, best.Value.Score));
        }

        character.SendPacket(new SCRankPersonalDataPacket(0, entries));
        Logger.Info("Rankings: sent {0} personal line(s) to {1}", entries.Count, character.Name);
    }

    /// <summary>
    /// One board's line for a character. The second value is left at zero: the boards' secondary figure
    /// (the item's own score, the period total) has no source in the World yet, and a zero says so.
    /// </summary>
    private static RankingEntryLine Line(Character character, uint boardId, long score)
    {
        return new RankingEntryLine(boardId, new RankingEntry
        {
            V1 = score,
            V2 = 0,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            WorldId = (byte)character.Transform.WorldId,
            Id = character.Id,
            AccountId = character.AccountId,
            Type = boardId,
            PrivacyStatus = 0,
            IsAllocated = true
        });
    }
}
