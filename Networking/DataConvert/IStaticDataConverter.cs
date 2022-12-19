namespace Networking.DataConvert;

public interface IStaticDataConverter : IConverter
{
    public ushort Length { get; }
}
