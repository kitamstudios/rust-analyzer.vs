using System;

namespace KS.RustAnalyzer.TestAdapter.Common;

public sealed class DisposableTempFile : IDisposable
{
    public DisposableTempFile(PathEx extension)
    {
        File = (PathEx)$"{PathExExtensions.GetTempFileName()}{extension}";
    }

    public PathEx File { get; private set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                if (File.FileExists())
                {
                    File.FileDelete();
                }
            }
        }
        catch
        {
            // NOTE: Must not fail.
        }
    }
}