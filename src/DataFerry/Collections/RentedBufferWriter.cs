using System.Buffers;

namespace lvlup.DataFerry.Collections
{
    public class RentedBufferWriter<T> : IBufferWriter<T>
    {
        private T[] _rentedBuffer;
        private int _index;

        private readonly StackArrayPool<T> _pool;

        public RentedBufferWriter(StackArrayPool<T> pool)
        {
            _pool = pool;
            _rentedBuffer = Array.Empty<T>();
            _index = 0;
        }

        public void Advance(int count)
        {
            _index += count;
        }

        public Memory<T> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _rentedBuffer.AsMemory(_index);
        }

        public Span<T> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _rentedBuffer.AsSpan(_index);
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (_rentedBuffer.Length - _index < sizeHint)
            {
                Grow(sizeHint);
            }
        }

        private void Grow(int requiredAdditionalCapacity)
        {
            var newSize = Math.Max(_rentedBuffer.Length * 2, _rentedBuffer.Length + requiredAdditionalCapacity);
            var newBuffer = _pool.Rent(newSize);

            Array.Copy(_rentedBuffer, newBuffer, _rentedBuffer.Length);
            _pool.Return(_rentedBuffer);

            _rentedBuffer = newBuffer;
        }

        public void Dispose()
        {
            if (_rentedBuffer.Length > 0)
            {
                _pool.Return(_rentedBuffer);
                _rentedBuffer = Array.Empty<T>();
            }
        }
    }
}
