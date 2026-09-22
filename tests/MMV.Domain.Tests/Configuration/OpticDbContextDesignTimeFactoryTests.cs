using System.Reflection;
using System.Reflection.Emit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Design;
using MMV.Domain.Tests.Data.Migrations;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P4-5E-C — Factory de découverte du projet de migrations PostgreSQL (Q1 option A, exception à D-01.2).
///
/// <para>
/// Cette classe n'existe que pour que <c>dotnet ef</c> découvre <c>OpticDbContext</c> quand le projet de
/// migrations est son propre projet de démarrage (D-04). Elle doit rester une <b>délégation pure</b> vers
/// <see cref="OpticDbContextFactory"/>, unique chemin logique de génération (ADR-007 §5.2, D-05).
/// </para>
///
/// <para>
/// <b>Preuve sur l'IL compilé, volontairement.</b> Exécuter la factory lirait l'environnement réel du
/// processus (partagé par les tests parallèles) et, sur la branche SQLite, créerait le dossier de la base
/// dans le profil du poste. Lire son IL prouve ce qu'elle appelle, sans l'exécuter et sans dépendre des
/// commentaires du source.
/// </para>
/// </summary>
public sealed class OpticDbContextDesignTimeFactoryTests
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    private static Type FactoryType() => Assembly
        .Load(DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName)
        .GetType(MigrationChainsTests.DesignTimeFactoryTypeName, throwOnError: true)!;

    [Fact]
    public void Factory_IsTheOnlyDesignTimeFactory_OfTheMigrationsAssembly_ForOpticDbContext()
    {
        var factories = FactoryType().Assembly.GetTypes()
            .Where(t => typeof(IDesignTimeDbContextFactory<OpticDbContext>).IsAssignableFrom(t))
            .ToArray();

        factories.Should().Equal(FactoryType());
        FactoryType().IsSealed.Should().BeTrue();
        FactoryType().IsPublic.Should().BeFalse("point d'entrée design-time, pas une API offerte à MMV.App");
    }

    [Fact]
    public void Factory_HoldsNoState_AndDeclaresNothingButCreateDbContext()
    {
        const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                                      | BindingFlags.Public | BindingFlags.NonPublic;

        FactoryType().GetFields(declared).Should().BeEmpty("aucune chaîne de connexion, aucun état");
        FactoryType().GetProperties(declared).Should().BeEmpty();
        FactoryType().GetMethods(declared).Select(m => m.Name)
            .Should().Equal(nameof(IDesignTimeDbContextFactory<OpticDbContext>.CreateDbContext));
    }

    [Fact]
    public void CreateDbContext_OnlyDelegatesToTheInfrastructureFactory()
    {
        var method = FactoryType().GetMethod(
            nameof(IDesignTimeDbContextFactory<OpticDbContext>.CreateDbContext), [typeof(string[])])!;

        var instructions = ReadIl(method).ToArray();

        // Ni UseNpgsql, ni Migrate(), ni EnsureCreated(), ni autre appel : exactement la délégation.
        instructions.Where(i => i.OpCode.OperandType == OperandType.InlineMethod)
            .Select(i => Describe(method.Module.ResolveMethod(i.Operand)!))
            .Should().Equal(
                $"{typeof(OpticDbContextFactory).FullName}::.ctor()",
                $"{typeof(OpticDbContextFactory).FullName}::{nameof(OpticDbContextFactory.CreateDbContext)}(System.String[])");

        instructions.Should().NotContain(i => i.OpCode.OperandType == OperandType.InlineString,
            "aucune chaîne littérale : ni chaîne de connexion, ni nom de provider (D-05.4)");
    }

    private static string Describe(MethodBase method)
        => $"{method.DeclaringType!.FullName}::{method.Name}" +
           $"({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.FullName))})";

    /// <summary>Décodeur IL minimal : chaque opcode, avec son opérande quand elle tient sur 32 bits (jeton de méthode ou de chaîne).</summary>
    private static IEnumerable<(OpCode OpCode, int Operand)> ReadIl(MethodInfo method)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var position = 0;

        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[position++]));
            }

            var opCode = OpCodesByValue[value];
            var operandSize = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, position),
                _ => 4
            };

            var operand = operandSize == 4 ? BitConverter.ToInt32(il, position) : 0;
            position += operandSize;

            yield return (opCode, operand);
        }
    }
}
