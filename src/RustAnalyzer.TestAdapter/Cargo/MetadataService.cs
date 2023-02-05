using System;
using KS.RustAnalyzer.TestAdapter.Common;

namespace KS.RustAnalyzer.TestAdapter.Cargo;

public sealed class MetadataService : IMetadataService, IDisposable
{
    private readonly ICargoService _cargoService;
    private readonly PathEx _workspaceRoot;
    private readonly TL _tl;
    private bool _disposedValue;

    public MetadataService(ICargoService cargoService, PathEx workspaceRoot, TL tl)
    {
        // TODO: MS: Start collecting metadata.
        // TODO: MS: subscribe to file chagne notifications.
        _cargoService = cargoService;
        _workspaceRoot = workspaceRoot;
        _tl = tl;
    }

    public void Dispose()
    {
        // NOTE: Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method.
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: MS: dispose managed state (managed objects)
            }

            // TODO: MS: set large fields to null
            _disposedValue = true;
        }
    }
}
