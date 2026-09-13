using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AAEmu.Game.Core.Managers;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class ExpeditionRecruitmentMySqlFixture : IAsyncLifetime, IExpeditionRecruitmentConnectionFactory,
    IExpeditionPersistenceConnectionFactory, IExpeditionActivityConnectionFactory
{
    private const string EnvironmentVariable = "AAEMU_RECRUITMENT_TEST_MYSQL";
    private static readonly Regex SafeName = new("^aaemu_recruitment_test_[0-9a-f]{12}$");
    private string _serverConnectionString;
    public string ConnectionString { get; private set; }
    public string Database { get; private set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(_serverConnectionString);

    public async ValueTask InitializeAsync()
    {
        _serverConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!Enabled) return;
        Database = "aaemu_recruitment_test_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        if (!SafeName.IsMatch(Database)) throw new InvalidOperationException("Unsafe test database name.");
        var supplied = new MySqlConnectionStringBuilder(_serverConnectionString);
        if (string.Equals(supplied.Database, "aaemu_game", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recruitment integration tests never accept aaemu_game.");
        var server = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = string.Empty };
        await using var connection = new MySqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await Execute(connection, $"CREATE DATABASE `{Database}` CHARACTER SET utf8mb4");
        supplied.Database = Database;
        ConnectionString = supplied.ConnectionString;
        await using var schema = Open();
        await Execute(schema, Schema);
    }

    public async ValueTask DisposeAsync()
    {
        if (!Enabled || !SafeName.IsMatch(Database ?? string.Empty)) return;
        var server = new MySqlConnectionStringBuilder(_serverConnectionString) { Database = string.Empty };
        await using var connection = new MySqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await Execute(connection, $"DROP DATABASE IF EXISTS `{Database}`");
    }

    public MySqlConnection Open()
    {
        if (!Enabled) throw new InvalidOperationException($"Set {EnvironmentVariable} to opt in.");
        var connection = new MySqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }

    private const string Schema = """
CREATE TABLE expeditions (id INT NOT NULL PRIMARY KEY,owner INT NOT NULL DEFAULT 0,owner_name VARCHAR(128) NOT NULL DEFAULT '',name VARCHAR(128) NOT NULL DEFAULT '',mother INT NOT NULL DEFAULT 0,level INT UNSIGNED NOT NULL DEFAULT 1,exp INT UNSIGNED NOT NULL DEFAULT 0,daily_exp INT UNSIGNED NOT NULL DEFAULT 0,last_exp_update_time DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,notice VARCHAR(800) NOT NULL DEFAULT '',residence_house_id INT UNSIGNED NOT NULL DEFAULT 0,interest SMALLINT NOT NULL DEFAULT 0,war_deposit INT UNSIGNED NOT NULL DEFAULT 0,war_wins INT UNSIGNED NOT NULL DEFAULT 0,war_losses INT UNSIGNED NOT NULL DEFAULT 0,war_draws INT UNSIGNED NOT NULL DEFAULT 0,daily_contribution_point INT UNSIGNED NOT NULL DEFAULT 0,last_contribution_point_added DATETIME NOT NULL DEFAULT '1970-01-01',last_assignment_update_time DATETIME NOT NULL DEFAULT '1970-01-01',war_enemy_expedition_id INT UNSIGNED NOT NULL DEFAULT 0,war_declared_at DATETIME NULL,war_protected_until DATETIME NULL,war_ends_at DATETIME NULL,war_kill_score INT UNSIGNED NOT NULL DEFAULT 0,war_is_declarer TINYINT(1) NOT NULL DEFAULT 0,created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE expedition_members (character_id INT UNSIGNED NOT NULL PRIMARY KEY, expedition_id INT NOT NULL, name VARCHAR(128) NOT NULL, level TINYINT NOT NULL, role TINYINT UNSIGNED NOT NULL, last_leave_time DATETIME NOT NULL, ability1 TINYINT NOT NULL, ability2 TINYINT NOT NULL, ability3 TINYINT NOT NULL, memo VARCHAR(63) NOT NULL, contribution_point INT UNSIGNED NOT NULL DEFAULT 0, weekly_contribution_point INT UNSIGNED NOT NULL DEFAULT 0, weekly_contribution_period_start DATE NOT NULL DEFAULT '1970-01-05');
CREATE TABLE characters (id INT UNSIGNED NOT NULL PRIMARY KEY, account_id INT UNSIGNED NOT NULL, name VARCHAR(128) NOT NULL, level TINYINT NOT NULL, heir_exp BIGINT UNSIGNED NOT NULL, faction_id INT UNSIGNED NOT NULL, ability1 TINYINT NOT NULL, ability2 TINYINT NOT NULL, ability3 TINYINT NOT NULL, expedition_id INT NOT NULL DEFAULT 0, family INT UNSIGNED NOT NULL DEFAULT 0, money BIGINT NOT NULL DEFAULT 0, expedition_rejoin_until BIGINT NOT NULL DEFAULT 0, deleted TINYINT(1) NOT NULL DEFAULT 0);
CREATE TABLE expedition_role_policies (expedition_id INT NOT NULL,role TINYINT UNSIGNED NOT NULL,name VARCHAR(128) NOT NULL,dominion_declare TINYINT(1) NOT NULL,invite TINYINT(1) NOT NULL,expel TINYINT(1) NOT NULL,promote TINYINT(1) NOT NULL,dismiss TINYINT(1) NOT NULL,chat TINYINT(1) NOT NULL,manager_chat TINYINT(1) NOT NULL,siege_master TINYINT(1) NOT NULL,join_siege TINYINT(1) NOT NULL,use_instance TINYINT(1) NOT NULL,PRIMARY KEY(expedition_id,role));
CREATE TABLE expedition_buff_purchases (expedition_id INT UNSIGNED NOT NULL,expedition_buff_id INT UNSIGNED NOT NULL,grade TINYINT UNSIGNED NOT NULL,PRIMARY KEY(expedition_id,expedition_buff_id));
CREATE TABLE expedition_daily_activity (expedition_id INT NOT NULL,character_id INT UNSIGNED NOT NULL,period_start DATETIME NOT NULL,contribution_used INT NOT NULL DEFAULT 0,PRIMARY KEY(expedition_id,character_id,period_start));
CREATE TABLE expedition_management_histories (id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,expedition_id INT NOT NULL,member_name VARCHAR(128) NOT NULL,history_type INT NOT NULL,amount BIGINT UNSIGNED NOT NULL,used_at DATETIME NOT NULL,detail_id INT UNSIGNED NOT NULL,detail_value INT NOT NULL);
CREATE TABLE expedition_shop_histories (id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,expedition_id INT NOT NULL,member_name VARCHAR(128) NOT NULL,item_id INT NOT NULL,stack INT NOT NULL,amount BIGINT UNSIGNED NOT NULL,purchased_at DATETIME(6) NOT NULL);
CREATE TABLE expedition_war_histories (id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,declarer_id INT NOT NULL,declarer_name VARCHAR(128) NOT NULL,defendant_id INT NOT NULL,defendant_name VARCHAR(128) NOT NULL,declared_at DATETIME NOT NULL,declarer_kills INT UNSIGNED NOT NULL,defendant_kills INT UNSIGNED NOT NULL);
CREATE TABLE expedition_portals (id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,expedition_id INT NOT NULL,name VARCHAR(128) NOT NULL,zone_id INT UNSIGNED NOT NULL,x FLOAT NOT NULL,y FLOAT NOT NULL,z FLOAT NOT NULL,z_rot FLOAT NOT NULL);
CREATE TABLE expedition_renames (expedition_id INT UNSIGNED PRIMARY KEY,last_renamed_at DATETIME NOT NULL);
CREATE TABLE expedition_instance_histories (history_id BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,expedition_id INT NOT NULL,instance_rank_detail_id INT UNSIGNED NOT NULL,instance_id INT UNSIGNED NOT NULL,score INT UNSIGNED NOT NULL,play_result TINYINT UNSIGNED NOT NULL,recorded_at DATETIME(6) NOT NULL);
CREATE TABLE expedition_instance_history_members (history_id BIGINT UNSIGNED NOT NULL,character_id BIGINT UNSIGNED NOT NULL,status TINYINT UNSIGNED NOT NULL,PRIMARY KEY(history_id,character_id));
CREATE TABLE item_containers (container_id BIGINT UNSIGNED NOT NULL,container_type VARCHAR(64) NOT NULL,slot_type INT NOT NULL,container_size INT NOT NULL,owner_id INT UNSIGNED NOT NULL,mate_id INT UNSIGNED NOT NULL DEFAULT 0,parent_item_id BIGINT UNSIGNED NOT NULL DEFAULT 0,PRIMARY KEY(container_id));
CREATE TABLE items (id BIGINT UNSIGNED NOT NULL,type VARCHAR(100) NOT NULL,template_id INT UNSIGNED NOT NULL,container_id BIGINT UNSIGNED NOT NULL DEFAULT 0,slot_type INT NOT NULL,slot INT NOT NULL,count INT NOT NULL,details BLOB,lifespan_mins INT NOT NULL,made_unit_id INT UNSIGNED NOT NULL DEFAULT 0,unsecure_time DATETIME NOT NULL,unpack_time DATETIME NOT NULL,owner INT UNSIGNED NOT NULL,created_at DATETIME NOT NULL,grade TINYINT NOT NULL,flags TINYINT UNSIGNED NOT NULL,ucc BIGINT UNSIGNED NOT NULL DEFAULT 0,expire_time DATETIME NULL,expire_online_minutes DOUBLE NOT NULL DEFAULT 0,charge_time DATETIME NULL,charge_count INT NOT NULL DEFAULT 0,PRIMARY KEY(id));
CREATE TABLE expedition_recruitments (expedition_id INT NOT NULL, interest_mask SMALLINT NOT NULL, introduction VARCHAR(100) NOT NULL, registered_at DATETIME(6) NOT NULL, expires_at DATETIME(6) NOT NULL, PRIMARY KEY(expedition_id));
CREATE TABLE expedition_recruitment_applications (expedition_id INT NOT NULL, character_id INT UNSIGNED NOT NULL, memo VARCHAR(100) NOT NULL, registered_at DATETIME(6) NOT NULL, PRIMARY KEY(expedition_id,character_id), KEY idx_character(character_id,registered_at), FOREIGN KEY(expedition_id) REFERENCES expedition_recruitments(expedition_id) ON DELETE CASCADE);
CREATE TABLE character_today_board_reset_counts (owner INT UNSIGNED NOT NULL,sort_id INT NOT NULL,day_key DATE NOT NULL,resets_used INT UNSIGNED NOT NULL DEFAULT 0,PRIMARY KEY(owner,sort_id));
CREATE TABLE expedition_public_assignments (expedition_id INT NOT NULL,period_start DATETIME NOT NULL,real_step INT UNSIGNED NOT NULL,group_id INT UNSIGNED NOT NULL,quest_context_id INT UNSIGNED NOT NULL,status TINYINT NOT NULL,objectives JSON NOT NULL,version INT UNSIGNED NOT NULL DEFAULT 0,selection_generation INT UNSIGNED NOT NULL DEFAULT 1,completed_at DATETIME NULL,guild_rewarded BOOLEAN NOT NULL DEFAULT FALSE,PRIMARY KEY(expedition_id,period_start,real_step),FOREIGN KEY(expedition_id) REFERENCES expeditions(id) ON DELETE CASCADE);
CREATE TABLE expedition_public_assignment_contributors (expedition_id INT NOT NULL,period_start DATETIME NOT NULL,real_step INT UNSIGNED NOT NULL,character_id INT UNSIGNED NOT NULL,character_name VARCHAR(128) NOT NULL,contribution BIGINT UNSIGNED NOT NULL DEFAULT 0,PRIMARY KEY(expedition_id,period_start,real_step,character_id),FOREIGN KEY(expedition_id,period_start,real_step) REFERENCES expedition_public_assignments(expedition_id,period_start,real_step) ON DELETE CASCADE);
CREATE TABLE expedition_public_assignment_claims (expedition_id INT NOT NULL,period_start DATETIME NOT NULL,real_step INT UNSIGNED NOT NULL,character_id INT UNSIGNED NOT NULL,character_name VARCHAR(128) NOT NULL,delivered_at DATETIME NULL,PRIMARY KEY(expedition_id,period_start,real_step,character_id),FOREIGN KEY(expedition_id,period_start,real_step) REFERENCES expedition_public_assignments(expedition_id,period_start,real_step) ON DELETE CASCADE);
""";
}
