using System.Text.Json;
using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Constants;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Converters;

public class CreateOrderRequestConverter : QuarkBinaryConverter<CreateOrderRequest>
{
    public override void Write(BinaryWriter writer, CreateOrderRequest value)
    {
        writer.Write(JsonSerializer.SerializeToUtf8Bytes(value, ModelJsonContext.Default.CreateOrderRequest));
    }

    public override CreateOrderRequest Read(BinaryReader reader)
    {
        var jsonBytes = reader.ReadBytes(reader.ReadInt32());
        return JsonSerializer.Deserialize(jsonBytes, ModelJsonContext.Default.CreateOrderRequest)!;
    }
}