using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace AAEmu.DB.Login
{
    public partial class LoginContext : DbContext
    {
        public LoginContext(string ConnectionString)
        {
            this.ConnectionString = ConnectionString;
        }

        public LoginContext(DbContextOptions<LoginContext> options)
            : base(options)
        {

        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseMySQL(ConnectionString);
            }
        }

        public void ThrowIfNotExists()
        {
            if (!(this.Database.GetService<IDatabaseCreator>() as RelationalDatabaseCreator).Exists())
                throw new Exception("Login db does not exist");
        }

        private readonly string ConnectionString;

        public virtual DbSet<GameServers> GameServers { get; set; }
        public virtual DbSet<Users> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GameServers>(entity =>
            {
                entity.ToTable("game_servers", "aaemu_login");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("tinyint(3) unsigned");

                entity.Property(e => e.Hidden)
                    .HasColumnName("hidden")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValueSql("1");

                entity.Property(e => e.Host)
                    .IsRequired()
                    .HasColumnName("host")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .IsUnicode(false);

                entity.Property(e => e.Port)
                    .HasColumnName("port")
                    .HasColumnType("int(11)");
            });

            modelBuilder.Entity<Users>(entity =>
            {
                entity.ToTable("users", "aaemu_login");

                entity.HasIndex(e => e.Username)
                    .HasName("username");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Email)
                    .IsRequired()
                    .HasColumnName("email")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.LastIp)
                    .IsRequired()
                    .HasColumnName("last_ip")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.LastLogin)
                    .HasColumnName("last_login")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Password)
                    .IsRequired()
                    .HasColumnName("password")
                    .IsUnicode(false);

                entity.Property(e => e.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasColumnType("bigint(20) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Username)
                    .IsRequired()
                    .HasColumnName("username")
                    .HasMaxLength(32)
                    .IsUnicode(false);
            });
        }
    }
}
