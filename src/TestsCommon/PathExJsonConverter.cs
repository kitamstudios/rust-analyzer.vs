using System;
using KS.RustAnalyzer.TestAdapter.Common;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.Tests.Common;

public class PathExJsonConverter : JsonConverter<PathEx>
{
    public override void WriteJson(JsonWriter writer, PathEx value, JsonSerializer serializer)
    {
        writer.WriteValue(value);
    }

    public override PathEx ReadJson(JsonReader reader, Type objectType, PathEx existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}
