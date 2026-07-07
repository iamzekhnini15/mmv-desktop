namespace MMV.Application.UseCases.Products.ListProductsForOrderPicker;

/// <summary>
/// Entrée (query) du use case <see cref="ListProductsForOrderPickerUseCase"/> — « Lister les produits pour le
/// sélecteur de commande » (P2D-6). Cette lecture n'a pas de critère (le formulaire charge tout le catalogue puis
/// filtre par type d'article et recherche en présentation, iso-fonctionnel). Objet conservé pour l'homogénéité des
/// query use cases et la garde « query nulle ».
/// </summary>
public sealed class ListProductsForOrderPickerQuery
{
}
