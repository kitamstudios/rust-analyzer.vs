using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace KS.RustAnalyzer.VS;

public class ContentDefinition
{
    [Export]
    [Name(Constants.RustLanguageContentType)]
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:Fields should be private", Justification = "MPF requires a field as per examples.")]
    public static ContentTypeDefinition RustContentTypeDefinition;

    [Export]
    [FileExtension(Constants.RustFileExtension)]
    [ContentType(Constants.RustLanguageContentType)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:Fields should be private", Justification = "MPF requires a field as per examples.")]
    public static FileExtensionToContentTypeDefinition RustFileExtensionDefinition;
}
