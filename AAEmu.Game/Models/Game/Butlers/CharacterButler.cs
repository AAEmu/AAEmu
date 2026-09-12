using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>Persistent, character-owned farmhand state.</summary>
public sealed class CharacterButler(uint characterId)
{
    internal object SyncRoot { get; } = new();
    internal bool IsDeleted { get; set; }

    public uint CharacterId { get; } = characterId;
    public uint HouseId { get; internal set; }
    public string Name { get; internal set; } = string.Empty;
    public uint LaborPower { get; internal set; }
    public ushort LpChargedAmount { get; internal set; }
    public ushort RemainProductionCost { get; internal set; }

    internal CharacterButlerRecord Snapshot() =>
        new(CharacterId, HouseId, Name, LaborPower, LpChargedAmount, RemainProductionCost);

    internal void Apply(CharacterButlerRecord record)
    {
        HouseId = record.HouseId;
        Name = record.Name ?? string.Empty;
        LaborPower = record.LaborPower;
        LpChargedAmount = record.LpChargedAmount;
        RemainProductionCost = record.RemainProductionCost;
    }

    public void Save(MySql.Data.MySqlClient.MySqlConnection connection,
        MySql.Data.MySqlClient.MySqlTransaction transaction) =>
        ButlerManager.Instance.Save(this, connection, transaction);

    public static ButlerInfoWire ResetWire =>
        ButlerInfoWire.Empty(0, CharacterBlocked.LocalWorldId, string.Empty, 0, 0, 0, 0);

    internal ButlerInfoWire FreeWire =>
        ButlerInfoWire.Empty(0, CharacterBlocked.LocalWorldId, Name, 0, LaborPower, LpChargedAmount, 0);
}

public readonly record struct CharacterButlerRecord(
    uint CharacterId,
    uint HouseId,
    string Name,
    uint LaborPower,
    ushort LpChargedAmount,
    ushort RemainProductionCost);
