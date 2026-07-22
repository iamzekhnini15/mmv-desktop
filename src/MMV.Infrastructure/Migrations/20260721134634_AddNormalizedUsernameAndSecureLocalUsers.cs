using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMV.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedUsernameAndSecureLocalUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P3-10 — Identifiant de connexion non ambigu entre postes.
            //
            // Jusqu'ici l'unicité du login reposait sur idx_users_username_unique, un index BINARY sur Username :
            // « admin », « Admin » et « ADMIN » y étaient trois comptes distincts. Deux postes pouvaient créer des
            // logins que l'exploitant tient pour identiques, et l'authentification devenait ambiguë (audit §18).
            // La colonne NormalizedUsername porte désormais la clé métier, protégée par un index unique.
            //
            // La colonne est créée NOT NULL avec une valeur par défaut vide TRANSITOIRE : SQLite l'exige pour un
            // ALTER TABLE ADD COLUMN NOT NULL. Le backfill ci-dessous la remplit pour chaque ligne, et une garde
            // finale vérifie qu'aucune ligne ne conserve cette valeur vide avant la création de l'index.
            migrationBuilder.AddColumn<string>(
                name: "NormalizedUsername",
                table: "Users",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // --- Garde « exact ou échec » (principe P3-4B) -----------------------------------------------------
            //
            // Règle applicative, source unique de vérité :
            //     UserIdentityPolicy.NormalizeUsername(u) = u.Trim().ToLowerInvariant().
            //
            // Le backfill doit produire EXACTEMENT cette valeur pour chaque login hérité, sinon échouer sans rien
            // modifier. Une normalisation « approximative » affaiblirait silencieusement l'unicité : deux logins
            // que l'application tient pour égaux pourraient coexister en base, ce qui est précisément le défaut
            // corrigé ici.
            //
            // lower(trim(x)) de SQLite égale Trim().ToLowerInvariant() SI ET SEULEMENT SI x est composé du jeu
            // ASCII autorisé par UserValidator (lettres, chiffres, point, underscore, tiret) :
            //   * le lower() natif de SQLite ne replie que A–Z, comme l'invariant sur l'ASCII — aucune extension
            //     ICU, aucune dépendance à la culture de la machine, même résultat que la migration soit jouée par
            //     l'application ou par `dotnet ef` ;
            //   * le trim() natif ne retire que l'espace U+0020, là où .NET Trim() retire toute espace Unicode.
            //     Un login portant une tabulation ou une espace insécable périphérique tomberait donc hors du jeu
            //     autorisé et est rejeté ci-dessous — jamais normalisé de travers.
            //
            // Toute ligne hors de ce contrat AVORTE la migration : la valeur fautive est insérée dans une table à
            // contrainte CHECK impossible, ce qui lève une erreur et annule la transaction. Aucune ligne n'est
            // supprimée, fusionnée ni renommée ; l'ancien schéma et les données restent intacts ; la migration
            // n'est PAS inscrite dans __EFMigrationsHistory. Le nom de la table porte l'action attendue de
            // l'opérateur.
            //
            // Cas couverts : Username NULL, vide ou blanc après trim, longueur hors bornes (3–50, celles de
            // UserValidator), caractère hors du jeu autorisé.
            migrationBuilder.Sql(
                "CREATE TABLE \"__abort_invalid_username_needs_manual_fix\" " +
                "(\"offending_username\" TEXT NOT NULL CHECK (\"offending_username\" = ''));\n" +
                "INSERT INTO \"__abort_invalid_username_needs_manual_fix\" (\"offending_username\")\n" +
                "SELECT COALESCE(\"Username\", '<NULL>') FROM \"Users\"\n" +
                "WHERE \"Username\" IS NULL\n" +
                "   OR length(trim(\"Username\")) < 3\n" +
                "   OR length(trim(\"Username\")) > 50\n" +
                "   OR trim(\"Username\") GLOB '*[^a-zA-Z0-9._-]*';\n" +
                "DROP TABLE \"__abort_invalid_username_needs_manual_fix\";");

            // Collision insensible à la casse entre comptes DISTINCTS (« admin » et « Admin » coexistants) :
            // refusée explicitement, avec un nom de table qui dit quoi faire. L'index unique créé plus bas
            // échouerait de toute façon, mais sur un message SQLite brut ne désignant pas les comptes fautifs.
            // Arbitrer automatiquement serait pire : fusionner deux comptes détruirait un historique (ventes,
            // mouvements de stock), et en renommer un inventerait un identifiant que personne n'a choisi.
            // L'exploitant tranche, puis rejoue la migration.
            migrationBuilder.Sql(
                "CREATE TABLE \"__abort_case_insensitive_username_collision_needs_manual_fix\" " +
                "(\"offending_username\" TEXT NOT NULL CHECK (\"offending_username\" = ''));\n" +
                "INSERT INTO \"__abort_case_insensitive_username_collision_needs_manual_fix\" (\"offending_username\")\n" +
                "SELECT \"Username\" FROM \"Users\"\n" +
                "WHERE lower(trim(\"Username\")) IN (\n" +
                "    SELECT lower(trim(\"Username\")) FROM \"Users\"\n" +
                "    GROUP BY lower(trim(\"Username\")) HAVING COUNT(*) > 1);\n" +
                "DROP TABLE \"__abort_case_insensitive_username_collision_needs_manual_fix\";");

            // À ce stade, chaque login hérité est ASCII autorisé et sans collision : lower(trim(...)) reproduit
            // exactement UserIdentityPolicy.NormalizeUsername. La forme AFFICHABLE (Username) reste inchangée.
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"NormalizedUsername\" = lower(trim(\"Username\"));");

            // Garde finale : aucune ligne ne conserve la valeur par défaut transitoire. Sans elle, une table vide
            // de lignes fautives mais non backfillée (cas impossible en exécution normale, possible sur base
            // altérée) produirait une colonne NOT NULL remplie de chaînes vides — un schéma conforme en apparence
            // mais dont la clé métier serait vide.
            migrationBuilder.Sql(
                "CREATE TABLE \"__abort_normalized_username_backfill_incomplete\" " +
                "(\"offending_user_id\" TEXT NOT NULL CHECK (\"offending_user_id\" = ''));\n" +
                "INSERT INTO \"__abort_normalized_username_backfill_incomplete\" (\"offending_user_id\")\n" +
                "SELECT CAST(\"UserId\" AS TEXT) FROM \"Users\" WHERE \"NormalizedUsername\" = '';\n" +
                "DROP TABLE \"__abort_normalized_username_backfill_incomplete\";");

            // L'ancien index unique sensible à la casse est SUPPRIMÉ, non conservé en doublon : il n'a jamais
            // garanti l'unicité du login au sens métier, et le maintenir ferait échouer un simple changement de
            // casse d'un login existant.
            migrationBuilder.DropIndex(
                name: "idx_users_username_unique",
                table: "Users");

            // Le défaut '' ci-dessus n'était qu'un artifice transitoire pour satisfaire la contrainte SQLite sur
            // ADD COLUMN NOT NULL ; il ne doit PAS subsister dans le schéma final. SQLite ne supporte aucun ALTER
            // COLUMN natif pour retirer un défaut : cet AlterColumn déclenche la reconstruction de table que le
            // générateur SQLite d'EF Core effectue pour toute opération de ce type, en recréant la colonne SANS
            // défaut (aucune valeur passée ici, contrairement à l'AddColumn ci-dessus). Sans cette étape, une
            // omission applicative de NormalizedUsername serait acceptée en silence avec une clé métier vide au
            // lieu d'échouer NOT NULL — exactement le défaut que la contrainte doit empêcher.
            migrationBuilder.AlterColumn<string>(
                name: "NormalizedUsername",
                table: "Users",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 50,
                oldNullable: false,
                oldDefaultValue: "");

            migrationBuilder.CreateIndex(
                name: "idx_users_normalized_username_unique",
                table: "Users",
                column: "NormalizedUsername",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback HONNÊTE, et volontairement non « intelligent » :
            //   * il restaure l'ancien comportement SENSIBLE À LA CASSE — « admin » et « Admin » redeviennent
            //     deux comptes distincts, avec l'ambiguïté d'authentification que P3-10 corrige ;
            //   * il ne fusionne, ne recrée et ne renomme AUCUNE donnée ;
            //   * les valeurs affichables Username restent strictement inchangées (elles n'ont jamais été
            //     modifiées par Up) : aucun compte ne perd son identifiant.
            //
            // La recréation de idx_users_username_unique peut légitimement échouer si des comptes ne différant
            // que par la casse ont été créés PENDANT que P3-10 était appliqué : l'ancien index ne peut pas les
            // représenter. Cet échec est le bon comportement — il signale que le retour arrière détruirait une
            // garantie d'unicité — plutôt que de supprimer des comptes pour le rendre possible.
            migrationBuilder.DropIndex(
                name: "idx_users_normalized_username_unique",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedUsername",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "idx_users_username_unique",
                table: "Users",
                column: "Username",
                unique: true);
        }
    }
}
