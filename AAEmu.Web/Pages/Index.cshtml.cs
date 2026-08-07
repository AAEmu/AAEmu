using AAEmu.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AAEmu.Web.Pages;

public class IndexModel(IUserRepository userRepository, ILogger<IndexModel> logger) : PageModel
{
    public int AccountCount { get; private set; }

    /// <summary>
    /// True when the login database could not be queried. The page still renders, minus the count.
    /// </summary>
    public bool DatabaseUnavailable { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            AccountCount = await userRepository.GetAccountCountAsync(cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not load the account count from the login database.");
            DatabaseUnavailable = true;
        }
    }
}
