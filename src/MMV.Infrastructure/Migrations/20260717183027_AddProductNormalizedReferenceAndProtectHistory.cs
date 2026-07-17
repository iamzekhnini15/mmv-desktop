using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductNormalizedReferenceAndProtectHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleItems_Products_ProductId",
                table: "SaleItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Products_ProductId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "idx_products_reference_unique",
                table: "Products");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedReference",
                table: "Products",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // P3-4B — Remplissage de la représentation normalisée pour les produits EXISTANTS, AVANT la création
            // de l'index unique.
            //
            // Règle applicative, source unique de vérité : Product.NormalizeReference(r) = r.Trim().ToUpperInvariant().
            // Le backfill doit produire pour CHAQUE référence héritée EXACTEMENT cette valeur, sinon échouer sans
            // rien modifier. Aucune valeur « approximativement normalisée » n'est acceptable : elle affaiblirait
            // silencieusement l'unicité (deux références que l'application tient pour égales pourraient coexister).
            //
            // La seule normalisation qu'une migration SQLite reproduit à l'IDENTIQUE — sans extension ICU, sans
            // dépendance à la culture de la machine, avec les mêmes fonctions natives que la migration soit jouée
            // par l'application ou par `dotnet ef` — est celle des références PUREMENT ASCII IMPRIMABLES : pour
            // elles, UPPER(TRIM(x)) de SQLite égale exactement Trim().ToUpperInvariant() (le UPPER natif ne replie
            // que a–z, comme l'invariant sur l'ASCII ; aucun autre caractère n'est modifié).
            //
            // Garde d'échec sûr (« exact ou rien ») : pour toute référence contenant un caractère HORS ASCII
            // imprimable — accents latins, alphabets grec/cyrillique, caractères de contrôle… — SQLite ne peut PAS
            // garantir l'égalité avec ToUpperInvariant. Plutôt qu'un backfill approximatif, la migration s'AVORTE :
            // la référence fautive est insérée dans une table à contrainte CHECK impossible (valeur non vide interdite),
            // ce qui lève une erreur et annule la transaction de migration. Aucune ligne n'est supprimée, fusionnée
            // ni modifiée ; l'ancien schéma et les données restent intacts ; la migration n'est PAS enregistrée dans
            // __EFMigrationsHistory. Le nom de la table de garde porte l'action opérateur : normaliser/nettoyer ces
            // références héritées via l'application (dont le setter recalcule NormalizedReference par ToUpperInvariant),
            // puis rejouer la migration. Les références enregistrées APRÈS migration passent toujours par
            // Product.NormalizeReference.
            migrationBuilder.Sql(
                "CREATE TABLE \"__abort_non_ascii_reference_needs_manual_normalization\" " +
                "(\"offending_reference\" TEXT NOT NULL CHECK (\"offending_reference\" = ''));\n" +
                "INSERT INTO \"__abort_non_ascii_reference_needs_manual_normalization\" (\"offending_reference\")\n" +
                "SELECT \"Reference\" FROM \"Products\" WHERE \"Reference\" GLOB '*[^ -~]*';\n" +
                "DROP TABLE \"__abort_non_ascii_reference_needs_manual_normalization\";");

            // À ce stade, toutes les références restantes sont ASCII imprimables : UPPER(TRIM(...)) reproduit
            // exactement Trim().ToUpperInvariant(). Si deux références convergent après normalisation, la création
            // de l'index unique ci-dessous ÉCHOUE volontairement (migration interrompue, données intactes) plutôt
            // que de fusionner ou supprimer des lignes.
            migrationBuilder.Sql(
                "UPDATE \"Products\" SET \"NormalizedReference\" = UPPER(TRIM(\"Reference\"));");

            migrationBuilder.CreateIndex(
                name: "idx_products_normalized_reference_unique",
                table: "Products",
                column: "NormalizedReference",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleItems_Products_ProductId",
                table: "SaleItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Products_ProductId",
                table: "StockMovements",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleItems_Products_ProductId",
                table: "SaleItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Products_ProductId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "idx_products_normalized_reference_unique",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "NormalizedReference",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "idx_products_reference_unique",
                table: "Products",
                column: "Reference",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleItems_Products_ProductId",
                table: "SaleItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Products_ProductId",
                table: "StockMovements",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
