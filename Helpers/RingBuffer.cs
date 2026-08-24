// ============================================================================
// PC Health Dashboard - Helpers/RingBuffer.cs
// Zero-Allocation In-Memory Circular Buffer for Time-Series Telemetry
// ============================================================================

using System;
using System.Threading;

namespace PCHealthDashboard.Helpers;

/// <summary>
/// A high-performance, thread-safe, zero-allocation generic circular buffer for value types.
/// Retains the last N telemetry points in RAM, guaranteeing Zero-Disk-Wear.
/// </summary>
/// <typeparam name="T">Struct value type to store (e.g. MetricPoint).</typeparam>
public sealed class RingBuffer<T> where T : struct
{
    private readonly T[] _buffer;
    private readonly int _capacity;
    private int _head; // Next write position
    private int _count;
    private readonly Lock _lock = new();

    /// <summary>
    /// Initializes a new instance of RingBuffer with the specified fixed capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of elements before overwriting oldest items.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if capacity is less than 1.</exception>
    public RingBuffer(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");

        _capacity = capacity;
        _buffer = new T[capacity];
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Maximum capacity of the ring buffer.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Current number of elements stored in the buffer.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Returns true if the buffer contains zero elements.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _count == 0;
            }
        }
    }

    /// <summary>
    /// Returns true if the buffer has reached its capacity and is overwriting oldest elements.
    /// </summary>
    public bool IsFull
    {
        get
        {
            lock (_lock)
            {
                return _count == _capacity;
            }
        }
    }

    /// <summary>
    /// Appends a new item into the ring buffer. If capacity is reached, the oldest item is overwritten.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="item">Item to insert by readonly reference.</param>
    public void Push(in T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity)
            {
                _count++;
            }
        }
    }

    /// <summary>
    /// Copies elements into the destination span in chronological order (0 = oldest, Count - 1 = newest).
    /// Zero heap allocation.
    /// </summary>
    /// <param name="destination">Span to receive the elements.</param>
    /// <returns>Number of elements copied (min between destination length and current count).</returns>
    public int CopyTo(Span<T> destination)
    {
        lock (_lock)
        {
            int toCopy = Math.Min(destination.Length, _count);
            if (toCopy == 0) return 0;

            int start = (_head - _count + _capacity) % _capacity;
            for (int i = 0; i < toCopy; i++)
            {
                destination[i] = _buffer[(start + i) % _capacity];
            }
            return toCopy;
        }
    }

    /// <summary>
    /// Copies the most recent elements into the destination span in chronological order.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="destination">Span to receive the elements.</param>
    /// <returns>Number of elements copied.</returns>
    public int CopyLatestTo(Span<T> destination)
    {
        lock (_lock)
        {
            int toCopy = Math.Min(destination.Length, _count);
            if (toCopy == 0) return 0;

            int start = (_head - toCopy + _capacity) % _capacity;
            for (int i = 0; i < toCopy; i++)
            {
                destination[i] = _buffer[(start + i) % _capacity];
            }
            return toCopy;
        }
    }

    /// <summary>
    /// Gets the element at the specified chronological index (0 is oldest, Count - 1 is newest).
    /// </summary>
    /// <param name="index">Chronological index between 0 and Count - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if index is outside [0, Count - 1].</exception>
    public T this[int index]
    {
        get
        {
            lock (_lock)
            {
                if (index < 0 || index >= _count)
                    throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range for count {_count}.");

                int start = (_head - _count + _capacity) % _capacity;
                return _buffer[(start + index) % _capacity];
            }
        }
    }

    /// <summary>
    /// Attempts to get the element at the specified chronological index.
    /// </summary>
    public bool TryGetAt(int index, out T item)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _count)
            {
                item = default;
                return false;
            }

            int start = (_head - _count + _capacity) % _capacity;
            item = _buffer[(start + index) % _capacity];
            return true;
        }
    }

    /// <summary>
    /// Attempts to retrieve the latest (newest) pushed item without removing it.
    /// </summary>
    public bool TryPeekLatest(out T item)
    {
        lock (_lock)
        {
            if (_count == 0)
            {
                item = default;
                return false;
            }

            int latestIndex = (_head - 1 + _capacity) % _capacity;
            item = _buffer[latestIndex];
            return true;
        }
    }

    /// <summary>
    /// Clears all elements from the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer, 0, _capacity);
            _head = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// Creates a snapshot array of the current elements in chronological order.
    /// Note: Allocates an array. For zero allocation, use <see cref="CopyTo(Span{T})"/>.
    /// </summary>
    public T[] ToArray()
    {
        lock (_lock)
        {
            if (_count == 0) return Array.Empty<T>();
            var result = new T[_count];
            int start = (_head - _count + _capacity) % _capacity;
            for (int i = 0; i < _count; i++)
            {
                result[i] = _buffer[(start + i) % _capacity];
            }
            return result;
        }
    }
}
