using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Networking.DataConvert.Datas;
using Networking.DataConvert.DataUse;
using Networking.DataConvert.Handlers;
using Networking.Exceptions;
using Utils;

namespace Networking.DataConvert;

public class DataConverter
{
    private static readonly byte[] NullBytes =
    {
        136,
        22,
        67,
        55,
        255,
        1
    };

    private static readonly List<IConverter> Converters = new()
    {
        new BoolConverter(), new DoubleConverter(), new StringConverter(), new FloatConverter(), new IntConverter(),
        new ArrayConverter(), new Dot2Converter(), new Dot2IntConverter(), new VersionConverter(),
        new IdentifierConverter(), new EnumConverter(), new ListConverter(), new UShortConverter(), new TypeConverter(),
        new PacketConverter(), new FlagsEnumConverter(), new LongConverter(), new UintConverter()
    };


    public static byte[] Serialize(object? obj, ushort? switcher = null, bool excludeNoSwitchers = false, ConvertType converterUsing = ConvertType.All)
    {
        if (obj is null) return NullBytes;
        var objType = obj.GetType();
        var converter = converterUsing is ConvertType.ExcludeCurrent or ConvertType.ExcludeAll ? null : GetConverterForType(objType);
        if (converter is null)
        {
            var dataUse = objType.GetCustomAttribute<DataConvertUseAttribute>()?.Types ?? DataType.Field | DataType.Property;
            ushort length = 0;
            var total = new List<byte[]>();
            var nextConverting = ChangeForNextConverting(converterUsing);
            if(dataUse.HasFlag(DataType.Field))
                foreach (var field in GetFields(objType, switcher, excludeNoSwitchers))
                {
                    if(!ValidateValue<SerializeHandlerAttribute>(obj, field)) continue;
                    var serialize = Serialize(field.GetValue(obj), switcher, excludeNoSwitchers, nextConverting);
                    total.Add(serialize);
                    length += (ushort)serialize.Length;
                }

            if(dataUse.HasFlag(DataType.Property))
                foreach (var property in GetProperties(objType, switcher, excludeNoSwitchers))
                {
                    if(!ValidateValue<SerializeHandlerAttribute>(obj, property)) continue;
                    var serialize = Serialize(property.GetValue(obj), switcher, excludeNoSwitchers, nextConverting);
                    total.Add(serialize);
                    length += (ushort)serialize.Length;
                }
            if(obj is ISerializeHandler h) h.OnSerialize();
            return Combine(Serialize(length), Combine(total));
        }
        else
        {
            var serialize = converter.Serialize(obj);
            if(obj is ISerializeHandler h) h.OnSerialize();
            return converter is IStaticDataConverter ? serialize : Combine(Serialize((ushort)serialize.Length), serialize);
        }
    }

    private static ConvertType ChangeForNextConverting(ConvertType converterUsing)
    {
        return converterUsing switch
        {
            ConvertType.ExcludeCurrent => ConvertType.All,
            ConvertType.OnlyCurrent => ConvertType.ExcludeAll,
            _ => converterUsing
        };
    }

    
    public static void DeserializeInject(byte[] buffer, object obj, ushort? switcher = null,
        bool excludeNoSwitchers = false, ConvertType converterUsing = ConvertType.All)
    {
        ushort index = 0;
        DeserializeInject(buffer, obj, ref index, switcher, excludeNoSwitchers, converterUsing);
    }
    public static T? Deserialize<T>(byte[] buffer, ref ushort index, ushort? switcher = null, bool excludeNoSwitchers = false, ConvertType converterUsing = ConvertType.All) => (T?)Deserialize(buffer, typeof(T), ref index, switcher, excludeNoSwitchers, converterUsing);
    public static T? Deserialize<T>(byte[] buffer, ushort? switcher = null, bool excludeNoSwitchers = false, ConvertType converterUsing = ConvertType.All)
    {
        ushort index = 0;
        return (T?)Deserialize(buffer, typeof(T), ref index, switcher, excludeNoSwitchers, converterUsing);
    }

    public static object? Deserialize(byte[] buffer, Type? type, ushort? switcher = null,
        bool excludeNoSwitchers = false, ConvertType converterUsing = ConvertType.All, ConvertType invokeEvents = ConvertType.All)
    {
        ushort index = 0;
        return Deserialize(buffer, type, ref index, switcher, excludeNoSwitchers, converterUsing, invokeEvents);
    }
    public static object? Deserialize(byte[] buffer, Type? type, ref ushort index, ushort? switcher = null, bool excludeNoSwitchers = false, ConvertType converterUsing = ConvertType.All, ConvertType invokeEvents = ConvertType.All)
    {
        if (DataIsNull(buffer, ref index) || type == null)
        {
            index += (ushort)NullBytes.Length;
            return null;
        }
        var converter = converterUsing is ConvertType.ExcludeCurrent or ConvertType.ExcludeAll ? null : GetConverterForType(type);
        switch (converter)
        {
            case IDynamicDataConverter:
            {
                var length = Deserialize<ushort>(buffer, ref index);
                var des = converter.Deserialize(buffer, index, length, type);
                index += length;
                return des;
            }
            case IStaticDataConverter staticDataConverter:
            {
                var des = converter.Deserialize(buffer, index, staticDataConverter.Length, type);
                index += staticDataConverter.Length;
                return des;
            }
        }

        if(!IsAvailableTypeForInstance(type)) return null;
        var o = ReflectionUtils.GetConstructor(type)?.Invoke(Array.Empty<object>()) ?? FormatterServices.GetUninitializedObject(type);
        DeserializeInject(buffer, o, ref index, switcher, excludeNoSwitchers, ChangeForNextConverting(converterUsing), ChangeForNextConverting(invokeEvents));
        return o;
    }


    public static void DeserializeInject(byte[] buffer, object? obj, ref ushort index, ushort? switcher = null, bool excludeNoSwitchers = false, ConvertType converterUsing = ConvertType.All, ConvertType invokeEvents = ConvertType.All)
    {
        var type = obj.GetType();
        var dataUse = type.GetCustomAttribute<DataConvertUseAttribute>()?.Types ?? DataType.Field | DataType.Property;
        Deserialize<ushort>(buffer, ref index);
        converterUsing = ChangeForNextConverting(converterUsing);
        if(dataUse.HasFlag(DataType.Field))
            foreach (var field in GetFields(type, switcher, excludeNoSwitchers))
            {
                try
                {
                    if(!ValidateValue<DeserializeHandlerAttribute>(obj, field)) continue;
                    if (Deserialize(buffer, field.FieldType, ref index, switcher, excludeNoSwitchers,
                            converterUsing) is not { } des) continue;
                    field.SetValue(obj, des);
                }
                catch (TargetInvocationException e)
                {
                    throw new DeserializeException($"type: {type.Name} field: {field.Name}\n{e.GetType().Name}: {e.InnerException}");
                }
                catch (Exception e)
                {
                    throw new DeserializeException($"type: {type.Name} field: {field.Name}\n{e.GetType().Name}: {e.Message}");
                }
            }
        if(dataUse.HasFlag(DataType.Property))
            foreach (var property in GetProperties(type, switcher, excludeNoSwitchers))
            {
                try
                {
                    if (!ValidateValue<DeserializeHandlerAttribute>(obj, property)) continue;
                    if (Deserialize(buffer, property.PropertyType, ref index, switcher, excludeNoSwitchers,
                            converterUsing) is not { } des) continue;
                    property.SetValue(obj, des);
                }
                catch (TargetInvocationException e)
                {
                    throw new DeserializeException($"type: {type.Name} property: {property.Name}\n{e.GetType().Name}: {e.InnerException}");
                }
                catch (Exception e)
                {
                    throw new DeserializeException($"type: {type.Name} property: {property.Name}\n{e.GetType().Name}: {e.Message}");
                }
            }
        if(invokeEvents is not (ConvertType.ExcludeCurrent or ConvertType.ExcludeAll) && obj is IDeserializeHandler handler)
            handler.OnDeserialize();
    }

    private static bool ValidateValue<T>(object obj, FieldInfo field) where T : HandlerAttribute
    {
        if (field.GetCustomAttribute<T>(true) is not { } attribute) return true;
        if(obj.GetType().GetMethod(attribute.MethodName,
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static) is not { } method) throw new HandlerException($"method with name {attribute.MethodName} not found");
        if (method.GetParameters().Length != 0) throw new HandlerException($"method with name {attribute.MethodName} has parameters");
        var invoke  = method.Invoke(obj, null);
        return method.ReturnParameter.ParameterType != typeof(bool) || (bool)invoke!;
    }
    
    private static bool ValidateValue<T>(object obj, PropertyInfo property) where T : HandlerAttribute
    {
        if (property.GetCustomAttribute<T>(true) is not { } attribute) return true;
        if(obj.GetType().GetMethod(attribute.MethodName,
               BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static) is not { } method) throw new HandlerException($"method with name {attribute.MethodName} not found");
        if (method.GetParameters().Length != 0) throw new HandlerException($"method with name {attribute.MethodName} has parameters");
        var invoke  = method.Invoke(obj, null);
        return method.ReturnParameter?.ParameterType != typeof(bool) || (bool)invoke!;
    }
    private static IEnumerable<FieldInfo> GetFields(Type type, ushort? switcher,
        bool excludeNoSwitchers)
    {
        var fields = new List<FieldInfo>().AsEnumerable();
        var baseType = type;
        while (baseType != null && baseType != typeof(object))
        {
            if (baseType.GetCustomAttribute<DataConvertUseAttribute>()?.Types is { } att &&
                !att.HasFlag(DataType.Field))
            {
                baseType = baseType.BaseType;
                continue;
            }
            fields = baseType.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.GetCustomAttributes()
                    .FirstOrDefault(c => c is ExcludeDataAttribute or CompilerGeneratedAttribute) == null)
                .Where(f => !f.IsInitOnly)
                .OrderBy(field => field.Name).Concat(fields);
            baseType = baseType.BaseType;
        }
        return switcher == null
            ? fields
            : excludeNoSwitchers
                ? fields.Where(f =>
                    f.GetCustomAttribute<DataSwitchAttribute>(true) is { } s && s.Id == switcher)
                : fields.Where(f =>
                    f.GetCustomAttribute<DataSwitchAttribute>(true) is not { } s || s.Id == switcher);
    }
    private static IEnumerable<PropertyInfo> GetProperties(Type type, ushort? switcher,
        bool excludeNoSwitchers)
    {
        if (type.GetCustomAttribute<DataConvertUseAttribute>()?.Types is {  } att && !att.HasFlag(DataType.Property)) return Enumerable.Empty<PropertyInfo>();
        var fields = type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.GetCustomAttributes().FirstOrDefault(c =>  c is ExcludeDataAttribute or CompilerGeneratedAttribute) == null).Where(f => f.GetMethod != null && f.SetMethod != null)
            .OrderBy(field => field.Name).AsEnumerable();
        return switcher == null
            ? fields
            : excludeNoSwitchers
                ? fields.Where(f =>
                    f.GetCustomAttribute<DataSwitchAttribute>(true) is { } s && s.Id == switcher)
                : fields.Where(f =>
                    f.GetCustomAttribute<DataSwitchAttribute>(true) is not { } s || s.Id == switcher);
    }

    private static bool DataIsNull(byte[] bytes, ref ushort index) => NullBytes[0] == bytes[index] &&
                                                                      NullBytes[1] == bytes[index + 1] &&
                                                                      NullBytes[2] == bytes[index + 2];
    private static bool IsAvailableTypeForInstance(Type type) => !type.IsAbstract && !type.IsInterface;

    public static IConverter? GetConverterForType(Type type) => Converters.FirstOrDefault(c =>
    {
        return c.IsValidConvertor(type);
    });

    public static void AddDynamicConverter(IDynamicDataConverter converter) => AddConverter(converter);
    public static void AddStaticConverter(IStaticDataConverter converter) => AddConverter(converter);

    private static void AddConverter(IConverter converter)
    {
        if (Converters.Contains(converter)) throw new ConverterException($"converter already exists");
        Converters.Add(converter);
    }

    public static byte[] Combine(params byte[][] buffers)
    {
        if (buffers.Length == 0) return Array.Empty<byte>();
        if (buffers.Length == 1) return buffers[0];
        var result = new byte[buffers.Sum(b => b.Length)];
        var index = 0;
        foreach (var buffer in buffers)
        {
            buffer.CopyTo(result, index);
            index += buffer.Length;
        }
        return result;
    }
    public static byte[] Combine(IEnumerable<byte[]> buffers) => Combine(buffers as byte[][] ?? buffers.ToArray());

    public static object GetUninitializedObject(Type type) => FormatterServices.GetUninitializedObject(type);
    public static T GetUninitializedObject<T>() => (T)GetUninitializedObject(typeof(T));
}