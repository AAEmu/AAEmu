namespace AAEmu.Game.Models.Game.Char;

public sealed class AbilitySetInfo
{
    public byte[] SavedAbilitySets { get; } = new byte[3];
    public byte[] SavedHighAbilitySets { get; } = new byte[3];
    public List<uint> Types { get; } = [];
    public List<uint> ExtraTypes { get; } = [];

    public void CopyFrom(Character character)
    {
        if (character == null)
            return;

        SavedAbilitySets[0] = (byte)character.Ability1;
        SavedAbilitySets[1] = (byte)character.Ability2;
        SavedAbilitySets[2] = (byte)character.Ability3;

        SavedHighAbilitySets[0] = (byte)character.HighAbility1;
        SavedHighAbilitySets[1] = (byte)character.HighAbility2;
        SavedHighAbilitySets[2] = (byte)character.HighAbility3;
    }

    public void Clear()
    {
        Array.Clear(SavedAbilitySets, 0, SavedAbilitySets.Length);
        Array.Clear(SavedHighAbilitySets, 0, SavedHighAbilitySets.Length);
        Types.Clear();
        ExtraTypes.Clear();
    }
}
