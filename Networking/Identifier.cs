using System;

namespace Networking
{
    public class Identifier : IEquatable<Identifier>
    {
        private Guid _guid;

        public Identifier()
        {
            _guid = Guid.NewGuid();
        }
        public Identifier(Guid guid)
        {
            _guid = guid;
        }
        public override string ToString() => _guid.ToString();

        public static implicit operator string(Identifier identifier) => identifier?.ToString();

        public static bool operator ==(Identifier left, Identifier right) => left?._guid == right?._guid;

        public static bool operator !=(Identifier left, Identifier right) => !(left == right);

        public bool Equals(Identifier other) => _guid.Equals(other._guid);

        public override bool Equals(object obj) => obj is Identifier other && Equals(other);

        public override int GetHashCode() => _guid.GetHashCode();
        public byte[] ToByteArray() => _guid.ToByteArray();
    }
}