namespace LedgerAllocationLab.Core;

public static class Allocator
{
    /// <summary>
    /// This method allocates a total amount of cents among a list of shares based on their weights. It returns a list of tuples containing the key and the allocated cents for each share.
    /// Example: var allocations = Allocator.Allocate(amountCents, rates.Select(r => (r.DistrictId, r.Rate)).OrderBy(r => r.DistrictId).ToList())
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <param name="totalCents"></param>
    /// <param name="shares"></param>
    /// <returns></returns>
    public static IReadOnlyList<(TKey Key, long Cents)> Allocate<TKey>(
    long totalCents, IReadOnlyList<(TKey Key, decimal Weight)> shares)
    {
        long[] cents = Allocate(totalCents, shares.Select(s => s.Weight).ToArray());
        return shares.Select((s, i) => (s.Key, cents[i])).ToList();
    }

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
        var allocOrdering = Enumerable.Range(0, weights.Count).OrderByDescending(j => exact[j] - result[j]).ThenBy(j => j);
        foreach (var i in allocOrdering.Take((int)remainder))
        {
            result[i]++;
        }
        return result;
    }
}
