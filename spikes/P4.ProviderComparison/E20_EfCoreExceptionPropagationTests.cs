using Microsoft.EntityFrameworkCore;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E20 (P4-4A0B) — PROPAGATION des exceptions à travers EF CORE.
///
/// <para>
/// E19 a relevé la forme des échecs tels que les rend <b>ADO.NET / Npgsql directement</b>. Or
/// <c>PersistenceErrorMapper</c> ne verra jamais ces exceptions-là : il verra celles que lui remet
/// <b>EF Core</b>, qui enveloppe une partie des échecs d'écriture dans une
/// <see cref="DbUpdateException"/>. Cette expérimentation répond à UNE seule question :
/// « quelle chaîne d'exceptions le code EF Core réel remet-il au mapper ? »
/// </para>
///
/// <para>
/// Elle ne re-prouve AUCUN scénario d'E19 : ni <c>40P01</c>, ni <c>25P02</c>, ni les 14 primitives,
/// ni la concurrence E18, ni la perte réseau E14 — ces preuves ont leurs lots dédiés. Aucun
/// conteneur n'est arrêté ici.
/// </para>
///
/// <para>
/// RÈGLE ABSOLUE reconduite d'E19 : aucune décision, aucune mesure et aucun journal ne s'appuie sur
/// <c>Exception.Message</c>, sur une sous-chaîne ou sur une expression régulière. Seuls des tests de
/// TYPE et des propriétés STRUCTURÉES sont utilisés, via <see cref="ExceptionShape"/> — aucune
/// seconde infrastructure de diagnostic n'est créée, et <c>ExceptionShape</c> n'est pas modifiée.
/// </para>
///
/// <para>
/// Le modèle de PRODUCTION n'est pas exercé ici, et surtout pas modifié : E20 utilise un modèle
/// minimal PROPRE AU HARNESS (<see cref="EfProbeContext"/>), créé par <c>EnsureCreated</c> dans une
/// base jetable. Aucune migration n'est générée, aucun paquet n'est ajouté, <c>src/**</c> et
/// <c>tests/**</c> de <c>MMV.sln</c> restent intacts.
/// </para>
///
/// <para>
/// PORTÉE DES RÉGLAGES : les <c>CommandTimeout</c> et le <c>SET LOCAL statement_timeout</c>
/// utilisés ci-dessous sont des instruments de MESURE, confinés au harness. Ils ne préjugent
/// d'AUCUNE politique de production : P4-4A n'introduit ni <c>lock_timeout</c>, ni
/// <c>statement_timeout</c>, ni <c>CommandTimeout</c> supplémentaire, ni réessai.
/// </para>
/// </summary>
[Collection(SpikeSerialCollection.Name)]
public class E20_EfCoreExceptionPropagationTests
{
    private const string Experiment = "E20-efcore-exception-propagation";

    private const ProviderKind Provider = ProviderKind.Postgres;

    private const string Name = nameof(ProviderKind.Postgres);

    /// <summary>Commande volontairement longue : l'échec mesuré doit venir du timeout ou de l'annulation, jamais de la fin normale.</summary>
    private const string LongSleep = "SELECT pg_sleep(30);";

    [SkippableFact]
    public async Task Ef_core_propagation_shapes_are_captured_structurally()
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(Provider), SpikeEnvironment.SkipReason(Provider));

        await using var database = await SpikeDatabase.CreateAsync(Provider, "e20");

        // Schéma du harness créé par EF lui-même : la chaîne mesurée est donc bien celle d'un modèle
        // EF réel, et non celle d'un SQL écrit à la main.
        await using (var seed = CreateContext(database))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Parents.Add(new EfProbeParent { Id = 1, Code = "ALPHA" });
            await seed.SaveChangesAsync();
        }

        SpikeLog.Section(Experiment, Name, "E20 — Propagation des exceptions a travers EF Core (aucun parsing de message)");
        SpikeLog.Write(Experiment, Name,
            "| Cas | Outer | Inner chain | SqlState | Signal structure preserve ? | Parcours mapper suffisant ? |");

        var measured = new List<(string Label, ExceptionShape? Shape)>();

        // === EF1 — UNIQUE 23505 par SaveChangesAsync ==============================================
        // Hypothèse à VÉRIFIER, jamais assertée d'avance : DbUpdateException enveloppe-t-elle une
        // PostgresException 23505 dont le SqlState ET le ConstraintName survivent ?
        await MeasureNewContextAsync(measured, database, "EF1. unique (23505 attendu) via SaveChangesAsync",
            context =>
            {
                context.Parents.Add(new EfProbeParent { Id = 2, Code = "ALPHA" }); // doublon volontaire
                return context.SaveChangesAsync();
            });

        // === EF2a — CLÉ ÉTRANGÈRE 23503 par SaveChangesAsync ======================================
        await MeasureNewContextAsync(measured, database, "EF2a. cle etrangere (23503 attendu) via SaveChangesAsync",
            context =>
            {
                context.Children.Add(new EfProbeChild { Id = 1, ParentId = 999 }); // parent inexistant
                return context.SaveChangesAsync();
            });

        // === EF2b — NOT NULL 23502 par SaveChangesAsync ===========================================
        // Colonne NOT NULL laissée nulle : EF n'effectue aucune validation cliente et transmet NULL,
        // c'est donc bien le SERVEUR qui refuse.
        await MeasureNewContextAsync(measured, database, "EF2b. not null (23502 attendu) via SaveChangesAsync",
            context =>
            {
                context.Parents.Add(new EfProbeParent { Id = 3, Code = null! });
                return context.SaveChangesAsync();
            });

        // === EF3a — TIMEOUT CLIENT sur le chemin REQUÊTE ==========================================
        await MeasureNewContextAsync(measured, database, "EF3a. timeout client (CommandTimeout=2s) sur requete EF",
            context =>
            {
                context.Database.SetCommandTimeout(2);
                return context.Database.ExecuteSqlRawAsync(LongSleep);
            });

        // === EF3b / EF4b — ÉCHECS PENDANT UNE ÉCRITURE RÉELLEMENT BLOQUÉE ========================
        // La ligne est détenue par une AUTRE transaction : SaveChangesAsync attend vraiment, ce qui
        // permet de mesurer un timeout PUIS une annulation PENDANT l'écriture, et non avant.
        await using (var holder = await database.OpenRawAsync())
        {
            await using var holdTransaction = await holder.BeginTransactionAsync();
            await LockRowAsync(holder, holdTransaction);

            // EF3b — cas le plus important : il dit si DbUpdateException conserve un échec qui NE
            // PORTE AUCUN SqlState (forme A/B1 d'E19).
            await MeasureNewContextAsync(measured, database, "EF3b. timeout client pendant SaveChangesAsync (verrou detenu)",
                async context =>
                {
                    context.Database.SetCommandTimeout(3);
                    var blocked = await context.Parents.SingleAsync(parent => parent.Id == 1);
                    blocked.Code = "BLOQUE-PAR-TIMEOUT";
                    await context.SaveChangesAsync();
                });

            // EF4b — question décisive : l'annulation reste-t-elle identifiable STRUCTURELLEMENT
            // alors que l'écriture est en cours et que le serveur répondra 57014 ?
            await MeasureNewContextAsync(measured, database, "EF4b. annulation CancellationToken pendant SaveChangesAsync",
                async context =>
                {
                    context.Database.SetCommandTimeout(60); // le timeout client ne doit PAS être la cause
                    var blocked = await context.Parents.SingleAsync(parent => parent.Id == 1);
                    blocked.Code = "BLOQUE-PAR-ANNULATION";

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await context.SaveChangesAsync(cts.Token);
                });

            await holdTransaction.RollbackAsync();
        }

        // === EF3c — EXPIRATION DÉCIDÉE PAR LE SERVEUR, VUE À TRAVERS EF ===========================
        // `SET LOCAL` confine le réglage à la transaction : rien n'est laissé derrière dans la
        // connexion rendue au pool.
        await MeasureNewContextAsync(measured, database, "EF3c. statement_timeout SERVEUR (57014 attendu) via requete EF",
            async context =>
            {
                context.Database.SetCommandTimeout(60); // le client ne doit PAS être la cause
                await using var transaction = await context.Database.BeginTransactionAsync();
                await context.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '1000ms';");

                try
                {
                    await context.Database.ExecuteSqlRawAsync(LongSleep);
                }
                finally
                {
                    await SafeRollbackAsync(transaction);
                }
            });

        // === EF4a — ANNULATION CLIENTE SUR LE CHEMIN REQUÊTE ======================================
        await MeasureNewContextAsync(measured, database, "EF4a. annulation CancellationToken sur requete EF",
            async context =>
            {
                context.Database.SetCommandTimeout(60);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await context.Database.ExecuteSqlRawAsync(LongSleep, cts.Token);
            });

        // === EF5 — ÉCHEC DE CONNEXION (endpoint local fermé) ======================================
        // Aucun conteneur n'est arrêté : un port fermé suffit, et les autres expérimentations ne sont
        // pas privées de leur serveur.
        await using (var context = CreateUnreachableContext())
        {
            measured.Add(("EF5a. connexion refusee sur requete EF (port ferme)",
                await MeasureAsync("EF5a. connexion refusee sur requete EF (port ferme)", context,
                    () => context.Parents.ToListAsync())));
        }

        await using (var context = CreateUnreachableContext())
        {
            context.Parents.Add(new EfProbeParent { Id = 42, Code = "INJOIGNABLE" });
            measured.Add(("EF5b. connexion refusee pendant SaveChangesAsync (port ferme)",
                await MeasureAsync("EF5b. connexion refusee pendant SaveChangesAsync (port ferme)", context,
                    () => context.SaveChangesAsync())));
        }

        // Une provocation qui n'échoue pas n'est pas une mesure : le rapport ne doit jamais
        // enregistrer une ligne absente comme si elle avait été observée.
        var silent = measured.Where(entry => entry.Shape is null).Select(entry => entry.Label).ToList();
        Assert.True(silent.Count == 0,
            "[Postgres] Scenario(s) sans exception levee, donc non mesurable(s) : " + string.Join(" / ", silent));
    }

    /// <summary>
    /// Un contexte NEUF par scénario : après un échec, PostgreSQL avorte la transaction et EF laisse
    /// l'entité en état modifié — réutiliser le contexte polluerait la mesure suivante.
    /// </summary>
    private static async Task MeasureNewContextAsync(
        List<(string Label, ExceptionShape? Shape)> measured,
        SpikeDatabase database,
        string label,
        Func<EfProbeContext, Task> action)
    {
        await using var context = CreateContext(database);
        measured.Add((label, await MeasureAsync(label, context, () => action(context))));
    }

    /// <summary>
    /// Verrouille la ligne 1 depuis une AUTRE connexion, sans la relâcher : toute écriture EF sur
    /// cette ligne attendra, ce qui permet de mesurer un timeout ou une annulation PENDANT
    /// <c>SaveChangesAsync</c> plutôt qu'avant.
    /// </summary>
    private static async Task LockRowAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE ef_probe_parent SET \"Code\" = 'VERROU' WHERE \"Id\" = 1;";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Après une erreur, PostgreSQL avorte la transaction (<c>25P02</c>) : l'échec éventuel du
    /// <c>ROLLBACK</c> ne doit pas masquer la mesure. <c>25P02</c> n'est pas re-mesuré ici — il
    /// relève d'un lot dédié.
    /// </summary>
    private static async Task SafeRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch (Exception exception)
        {
            SpikeLog.Write(Experiment, Name, $"(nettoyage) rollback non abouti : {exception.GetType().FullName}");
        }
    }

    /// <summary>
    /// Exécute une action EF ATTENDUE EN ÉCHEC et relève la forme complète de l'échec, ainsi que
    /// l'état de la connexion sous-jacente avant/après (observation, jamais critère).
    /// </summary>
    private static async Task<ExceptionShape?> MeasureAsync(string label, DbContext context, Func<Task> action)
    {
        var before = context.Database.GetDbConnection().State;

        try
        {
            await action();
            SpikeLog.Write(Experiment, Name, $"{label} | AUCUNE ERREUR LEVEE (resultat inattendu a consigner)");
            return null;
        }
        catch (Exception exception)
        {
            var after = context.Database.GetDbConnection().State;
            var shape = ExceptionShape.Capture(exception, before, after);
            Record(label, shape);
            return shape;
        }
    }

    private static void Record(string label, ExceptionShape shape)
    {
        SpikeLog.Write(Experiment, Name, EfMatrixRow(label, shape));
        SpikeLog.Write(Experiment, Name,
            $"{label} | DbUpdateException={(Has<DbUpdateException>(shape) ? "OUI" : "NON")}" +
            $" | DbUpdateConcurrencyException={(Has<DbUpdateConcurrencyException>(shape) ? "OUI" : "NON")}" +
            $" | PostgresException={(shape.HasPostgresException ? "OUI" : "NON")}" +
            $" | {shape.ToEvidenceLine()}");
    }

    /// <summary>
    /// Test de TYPE sur les trames DÉJÀ relevées par <see cref="ExceptionShape"/>. Ce n'est pas une
    /// seconde sonde : la chaîne n'a été parcourue qu'une fois, par <c>ExceptionShape</c>, et l'on se
    /// contente d'interroger le type CLR qu'elle a conservé. Aucun nom n'est comparé en texte.
    /// </summary>
    private static bool Has<T>(ExceptionShape shape) where T : Exception
        => shape.Frames.Any(frame => typeof(T).IsAssignableFrom(frame.ClrType));

    /// <summary>
    /// Ligne de la matrice EF du rapport. « Signal structuré préservé » est vrai dès qu'au moins un
    /// signal EXPLOITABLE subsiste dans la chaîne (<c>SqlState</c>, <c>SocketErrorCode</c>,
    /// <c>TimeoutException</c> ou annulation). « Parcours mapper suffisant » vaut la même chose PAR
    /// CONSTRUCTION : <see cref="ExceptionShape"/> ne parcourt QUE des types et des
    /// <c>InnerException</c> — si elle a trouvé le signal, un mapper procédant de la même façon le
    /// trouvera aussi, sans jamais lire un message.
    /// </summary>
    private static string EfMatrixRow(string label, ExceptionShape shape)
    {
        var inner = shape.Frames.Count > 1
            ? string.Join(" -> ", shape.Frames.Skip(1).Select(frame => frame.TypeName))
            : "(aucune)";

        var structured = shape.SqlState is not null
            || shape.SocketErrorCode is not null
            || shape.HasTimeoutException
            || shape.HasOperationCanceledException;

        return $"| {label} | {shape.OuterType} | {inner} | {shape.SqlState ?? "(aucun)"} | " +
               $"{(structured ? "OUI" : "NON")} | {(structured ? "OUI" : "NON")} |";
    }

    private static EfProbeContext CreateContext(SpikeDatabase database)
        => new(new DbContextOptionsBuilder<EfProbeContext>().UseNpgsql(database.ConnectionString).Options);

    /// <summary>
    /// Contexte EF pointant un port local FERMÉ. Aucun conteneur n'est arrêté : l'échec de connexion
    /// est provoqué sans priver les autres expérimentations de leur serveur.
    /// </summary>
    private static EfProbeContext CreateUnreachableContext()
        => new(new DbContextOptionsBuilder<EfProbeContext>()
            .UseNpgsql(SpikeConnections.WithUnreachablePort(Provider))
            .Options);
}

/// <summary>Entité de SONDE, propre au harness E20. Rien de ceci n'appartient au modèle de production.</summary>
public sealed class EfProbeParent
{
    public int Id { get; set; }

    /// <summary>NOT NULL et UNIQUE : porte à la fois la mesure <c>23505</c> et la mesure <c>23502</c>.</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>Entité de SONDE portant la clé étrangère mesurée par EF2a.</summary>
public sealed class EfProbeChild
{
    public int Id { get; set; }

    public int ParentId { get; set; }
}

/// <summary>
/// Contexte EF MINIMAL du harness E20. Il n'hérite PAS d'<c>OpticDbContext</c> et ne partage rien
/// avec le modèle de production : la mesure porte sur la MÉCANIQUE de propagation d'EF Core, pas sur
/// le schéma de MMV. Les contraintes sont nommées explicitement afin que <c>ConstraintName</c> soit
/// vérifiable dans la chaîne d'exceptions.
/// </summary>
public sealed class EfProbeContext : DbContext
{
    public EfProbeContext(DbContextOptions<EfProbeContext> options) : base(options)
    {
    }

    public DbSet<EfProbeParent> Parents => Set<EfProbeParent>();

    public DbSet<EfProbeChild> Children => Set<EfProbeChild>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfProbeParent>(entity =>
        {
            entity.ToTable("ef_probe_parent");
            entity.HasKey(parent => parent.Id);
            entity.Property(parent => parent.Id).ValueGeneratedNever();
            entity.HasIndex(parent => parent.Code)
                .IsUnique()
                .HasDatabaseName("ux_ef_probe_parent_code");
        });

        modelBuilder.Entity<EfProbeChild>(entity =>
        {
            entity.ToTable("ef_probe_child");
            entity.HasKey(child => child.Id);
            entity.Property(child => child.Id).ValueGeneratedNever();
            entity.HasOne<EfProbeParent>()
                .WithMany()
                .HasForeignKey(child => child.ParentId)
                .HasConstraintName("fk_ef_probe_child_parent");
        });
    }
}
