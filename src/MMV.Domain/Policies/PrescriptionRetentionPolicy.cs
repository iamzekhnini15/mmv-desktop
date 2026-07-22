namespace MMV.Domain.Policies;

/// <summary>
/// Propriétaire <b>unique</b> de la règle de conservation des ordonnances (P3-12). Domain pur : aucune dépendance
/// EF, SQLite, Application ni UI.
/// </summary>
/// <remarks>
/// <para>
/// <b>La règle.</b> Une ordonnance <b>existante</b> ne peut jamais être supprimée physiquement. La règle est
/// <b>inconditionnelle</b> : elle ne dépend ni de l'âge de l'ordonnance, ni de son auteur, ni d'un quelconque
/// indice d'usage. C'est pourquoi cette politique n'expose <b>aucun prédicat</b> — il n'existe pas d'état dans
/// lequel la suppression serait permise, et offrir un <c>CanDelete(…)</c> qui répondrait invariablement
/// <c>false</c> laisserait croire au lecteur qu'une condition existe.
/// </para>
/// <para>
/// <b>Pourquoi inconditionnelle.</b> L'ordonnance est un enregistrement médical source. Autoriser la suppression
/// « lorsqu'elle est inutilisée » supposerait de savoir la reconnaître, ce que le modèle ne permet pas : il
/// n'existe <b>aucune</b> clé étrangère <c>SaleItem → Prescription</c> ni <c>OrderItem → Prescription</c> (audit
/// P3-12 §13.1). Le lien entre une vente et l'ordonnance qui l'a motivée n'est pas représenté. Déduire cet usage
/// des dates, ou d'une ressemblance entre valeurs optiques, serait une <b>heuristique</b> — et une heuristique qui
/// se trompe détruit un dossier médical sans trace. En l'absence de preuve, la conservation prime.
/// </para>
/// <para>
/// <b>Ce que la règle ne prétend pas être.</b> Elle n'est ni un archivage, ni un versionnement, ni une
/// immutabilité forte : une ordonnance reste <b>modifiable</b> (<c>UpdatePrescriptionUseCase</c>), et deux postes
/// qui la corrigent concurremment s'écrasent toujours en silence. Ces sujets — archivage, FK
/// <c>Prescription ↔ Sale</c>, concurrence — restent des entrées du futur cadrage ; P3-12 ferme uniquement la
/// perte <b>définitive</b> de l'enregistrement.
/// </para>
/// <para>
/// <b>Pourquoi en Domain.</b> Avant P3-12, la seule chose qui retenait l'exploitant était une boîte de dialogue de
/// confirmation dans <c>CustomerPrescriptionsViewModel</c>. Une confirmation d'interface rend l'acte délibéré ;
/// elle ne le rend pas impossible, et tout appelant entrant par la couche Application la contournait. Déclarer
/// l'invariant ici — et non dans la ViewModel — en fait une affirmation sur la nature de la donnée, que le Domain
/// possède, plutôt qu'un détail d'écran.
/// </para>
/// <para>
/// <b>Ce que cette classe fait, et ne fait pas.</b> Elle <b>déclare</b> l'invariant de conservation et porte son
/// message ; elle ne l'<b>exécute</b> pas — une constante n'intercepte rien. Le point d'opposition effectif est
/// <c>MMV.Application.UseCases.Prescriptions.DeletePrescription.DeletePrescriptionUseCase</c>, seul chemin runtime
/// de suppression d'ordonnance du dépôt, qui lève une <c>BusinessRuleException</c> portant ce message avant toute
/// mutation. Déclaration et opposition sont donc deux responsabilités distinctes, tenues par deux couches.
/// </para>
/// </remarks>
public static class PrescriptionRetentionPolicy
{
    /// <summary>
    /// Message métier stable opposé à toute tentative de suppression physique d'une ordonnance existante.
    /// Exposé en constante pour que le use case, les tests et une future UI s'y réfèrent sans dupliquer de
    /// littéral (patron P3-2B / P3-3B). Ne divulgue aucun détail technique (EF, SQLite, table, clé étrangère).
    /// </summary>
    public const string PhysicalDeletionForbiddenMessage =
        "La suppression physique d'une ordonnance est interdite afin de préserver l'historique médical.";
}
