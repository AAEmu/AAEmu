using Microsoft.EntityFrameworkCore;

namespace AAEmu.Login.Models
{
    public class AuthContext : DbContext
    {
        public AuthContext()
        {
        }

        public AuthContext(DbContextOptions<AuthContext> options)
            : base(options)
        {
        }

        public DbSet<Account> Accounts { get; set; }
        public DbSet<GameServer> GameServers { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured)
                return;
            optionsBuilder
                .UseNpgsql(
                    $"Host=localhost;Port=5432;Database=Auth;Username=postgres;Password=postgres"); //For Migration Generation
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.UseSerialColumns();
    }
}
