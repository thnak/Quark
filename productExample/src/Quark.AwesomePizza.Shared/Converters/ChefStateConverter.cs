using Quark.Abstractions;
using Quark.AwesomePizza.Shared.Models;

namespace Quark.AwesomePizza.Shared.Converters;

/// <summary>
/// Binary converter for ChefState model.
/// Provides AOT-compatible serialization for chef state data.
/// </summary>
public class ChefStateConverter : QuarkBinaryConverter<ChefState>
{
    public override void Write(BinaryWriter writer, ChefState value)
    {
        writer.Write(value.ChefId);
        writer.Write(value.Name);
        writer.Write((int)value.Status);
        writer.Write(value.CurrentOrders.Count);
        foreach (var order in value.CurrentOrders)
        {
            writer.Write(order);
        }
        writer.Write(value.CompletedToday);
    }

    public override ChefState Read(BinaryReader reader)
    {
        var chefId = reader.ReadString();
        var name = reader.ReadString();
        var status = (ChefStatus)reader.ReadInt32();
        var orderCount = reader.ReadInt32();
        var currentOrders = new List<string>();
        for (int i = 0; i < orderCount; i++)
        {
            currentOrders.Add(reader.ReadString());
        }
        var completedToday = reader.ReadInt32();

        return new ChefState
        {
            ChefId = chefId,
            Name = name,
            Status = status,
            CurrentOrders = currentOrders,
            CompletedToday = completedToday
        };
    }
}
