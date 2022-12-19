using System;

namespace Networking.DataConvert.DataUse;

[AttributeUsage(AttributeTargets.Class)]
public class DataConvertUseAttribute : Attribute
{
    public DataType Types { get; }

    public DataConvertUseAttribute(DataType types)
    {
        Types = types;
    }
}