namespace LedgerAllocationLab.Core.Tests;

public class AllocatorTests
{
    public static TheoryData<long, decimal[], long[]> TestCases() => new()
    {
        { 100L, new decimal[] { 0.5m, 0.5m }, new long[] { 50, 50 } },
        { 100L, new decimal[] { 0.3m, 0.7m }, new long[] { 30, 70 } },
        { 100L, new decimal[] { 0.1m, 0.2m, 0.3m, 0.4m }, new long[] { 10, 20, 30, 40 } },
        { 100L, new decimal[] { 0.3333m, 0.3333m, 0.3334m }, new long[] { 33, 33, 34 } },
        { 100L, new decimal[] { 0.1m, 0.1m, 0.1m }, new long[] { 34, 33, 33 } },
        { 100000L, new decimal[] { 1.5000m, 0.7500m, 4.2000m, 0.5500m }, new long[] { 21429, 10714, 60000, 7857 } },
        { 4578L, new decimal[] { 0.5m }, new long[] { 4578 } }, //one weight, single value
        { 3245L, new decimal[] { 0.0001m }, new long[] { 3245 } }, //one weight, small value
        { 9999L, new decimal[] { 4.2m, 0.0m }, new long[] { 9999, 0 } }, //zero weight
        { 9999L, new decimal[] { 0.0m, 4.5m }, new long[] { 0, 9999 } }, //zero weight
        { 0L, new decimal[] { 0.5m }, new long[] { 0 } }, //zero totalCents
        { 0L, new decimal[] { 0.5m, 1.2m }, new long[] { 0, 0 } }, //zero totalCents
        { 100L, new decimal[] { 0.0m, 0.0m, 1.2m }, new long[] { 0, 0, 100 } }, //zero weights
        { 106L, new decimal[] { 0.5m, 0.3m, 0.3m, 0.3m }, new long[] { 38, 23, 23, 22 } }, //positional tie-breaker
        { 100L, new decimal[] { 0.0m, 1m, 1m, 1m }, new long[] { 0, 34, 33, 33 } },
        { 1L, new decimal[] { 0.5m, 0.3m, 0.3m, 0.3m }, new long[] { 1, 0, 0, 0 } },
        { 9_000_000_000_000_000L, [1m, 2m], [3_000_000_000_000_000, 6_000_000_000_000_000]}
    };

    public static TheoryData<long, decimal[], Type, string[]> InvalidTestCases() => new()
    {
        { 100L, new decimal[] { -0.5m, 1.5m }, typeof(ArgumentOutOfRangeException), new string[] { "-0.5", "Weights[0]" } }, // Negative weight
        { 100L, Array.Empty<decimal>(), typeof(ArgumentException), Array.Empty<string>() }, // Empty weights
        { 100L, new decimal[] { 0m, 0m }, typeof(ArgumentException), Array.Empty<string>() }, // All weights zero
        { 100L, new decimal[] { 0m }, typeof(ArgumentException), Array.Empty<string>() }, // All weights zero
        { 100L, null!, typeof(ArgumentNullException), Array.Empty<string>() }, // null weights
        { -100L, [1m], typeof(ArgumentOutOfRangeException), new string[] { "-100", "totalCents" } }, // Negative totalCents
        { 0, [-1m], typeof(ArgumentOutOfRangeException), new string[] { "-1", "Weights[0]" } }, // negative weight with zero totalCents should throw for negative weight first before returning all zeros for zero totalCents
        { 0, [0m, 0m], typeof(ArgumentException), Array.Empty<string>() }, // all weights zero should throw before returning all zeros for zero totalCents
    };

    [Theory]
    [MemberData(nameof(TestCases))]
    public void Allocate_ShouldReturnExpectedAllocation(long totalCents, decimal[] weights, long[] expected)
    {
        var result = Allocator.Allocate(totalCents, weights);

        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(InvalidTestCases))]
    public void Allocate_ShouldThrow_ForInvalidInputs(long totalCents, decimal[] weights, Type expectedExceptionType, string[] expectedInMessage)
    {
        var exception = Assert.Throws(expectedExceptionType, () => Allocator.Allocate(totalCents, weights));
        foreach (var message in expectedInMessage)
        {
            Assert.Contains(message, exception.Message);
        }
    }

    [Fact]
    public void Allocate_Invariants_HoldForRandomInputs()
    {
        var rng = new Random(20260924);
        for (var n = 0; n < 5000; n++)
        {
            var total = rng.NextInt64(0, 10_000_000);
            var weights = Enumerable.Range(0, rng.Next(1, 12))
                .Select(_ => Math.Round((decimal)rng.NextDouble() * 10m, 4)).ToArray();
            if (weights.Sum() == 0m) { continue; }

            var result = Allocator.Allocate(total, weights);
            var sum = weights.Sum();

            Assert.Equal(total, result.Sum());
            for (var i = 0; i < weights.Length; i++)
            {
                var exact = total * weights[i] / sum;
                Assert.True(result[i] >= 0);
                Assert.True(Math.Abs(result[i] - exact) < 1m, $"case {n}, index {i}");
            }
            Assert.Equal(result, Allocator.Allocate(total, weights));
        }
    }
}
