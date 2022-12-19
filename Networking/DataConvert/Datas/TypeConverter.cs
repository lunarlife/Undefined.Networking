using System;
using System.Linq;

namespace Networking.DataConvert.Datas;

public class TypeConverter : IDynamicDataConverter
{

    public bool IsValidConvertor(Type type) => typeof(Type).IsAssignableFrom(type);

    public byte[] Serialize(object o) => DataConverter.Serialize(((Type)o).FullName);

    public object? Deserialize(byte[] data, Type type)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var name = DataConverter.Deserialize<string>(data);
        return assemblies.SelectMany(assembly => assembly.GetTypes()).FirstOrDefault(t => name == t.FullName);
    }
}