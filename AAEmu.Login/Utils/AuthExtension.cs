using System;
using System.Collections.Immutable;
using System.Linq;
using AAEmu.Login.Models;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Login.Utils
{
    public static class AuthExtension
    {
        public static ImmutableList<GameServer> GetServers(this AuthContext context)
        {
            return context.GameServers
                .AsNoTracking()
                .ToImmutableList();
        }

        public static Account GetAccount(this AuthContext context, ulong id)
        {
            return context.Accounts.AsEnumerable().First(a => a.Id == id);
        }

        public static Account GetAccount(this AuthContext context, string username)
        {
            return context.Accounts.AsEnumerable().First(a =>
                string.Equals(a.Username, username, StringComparison.CurrentCultureIgnoreCase));
        }
    }
}
