using System.Threading.Tasks;
using KS.RustAnalyzer.VS;

namespace KS.RustAnalyzer.Cargo;

public class CargoExeRunner
{
    public static Task<bool> CompileFileAsync(string filePath, OutputPaneWrapper outputPane)
    {
        return Task.FromResult(true);
    }

    public static Task<bool> CompileProjectAsync(string filePath, OutputPaneWrapper outputPane)
    {
        return Task.FromResult(true);
    }
}