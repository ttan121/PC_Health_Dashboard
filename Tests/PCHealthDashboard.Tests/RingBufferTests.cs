// ============================================================================
// PC Health Dashboard - Tests/PCHealthDashboard.Tests/RingBufferTests.cs
// Unit Tests for Zero-Allocation Thread-Safe RingBuffer
// ============================================================================

using System;
using System.Threading.Tasks;
using PCHealthDashboard.Helpers;
using PCHealthDashboard.Models;
using Xunit;

namespace PCHealthDashboard.Tests;

public class RingBufferTests
{
    [Fact]
    public void TEST_RB_01_CapacityAndFIFO_PreservedWhenOverflowing()
    {
        // Arrange
        const int capacity = 60;
        var buffer = new RingBuffer<MetricPoint>(capacity);

        // Act: Push 1,000 sequential items
        for (int i = 0; i < 1000; i++)
        {
            buffer.Push(new MetricPoint(i, (float)i));
        }

        // Assert: Count is exactly capacity and buffer is full
        Assert.Equal(capacity, buffer.Count);
        Assert.True(buffer.IsFull);
        Assert.False(buffer.IsEmpty);

        // Copy out elements into array
        var array = buffer.ToArray();
        Assert.Equal(capacity, array.Length);

        // Expected items are the last 60 elements (940 to 999) in order
        for (int i = 0; i < capacity; i++)
        {
            int expectedValue = 1000 - capacity + i;
            Assert.Equal((float)expectedValue, array[i].Value);
            Assert.Equal(expectedValue, array[i].TimestampUtcTicks);
            Assert.Equal((float)expectedValue, buffer[i].Value);
        }
    }

    [Fact]
    public void TEST_RB_02_ZeroAllocation_DuringContinuousPushesAndSpanCopy()
    {
        // Arrange
        const int capacity = 300;
        var buffer = new RingBuffer<MetricPoint>(capacity);

        // Warm up JIT and thread local storage
        for (int i = 0; i < 1000; i++)
        {
            buffer.Push(new MetricPoint(i, (float)i));
        }

        Span<MetricPoint> stackSpan = stackalloc MetricPoint[capacity];
        buffer.CopyTo(stackSpan);

        // GC collect to clear prior noise
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Act & Measure: 10,000 pushes and 100 Span copies
        long bytesAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var dummyPoint = new MetricPoint(123456L, 42.5f);
        for (int i = 0; i < 10_000; i++)
        {
            buffer.Push(in dummyPoint);
            if (i % 100 == 0)
            {
                buffer.CopyTo(stackSpan);
            }
        }

        long bytesAllocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocated = bytesAllocatedAfter - bytesAllocatedBefore;

        // Assert: Zero heap allocation
        Assert.Equal(0L, totalAllocated);
    }

    [Fact]
    public void TEST_RB_03_ThreadSafety_UnderConcurrentPushes()
    {
        // Arrange
        const int capacity = 100;
        var buffer = new RingBuffer<MetricPoint>(capacity);
        const int threadCount = 8;
        const int pushesPerThread = 2000;

        // Act: Concurrently push from multiple background threads
        Parallel.For(0, threadCount, t =>
        {
            for (int i = 0; i < pushesPerThread; i++)
            {
                buffer.Push(new MetricPoint(t * 10000 + i, (float)i));
            }
        });

        // Assert
        Assert.Equal(capacity, buffer.Count);
        Assert.True(buffer.IsFull);

        // Verify indexer and ToArray operate without exception
        var snapshot = buffer.ToArray();
        Assert.Equal(capacity, snapshot.Length);
    }

    [Fact]
    public void TEST_RB_04_IndexerAndTryGetAt_ChronologicalOrdering()
    {
        // Arrange
        var buffer = new RingBuffer<int>(5);

        // Push 3 items
        buffer.Push(10);
        buffer.Push(20);
        buffer.Push(30);

        Assert.Equal(3, buffer.Count);
        Assert.Equal(10, buffer[0]);
        Assert.Equal(20, buffer[1]);
        Assert.Equal(30, buffer[2]);

        Assert.True(buffer.TryGetAt(1, out int val));
        Assert.Equal(20, val);

        Assert.False(buffer.TryGetAt(3, out _));
        Assert.False(buffer.TryGetAt(-1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[3]);

        // Push 4 more items to overflow (total 7 pushed: 10,20,30,40,50,60,70)
        // Capacity 5 should contain: 30, 40, 50, 60, 70
        buffer.Push(40);
        buffer.Push(50);
        buffer.Push(60);
        buffer.Push(70);

        Assert.Equal(5, buffer.Count);
        Assert.Equal(30, buffer[0]);
        Assert.Equal(40, buffer[1]);
        Assert.Equal(50, buffer[2]);
        Assert.Equal(60, buffer[3]);
        Assert.Equal(70, buffer[4]);
    }

    [Fact]
    public void TEST_RB_05_CopyLatestTo_CopiesMostRecentElements()
    {
        // Arrange
        var buffer = new RingBuffer<int>(10);
        for (int i = 1; i <= 8; i++)
        {
            buffer.Push(i); // 1,2,3,4,5,6,7,8
        }

        // Act: Copy latest 3 items
        Span<int> dest = stackalloc int[3];
        int copied = buffer.CopyLatestTo(dest);

        // Assert
        Assert.Equal(3, copied);
        Assert.Equal(6, dest[0]);
        Assert.Equal(7, dest[1]);
        Assert.Equal(8, dest[2]);
    }

    [Fact]
    public void TEST_RB_06_TryPeekLatest_And_Clear()
    {
        // Arrange
        var buffer = new RingBuffer<int>(5);

        Assert.False(buffer.TryPeekLatest(out _));

        buffer.Push(100);
        buffer.Push(200);

        Assert.True(buffer.TryPeekLatest(out int latest));
        Assert.Equal(200, latest);

        // Clear
        buffer.Clear();
        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.IsEmpty);
        Assert.False(buffer.TryPeekLatest(out _));
    }
}
