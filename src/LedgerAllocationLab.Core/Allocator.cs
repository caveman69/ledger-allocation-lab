namespace LedgerAllocationLab.Core;

public static class Allocator
{
    public static long[] Allocate(long totalCents, IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
        {
            throw new ArgumentException("Weights cannot be empty.", nameof(weights));
        }

        if (totalCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCents), totalCents, "Total cents must be zero or greater.");
        } 

        var sumWeight = 0m;
        for (var i = 0; i < weights.Count; i++)
        {
            if (weights[i] < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weights), weights[i], $"Weights[{i}] is negative: {weights[i]}. Weights must be zero or greater.");
            }
            sumWeight += weights[i];
        }

        if (sumWeight == 0m)
        {
            throw new ArgumentException("Weights must not all be zero.", nameof(weights));
        }

        if (totalCents == 0)
        {
            return new long[weights.Count];
        }

        var exact = weights.Select(w => (totalCents * w) / sumWeight).ToArray();
        var result = exact.Select(e => (long)Math.Floor(e)).ToArray();
        var remainder = totalCents - result.Sum();
        foreach (var i in Enumerable.Range(0, weights.Count).OrderByDescending(j => exact[j] - result[j]).ThenBy(j => j).Take((int)remainder))
        {
            result[i]++;
        }
        return result;
    }
}
