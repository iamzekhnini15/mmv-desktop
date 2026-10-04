using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MMV.Infrastructure.Configuration;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P4-8 (D-13) — configuration du poste protégée par le <b>vrai DPAPI CurrentUser</b> (suite unitaire Windows) :
/// aller-retour, aucun champ en clair dans le fichier, refus explicite d'un fichier corrompu, altéré, tronqué ou d'un
/// format inconnu, écriture atomique et remplaçable (rotation).
/// </summary>
public sealed class WorkstationDatabaseSettingsFileTests : IDisposable
{
    private const string Secret = "unit-Workstation-Secret-0123456789";
    private const string Host = "srv-mmv.magasin.lan";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mmv-ws-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string FilePath => Path.Combine(_directory, WorkstationDatabaseSettingsFile.FileName);

    private WorkstationDatabaseSettingsFile File() => new(FilePath, new DpapiCurrentUserSecretProtector());

    private static PostgreSqlConnectionSettings Settings(string secret = Secret) => new(Host, 5433, "mmv", "mmv_poste", secret, null);

    [Fact]
    public void Dpapi_round_trip_restores_the_secret_and_the_ciphertext_never_contains_it()
    {
        var protector = new DpapiCurrentUserSecretProtector();
        var plaintext = Encoding.UTF8.GetBytes(Secret);

        var protectedBytes = protector.Protect(plaintext);

        Encoding.UTF8.GetString(protector.Unprotect(protectedBytes)).Should().Be(Secret);
        Convert.ToHexString(protectedBytes).Should().NotContain(Convert.ToHexString(plaintext));
        protectedBytes.Should().NotEqual(protector.Protect(plaintext), "DPAPI sale chaque protection");
    }

    [Fact]
    public void Dpapi_refuses_altered_ciphertext()
    {
        var protector = new DpapiCurrentUserSecretProtector();
        var protectedBytes = protector.Protect(Encoding.UTF8.GetBytes(Secret));
        protectedBytes[^5] ^= 0xFF;

        var act = () => protector.Unprotect(protectedBytes);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Save_then_load_round_trips_and_no_field_is_stored_in_clear()
    {
        var file = File();

        file.Save(Settings());

        file.Load().Should().Be(Settings());
        var stored = System.IO.File.ReadAllText(FilePath);
        stored.Should().NotContain(Secret).And.NotContain(Host).And.NotContain("mmv_poste");
        stored.Should().Contain(WorkstationDatabaseSettingsFile.FormatName).And.Contain(DpapiCurrentUserSecretProtector.SchemeName);
    }

    [Fact]
    public void Save_replaces_the_previous_configuration_atomically()
    {
        var file = File();
        file.Save(Settings());

        file.Save(Settings("unit-Rotated-Secret-0123456789ab"));

        file.Load().Password.Should().Be("unit-Rotated-Secret-0123456789ab");
        Directory.GetFiles(_directory).Should().ContainSingle("aucun fichier temporaire ne subsiste");
    }

    [Fact]
    public void Default_path_is_in_the_user_data_folder_outside_any_installation_folder()
    {
        WorkstationDatabaseSettingsFile.DefaultPath.Should()
            .StartWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            .And.EndWith(Path.Combine("ManageMyVision", WorkstationDatabaseSettingsFile.FileName));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"Format\":\"other/9\",\"Protection\":\"dpapi-current-user\",\"Payload\":\"AAAA\"}")]
    [InlineData("{\"Format\":\"mmv-workstation-database/1\",\"Protection\":\"none\",\"Payload\":\"AAAA\"}")]
    [InlineData("{\"Format\":\"mmv-workstation-database/1\",\"Protection\":\"dpapi-current-user\",\"Payload\":\"\"}")]
    [InlineData("{\"Format\":\"mmv-workstation-database/1\",\"Protection\":\"dpapi-current-user\",\"Payload\":\"%%%not-base64\"}")]
    [InlineData("{\"Format\":\"mmv-workstation-database/1\",\"Protection\":\"dpapi-current-user\",\"Payload\":\"AAECAwQFBgcICQ==\"}")]
    public void Invalid_files_are_refused_explicitly_never_ignored(string content)
    {
        Directory.CreateDirectory(_directory);
        System.IO.File.WriteAllText(FilePath, content);

        var act = () => File().Load();

        act.Should().Throw<DatabaseConfigurationException>()
            .Which.Message.Should().Contain("inutilisable").And.Contain("configure-workstation");
    }

    [Fact]
    public void Corrupted_payload_is_refused_without_leaking_anything()
    {
        var file = File();
        file.Save(Settings());
        var envelope = JsonDocument.Parse(System.IO.File.ReadAllText(FilePath)).RootElement;
        var payload = Convert.FromBase64String(envelope.GetProperty("Payload").GetString()!);
        payload[payload.Length / 2] ^= 0x01;
        System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(new
        {
            Format = envelope.GetProperty("Format").GetString(),
            Protection = envelope.GetProperty("Protection").GetString(),
            Payload = Convert.ToBase64String(payload)
        }));

        var act = () => file.Load();

        act.Should().Throw<DatabaseConfigurationException>()
            .Which.Message.Should().Contain("corrompu").And.NotContain(Secret);
    }

    [Fact]
    public void Truncated_file_is_refused()
    {
        var file = File();
        file.Save(Settings());
        var bytes = System.IO.File.ReadAllBytes(FilePath);
        System.IO.File.WriteAllBytes(FilePath, bytes[..(bytes.Length / 2)]);

        var act = () => file.Load();

        act.Should().Throw<DatabaseConfigurationException>();
    }

    [Fact]
    public void Decrypted_payload_missing_a_field_is_refused()
    {
        var protector = new DpapiCurrentUserSecretProtector();
        Directory.CreateDirectory(_directory);
        var payload = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new { Host, Port = 5432, Database = "mmv" }));
        System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(new
        {
            Format = WorkstationDatabaseSettingsFile.FormatName,
            Protection = DpapiCurrentUserSecretProtector.SchemeName,
            Payload = Convert.ToBase64String(payload)
        }));

        var act = () => File().Load();

        act.Should().Throw<DatabaseConfigurationException>().Which.Message.Should().Contain("manquant");
    }

    [Fact]
    public void Invalid_settings_are_never_written()
    {
        var file = File();

        var act = () => file.Save(new PostgreSqlConnectionSettings("", 5432, "mmv", "r", Secret, null));

        act.Should().Throw<DatabaseConfigurationException>();
        file.Exists.Should().BeFalse();
    }
}
