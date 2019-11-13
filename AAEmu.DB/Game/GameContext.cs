using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace AAEmu.DB.Game
{
    public partial class GameContext : DbContext
    {
        public GameContext(string ConnectionString)
        {
            this.ConnectionString = ConnectionString;
        }

        public GameContext(DbContextOptions<GameContext> options)
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
                throw new Exception("Game db does not exist");
        }
        private readonly string ConnectionString;
        public virtual DbSet<Abilities> Abilities { get; set; }
        public virtual DbSet<Actabilities> Actabilities { get; set; }
        public virtual DbSet<Appellations> Appellations { get; set; }
        public virtual DbSet<Blocked> Blocked { get; set; }
        public virtual DbSet<CashShopItem> CashShopItem { get; set; }
        public virtual DbSet<Characters> Characters { get; set; }
        public virtual DbSet<CompletedQuests> CompletedQuests { get; set; }
        public virtual DbSet<ExpeditionMembers> ExpeditionMembers { get; set; }
        public virtual DbSet<ExpeditionRolePolicies> ExpeditionRolePolicies { get; set; }
        public virtual DbSet<Expeditions> Expeditions { get; set; }
        public virtual DbSet<FamilyMembers> FamilyMembers { get; set; }
        public virtual DbSet<Friends> Friends { get; set; }
        public virtual DbSet<Housings> Housings { get; set; }
        public virtual DbSet<Items> Items { get; set; }
        public virtual DbSet<Mates> Mates { get; set; }
        public virtual DbSet<Options> Options { get; set; }
        public virtual DbSet<PortalBookCoords> PortalBookCoords { get; set; }
        public virtual DbSet<PortalVisitedDistrict> PortalVisitedDistrict { get; set; }
        public virtual DbSet<Quests> Quests { get; set; }
        public virtual DbSet<Skills> Skills { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Abilities>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("abilities", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("tinyint(3) unsigned");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Exp)
                    .HasColumnName("exp")
                    .HasColumnType("int(11)");
            });

            modelBuilder.Entity<Actabilities>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("actabilities", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Point)
                    .HasColumnName("point")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Step)
                    .HasColumnName("step")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");
            });

            modelBuilder.Entity<Appellations>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("appellations", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Active)
                    .HasColumnName("active")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValueSql("0");
            });

            modelBuilder.Entity<Blocked>(entity =>
            {
                entity.HasKey(e => new { e.Owner, e.BlockedId });

                entity.ToTable("blocked", "aaemu_game");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11)");

                entity.Property(e => e.BlockedId)
                    .HasColumnName("blocked_id")
                    .HasColumnType("int(11)");
            });

            modelBuilder.Entity<CashShopItem>(entity =>
            {
                entity.ToTable("cash_shop_item", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.BonusType)
                    .HasColumnName("bonus_type")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.BounsCount)
                    .HasColumnName("bouns_count")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.BuyCount)
                    .HasColumnName("buy_count")
                    .HasColumnType("smallint(5) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.BuyId)
                    .HasColumnName("buy_id")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.BuyType)
                    .HasColumnName("buy_type")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.CashName)
                    .IsRequired()
                    .HasColumnName("cash_name")
                    .HasMaxLength(255)
                    .IsUnicode(false);

                entity.Property(e => e.CmdUi)
                    .HasColumnName("cmd_ui")
                    .HasColumnType("tinyint(1) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.DefaultFlag)
                    .HasColumnName("default_flag")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.DisPrice)
                    .HasColumnName("dis_price")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.EndDate)
                    .HasColumnName("end_date")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.EventDate)
                    .HasColumnName("event_date")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.EventType)
                    .HasColumnName("event_type")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.IsHidden)
                    .HasColumnName("is_hidden")
                    .HasColumnType("tinyint(1) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.IsSell)
                    .HasColumnName("is_sell")
                    .HasColumnType("tinyint(1) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.ItemCount)
                    .HasColumnName("item_count")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("1");

                entity.Property(e => e.ItemTemplateId)
                    .HasColumnName("item_template_id")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LevelMax)
                    .HasColumnName("level_max")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LevelMin)
                    .HasColumnName("level_min")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LimitType)
                    .HasColumnName("limit_type")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.MainTab)
                    .HasColumnName("main_tab")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("1");

                entity.Property(e => e.Price)
                    .HasColumnName("price")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Remain)
                    .HasColumnName("remain")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.SelectType)
                    .HasColumnName("select_type")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.StartDate)
                    .HasColumnName("start_date")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.SubTab)
                    .HasColumnName("sub_tab")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("1");

                entity.Property(e => e.Type)
                    .HasColumnName("type")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.UniqId)
                    .HasColumnName("uniq_id")
                    .HasColumnType("int(10) unsigned")
                    .HasDefaultValueSql("0");
            });

            modelBuilder.Entity<Characters>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.AccountId });

                entity.ToTable("characters", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.AccountId)
                    .HasColumnName("account_id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Ability1)
                    .HasColumnName("ability1")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.Ability2)
                    .HasColumnName("ability2")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.Ability3)
                    .HasColumnName("ability3")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.AccessLevel)
                    .HasColumnName("access_level")
                    .HasColumnType("int(3) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.AutoUseAapoint)
                    .HasColumnName("auto_use_aapoint")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.BmPoint)
                    .HasColumnName("bm_point")
                    .HasColumnType("int(11)");

                entity.Property(e => e.ConsumedLp)
                    .HasColumnName("consumed_lp")
                    .HasColumnType("int(11)");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.CrimePoint)
                    .HasColumnName("crime_point")
                    .HasColumnType("int(11)");

                entity.Property(e => e.CrimeRecord)
                    .HasColumnName("crime_record")
                    .HasColumnType("int(11)");

                entity.Property(e => e.DeadCount)
                    .HasColumnName("dead_count")
                    .HasColumnType("mediumint(8) unsigned");

                entity.Property(e => e.DeadTime)
                    .HasColumnName("dead_time")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.DeleteRequestTime)
                    .HasColumnName("delete_request_time")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.DeleteTime)
                    .HasColumnName("delete_time")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.Deleted)
                    .HasColumnName("deleted")
                    .HasColumnType("int(11)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.ExpandedExpert)
                    .HasColumnName("expanded_expert")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.ExpeditionId)
                    .HasColumnName("expedition_id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Expirience)
                    .HasColumnName("expirience")
                    .HasColumnType("int(11)");

                entity.Property(e => e.FactionId)
                    .HasColumnName("faction_id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.FactionName)
                    .IsRequired()
                    .HasColumnName("faction_name")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.Family)
                    .HasColumnName("family")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Gender)
                    .HasColumnName("gender")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.Gift)
                    .HasColumnName("gift")
                    .HasColumnType("int(11)");

                entity.Property(e => e.HonorPoint)
                    .HasColumnName("honor_point")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Hp)
                    .HasColumnName("hp")
                    .HasColumnType("int(11)");

                entity.Property(e => e.LaborPower)
                    .HasColumnName("labor_power")
                    .HasColumnType("mediumint(9)");

                entity.Property(e => e.LaborPowerModified)
                    .HasColumnName("labor_power_modified")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.LeaveTime)
                    .HasColumnName("leave_time")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.Level)
                    .HasColumnName("level")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.Money)
                    .HasColumnName("money")
                    .HasColumnType("bigint(20)");

                entity.Property(e => e.Money2)
                    .HasColumnName("money2")
                    .HasColumnType("bigint(20)");

                entity.Property(e => e.Mp)
                    .HasColumnName("mp")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.NumBankSlot)
                    .HasColumnName("num_bank_slot")
                    .HasColumnType("smallint(5) unsigned")
                    .HasDefaultValueSql("50");

                entity.Property(e => e.NumInvSlot)
                    .HasColumnName("num_inv_slot")
                    .HasColumnType("tinyint(3) unsigned")
                    .HasDefaultValueSql("50");

                entity.Property(e => e.Point)
                    .HasColumnName("point")
                    .HasColumnType("int(11)");

                entity.Property(e => e.PrevPoint)
                    .HasColumnName("prev_point")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Race)
                    .HasColumnName("race")
                    .HasColumnType("tinyint(2)");

                entity.Property(e => e.RecoverableExp)
                    .HasColumnName("recoverable_exp")
                    .HasColumnType("int(11)");

                entity.Property(e => e.RezPenaltyDuration)
                    .HasColumnName("rez_penalty_duration")
                    .HasColumnType("int(11)");

                entity.Property(e => e.RezTime)
                    .HasColumnName("rez_time")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.RezWaitDuration)
                    .HasColumnName("rez_wait_duration")
                    .HasColumnType("int(11)");

                entity.Property(e => e.RotationX)
                    .HasColumnName("rotation_x")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.RotationY)
                    .HasColumnName("rotation_y")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.RotationZ)
                    .HasColumnName("rotation_z")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.Slots)
                    .IsRequired()
                    .HasColumnName("slots")
                    .HasColumnType("blob");

                entity.Property(e => e.TransferRequestTime)
                    .HasColumnName("transfer_request_time")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.UnitModelParams)
                    .IsRequired()
                    .HasColumnName("unit_model_params")
                    .HasColumnType("blob");

                entity.Property(e => e.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.VocationPoint)
                    .HasColumnName("vocation_point")
                    .HasColumnType("int(11)");

                entity.Property(e => e.WorldId)
                    .HasColumnName("world_id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.X).HasColumnName("x");

                entity.Property(e => e.Y).HasColumnName("y");

                entity.Property(e => e.Z).HasColumnName("z");

                entity.Property(e => e.ZoneId)
                    .HasColumnName("zone_id")
                    .HasColumnType("int(11) unsigned");
            });

            modelBuilder.Entity<CompletedQuests>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("completed_quests", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Data)
                    .IsRequired()
                    .HasColumnName("data")
                    .HasColumnType("tinyblob");
            });

            modelBuilder.Entity<ExpeditionMembers>(entity =>
            {
                entity.HasKey(e => e.CharacterId);

                entity.ToTable("expedition_members", "aaemu_game");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("character_id")
                    .HasColumnType("int(11)")
                    .ValueGeneratedNever();

                entity.Property(e => e.Ability1)
                    .HasColumnName("ability1")
                    .HasColumnType("tinyint(4) unsigned");

                entity.Property(e => e.Ability2)
                    .HasColumnName("ability2")
                    .HasColumnType("tinyint(4) unsigned");

                entity.Property(e => e.Ability3)
                    .HasColumnName("ability3")
                    .HasColumnType("tinyint(4) unsigned");

                entity.Property(e => e.ExpeditionId)
                    .HasColumnName("expedition_id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.LastLeaveTime)
                    .HasColumnName("last_leave_time")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.Level)
                    .HasColumnName("level")
                    .HasColumnType("tinyint(4) unsigned");

                entity.Property(e => e.Memo)
                    .IsRequired()
                    .HasColumnName("memo")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.Role)
                    .HasColumnName("role")
                    .HasColumnType("tinyint(4) unsigned");
            });

            modelBuilder.Entity<ExpeditionRolePolicies>(entity =>
            {
                entity.HasKey(e => new { e.ExpeditionId, e.Role });

                entity.ToTable("expedition_role_policies", "aaemu_game");

                entity.Property(e => e.ExpeditionId)
                    .HasColumnName("expedition_id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Role)
                    .HasColumnName("role")
                    .HasColumnType("tinyint(4) unsigned");

                entity.Property(e => e.Chat)
                    .HasColumnName("chat")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.Dismiss)
                    .HasColumnName("dismiss")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.DominionDeclare)
                    .HasColumnName("dominion_declare")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.Expel)
                    .HasColumnName("expel")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.Invite)
                    .HasColumnName("invite")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.JoinSiege)
                    .HasColumnName("join_siege")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.ManagerChat)
                    .HasColumnName("manager_chat")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.Promote)
                    .HasColumnName("promote")
                    .HasColumnType("tinyint(1)");

                entity.Property(e => e.SiegeMaster)
                    .HasColumnName("siege_master")
                    .HasColumnType("tinyint(1)");
            });

            modelBuilder.Entity<Expeditions>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("expeditions", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11)");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.Mother)
                    .HasColumnName("mother")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.OwnerName)
                    .IsRequired()
                    .HasColumnName("owner_name")
                    .HasMaxLength(128)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<FamilyMembers>(entity =>
            {
                entity.HasKey(e => new { e.CharacterId, e.FamilyId });

                entity.ToTable("family_members", "aaemu_game");

                entity.Property(e => e.CharacterId)
                    .HasColumnName("character_id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.FamilyId)
                    .HasColumnName("family_id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasMaxLength(45)
                    .IsUnicode(false);

                entity.Property(e => e.Role)
                    .HasColumnName("role")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Title)
                    .HasColumnName("title")
                    .HasMaxLength(45)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<Friends>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("friends", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11)");

                entity.Property(e => e.FriendId)
                    .HasColumnName("friend_id")
                    .HasColumnType("int(11)");
            });

            modelBuilder.Entity<Housings>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.AccountId, e.Owner });

                entity.ToTable("housings", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.AccountId)
                    .HasColumnName("account_id")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.CoOwner)
                    .HasColumnName("co_owner")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.CurrentAction)
                    .HasColumnName("current_action")
                    .HasColumnType("int(11)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.CurrentStep)
                    .HasColumnName("current_step")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.Permission)
                    .HasColumnName("permission")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.RotationZ)
                    .HasColumnName("rotation_z")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.TemplateId)
                    .HasColumnName("template_id")
                    .HasColumnType("int(10) unsigned");

                entity.Property(e => e.X).HasColumnName("x");

                entity.Property(e => e.Y).HasColumnName("y");

                entity.Property(e => e.Z).HasColumnName("z");
            });

            modelBuilder.Entity<Items>(entity =>
            {
                entity.ToTable("items", "aaemu_game");

                entity.HasIndex(e => e.Owner)
                    .HasName("owner");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("bigint(20) unsigned")
                    .ValueGeneratedNever();

                entity.Property(e => e.Count)
                    .HasColumnName("count")
                    .HasColumnType("int(11)");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.Details)
                    .HasColumnName("details")
                    .HasColumnType("blob");

                entity.Property(e => e.Grade)
                    .HasColumnName("grade")
                    .HasColumnType("tinyint(1)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.LifespanMins)
                    .HasColumnName("lifespan_mins")
                    .HasColumnType("int(11)");

                entity.Property(e => e.MadeUnitId)
                    .HasColumnName("made_unit_id")
                    .HasColumnType("int(11) unsigned")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Slot)
                    .HasColumnName("slot")
                    .HasColumnType("int(11)");

                entity.Property(e => e.SlotType)
                    .IsRequired()
                    .HasColumnName("slot_type")
                    .HasColumnType("enum('Equipment','Inventory','Bank')");

                entity.Property(e => e.TemplateId)
                    .HasColumnName("template_id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Type)
                    .IsRequired()
                    .HasColumnName("type")
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.UnpackTime)
                    .HasColumnName("unpack_time")
                    .HasDefaultValueSql("0001-01-01 00:00:00");

                entity.Property(e => e.UnsecureTime)
                    .HasColumnName("unsecure_time")
                    .HasDefaultValueSql("0001-01-01 00:00:00");
            });

            modelBuilder.Entity<Mates>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.ItemId, e.Owner });

                entity.ToTable("mates", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.ItemId)
                    .HasColumnName("item_id")
                    .HasColumnType("bigint(20) unsigned");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.Hp)
                    .HasColumnName("hp")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Level)
                    .HasColumnName("level")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.Mileage)
                    .HasColumnName("mileage")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Mp)
                    .HasColumnName("mp")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .IsUnicode(false);

                entity.Property(e => e.UpdatedAt)
                    .HasColumnName("updated_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.Xp)
                    .HasColumnName("xp")
                    .HasColumnType("int(11)");
            });

            modelBuilder.Entity<Options>(entity =>
            {
                entity.HasKey(e => new { e.Key, e.Owner });

                entity.ToTable("options", "aaemu_game");

                entity.Property(e => e.Key)
                    .HasColumnName("key")
                    .HasMaxLength(100)
                    .IsUnicode(false);

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Value)
                    .IsRequired()
                    .HasColumnName("value")
                    .IsUnicode(false);
            });

            modelBuilder.Entity<PortalBookCoords>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("portal_book_coords", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasColumnName("name")
                    .HasMaxLength(128)
                    .IsUnicode(false);

                entity.Property(e => e.SubZoneId)
                    .HasColumnName("sub_zone_id")
                    .HasColumnType("int(11)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.X)
                    .HasColumnName("x")
                    .HasColumnType("int(11)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Y)
                    .HasColumnName("y")
                    .HasColumnType("int(11)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Z)
                    .HasColumnName("z")
                    .HasColumnType("int(11)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.ZRot)
                    .HasColumnName("z_rot")
                    .HasColumnType("int(11)")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.ZoneId)
                    .HasColumnName("zone_id")
                    .HasColumnType("int(11)")
                    .HasDefaultValueSql("0");
            });

            modelBuilder.Entity<PortalVisitedDistrict>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Subzone, e.Owner });

                entity.ToTable("portal_visited_district", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Subzone)
                    .HasColumnName("subzone")
                    .HasColumnType("int(11)");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11)");
            });

            modelBuilder.Entity<Quests>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("quests", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Data)
                    .IsRequired()
                    .HasColumnName("data")
                    .HasColumnType("tinyblob");

                entity.Property(e => e.Status)
                    .HasColumnName("status")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.TemplateId)
                    .HasColumnName("template_id")
                    .HasColumnType("int(11) unsigned");
            });

            modelBuilder.Entity<Skills>(entity =>
            {
                entity.HasKey(e => new { e.Id, e.Owner });

                entity.ToTable("skills", "aaemu_game");

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Owner)
                    .HasColumnName("owner")
                    .HasColumnType("int(11) unsigned");

                entity.Property(e => e.Level)
                    .HasColumnName("level")
                    .HasColumnType("tinyint(4)");

                entity.Property(e => e.Type)
                    .IsRequired()
                    .HasColumnName("type")
                    .HasColumnType("enum('Skill','Buff')");
            });
        }
    }
}
