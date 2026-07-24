using Xunit;

namespace MMV.P4.ProviderComparison.Support;

/// <summary>
/// Collection xUnit SÉRIALISANTE pour les expérimentations qui ARRÊTENT le serveur (E14, E15).
///
/// Sans elle, xUnit exécuterait ces classes en parallèle des autres : un arrêt de conteneur
/// provoquerait des échecs sans rapport dans les expérimentations voisines, et les mesures de
/// panne seraient ininterprétables. Les classes membres s'exécutent donc l'une après l'autre.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpikeSerialCollection
{
    public const string Name = "spike-serial-outage";
}
