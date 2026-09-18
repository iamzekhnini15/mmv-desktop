using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MMV.Infrastructure.Data.Time;

/// <summary>
/// Convertisseur de valeur <b>validant</b> appliqué à <b>toutes</b> les propriétés d'instants du modèle EF
/// (P4-5D, obligation T3 d'<c>ADR-PROD-DB-004</c> §5.4 — option D3).
///
/// <list type="bullet">
///   <item><description>
///   <b>Écriture</b> — <see cref="NonUtcDateTimeException"/> si <c>Kind != Utc</c>. Aucune correction
///   silencieuse : l'option D1 (<c>ToUniversalTime()</c> automatique) a été rejetée par l'ADR parce
///   qu'elle <b>masque</b> la faute au lieu de la révéler.
///   </description></item>
///   <item><description>
///   <b>Lecture</b> — <c>DateTime.SpecifyKind(value, Utc)</c>. Ce n'est pas une conversion : la valeur
///   stockée <i>est</i> UTC par l'invariant d'écriture, seul son étiquetage diffère selon le provider.
///   SQLite rend <see cref="DateTimeKind.Unspecified"/>, PostgreSQL rend <see cref="DateTimeKind.Utc"/> ;
///   sans cette branche, tout code comparant ou formatant une date changerait de comportement entre les
///   deux moteurs <b>sans que rien n'échoue</b> (ADR-PROD-DB-004 §4.2).
///   </description></item>
/// </list>
///
/// <para>
/// <b>Indépendant du provider — volontairement.</b> Le convertisseur n'interroge jamais
/// <c>Database.ProviderName</c> et n'est pas construit par <c>ModelPortability</c> : il ne choisit rien,
/// il impose le même invariant des deux côtés. C'est ce qui permet à la suite SQLite existante de
/// détecter, <b>sans serveur PostgreSQL</b>, une écriture que Npgsql refuserait en production (§2.1).
/// </para>
///
/// <para>
/// <b>Le schéma n'est pas modifié.</b> Le type fourni au provider reste <see cref="DateTime"/> : aucune
/// colonne ne change de type physique, aucune migration n'est requise, et le contrôle de dérive de modèle
/// EF reste vert (ADR-PROD-DB-004 §7.3).
/// </para>
///
/// <para>
/// <b>Les dates civiles ne passent pas par ici.</b> <c>Customer.BirthDate</c> et
/// <c>Prescription.IssueDate</c> sont des <see cref="DateOnly"/> depuis P4-5D : elles ne sont pas des
/// instants, ne portent aucun <see cref="DateTimeKind"/>, et sont donc hors du périmètre de
/// <see cref="ApplyTo"/> par construction — pas par exception déclarée (§5, décision 7).
/// </para>
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    /// <summary>Instance partagée (le convertisseur est sans état).</summary>
    public static readonly UtcDateTimeConverter Instance = new();

    private UtcDateTimeConverter()
        : base(v => EnsureUtc(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }

    /// <summary>
    /// Vérifie qu'un instant est admissible en persistance et le renvoie inchangé.
    /// </summary>
    /// <exception cref="NonUtcDateTimeException">Si <paramref name="value"/> ne porte pas <c>Kind = Utc</c>.</exception>
    public static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new NonUtcDateTimeException(value);

    /// <summary>
    /// Applique le convertisseur à <b>toute</b> propriété <see cref="DateTime"/> ou <c>DateTime?</c> du
    /// modèle, entités et propriétés de navigation possédées comprises.
    ///
    /// <para>
    /// <b>Balayage exhaustif plutôt qu'énumération.</b> Une liste écrite à la main des 19 colonnes
    /// d'instants serait une seconde source de vérité : la première propriété datée ajoutée après P4-5D y
    /// échapperait en silence — et le silence est précisément le mode de défaillance que cet ADR combat.
    /// Le balayage, lui, couvre par construction toute propriété future.
    /// </para>
    /// </summary>
    /// <param name="modelBuilder">Constructeur de modèle, appelé en fin d'<c>OnModelCreating</c>.</param>
    /// <returns>Le nombre de propriétés effectivement protégées — mesurable par les tests.</returns>
    public static int ApplyTo(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var applied = 0;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType != typeof(DateTime) && property.ClrType != typeof(DateTime?))
                {
                    continue;
                }

                // Un convertisseur déjà déclaré au site de la propriété serait une décision locale
                // concurrente de l'invariant global : on ne l'écrase pas, on le laisse visible.
                if (property.GetValueConverter() is not null)
                {
                    continue;
                }

                property.SetValueConverter(Instance);
                applied++;
            }
        }

        return applied;
    }
}
