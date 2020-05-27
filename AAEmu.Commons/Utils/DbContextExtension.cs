using Microsoft.EntityFrameworkCore;

namespace AAEmu.Commons.Utils
{
    public static class DbContextExtension
    {
        public static bool CanConnect(this DbContext context)
        {
            var canConnect = context.Database.CanConnect();
            if (canConnect)
                context.Database.EnsureCreated();
            return canConnect;
        }
    }
}
