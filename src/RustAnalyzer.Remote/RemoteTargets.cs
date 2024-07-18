using System.Collections.Generic;

namespace KS.RustAnalyzer.Remote;

public interface IRemoteTargets
{
    IEnumerable<IRemoteTarget> Enumerate();
}

public class RemoteTargets : IRemoteTargets
{
    public IEnumerable<IRemoteTarget> Enumerate()
    {
        throw new System.NotImplementedException();
    }
}
