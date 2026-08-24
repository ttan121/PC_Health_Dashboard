using Xunit;

// Disable parallel test execution so process-wide allocation metrics (GC.GetTotalAllocatedBytes) are not polluted by concurrent test runners.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
