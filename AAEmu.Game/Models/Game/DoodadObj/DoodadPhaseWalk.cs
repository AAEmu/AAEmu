namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// Visit set for one <see cref="Doodad.DoChangePhase"/> walk.
/// Phase graphs may loop (town mailbox owl sit → fly → empty → land → sit).
/// Cycle detection applies only inside that walk; a later timer hop is a new walk
/// and may revisit a phase.
/// </summary>
public static class DoodadPhaseWalk
{
    public static void Begin(ICollection<uint> visited)
    {
        visited.Clear();
    }

    /// <summary>
    /// One <see cref="Doodad.DoChangePhase"/> walk: clear, run the hop, clear again
    /// so the next timer hop can revisit a looping phase.
    /// </summary>
    public static T Run<T>(ICollection<uint> visited, Func<T> body)
    {
        Begin(visited);
        try
        {
            return body();
        }
        finally
        {
            Begin(visited);
        }
    }

    /// <returns>
    /// <c>false</c> if <paramref name="phase"/> was already visited on this walk
    /// (caller must stop; the set is cleared).
    /// </returns>
    public static bool TryVisit(ICollection<uint> visited, uint phase)
    {
        if (visited.Contains(phase))
        {
            visited.Clear();
            return false;
        }

        visited.Add(phase);
        return true;
    }
}
