using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Database.Mysql;

public partial class AAEmuMysqlDbContext : DbContext
{
    public AAEmuMysqlDbContext()
    {
    }

    public AAEmuMysqlDbContext(DbContextOptions<AAEmuMysqlDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Ability> Abilities { get; set; }

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<Actability> Actabilities { get; set; }

    public virtual DbSet<Appellation> Appellations { get; set; }

    public virtual DbSet<AuctionHouse> AuctionHouses { get; set; }

    public virtual DbSet<Blocked> Blockeds { get; set; }

    public virtual DbSet<CashShopItem> CashShopItems { get; set; }

    public virtual DbSet<Character> Characters { get; set; }

    public virtual DbSet<CofferItem> CofferItems { get; set; }

    public virtual DbSet<CompletedQuest> CompletedQuests { get; set; }

    public virtual DbSet<Crime> Crimes { get; set; }

    public virtual DbSet<Doodad> Doodads { get; set; }

    public virtual DbSet<Expedition> Expeditions { get; set; }

    public virtual DbSet<ExpeditionMember> ExpeditionMembers { get; set; }

    public virtual DbSet<ExpeditionRolePolicy> ExpeditionRolePolicies { get; set; }

    public virtual DbSet<FamilyMember> FamilyMembers { get; set; }

    public virtual DbSet<Friend> Friends { get; set; }

    public virtual DbSet<Housing> Housings { get; set; }

    public virtual DbSet<HousingDecor> HousingDecors { get; set; }

    public virtual DbSet<Item> Items { get; set; }

    public virtual DbSet<ItemContainer> ItemContainers { get; set; }

    public virtual DbSet<Mail> Mails { get; set; }

    public virtual DbSet<MailsItem> MailsItems { get; set; }

    public virtual DbSet<Mate> Mates { get; set; }

    public virtual DbSet<Music> Musics { get; set; }

    public virtual DbSet<Option> Options { get; set; }

    public virtual DbSet<PortalBookCoord> PortalBookCoords { get; set; }

    public virtual DbSet<PortalVisitedDistrict> PortalVisitedDistricts { get; set; }

    public virtual DbSet<Quest> Quests { get; set; }

    public virtual DbSet<Skill> Skills { get; set; }

    public virtual DbSet<Ucc> Uccs { get; set; }

    public virtual DbSet<Update> Updates { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseMySql("server=localhost;port=3306;user=root;password=123456;database=aaemu_game", Microsoft.EntityFrameworkCore.ServerVersion.Parse("10.5.18-mariadb"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_general_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Ability>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("abilities", tb => tb.HasComment("Skillsets Exp"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Exp)
                .HasColumnType("int(11)")
                .HasColumnName("exp");
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("PRIMARY");

            entity
                .ToTable("accounts", tb => tb.HasComment("Account specific values not related to login"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.AccountId)
                .ValueGeneratedNever()
                .HasColumnType("int(11)")
                .HasColumnName("account_id");
            entity.Property(e => e.Credits)
                .HasColumnType("int(11)")
                .HasColumnName("credits");
        });

        modelBuilder.Entity<Actability>(entity =>
        {
            entity.HasKey(e => new { e.Owner, e.Id })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("actabilities", tb => tb.HasComment("Vocations"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.Point)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("point");
            entity.Property(e => e.Step)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("step");
        });

        modelBuilder.Entity<Appellation>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("appellations", tb => tb.HasComment("Earned titles"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Active).HasColumnName("active");
        });

        modelBuilder.Entity<AuctionHouse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("auction_house", tb => tb.HasComment("Listed AH Items"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.BidMoney)
                .HasColumnType("int(11)")
                .HasColumnName("bid_money");
            entity.Property(e => e.BidWorldId).HasColumnName("bid_world_id");
            entity.Property(e => e.BidderId)
                .HasColumnType("int(11)")
                .HasColumnName("bidder_id");
            entity.Property(e => e.BidderName)
                .IsRequired()
                .HasMaxLength(45)
                .HasColumnName("bidder_name");
            entity.Property(e => e.ClientId)
                .HasColumnType("int(11)")
                .HasColumnName("client_id");
            entity.Property(e => e.ClientName)
                .IsRequired()
                .HasMaxLength(45)
                .HasColumnName("client_name");
            entity.Property(e => e.CreationTime)
                .HasColumnType("datetime")
                .HasColumnName("creation_time");
            entity.Property(e => e.DetailType).HasColumnName("detail_type");
            entity.Property(e => e.DirectMoney)
                .HasColumnType("int(11)")
                .HasColumnName("direct_money");
            entity.Property(e => e.Duration)
                .HasColumnType("tinyint(4)")
                .HasColumnName("duration");
            entity.Property(e => e.EndTime)
                .HasColumnType("datetime")
                .HasColumnName("end_time");
            entity.Property(e => e.Extra)
                .HasColumnType("int(11)")
                .HasColumnName("extra");
            entity.Property(e => e.Flags).HasColumnName("flags");
            entity.Property(e => e.Grade).HasColumnName("grade");
            entity.Property(e => e.ItemId)
                .HasColumnType("int(11)")
                .HasColumnName("item_id");
            entity.Property(e => e.LifespanMins)
                .HasColumnType("int(11)")
                .HasColumnName("lifespan_mins");
            entity.Property(e => e.ObjectId)
                .HasColumnType("int(11)")
                .HasColumnName("object_id");
            entity.Property(e => e.StackSize)
                .HasColumnType("int(11)")
                .HasColumnName("stack_size");
            entity.Property(e => e.StartMoney)
                .HasColumnType("int(11)")
                .HasColumnName("start_money");
            entity.Property(e => e.Type1)
                .HasColumnType("int(11)")
                .HasColumnName("type_1");
            entity.Property(e => e.UnpackDateTime)
                .IsRequired()
                .HasMaxLength(45)
                .HasColumnName("unpack_date_time");
            entity.Property(e => e.UnsecureDateTime)
                .IsRequired()
                .HasMaxLength(45)
                .HasColumnName("unsecure_date_time");
            entity.Property(e => e.WorldId)
                .HasColumnType("tinyint(4)")
                .HasColumnName("world_id");
            entity.Property(e => e.WorldId2)
                .HasColumnType("tinyint(4)")
                .HasColumnName("world_id_2");
        });

        modelBuilder.Entity<Blocked>(entity =>
        {
            entity.HasKey(e => new { e.Owner, e.BlockedId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("blocked")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Owner)
                .HasColumnType("int(11)")
                .HasColumnName("owner");
            entity.Property(e => e.BlockedId)
                .HasColumnType("int(11)")
                .HasColumnName("blocked_id");
        });

        modelBuilder.Entity<CashShopItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("cash_shop_item", tb => tb.HasComment("此表来自于代码中的字段并去除重复字段生成。字段名称和内容以代码为准。"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasComment("shop_id")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.BonusType)
                .HasDefaultValueSql("'0'")
                .HasComment("赠送类型")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("bonus_type");
            entity.Property(e => e.BounsCount)
                .HasDefaultValueSql("'0'")
                .HasComment("赠送数量")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("bouns_count");
            entity.Property(e => e.BuyCount)
                .HasDefaultValueSql("'0'")
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("buy_count");
            entity.Property(e => e.BuyId)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("buy_id");
            entity.Property(e => e.BuyType)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("buy_type");
            entity.Property(e => e.CashName)
                .IsRequired()
                .HasMaxLength(255)
                .HasComment("出售名称")
                .HasColumnName("cash_name");
            entity.Property(e => e.CmdUi)
                .HasDefaultValueSql("'0'")
                .HasComment("是否限制一人一次")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("cmd_ui");
            entity.Property(e => e.DefaultFlag)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("default_flag");
            entity.Property(e => e.DisPrice)
                .HasDefaultValueSql("'0'")
                .HasComment("当前售价")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("dis_price");
            entity.Property(e => e.EndDate)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasComment("出售截止")
                .HasColumnType("datetime")
                .HasColumnName("end_date");
            entity.Property(e => e.EventDate)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasComment("活动时间")
                .HasColumnType("datetime")
                .HasColumnName("event_date");
            entity.Property(e => e.EventType)
                .HasDefaultValueSql("'0'")
                .HasComment("活动类型")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("event_type");
            entity.Property(e => e.IsHidden)
                .HasDefaultValueSql("'0'")
                .HasComment("是否隐藏")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("is_hidden");
            entity.Property(e => e.IsSell)
                .HasDefaultValueSql("'0'")
                .HasComment("是否出售")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("is_sell");
            entity.Property(e => e.ItemCount)
                .HasDefaultValueSql("'1'")
                .HasComment("捆绑数量")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("item_count");
            entity.Property(e => e.ItemTemplateId)
                .HasDefaultValueSql("'0'")
                .HasComment("物品模板id")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("item_template_id");
            entity.Property(e => e.LevelMax)
                .HasDefaultValueSql("'0'")
                .HasComment("等级限制")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("level_max");
            entity.Property(e => e.LevelMin)
                .HasDefaultValueSql("'0'")
                .HasComment("等级限制")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("level_min");
            entity.Property(e => e.LimitType)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("limit_type");
            entity.Property(e => e.MainTab)
                .HasDefaultValueSql("'1'")
                .HasComment("主分类1-6")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("main_tab");
            entity.Property(e => e.Price)
                .HasDefaultValueSql("'0'")
                .HasComment("价格")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("price");
            entity.Property(e => e.Remain)
                .HasDefaultValueSql("'0'")
                .HasComment("剩余数量")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("remain");
            entity.Property(e => e.SelectType)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("select_type");
            entity.Property(e => e.StartDate)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasComment("出售开始")
                .HasColumnType("datetime")
                .HasColumnName("start_date");
            entity.Property(e => e.SubTab)
                .HasDefaultValueSql("'1'")
                .HasComment("子分类1-7")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("sub_tab");
            entity.Property(e => e.Type)
                .HasDefaultValueSql("'0'")
                .HasComment("货币类型")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("type");
            entity.Property(e => e.UniqId)
                .HasDefaultValueSql("'0'")
                .HasComment("唯一ID")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("uniq_id");
        });

        modelBuilder.Entity<Character>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.AccountId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("characters", tb => tb.HasComment("Basic player character data"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.AccountId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("account_id");
            entity.Property(e => e.Ability1)
                .HasColumnType("tinyint(4)")
                .HasColumnName("ability1");
            entity.Property(e => e.Ability2)
                .HasColumnType("tinyint(4)")
                .HasColumnName("ability2");
            entity.Property(e => e.Ability3)
                .HasColumnType("tinyint(4)")
                .HasColumnName("ability3");
            entity.Property(e => e.AccessLevel)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("access_level");
            entity.Property(e => e.AutoUseAapoint).HasColumnName("auto_use_aapoint");
            entity.Property(e => e.BmPoint)
                .HasColumnType("int(11)")
                .HasColumnName("bm_point");
            entity.Property(e => e.ConsumedLp)
                .HasColumnType("int(11)")
                .HasColumnName("consumed_lp");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.CrimePoint)
                .HasColumnType("int(11)")
                .HasColumnName("crime_point");
            entity.Property(e => e.CrimeRecord)
                .HasColumnType("int(11)")
                .HasColumnName("crime_record");
            entity.Property(e => e.DeadCount)
                .HasColumnType("mediumint(8) unsigned")
                .HasColumnName("dead_count");
            entity.Property(e => e.DeadTime)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("dead_time");
            entity.Property(e => e.DeleteRequestTime)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("delete_request_time");
            entity.Property(e => e.DeleteTime)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("delete_time");
            entity.Property(e => e.Deleted)
                .HasColumnType("int(11)")
                .HasColumnName("deleted");
            entity.Property(e => e.ExpandedExpert)
                .HasColumnType("tinyint(4)")
                .HasColumnName("expanded_expert");
            entity.Property(e => e.ExpeditionId)
                .HasColumnType("int(11)")
                .HasColumnName("expedition_id");
            entity.Property(e => e.Expirience)
                .HasColumnType("int(11)")
                .HasColumnName("expirience");
            entity.Property(e => e.FactionId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("faction_id");
            entity.Property(e => e.FactionName)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("faction_name");
            entity.Property(e => e.Family)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("family");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.Gift)
                .HasColumnType("int(11)")
                .HasColumnName("gift");
            entity.Property(e => e.HonorPoint)
                .HasColumnType("int(11)")
                .HasColumnName("honor_point");
            entity.Property(e => e.HostileFactionKills)
                .HasColumnType("int(11)")
                .HasColumnName("hostile_faction_kills");
            entity.Property(e => e.Hp)
                .HasColumnType("int(11)")
                .HasColumnName("hp");
            entity.Property(e => e.LaborPower)
                .HasColumnType("mediumint(9)")
                .HasColumnName("labor_power");
            entity.Property(e => e.LaborPowerModified)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("labor_power_modified");
            entity.Property(e => e.LeaveTime)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("leave_time");
            entity.Property(e => e.Level)
                .HasColumnType("tinyint(4)")
                .HasColumnName("level");
            entity.Property(e => e.Money)
                .HasColumnType("bigint(20)")
                .HasColumnName("money");
            entity.Property(e => e.Money2)
                .HasColumnType("bigint(20)")
                .HasColumnName("money2");
            entity.Property(e => e.Mp)
                .HasColumnType("int(11)")
                .HasColumnName("mp");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.NumBankSlot)
                .HasDefaultValueSql("'50'")
                .HasColumnType("smallint(5) unsigned")
                .HasColumnName("num_bank_slot");
            entity.Property(e => e.NumInvSlot)
                .HasDefaultValueSql("'50'")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("num_inv_slot");
            entity.Property(e => e.Pitch).HasColumnName("pitch");
            entity.Property(e => e.Point)
                .HasColumnType("int(11)")
                .HasColumnName("point");
            entity.Property(e => e.PrevPoint)
                .HasColumnType("int(11)")
                .HasColumnName("prev_point");
            entity.Property(e => e.PvpHonor)
                .HasColumnType("int(11)")
                .HasColumnName("pvp_honor");
            entity.Property(e => e.Race)
                .HasColumnType("tinyint(4)")
                .HasColumnName("race");
            entity.Property(e => e.RecoverableExp)
                .HasColumnType("int(11)")
                .HasColumnName("recoverable_exp");
            entity.Property(e => e.ReturnDistrict)
                .HasColumnType("int(11)")
                .HasColumnName("return_district");
            entity.Property(e => e.RezPenaltyDuration)
                .HasColumnType("int(11)")
                .HasColumnName("rez_penalty_duration");
            entity.Property(e => e.RezTime)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("rez_time");
            entity.Property(e => e.RezWaitDuration)
                .HasColumnType("int(11)")
                .HasColumnName("rez_wait_duration");
            entity.Property(e => e.Roll).HasColumnName("roll");
            entity.Property(e => e.Slots)
                .IsRequired()
                .HasColumnType("blob")
                .HasColumnName("slots");
            entity.Property(e => e.TransferRequestTime)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("transfer_request_time");
            entity.Property(e => e.UnitModelParams)
                .IsRequired()
                .HasColumnType("blob")
                .HasColumnName("unit_model_params");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("updated_at");
            entity.Property(e => e.VocationPoint)
                .HasColumnType("int(11)")
                .HasColumnName("vocation_point");
            entity.Property(e => e.WorldId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("world_id");
            entity.Property(e => e.X).HasColumnName("x");
            entity.Property(e => e.Y).HasColumnName("y");
            entity.Property(e => e.Yaw).HasColumnName("yaw");
            entity.Property(e => e.Z).HasColumnName("z");
            entity.Property(e => e.ZoneId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<CofferItem>(entity =>
        {
            entity
                .HasNoKey()
                .ToTable("coffer_items")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.DecorId)
                .HasColumnType("int(11)")
                .HasColumnName("decor_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("bigint(20)")
                .HasColumnName("item_id");
            entity.Property(e => e.Slot)
                .HasColumnType("int(11)")
                .HasColumnName("slot");
        });

        modelBuilder.Entity<CompletedQuest>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("completed_quests", tb => tb.HasComment("Quests marked as completed for character"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Data)
                .IsRequired()
                .HasColumnType("tinyblob")
                .HasColumnName("data");
        });

        modelBuilder.Entity<Crime>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("crimes")
                .UseCollation("utf8mb4_unicode_ci");

            entity.HasIndex(e => e.Id, "id_UNIQUE").IsUnique();

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.CrimeKind)
                .HasColumnType("tinyint(4)")
                .HasColumnName("crime_kind");
            entity.Property(e => e.CriminalId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("criminal_id");
            entity.Property(e => e.CriminalName)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("criminal_name");
            entity.Property(e => e.Desc)
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnName("desc");
            entity.Property(e => e.ReporterId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("reporter_id");
            entity.Property(e => e.ReporterName)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("reporter_name");
            entity.Property(e => e.Time)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("time");
            entity.Property(e => e.Type3)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("type3");
            entity.Property(e => e.Type4)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("type4");
            entity.Property(e => e.Type5)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("type5");
            entity.Property(e => e.Type6)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("type6");
            entity.Property(e => e.X)
                .HasColumnType("bigint(20)")
                .HasColumnName("x");
            entity.Property(e => e.Y)
                .HasColumnType("bigint(20)")
                .HasColumnName("y");
            entity.Property(e => e.Z).HasColumnName("z");
        });

        modelBuilder.Entity<Doodad>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("doodads", tb => tb.HasComment("Persistent doodads (e.g. tradepacks, furniture)"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.CurrentPhaseId)
                .HasColumnType("int(11)")
                .HasColumnName("current_phase_id");
            entity.Property(e => e.Data)
                .HasComment("Doodad specific permissions if used")
                .HasColumnType("int(11)")
                .HasColumnName("data");
            entity.Property(e => e.GrowthTime)
                .HasColumnType("datetime")
                .HasColumnName("growth_time");
            entity.Property(e => e.HouseId)
                .HasComment("House DB Id if it is on actual house land")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("house_id");
            entity.Property(e => e.ItemContainerId)
                .HasComment("ItemContainer Id for Coffers")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("item_container_id");
            entity.Property(e => e.ItemId)
                .HasComment("Item DB Id of the associated item")
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item_id");
            entity.Property(e => e.ItemTemplateId)
                .HasComment("ItemTemplateId of associated item")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("item_template_id");
            entity.Property(e => e.OwnerId)
                .HasComment("Character DB Id")
                .HasColumnType("int(11)")
                .HasColumnName("owner_id");
            entity.Property(e => e.OwnerType)
                .HasDefaultValueSql("'255'")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("owner_type");
            entity.Property(e => e.ParentDoodad)
                .HasComment("doodads DB Id this object is standing on")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("parent_doodad");
            entity.Property(e => e.PhaseTime)
                .HasColumnType("datetime")
                .HasColumnName("phase_time");
            entity.Property(e => e.Pitch).HasColumnName("pitch");
            entity.Property(e => e.PlantTime)
                .HasColumnType("datetime")
                .HasColumnName("plant_time");
            entity.Property(e => e.Roll).HasColumnName("roll");
            entity.Property(e => e.TemplateId)
                .HasColumnType("int(11)")
                .HasColumnName("template_id");
            entity.Property(e => e.X).HasColumnName("x");
            entity.Property(e => e.Y).HasColumnName("y");
            entity.Property(e => e.Yaw).HasColumnName("yaw");
            entity.Property(e => e.Z).HasColumnName("z");
        });

        modelBuilder.Entity<Expedition>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("expeditions", tb => tb.HasComment("Guilds"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Mother)
                .HasColumnType("int(11)")
                .HasColumnName("mother");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Owner)
                .HasColumnType("int(11)")
                .HasColumnName("owner");
            entity.Property(e => e.OwnerName)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("owner_name");
        });

        modelBuilder.Entity<ExpeditionMember>(entity =>
        {
            entity.HasKey(e => e.CharacterId).HasName("PRIMARY");

            entity
                .ToTable("expedition_members", tb => tb.HasComment("Guild members"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.CharacterId)
                .ValueGeneratedNever()
                .HasColumnType("int(11)")
                .HasColumnName("character_id");
            entity.Property(e => e.Ability1)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("ability1");
            entity.Property(e => e.Ability2)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("ability2");
            entity.Property(e => e.Ability3)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("ability3");
            entity.Property(e => e.ExpeditionId)
                .HasColumnType("int(11)")
                .HasColumnName("expedition_id");
            entity.Property(e => e.LastLeaveTime)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("last_leave_time");
            entity.Property(e => e.Level)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("level");
            entity.Property(e => e.Memo)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("memo");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Role)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("role");
        });

        modelBuilder.Entity<ExpeditionRolePolicy>(entity =>
        {
            entity.HasKey(e => new { e.ExpeditionId, e.Role })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("expedition_role_policies", tb => tb.HasComment("Guild role settings"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.ExpeditionId)
                .HasColumnType("int(11)")
                .HasColumnName("expedition_id");
            entity.Property(e => e.Role)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("role");
            entity.Property(e => e.Chat).HasColumnName("chat");
            entity.Property(e => e.Dismiss).HasColumnName("dismiss");
            entity.Property(e => e.DominionDeclare).HasColumnName("dominion_declare");
            entity.Property(e => e.Expel).HasColumnName("expel");
            entity.Property(e => e.Invite).HasColumnName("invite");
            entity.Property(e => e.JoinSiege).HasColumnName("join_siege");
            entity.Property(e => e.ManagerChat).HasColumnName("manager_chat");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Promote).HasColumnName("promote");
            entity.Property(e => e.SiegeMaster).HasColumnName("siege_master");
        });

        modelBuilder.Entity<FamilyMember>(entity =>
        {
            entity.HasKey(e => new { e.FamilyId, e.CharacterId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("family_members", tb => tb.HasComment("Family members"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.FamilyId)
                .HasColumnType("int(11)")
                .HasColumnName("family_id");
            entity.Property(e => e.CharacterId)
                .HasColumnType("int(11)")
                .HasColumnName("character_id");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(45)
                .HasColumnName("name");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.Title)
                .HasMaxLength(45)
                .HasColumnName("title");
        });

        modelBuilder.Entity<Friend>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("friends", tb => tb.HasComment("Friendslist"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(11)")
                .HasColumnName("owner");
            entity.Property(e => e.FriendId)
                .HasColumnType("int(11)")
                .HasColumnName("friend_id");
        });

        modelBuilder.Entity<Housing>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("housings", tb => tb.HasComment("Player buildings"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.AccountId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("account_id");
            entity.Property(e => e.AllowRecover)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("allow_recover");
            entity.Property(e => e.CoOwner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("co_owner");
            entity.Property(e => e.CurrentAction)
                .HasColumnType("int(11)")
                .HasColumnName("current_action");
            entity.Property(e => e.CurrentStep)
                .HasColumnType("tinyint(4)")
                .HasColumnName("current_step");
            entity.Property(e => e.FactionId)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("faction_id");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Permission)
                .HasColumnType("tinyint(4)")
                .HasColumnName("permission");
            entity.Property(e => e.Pitch).HasColumnName("pitch");
            entity.Property(e => e.PlaceDate)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("place_date");
            entity.Property(e => e.ProtectedUntil)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("protected_until");
            entity.Property(e => e.Roll).HasColumnName("roll");
            entity.Property(e => e.SellPrice)
                .HasColumnType("bigint(20)")
                .HasColumnName("sell_price");
            entity.Property(e => e.SellTo)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("sell_to");
            entity.Property(e => e.TemplateId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("template_id");
            entity.Property(e => e.X).HasColumnName("x");
            entity.Property(e => e.Y).HasColumnName("y");
            entity.Property(e => e.Yaw).HasColumnName("yaw");
            entity.Property(e => e.Z).HasColumnName("z");
        });

        modelBuilder.Entity<HousingDecor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("housing_decors")
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.DesignId)
                .HasColumnType("int(11)")
                .HasColumnName("design_id");
            entity.Property(e => e.HouseId)
                .HasColumnType("int(11)")
                .HasColumnName("house_id");
            entity.Property(e => e.ItemId)
                .HasColumnType("bigint(20)")
                .HasColumnName("item_id");
            entity.Property(e => e.ItemTemplateId)
                .HasColumnType("int(11)")
                .HasColumnName("item_template_id");
            entity.Property(e => e.QuatW).HasColumnName("quat_w");
            entity.Property(e => e.QuatX).HasColumnName("quat_x");
            entity.Property(e => e.QuatY).HasColumnName("quat_y");
            entity.Property(e => e.QuatZ).HasColumnName("quat_z");
            entity.Property(e => e.X).HasColumnName("x");
            entity.Property(e => e.Y).HasColumnName("y");
            entity.Property(e => e.Z).HasColumnName("z");
        });

        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("items", tb => tb.HasComment("All items"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.HasIndex(e => e.Owner, "owner");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.ChargeCount)
                .HasComment("Number of charges left")
                .HasColumnType("int(11)")
                .HasColumnName("charge_count");
            entity.Property(e => e.ChargeTime)
                .HasComment("Time charged items got activated")
                .HasColumnType("datetime")
                .HasColumnName("charge_time");
            entity.Property(e => e.ContainerId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("container_id");
            entity.Property(e => e.Count)
                .HasColumnType("int(11)")
                .HasColumnName("count");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Details)
                .HasColumnType("blob")
                .HasColumnName("details");
            entity.Property(e => e.ExpireOnlineMinutes)
                .HasComment("Time left when player online")
                .HasColumnName("expire_online_minutes");
            entity.Property(e => e.ExpireTime)
                .HasComment("Fixed time expire")
                .HasColumnType("datetime")
                .HasColumnName("expire_time");
            entity.Property(e => e.Flags)
                .HasColumnType("tinyint(3) unsigned")
                .HasColumnName("flags");
            entity.Property(e => e.Grade)
                .HasDefaultValueSql("'0'")
                .HasColumnName("grade");
            entity.Property(e => e.LifespanMins)
                .HasColumnType("int(11)")
                .HasColumnName("lifespan_mins");
            entity.Property(e => e.MadeUnitId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("made_unit_id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Slot)
                .HasColumnType("int(11)")
                .HasColumnName("slot");
            entity.Property(e => e.SlotType)
                .IsRequired()
                .HasColumnType("enum('Equipment','Inventory','Bank','Trade','Mail','System')")
                .HasColumnName("slot_type");
            entity.Property(e => e.TemplateId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("template_id");
            entity.Property(e => e.Type)
                .IsRequired()
                .HasMaxLength(100)
                .HasColumnName("type");
            entity.Property(e => e.Ucc)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("ucc");
            entity.Property(e => e.UnpackTime)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("unpack_time");
            entity.Property(e => e.UnsecureTime)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("unsecure_time");
        });

        modelBuilder.Entity<ItemContainer>(entity =>
        {
            entity.HasKey(e => e.ContainerId).HasName("PRIMARY");

            entity.ToTable("item_containers");

            entity.Property(e => e.ContainerId)
                .ValueGeneratedNever()
                .HasColumnType("int(10) unsigned")
                .HasColumnName("container_id");
            entity.Property(e => e.ContainerSize)
                .HasDefaultValueSql("'50'")
                .HasComment("Maximum Container Size")
                .HasColumnType("int(11)")
                .HasColumnName("container_size");
            entity.Property(e => e.ContainerType)
                .IsRequired()
                .HasMaxLength(64)
                .HasDefaultValueSql("'ItemContainer'")
                .HasComment("Partial Container Class Name")
                .HasColumnName("container_type");
            entity.Property(e => e.OwnerId)
                .HasComment("Owning Character Id")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner_id");
            entity.Property(e => e.SlotType)
                .IsRequired()
                .HasComment("Internal Container Type")
                .HasColumnType("enum('Equipment','Inventory','Bank','Trade','Mail','System')")
                .HasColumnName("slot_type");
        });

        modelBuilder.Entity<Mail>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("mails", tb => tb.HasComment("In-game mails"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Attachment0)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment0");
            entity.Property(e => e.Attachment1)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment1");
            entity.Property(e => e.Attachment2)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment2");
            entity.Property(e => e.Attachment3)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment3");
            entity.Property(e => e.Attachment4)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment4");
            entity.Property(e => e.Attachment5)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment5");
            entity.Property(e => e.Attachment6)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment6");
            entity.Property(e => e.Attachment7)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment7");
            entity.Property(e => e.Attachment8)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment8");
            entity.Property(e => e.Attachment9)
                .HasColumnType("bigint(20)")
                .HasColumnName("attachment9");
            entity.Property(e => e.AttachmentCount)
                .HasColumnType("int(11)")
                .HasColumnName("attachment_count");
            entity.Property(e => e.Extra)
                .HasColumnType("bigint(20)")
                .HasColumnName("extra");
            entity.Property(e => e.MoneyAmount1)
                .HasColumnType("int(11)")
                .HasColumnName("money_amount_1");
            entity.Property(e => e.MoneyAmount2)
                .HasColumnType("int(11)")
                .HasColumnName("money_amount_2");
            entity.Property(e => e.MoneyAmount3)
                .HasColumnType("int(11)")
                .HasColumnName("money_amount_3");
            entity.Property(e => e.OpenDate)
                .HasColumnType("datetime")
                .HasColumnName("open_date");
            entity.Property(e => e.ReceivedDate)
                .HasColumnType("datetime")
                .HasColumnName("received_date");
            entity.Property(e => e.ReceiverId)
                .HasColumnType("int(11)")
                .HasColumnName("receiver_id");
            entity.Property(e => e.ReceiverName)
                .IsRequired()
                .HasMaxLength(45)
                .HasColumnName("receiver_name");
            entity.Property(e => e.Returned)
                .HasColumnType("int(11)")
                .HasColumnName("returned");
            entity.Property(e => e.SendDate)
                .HasColumnType("datetime")
                .HasColumnName("send_date");
            entity.Property(e => e.SenderId)
                .HasColumnType("int(11)")
                .HasColumnName("sender_id");
            entity.Property(e => e.SenderName)
                .IsRequired()
                .HasMaxLength(45)
                .HasColumnName("sender_name");
            entity.Property(e => e.Status)
                .HasColumnType("int(11)")
                .HasColumnName("status");
            entity.Property(e => e.Text)
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("text");
            entity.Property(e => e.Title)
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("title");
            entity.Property(e => e.Type)
                .HasColumnType("int(11)")
                .HasColumnName("type");
        });

        modelBuilder.Entity<MailsItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("mails_items")
                .UseCollation("utf8mb4_unicode_ci");

            entity.Property(e => e.Id)
                .HasColumnType("bigint(20)")
                .HasColumnName("id");
            entity.Property(e => e.Item0)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item0");
            entity.Property(e => e.Item1)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item1");
            entity.Property(e => e.Item2)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item2");
            entity.Property(e => e.Item3)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item3");
            entity.Property(e => e.Item4)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item4");
            entity.Property(e => e.Item5)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item5");
            entity.Property(e => e.Item6)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item6");
            entity.Property(e => e.Item7)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item7");
            entity.Property(e => e.Item8)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item8");
            entity.Property(e => e.Item9)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item9");
        });

        modelBuilder.Entity<Mate>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.ItemId, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0, 0 });

            entity
                .ToTable("mates", tb => tb.HasComment("Player mounts and pets"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.ItemId)
                .HasColumnType("bigint(20) unsigned")
                .HasColumnName("item_id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Hp)
                .HasColumnType("int(11)")
                .HasColumnName("hp");
            entity.Property(e => e.Level)
                .HasColumnType("tinyint(4)")
                .HasColumnName("level");
            entity.Property(e => e.Mileage)
                .HasColumnType("int(11)")
                .HasColumnName("mileage");
            entity.Property(e => e.Mp)
                .HasColumnType("int(11)")
                .HasColumnName("mp");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("updated_at");
            entity.Property(e => e.Xp)
                .HasColumnType("int(11)")
                .HasColumnName("xp");
        });

        modelBuilder.Entity<Music>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("music", tb => tb.HasComment("User Created Content (music)"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Author)
                .HasComment("PlayerId")
                .HasColumnType("int(11)")
                .HasColumnName("author");
            entity.Property(e => e.Song)
                .IsRequired()
                .HasComment("Song MML")
                .HasColumnType("text")
                .HasColumnName("song");
            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("title");
        });

        modelBuilder.Entity<Option>(entity =>
        {
            entity.HasKey(e => new { e.Key, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("options", tb => tb.HasComment("Settings that the client stores on the server"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Key)
                .HasMaxLength(100)
                .HasColumnName("key");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Value)
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("value");
        });

        modelBuilder.Entity<PortalBookCoord>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("portal_book_coords", tb => tb.HasComment("Recorded house portals in the portal book"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(11)")
                .HasColumnName("owner");
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.SubZoneId)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("sub_zone_id");
            entity.Property(e => e.X)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("x");
            entity.Property(e => e.Y)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("y");
            entity.Property(e => e.Z)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("z");
            entity.Property(e => e.ZRot)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("z_rot");
            entity.Property(e => e.ZoneId)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)")
                .HasColumnName("zone_id");
        });

        modelBuilder.Entity<PortalVisitedDistrict>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Subzone, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0, 0 });

            entity
                .ToTable("portal_visited_district", tb => tb.HasComment("List of visited area for the portal book"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Subzone)
                .HasColumnType("int(11)")
                .HasColumnName("subzone");
            entity.Property(e => e.Owner)
                .HasColumnType("int(11)")
                .HasColumnName("owner");
        });

        modelBuilder.Entity<Quest>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("quests", tb => tb.HasComment("Currently open quests"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Data)
                .IsRequired()
                .HasColumnType("tinyblob")
                .HasColumnName("data");
            entity.Property(e => e.Status)
                .HasColumnType("tinyint(4)")
                .HasColumnName("status");
            entity.Property(e => e.TemplateId)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("template_id");
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Owner })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity
                .ToTable("skills", tb => tb.HasComment("Learned character skills"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("id");
            entity.Property(e => e.Owner)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("owner");
            entity.Property(e => e.Level)
                .HasColumnType("tinyint(4)")
                .HasColumnName("level");
            entity.Property(e => e.Type)
                .IsRequired()
                .HasColumnType("enum('Skill','Buff')")
                .HasColumnName("type");
        });

        modelBuilder.Entity<Ucc>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity
                .ToTable("uccs", tb => tb.HasComment("User Created Content (crests)"))
                .HasCharSet("utf8")
                .UseCollation("utf8_general_ci");

            entity.Property(e => e.Id)
                .HasColumnType("int(11)")
                .HasColumnName("id");
            entity.Property(e => e.Color1B)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color1B");
            entity.Property(e => e.Color1G)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color1G");
            entity.Property(e => e.Color1R)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color1R");
            entity.Property(e => e.Color2B)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color2B");
            entity.Property(e => e.Color2G)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color2G");
            entity.Property(e => e.Color2R)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color2R");
            entity.Property(e => e.Color3B)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color3B");
            entity.Property(e => e.Color3G)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color3G");
            entity.Property(e => e.Color3R)
                .HasColumnType("int(10) unsigned")
                .HasColumnName("color3R");
            entity.Property(e => e.Data)
                .HasComment("Raw uploaded UCC data")
                .HasColumnType("mediumblob")
                .HasColumnName("data");
            entity.Property(e => e.Modified)
                .HasDefaultValueSql("'0001-01-01 00:00:00'")
                .HasColumnType("datetime")
                .HasColumnName("modified");
            entity.Property(e => e.Pattern1)
                .HasComment("Background pattern")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("pattern1");
            entity.Property(e => e.Pattern2)
                .HasComment("Crest")
                .HasColumnType("int(10) unsigned")
                .HasColumnName("pattern2");
            entity.Property(e => e.Type)
                .HasColumnType("tinyint(4)")
                .HasColumnName("type");
            entity.Property(e => e.UploaderId)
                .HasComment("PlayerID")
                .HasColumnType("int(11)")
                .HasColumnName("uploader_id");
        });

        modelBuilder.Entity<Update>(entity =>
        {
            entity.HasKey(e => e.ScriptName).HasName("PRIMARY");

            entity.ToTable("updates", tb => tb.HasComment("Table containing SQL update script information"));

            entity.Property(e => e.ScriptName).HasColumnName("script_name");
            entity.Property(e => e.InstallDate)
                .HasColumnType("datetime")
                .HasColumnName("install_date");
            entity.Property(e => e.Installed)
                .HasColumnType("tinyint(4)")
                .HasColumnName("installed");
            entity.Property(e => e.LastError)
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("last_error");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
