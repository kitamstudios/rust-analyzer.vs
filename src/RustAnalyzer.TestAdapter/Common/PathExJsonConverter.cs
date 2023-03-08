using System;
using Newtonsoft.Json;

namespace KS.RustAnalyzer.TestAdapter.Common;

public class PathExJsonConverter : JsonConverter<PathEx>
{
    public override bool CanRead => false;

    public override bool CanWrite => true;

    public override void WriteJson(JsonWriter writer, PathEx value, JsonSerializer serializer)
    {
        writer.WriteValue(value);
    }

    public override PathEx ReadJson(JsonReader reader, Type objectType, PathEx existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException();
    }
}
