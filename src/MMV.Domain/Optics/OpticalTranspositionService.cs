namespace MMV.Domain.Optics;

/// <summary>
/// Transposition optique <b>pure</b> (P3-6B) : exprime une même correction cylindrique sous sa forme équivalente
/// (cylindre de signe opposé, axe tourné de 90°) pour le <b>technicien / laboratoire</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>C'est une projection de lecture, jamais une mutation.</b> Le service est statique, sans état, sans dépôt et
/// sans dépendance EF : il reçoit un <see cref="OpticalCorrection"/> (value object immuable) et renvoie un
/// nouveau value object. Il ne peut donc <b>pas</b> modifier <c>Prescription</c>, <c>SaleItem</c> ou
/// <c>OrderItem</c> — l'ordonnance source reste intangible (roadmap P3 §1.3).
/// </para>
/// <para>
/// <b>Formule</b> (notes domaine §3.2) :
/// <code>
/// sphère'   = sphère + cylindre
/// cylindre' = −cylindre
/// axe'      = axe + 90        ; si axe' &gt; 180 → axe' − 180 ; puis normalisation 0 → 180
/// </code>
/// Exemple : <c>+2.00 (−1.00) axe 180</c> ⇔ <c>+1.00 (+1.00) axe 90</c>.
/// </para>
/// <para>
/// <b>Exactitude, sans arrondi arbitraire.</b> Les corrections sont saisies au quart de dioptrie — des fractions
/// binaires <b>exactes</b> en <see cref="double"/> — donc l'addition <c>sphère + cylindre</c> et l'opposé
/// <c>−cylindre</c> sont exacts ; l'axe est entier. Aucune tolérance ni arrondi n'est introduit : en ajouter
/// fabriquerait une imprécision là où il n'y en a pas.
/// </para>
/// <para>
/// <b>Involution.</b> Transposer deux fois une correction transposable redonne la correction d'origine sous sa
/// forme <b>canonique</b> (axe <c>0</c> ramené à <c>180</c>, cf. <see cref="OpticalAxisNormalizer"/>).
/// </para>
/// </remarks>
public static class OpticalTranspositionService
{
    /// <summary>Rotation d'axe appliquée par la transposition (degrés).</summary>
    private const int AxisRotationDegrees = 90;

    /// <summary>Demi-tour d'axe : au-delà, l'axe repasse dans le domaine utile <c>]0, 180]</c>.</summary>
    private const int AxisHalfTurnDegrees = 180;

    /// <summary>
    /// Renvoie la forme transposée de <paramref name="source"/>, ou la source inchangée lorsqu'il n'y a rien à
    /// transposer ou que la transposition est impossible.
    /// </summary>
    /// <remarks>
    /// Cas traités, tous sans invention de donnée :
    /// <list type="bullet">
    ///   <item><b>Cylindre absent</b> ⇒ aucune correction cylindrique à re-noter : source inchangée,
    ///         <c>IsTransposed = false</c>.</item>
    ///   <item><b>Cylindre nul</b> (<c>0</c> = « pas d'astigmatisme », même sémantique que
    ///         <c>PrescriptionValidator</c>) ⇒ identité, <c>IsTransposed = false</c>.</item>
    ///   <item><b>Cylindre non nul mais axe absent</b> ⇒ la donnée est déjà incohérente en amont (règle P3-3B :
    ///         un cylindre orientable exige un axe). La transposition <b>ne la corrige pas</b> et n'invente
    ///         aucun axe : source inchangée, <c>IsTransposed = false</c>.</item>
    ///   <item><b>Cylindre non nul et axe présent</b> ⇒ transposition appliquée, <c>IsTransposed = true</c>.</item>
    /// </list>
    /// Une valeur non finie (<c>NaN</c>, ±∞) est traitée comme non transposable : elle est refusée en amont par
    /// <c>PrescriptionValidator</c> et ne doit pas produire ici une notation absurde.
    /// </remarks>
    public static OpticalTranspositionResult Transpose(OpticalCorrection source)
    {
        var cylinder = source.Cylinder;

        // Rien à transposer : pas de cylindre, cylindre nul, ou valeur non finie (refusée en amont).
        if (!cylinder.HasValue || !double.IsFinite(cylinder.Value) || cylinder.Value == 0d)
        {
            return new OpticalTranspositionResult(false, source);
        }

        // Cylindre orientable sans axe : incohérence amont — on ne fabrique pas d'axe.
        if (!source.Axis.HasValue)
        {
            return new OpticalTranspositionResult(false, source);
        }

        // La sphère transposée n'a de sens que si la sphère source est exploitable. Une sphère absente reste
        // absente (on n'invente pas un 0.00), une sphère non finie n'est pas propagée en calcul.
        var sphere = source.Sphere;
        double? transposedSphere = sphere.HasValue && double.IsFinite(sphere.Value)
            ? sphere.Value + cylinder.Value
            : sphere;

        var transposed = new OpticalCorrection(
            Sphere: transposedSphere,
            Cylinder: -cylinder.Value,
            Axis: RotateAxis(source.Axis.Value));

        return new OpticalTranspositionResult(true, transposed);
    }

    /// <summary>
    /// Tourne l'axe de 90° en le maintenant dans le domaine utile, puis applique la convention canonique
    /// <c>0 → 180</c> via <see cref="OpticalAxisNormalizer"/> (source unique de cette convention, posée en P3-3B).
    /// </summary>
    private static int RotateAxis(int axis)
    {
        var rotated = axis + AxisRotationDegrees;
        if (rotated > AxisHalfTurnDegrees)
        {
            rotated -= AxisHalfTurnDegrees;
        }

        // NormalizeAxis renvoie int? mais ne peut pas produire null pour une entrée non nulle.
        return OpticalAxisNormalizer.NormalizeAxis(rotated)!.Value;
    }
}
