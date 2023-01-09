using System.ComponentModel.Composition;

namespace KS.RustAnalyzer.VS;

[Export(typeof(OutputPaneWrapper))]
public class OutputPaneWrapper
{
    public void InitializeOutputPanes()
    {
    }
}