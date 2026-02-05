using System.Text.Json;
using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Constants;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Converters;

public class CreateOrderResponseConverter : QuarkBinaryConverter<CreateOrderResponse>
{
    public override void Write(BinaryWriter writer, CreateOrderResponse value)
    {
        writer.Write(JsonSerializer.SerializeToUtf8Bytes(value, ModelJsonContext.Default.CreateOrderResponse));
    }

    public override CreateOrderResponse Read(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(reader.ReadInt32());
        return JsonSerializer.Deserialize<CreateOrderResponse>(bytes, ModelJsonContext.Default.CreateOrderResponse)!;
    }
}