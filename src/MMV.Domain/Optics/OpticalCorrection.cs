namespace MMV.Domain.Optics;

/// <summary>
/// Correction optique <b>immuable</b> réduite au trio sphère / cylindre / axe (P3-6B) — la seule matière de la
/// <b>transposition</b>.
/// </summary>
/// <remarks>
/// <para>
/// Ce type est volontairement <b>étroit</b> : il ne porte ni addition, ni prisme, ni base, ni acuité. Il est donc
/// <b>structurellement impossible</b> qu'un service de transposition altère ces champs — ils ne lui sont jamais
/// confiés. C'est la garantie la plus forte de la règle « la transposition ne touche que sphère/cylindre/axe ».
/// </para>
/// <para>
/// Value object sans identité ni état mutable : le transporter ne peut pas modifier une entité
/// (<c>Prescription</c>, <c>SaleItem</c>, <c>OrderItem</c>) — il en est une simple <i>lecture</i>.
/// </para>
/// </remarks>
/// <param name="Sphere">Sphère (dioptries), ou <c>null</c> si non renseignée.</param>
/// <param name="Cylinder">Cylindre (dioptries), ou <c>null</c> si non renseigné.</param>
/// <param name="Axis">Axe (degrés), ou <c>null</c> si non renseigné.</param>
public readonly record struct OpticalCorrection(double? Sphere, double? Cylinder, int? Axis);
