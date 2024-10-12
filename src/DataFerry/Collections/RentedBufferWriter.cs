using System.Buffers;

namespace lvlup.DataFerry.Collections
{
    public class RentedBufferWriter<T> : IBufferWriter<T>
    {
        public void Advance(int count)
        {
            throw new NotImplementedException();
        }

        public Memory<T> GetMemory(int sizeHint = 0)
        {
            throw new NotImplementedException();
        }

        public Span<T> GetSpan(int sizeHint = 0)
        {
            throw new NotImplementedException();
        }
    }
}
