using Microsoft.EntityFrameworkCore;

namespace AAEmu.Login.Models.Database;

public partial class LoginDbContext(DbContextOptions<LoginDbContext> options) : DbContext(options)
{
    public virtual DbSet<GameServer> GameServers { get; set; }

    public virtual DbSet<Update> Updates { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_0900_ai_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<GameServer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("game_servers", tb => tb.HasComment("Server list"))
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.Property(e => e.Id)
                .HasConversion<GameServerIdConverter>()
                .HasColumnName("id");
            entity.Property(e => e.Hidden)
                .IsRequired()
                .HasColumnName("hidden");
            entity.Property(e => e.Host)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("host");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("name");
            entity.Property(e => e.Port).HasColumnName("port");
        });

        modelBuilder.Entity<Update>(entity =>
        {
            entity.HasKey(e => e.ScriptName).HasName("PRIMARY");

            entity
                .ToTable("updates", tb => tb.HasComment("Table containing SQL update script information"))
                .UseCollation("utf8mb4_general_ci");

            entity.Property(e => e.ScriptName).HasColumnName("script_name");
            entity.Property(e => e.InstallDate)
                .HasColumnType("datetime")
                .HasColumnName("install_date");
            entity.Property(e => e.Installed).HasColumnName("installed");
            entity.Property(e => e.LastError)
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("last_error");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("users", tb => tb.HasComment("Account login information"))
                .HasCharSet("utf8mb3")
                .UseCollation("utf8mb3_general_ci");

            entity.HasIndex(e => e.Username, "username");

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasConversion<AccountIdConverter>()
                .HasColumnName("id");
            entity.Property(e => e.BanReason).HasColumnName("ban_reason");
            entity.Property(e => e.Banned).HasColumnName("banned");
            entity.Property(e => e.CreatedAt)
                .HasConversion<UnixMillisecondsDateTimeConverter>()
                .HasColumnType("bigint unsigned")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("email");
            entity.Property(e => e.LastIp)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("last_ip");
            entity.Property(e => e.LastLogin)
                .HasConversion<UnixMillisecondsDateTimeConverter>()
                .HasColumnType("bigint unsigned")
                .HasColumnName("last_login");
            entity.Property(e => e.Password)
                .IsRequired()
                .HasComment("Hashed password of the user")
                .HasColumnType("text")
                .HasColumnName("password");
            entity.Property(e => e.UpdatedAt)
                .HasConversion<UnixMillisecondsDateTimeConverter>()
                .HasColumnType("bigint unsigned")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(32)
                .HasColumnName("username");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
