using AAEmu.Web.Data;
using AAEmu.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AAEmu.Web.Pages.Characters;

public class IndexModel(
    IGameRepository gameRepository,
    IUserRepository userRepository,
    ILogger<IndexModel> logger) : PageModel
{
    private const int PageSize = 25;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "deleted")]
    public bool IncludeDeleted { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<CharacterSummary> Characters { get; private set; } = [];
    public int TotalCount { get; private set; }
    public bool DatabaseUnavailable { get; private set; }

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public int FirstRowOnPage => TotalCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
    public int LastRowOnPage => Math.Min(PageNumber * PageSize, TotalCount);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (PageNumber < 1)
            PageNumber = 1;

        try
        {
            (Characters, TotalCount) = await gameRepository.SearchCharactersAsync(
                Search, IncludeDeleted, PageNumber, PageSize, cancellationToken);

            if (Characters.Count == 0 && TotalCount > 0 && PageNumber > TotalPages)
            {
                PageNumber = TotalPages;
                (Characters, TotalCount) = await gameRepository.SearchCharactersAsync(
                    Search, IncludeDeleted, PageNumber, PageSize, cancellationToken);
            }

            await ResolveUsernamesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not load characters from the game database.");
            DatabaseUnavailable = true;
        }
    }

    /// <summary>
    /// Fills in the owning account name for the characters on this page. The two databases may live
    /// on different servers, so this is a second query rather than a SQL join.
    /// </summary>
    private async Task ResolveUsernamesAsync(CancellationToken cancellationToken)
    {
        if (Characters.Count == 0)
            return;

        var accountIds = Characters.Select(c => c.AccountId).Distinct().ToList();

        try
        {
            var usernames = await userRepository.GetUsernamesAsync(accountIds, cancellationToken);
            foreach (var character in Characters)
            {
                character.Username = usernames.GetValueOrDefault(character.AccountId);
            }
        }
        catch (Exception e)
        {
            // Non-fatal: the table falls back to showing the raw account id.
            logger.LogWarning(e, "Could not resolve usernames for the character list.");
        }
    }
}
