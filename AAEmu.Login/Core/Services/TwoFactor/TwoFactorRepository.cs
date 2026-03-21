using AAEmu.Login.Models;
using AAEmu.Login.Models.TwoFactor;
using AAEmu.Login.Utils;
using MySql.Data.MySqlClient;

namespace AAEmu.Login.Core.Services.TwoFactor;

public class TwoFactorRepository(IMySqlConnectionFactory connectionFactory) : ITwoFactorRepository
{
    public async Task<TwoFactorConfig?> GetConfigAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        await using var connection = connectionFactory.CreateConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT user_id, enabled_methods, otp_secret, otp_verified,
                   cert_pin_hash, ars_phone_number, ars_phone_verified,
                   created_at, updated_at
            FROM user_2fa
            WHERE user_id = @user_id
            """;
        command.Parameters.AddWithValue("@user_id", accountId.Value);

        await using var reader = (MySqlDataReader)await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var userIdOrdinal = reader.GetOrdinal("user_id");
        var enabledMethodsOrdinal = reader.GetOrdinal("enabled_methods");
        var otpSecretOrdinal = reader.GetOrdinal("otp_secret");
        var otpVerifiedOrdinal = reader.GetOrdinal("otp_verified");
        var certPinHashOrdinal = reader.GetOrdinal("cert_pin_hash");
        var arsPhoneNumberOrdinal = reader.GetOrdinal("ars_phone_number");
        var arsPhoneVerifiedOrdinal = reader.GetOrdinal("ars_phone_verified");
        var createdAtOrdinal = reader.GetOrdinal("created_at");
        var updatedAtOrdinal = reader.GetOrdinal("updated_at");

        return new TwoFactorConfig(
            UserId: new AccountId(reader.GetUInt32(userIdOrdinal)),
            EnabledMethods: (TwoFactorMethod)reader.GetByte(enabledMethodsOrdinal),
            OtpSecret: reader.IsDBNull(otpSecretOrdinal) ? null : reader.GetString(otpSecretOrdinal),
            OtpVerified: reader.GetBoolean(otpVerifiedOrdinal),
            CertPinHash: reader.IsDBNull(certPinHashOrdinal) ? null : reader.GetString(certPinHashOrdinal),
            ArsPhoneNumber: reader.IsDBNull(arsPhoneNumberOrdinal) ? null : reader.GetString(arsPhoneNumberOrdinal),
            ArsPhoneVerified: reader.GetBoolean(arsPhoneVerifiedOrdinal),
            CreatedAt: reader.GetInt64(createdAtOrdinal),
            UpdatedAt: reader.GetInt64(updatedAtOrdinal)
        );
    }
}
