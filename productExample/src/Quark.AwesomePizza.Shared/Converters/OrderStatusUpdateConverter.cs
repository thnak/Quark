using System.Text.Json;
using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Constants;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Converters;

public class OrderStatusUpdateConverter : QuarkBinaryConverter<OrderStatusUpdate>
{
    public override void Write(BinaryWriter writer, OrderStatusUpdate value)
    {
        writer.Write(JsonSerializer.SerializeToUtf8Bytes(value, ModelJsonContext.Default.OrderStatusUpdate));
    }

    public override OrderStatusUpdate Read(BinaryReader reader)
    {
        var jsonBytes = reader.ReadBytes(reader.ReadInt32());
        return JsonSerializer.Deserialize(jsonBytes, ModelJsonContext.Default.OrderStatusUpdate)!;
    }
}