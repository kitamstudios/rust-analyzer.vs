using Newtonsoft.Json;

namespace KS.RustAnalyzer.TestAdapter.Common;

public static class ObjectExtensions
{
    public static string SerializeObject(this object @this, Formatting formatting = Formatting.None)
    {
        return JsonConvert.SerializeObject(@this, formatting);
    }

    public static string SerializeObject(this object @this, Formatting formatting, params JsonConverter[] converters)
    {
        return JsonConvert.SerializeObject(@this, formatting, converters);
    }
}
