namespace MMV.Domain.Entities;

/// <summary>
/// Précondition de concurrence d'une création de version de fiche atelier (P3-6B, durcissement) :
/// <b>quelle version courante l'appelant a lue</b> avant de demander la suivante.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi une précondition explicite.</b> Calculer le prochain numéro par <c>MAX(Version) + 1</c> ne protège
/// de rien en multi-poste : deux postes ayant tous deux lu la version 1 produiraient une v2 <b>et</b> une v3, la
/// seconde s'appuyant silencieusement sur un travail qu'elle n'a jamais vu. En transmettant l'identité de la
/// version lue, la bascule devient un <b>compare-and-swap</b> : elle ne réussit que si cette version est encore
/// la version courante, et le numéro suivant en découle au lieu d'être redécouvert.
/// </para>
/// <para>
/// <see cref="None"/> exprime « aucune version n'existait à la lecture » : la création vise alors la version 1,
/// et c'est l'unicité <c>(OrderId, Version)</c> qui arbitre une éventuelle création concurrente.
/// </para>
/// </remarks>
public readonly record struct WorkshopSheetVersionPrecondition
{
    private WorkshopSheetVersionPrecondition(long? currentSheetId, int currentVersion)
    {
        CurrentWorkshopSheetId = currentSheetId;
        CurrentVersion = currentVersion;
    }

    /// <summary>Identifiant de la version courante lue par l'appelant, ou <c>null</c> s'il n'en a lu aucune.</summary>
    public long? CurrentWorkshopSheetId { get; }

    /// <summary>Numéro de la version courante lue (<c>0</c> lorsqu'aucune version n'existait).</summary>
    public int CurrentVersion { get; }

    /// <summary>Numéro de la version que cette précondition autorise à créer.</summary>
    public int NextVersion => CurrentVersion + 1;

    /// <summary>Vrai lorsqu'une version courante était présente et doit encore l'être au moment de la bascule.</summary>
    public bool ExpectsExistingVersion => CurrentWorkshopSheetId.HasValue;

    /// <summary>Aucune version n'existait : la création vise la version 1.</summary>
    public static WorkshopSheetVersionPrecondition None { get; } = new(null, 0);

    /// <summary>La version <paramref name="sheet"/> était courante à la lecture ; elle doit l'être encore.</summary>
    public static WorkshopSheetVersionPrecondition From(WorkshopSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return new WorkshopSheetVersionPrecondition(sheet.WorkshopSheetId, sheet.Version);
    }
}
