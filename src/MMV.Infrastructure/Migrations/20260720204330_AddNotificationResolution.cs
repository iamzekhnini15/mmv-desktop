using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <summary>
    /// P3-8 — Introduit la <b>résolution métier</b> des notifications et protège l'unicité des alertes de stock bas
    /// actives. Migration <b>additive</b> en trois temps, dont l'ordre est contraint :
    /// <list type="number">
    ///   <item>ajout de la colonne nullable <c>ResolvedAt</c> ;</item>
    ///   <item><b>dédoublonnage déterministe</b> des alertes <c>LowStock/Product</c> historiques ;</item>
    ///   <item>création de l'index unique filtré.</item>
    /// </list>
    /// L'étape 2 ne peut pas être omise : le défaut corrigé par P3-8 produisait un doublon à chaque connexion suivant
    /// un « tout marquer comme lu ». Sur toute base ayant vécu, créer l'index avant de dédoublonner échouerait.
    /// </summary>
    public partial class AddNotificationResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "Notifications",
                type: "TEXT",
                nullable: true);

            // ------------------------------------------------------------------------------------------------
            // Normalisation déterministe des doublons historiques — AUCUNE ligne n'est supprimée.
            // ------------------------------------------------------------------------------------------------
            //
            // Pour chaque groupe (Type='LowStock', EntityType='Product', même EntityId non nul), EXACTEMENT une
            // ligne reste active ; toutes les autres sont fermées.
            //
            // Choix du gardien, dans cet ordre :
            //   1. IsRead ASC          — une alerte NON LUE est préférée. Stockée en INTEGER 0/1, donc 0 (non lue)
            //                            trie avant 1 (lue). Fermer la seule alerte que l'opérateur n'a pas encore
            //                            vue, en gardant active une alerte déjà consultée, ferait disparaître
            //                            l'information du badge sans que personne ne l'ait jamais lue.
            //   2. NotificationId ASC  — départage : la plus ancienne l'emporte. Le critère est total, donc le
            //                            résultat est reproductible à l'identique sur toute base, indépendamment de
            //                            l'ordre physique des lignes.
            //
            // Les lignes fermées reçoivent ResolvedAt = CreatedAt, JAMAIS l'heure d'exécution de la migration ni une
            // date globale arbitraire. Sémantique assumée et documentée : c'est la fermeture TECHNIQUE d'une ligne
            // dupliquée, pas une affirmation sur la date réelle de réapprovisionnement — qu'aucune donnée en base ne
            // permet de reconstituer. Utiliser l'heure de migration inventerait un événement métier collectif qui
            // n'a jamais eu lieu ; utiliser CreatedAt maintient chaque doublon à sa propre place dans l'historique.
            //
            // Seuls les LowStock/Product sont concernés : les faits historiques (OrderStatusChanged,
            // PaymentReceived, Info, StockOut de seed) ne sont jamais touchés, et plusieurs lignes par entité y
            // restent parfaitement légitimes.
            migrationBuilder.Sql(
                "UPDATE \"Notifications\"\n" +
                "SET \"ResolvedAt\" = \"CreatedAt\"\n" +
                "WHERE \"Type\" = 'LowStock'\n" +
                "  AND \"EntityType\" = 'Product'\n" +
                "  AND \"EntityId\" IS NOT NULL\n" +
                "  AND \"ResolvedAt\" IS NULL\n" +
                "  AND \"NotificationId\" NOT IN (\n" +
                "      SELECT \"NotificationId\" FROM (\n" +
                "          SELECT \"NotificationId\",\n" +
                "                 ROW_NUMBER() OVER (\n" +
                "                     PARTITION BY \"EntityId\"\n" +
                "                     ORDER BY \"IsRead\" ASC, \"NotificationId\" ASC\n" +
                "                 ) AS \"rn\"\n" +
                "          FROM \"Notifications\"\n" +
                "          WHERE \"Type\" = 'LowStock'\n" +
                "            AND \"EntityType\" = 'Product'\n" +
                "            AND \"EntityId\" IS NOT NULL\n" +
                "            AND \"ResolvedAt\" IS NULL\n" +
                "      )\n" +
                "      WHERE \"rn\" = 1\n" +
                "  );");

            migrationBuilder.CreateIndex(
                name: "idx_notifications_active_low_stock_unique",
                table: "Notifications",
                columns: new[] { "Type", "EntityType", "EntityId" },
                unique: true,
                filter: "\"Type\" = 'LowStock' AND \"EntityType\" = 'Product' AND \"EntityId\" IS NOT NULL AND \"ResolvedAt\" IS NULL");
        }

        /// <summary>
        /// Rollback.
        ///
        /// <para>
        /// <b>Perte assumée et irréversible d'information.</b> Supprimer <c>ResolvedAt</c> efface la distinction
        /// entre alerte active et alerte terminée. Les doublons historiques fermés par le <c>Up</c> redeviennent donc
        /// implicitement actifs — l'ancien schéma ne sait pas les représenter autrement —, et rejouer le <c>Up</c>
        /// les refermerait sur leur <c>CreatedAt</c>, à l'identique. Aucune ligne n'est perdue dans un sens comme
        /// dans l'autre : seule l'information de résolution disparaît.
        /// </para>
        ///
        /// <para>
        /// La suppression de colonne est ici sûre : SQLite supporte <c>DROP COLUMN</c> depuis la version 3.35, et
        /// <c>ResolvedAt</c> n'est référencée par aucune contrainte survivante une fois l'index filtré supprimé —
        /// d'où l'ordre index puis colonne.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_notifications_active_low_stock_unique",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Notifications");
        }
    }
}
