using Quark.Abstractions;
using System.IO;

namespace Quark.Tests;

/// <summary>
/// Binary converter for ObjectClassValue test type.
/// </summary>
public class ObjectClassValueConverter : QuarkBinaryConverter<ObjectClassValue>
{
    public override void Write(BinaryWriter writer, ObjectClassValue value)
    {
        writer.Write(value.Name ?? string.Empty);
        writer.Write(value.Value);
        writer.Write(value.Time.ToBinary());
        
        // Write Tags list
        writer.Write(value.Tags?.Count ?? 0);
        if (value.Tags != null)
        {
            foreach (var tag in value.Tags)
            {
                writer.Write(tag ?? string.Empty);
            }
        }
        
        // Write Infos list
        writer.Write(value.Infos?.Count ?? 0);
        if (value.Infos != null)
        {
            foreach (var info in value.Infos)
            {
                writer.Write(info.Id);
                writer.Write(info.Description ?? string.Empty);
            }
        }
        
        // Write EmptyList
        writer.Write(value.EmptyList?.Count ?? 0);
        if (value.EmptyList != null)
        {
            foreach (var item in value.EmptyList)
            {
                writer.Write(item ?? string.Empty);
            }
        }
    }
    
    public override ObjectClassValue Read(BinaryReader reader)
    {
        var result = new ObjectClassValue
        {
            Name = reader.ReadString(),
            Value = reader.ReadInt32(),
            Time = DateTime.FromBinary(reader.ReadInt64())
        };
        
        // Read Tags list
        var tagsCount = reader.ReadInt32();
        result.Tags = new List<string>(tagsCount);
        for (int i = 0; i < tagsCount; i++)
        {
            result.Tags.Add(reader.ReadString());
        }
        
        // Read Infos list
        var infosCount = reader.ReadInt32();
        result.Infos = new List<Info>(infosCount);
        for (int i = 0; i < infosCount; i++)
        {
            result.Infos.Add(new Info
            {
                Id = reader.ReadInt32(),
                Description = reader.ReadString()
            });
        }
        
        // Read EmptyList
        var emptyListCount = reader.ReadInt32();
        result.EmptyList = new List<string>(emptyListCount);
        for (int i = 0; i < emptyListCount; i++)
        {
            result.EmptyList.Add(reader.ReadString());
        }
        
        return result;
    }
}
