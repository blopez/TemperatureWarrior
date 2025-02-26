using System.Collections.Generic;

namespace RingBuffer
{
    public class RingBuffer<T>
    {

        // tail == head -> buffer vacío
        // tail + 1 == head -> buffer lleno
        private int head = 0;
        private int tail = 0;

        // Lista en lugar de cola para permitir resizing entre distintos usos (rondas)
        private List<T> buffer;

        public int Capacity { get { return buffer.Capacity; } }

        public RingBuffer() : this(4) { }

        public RingBuffer(int capacity)
        {
            buffer = new(capacity);
            ResizeAndReset(capacity);
        }

        public bool Dequeue(out T result)
        {
            result = default!;
            if (tail == head)
                return false;
            result = buffer[head];
            head = (head + 1) % Capacity;
            return true;
        }

        public bool Enqueue(T item)
        {
            var tail_ = tail;
            if (tail_ + 1 == head)
                return false;
            buffer[tail_] = item;
            tail = (tail + 1) % Capacity;
            return true;
        }

        public void Clear() { buffer.Clear(); }

        public void ResizeAndReset(int newCapacity)
        {
            // el buffer tiene como mínimo una capacidad de Count
            var capacityDelta = newCapacity - buffer.Count;
            if (capacityDelta > 0) // Forzar crecimiento
                for (int i = 0; i < capacityDelta; ++i)
                    buffer.Add(default!);
            head = 0;
            tail = 0;
        }

    }
}