using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Models.Game.Char;

public enum PortalBookType : byte
{
    Return = 1,
    Private = 2
}

public readonly record struct FavoritePortalRef(byte PortalType, uint PortalId)
{
    public bool IsValid => PortalType is (byte)PortalBookType.Return or (byte)PortalBookType.Private;
}

public readonly record struct FavoritePortalChange(byte PortalType, uint PortalId, bool IsFavorite)
{
    public FavoritePortalRef Reference => new(PortalType, PortalId);
}

/// <summary>
/// Ordered favorite membership for one portal book. The stored order is insertion order: an
/// existing favorite keeps its slot, a new favorite is appended, and a removal closes the gap.
/// The portal book itself keeps its own order; this state only supplies membership flags.
/// Every mutation receives the content-resolved capacity explicitly; this state has no literal fallback.
/// </summary>
public sealed class FavoritePortalState
{
    private readonly FavoritePortalRef[] _favorites;

    public static FavoritePortalState Empty { get; } = new([]);

    private FavoritePortalState(FavoritePortalRef[] favorites)
    {
        _favorites = favorites;
    }

    public IReadOnlyList<FavoritePortalRef> Favorites => _favorites;

    public bool Contains(FavoritePortalRef favorite)
    {
        return _favorites.Contains(favorite);
    }

    public static bool TryCreate(
        IEnumerable<FavoritePortalRef> favorites,
        Func<FavoritePortalRef, bool> exists,
        out FavoritePortalState state)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(exists);

        var result = new List<FavoritePortalRef>();
        var seen = new HashSet<FavoritePortalRef>();
        foreach (var favorite in favorites)
        {
            if (!favorite.IsValid || !exists(favorite) || !seen.Add(favorite))
            {
                state = Empty;
                return false;
            }

            result.Add(favorite);
        }

        state = new(result.ToArray());
        return true;
    }

    public bool TryApply(
        IEnumerable<FavoritePortalChange> changes,
        Func<FavoritePortalRef, bool> exists,
        int maximumFavorites,
        out FavoritePortalState state)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(exists);
        if (maximumFavorites < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFavorites));

        var pending = changes.ToArray();
        if (pending.Length == 0)
        {
            state = this;
            return true;
        }

        var seen = new HashSet<FavoritePortalRef>();
        var result = new List<FavoritePortalRef>(_favorites);
        foreach (var change in pending)
        {
            var favorite = change.Reference;
            if (!favorite.IsValid || !seen.Add(favorite))
            {
                state = this;
                return false;
            }

            // Removing an entry that has already disappeared is idempotent. Adding one must
            // resolve against a portal the character actually owns.
            if (change.IsFavorite && !exists(favorite))
            {
                state = this;
                return false;
            }

            var index = result.IndexOf(favorite);
            if (change.IsFavorite)
            {
                if (index < 0)
                    result.Add(favorite);
            }
            else if (index >= 0)
            {
                result.RemoveAt(index);
            }
        }

        if (result.Count > maximumFavorites)
        {
            state = this;
            return false;
        }

        state = new(result.ToArray());
        return true;
    }

    public FavoritePortalState Without(FavoritePortalRef favorite)
    {
        if (!_favorites.Contains(favorite))
            return this;

        var result = _favorites.ToList();
        result.Remove(favorite);
        return new(result.ToArray());
    }

    public IReadOnlyList<Portal> BuildFlaggedPortals(IEnumerable<Portal> portals, PortalBookType portalType)
    {
        ArgumentNullException.ThrowIfNull(portals);

        var type = (byte)portalType;
        return portals.Select(portal =>
        {
            portal.IsFavorite = Contains(new FavoritePortalRef(type, portal.Id));
            return portal;
        }).ToArray();
    }
}
