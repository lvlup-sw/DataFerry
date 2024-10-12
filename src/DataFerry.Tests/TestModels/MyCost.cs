namespace lvlup.DataFerry.Tests.TestModels
{
    public struct MyCost : IComparable<MyCost>
    {
        public int Value { get; set; }

        public MyCost(int value)
        {
            Value = value;
        }

        public static MyCost operator -(MyCost a, MyCost b)
        {
            return new MyCost(a.Value - b.Value);
        }

        public static MyCost operator +(MyCost a, MyCost b)
        {
            return new MyCost(a.Value + b.Value);
        }

        public readonly int CompareTo(MyCost other)
        {
            return Value.CompareTo(other.Value);
        }
    }
}
