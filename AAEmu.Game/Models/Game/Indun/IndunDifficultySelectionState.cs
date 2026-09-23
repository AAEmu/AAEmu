namespace AAEmu.Game.Models.Game.Indun;

internal sealed class IndunDifficultySelectionState
{
    private uint? _selectorId;
    private Action _completion;

    internal bool IsReservedBy(uint characterId) => characterId != 0 && _selectorId == characterId;

    /// <summary>True while any character holds this selection open.</summary>
    internal bool IsHeld => _selectorId != null;

    internal bool Reserve(uint characterId, byte? selected, Action completion)
    {
        if (characterId == 0 || selected != null || (_selectorId != null && _selectorId != characterId))
            return false;
        _selectorId = characterId;
        _completion = completion;
        return true;
    }

    internal IndunDifficultySelection Select(uint characterId, byte? selected, byte proposed)
    {
        if (_selectorId != characterId)
            return IndunDifficultySelection.Unauthorized;
        if (selected == null)
            return IndunDifficultySelection.First;
        return selected == proposed ? IndunDifficultySelection.Same : IndunDifficultySelection.Conflict;
    }

    internal bool TryApply(
        uint characterId,
        ref byte? selected,
        byte proposed,
        bool valid,
        out Action completion,
        out bool changed)
    {
        completion = null;
        changed = false;
        if (!valid)
            return false;

        var selection = Select(characterId, selected, proposed);
        if (selection is IndunDifficultySelection.Unauthorized or IndunDifficultySelection.Conflict)
            return false;

        if (selection == IndunDifficultySelection.First)
        {
            selected = proposed;
            changed = true;
        }
        completion = TakeCompletion(characterId);
        return true;
    }

    internal void Release(uint characterId)
    {
        if (_selectorId == characterId)
        {
            _selectorId = null;
            _completion = null;
        }
    }

    internal Action TakeCompletion(uint characterId)
    {
        if (_selectorId != characterId)
            return null;
        var completion = _completion;
        _selectorId = null;
        _completion = null;
        return completion;
    }
}

internal enum IndunDifficultySelection
{
    First,
    Same,
    Conflict,
    Unauthorized
}
