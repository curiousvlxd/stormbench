using Microsoft.Extensions.Options;
using StormBench.Domain.Control;

namespace StormBench.Scada.Optimization;

/// <summary>Binds the central optimiser's <see cref="EfdParameters"/> to the section <c>Optimization:Efd</c>.</summary>
internal sealed class EfdParametersSetup(IConfiguration configuration) : IConfigureOptions<EfdParameters>
{
    public const string Section = "Optimization:Efd";

    public void Configure(EfdParameters options) => configuration.GetSection(Section).Bind(options);
}
