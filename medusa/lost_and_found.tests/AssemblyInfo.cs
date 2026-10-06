using Xunit;

// Keep the shared Clock and temp-directory usage deterministic across the suite.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
