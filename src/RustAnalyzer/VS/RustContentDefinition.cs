using System.ComponentModel.Composition;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Utilities;

namespace KS.RustAnalyzer.VS;

public class RustContentDefinition
{
    [Export]
    [Name(RustConstants.RustLanguageContentType)]
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
#pragma warning disable SA1401 // Fields should be private
    public static ContentTypeDefinition RustContentTypeDefinition;
#pragma warning restore SA1401 // Fields should be private

    [Export]
    [FileExtension(RustConstants.RustFileExtension)]
    [ContentType(RustConstants.RustLanguageContentType)]
#pragma warning disable SA1401 // Fields should be private
    public static FileExtensionToContentTypeDefinition RustFileExtensionDefinition;
#pragma warning restore SA1401 // Fields should be private
}
