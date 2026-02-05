using System.Text.Json;
using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Constants;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Converters;

public class OrderStateConverter : QuarkBinaryConverter<OrderState>
{
    public override void Write(BinaryWriter writer, OrderState value)
    {
        writer.Write(JsonSerializer.SerializeToUtf8Bytes(value, ModelJsonContext.Default.OrderState));
    }

    public override OrderState Read(BinaryReader reader)
    {
        var jsonBytes = reader.ReadBytes(reader.ReadInt32());
        return JsonSerializer.Deserialize(jsonBytes, ModelJsonContext.Default.OrderState)!;
    }
}