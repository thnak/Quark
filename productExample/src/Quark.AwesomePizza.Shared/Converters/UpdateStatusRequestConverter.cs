using System.Text.Json;
using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Constants;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Converters;

public class UpdateStatusRequestConverter : QuarkBinaryConverter<UpdateStatusRequest>
{
    public override void Write(BinaryWriter writer, UpdateStatusRequest value)
    {
        writer.Write(JsonSerializer.SerializeToUtf8Bytes(value, ModelJsonContext.Default.UpdateStatusRequest));
    }

    public override UpdateStatusRequest Read(BinaryReader reader)
    {
        var jsonBytes = reader.ReadBytes(reader.ReadInt32());
        return JsonSerializer.Deserialize(jsonBytes, ModelJsonContext.Default.UpdateStatusRequest)!;
    }
}