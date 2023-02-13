using System.ComponentModel.Composition;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.Infrastructure;

public interface IPreReqsCheckService
{
    bool Satisfied();
}

[Export(typeof(IPreReqsCheckService))]
[PartCreationPolicy(CreationPolicy.Shared)]
public sealed class PreReqsCheckService : IPreReqsCheckService
{
    private readonly ICargoService _cargoService;

    [ImportingConstructor]
    public PreReqsCheckService([Import] ICargoService cargoService)
    {
        _cargoService = cargoService;
    }

    public bool Satisfied()
    {
        return _cargoService.GetCargoExePath() != null;
    }
}
