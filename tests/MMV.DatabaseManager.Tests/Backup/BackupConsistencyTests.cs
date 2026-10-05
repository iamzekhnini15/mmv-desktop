using FluentAssertions;
using MMV.DatabaseManager.Backup;

namespace MMV.DatabaseManager.Tests.Backup;

/// <summary>
/// P4-9 — rapprochement sauvegarde ⇔ base : contenu restauré (vérification), puis base réelle sous le verrou
/// (même installation, même base, aucune écriture depuis, âge serveur borné).
/// </summary>
public sealed class BackupConsistencyTests
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(2);

    private static BackupManifest Manifest() => new(Guid.NewGuid(), "1.0.0", "OP", "bkp", "17.10",
        TemporaryBackup.State(), "x.dump", 1, new string('0', 64));

    private static DatabaseFingerprint Live(Func<DatabaseFingerprint, DatabaseFingerprint>? change = null, TimeSpan? after = null)
    {
        var live = TemporaryBackup.State(TemporaryBackup.CreatedAt + (after ?? TimeSpan.FromMinutes(10)));
        return change is null ? live : change(live);
    }

    [Fact]
    public void Same_installation_same_database_same_content_within_age_is_accepted()
    {
        BackupConsistency.CheckCurrent(Manifest(), Live(), MaximumAge).Should().BeNull();
    }

    [Fact]
    public void Exactly_the_maximum_age_is_accepted_and_one_second_more_is_refused()
    {
        BackupConsistency.CheckCurrent(Manifest(), Live(after: MaximumAge), MaximumAge).Should().BeNull();
        BackupConsistency.CheckCurrent(Manifest(), Live(after: MaximumAge + TimeSpan.FromSeconds(1)), MaximumAge)
            .Should().Contain("trop ancienne");
    }

    [Fact]
    public void Backup_dated_after_the_server_clock_is_refused()
    {
        BackupConsistency.CheckCurrent(Manifest(), Live(after: TimeSpan.FromSeconds(-1)), MaximumAge)
            .Should().Contain("postérieur");
    }

    [Fact]
    public void Backup_of_another_installation_is_refused()
    {
        BackupConsistency.CheckCurrent(Manifest(), Live(l => l with { SystemIdentifier = "7499999999999999999" }), MaximumAge)
            .Should().Contain("autre installation");
    }

    [Fact]
    public void Backup_of_another_database_is_refused_by_name_and_by_oid()
    {
        BackupConsistency.CheckCurrent(Manifest(), Live(l => l with { Database = "mmv_other" }), MaximumAge)
            .Should().Contain("autre base");
        BackupConsistency.CheckCurrent(Manifest(), Live(l => l with { DatabaseOid = 99999 }), MaximumAge)
            .Should().Contain("autre base", "une base supprimée puis recréée sous le même nom n'est pas la base sauvegardée");
    }

    [Fact]
    public void Different_migration_history_is_refused()
    {
        BackupConsistency.CheckCurrent(Manifest(),
                Live(l => l with { AppliedMigrations = [.. l.AppliedMigrations, "20261001000000_Next"] }), MaximumAge)
            .Should().Contain("historique de migration différent");
    }

    [Fact]
    public void Rows_written_after_the_backup_are_refused()
    {
        BackupConsistency.CheckCurrent(Manifest(),
                Live(l => l with { Tables = l.Tables.Select(t => t.Table == "Customers" ? t with { Rows = 13 } : t).ToArray() }),
                MaximumAge)
            .Should().Contain("la base a changé").And.Contain("public.Customers (12 → 13)");
    }

    [Fact]
    public void Missing_or_extra_tables_are_refused()
    {
        BackupConsistency.CompareContent(Manifest(), Live(l => l with { Tables = l.Tables.Skip(1).ToArray() }))
            .Should().Contain("absente(s) de la base").And.Contain("mmv_meta.migration_run");
        BackupConsistency.CompareContent(Manifest(), Live(l => l with { Tables = [.. l.Tables, new TableRowCount("public", "Extra", 0)] }))
            .Should().Contain("absente(s) de la sauvegarde").And.Contain("public.Extra");
    }

    [Fact]
    public void Content_comparison_ignores_identity_because_the_restored_database_is_another_one()
    {
        BackupConsistency.CompareContent(Manifest(),
                Live(l => l with { Database = "mmv_restore_check_x", DatabaseOid = 77777 }))
            .Should().BeNull();
    }
}
