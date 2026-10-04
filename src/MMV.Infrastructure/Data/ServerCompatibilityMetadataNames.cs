namespace MMV.Infrastructure.Data;

/// <summary>
/// Noms des objets serveur MMV <b>hors modèle EF</b> (P4-6B, ADR-PROD-DB-009 DP-3.5, DP-8), déclarés en un
/// seul point — même discipline que <see cref="Configuration.DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName"/>.
///
/// <para>
/// Un schéma dédié, propriété du rôle migrateur, porte deux tables aux droits différents : le rôle applicatif
/// lit <see cref="CompatibilityTable"/> et n'a <b>aucun</b> accès à <see cref="JournalTable"/>. Ces noms ne
/// changent <b>jamais</b> : un test les fige, pour qu'une modification soit un acte de revue.
/// </para>
/// </summary>
public static class ServerCompatibilityMetadataNames
{
    /// <summary>Schéma PostgreSQL dédié aux métadonnées de cycle de vie.</summary>
    public const string Schema = "mmv_meta";

    /// <summary>Métadonnée de compatibilité : une ligne unique, lisible par le rôle applicatif.</summary>
    public const string CompatibilityTable = "schema_compatibility";

    /// <summary>Journal autoritatif des exécutions de l'outil : jamais lisible par le rôle applicatif.</summary>
    public const string JournalTable = "migration_run";

    /// <summary><c>mmv_meta.schema_compatibility</c>.</summary>
    public const string QualifiedCompatibilityTable = Schema + "." + CompatibilityTable;

    /// <summary><c>mmv_meta.migration_run</c>.</summary>
    public const string QualifiedJournalTable = Schema + "." + JournalTable;
}
