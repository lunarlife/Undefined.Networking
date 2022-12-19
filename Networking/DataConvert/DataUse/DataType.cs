using System;

namespace Networking.DataConvert.DataUse;

[Flags]
public enum DataType
{
    Field = 1 << 0,
    Property = 1 << 1
}