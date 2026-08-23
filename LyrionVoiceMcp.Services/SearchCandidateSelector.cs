using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Services;

internal sealed class SearchCandidateSelector
{
    private readonly Random random;

    public SearchCandidateSelector()
        : this(Random.Shared)
    {
    }

    internal SearchCandidateSelector(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        this.random = random;
    }

    public SearchCandidateReservoir CreateReservoir(int capacity) =>
        new(capacity, random);

    public IReadOnlyList<CatalogueSearchCandidate> Rotate(
        IReadOnlyCollection<CatalogueSearchCandidate> candidates,
        int limit,
        Func<CatalogueSearchCandidate, double>? weight = null)
    {
        if (limit <= 0 || candidates.Count == 0)
        {
            return [];
        }

        var selected = new List<CatalogueSearchCandidate>(
            Math.Min(limit, candidates.Count));
        foreach (var scoreBand in candidates
            .GroupBy(candidate => candidate.Score)
            .OrderByDescending(group => group.Key))
        {
            var albumBuckets = scoreBand
                .GroupBy(AlbumKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => Order(group.ToList(), weight))
                .ToList();
            if (weight is null)
            {
                Shuffle(albumBuckets);
            }
            else
            {
                albumBuckets = WeightedOrder(
                    albumBuckets,
                    bucket => weight(bucket[0]));
            }
            for (var offset = 0; selected.Count < limit; offset++)
            {
                var added = false;
                foreach (var bucket in albumBuckets)
                {
                    if (offset >= bucket.Count)
                    {
                        continue;
                    }

                    selected.Add(bucket[offset]);
                    added = true;
                    if (selected.Count == limit)
                    {
                        break;
                    }
                }

                if (!added)
                {
                    break;
                }
            }

            if (selected.Count == limit)
            {
                break;
            }
        }

        return selected;
    }

    private static string AlbumKey(CatalogueSearchCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.Album)
            ? $"\0{candidate.Identity.Kind}:{candidate.Identity.Id}"
            : candidate.Album;

    private List<T> Shuffle<T>(List<T> values)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var replacement = random.Next(index + 1);
            (values[index], values[replacement]) = (values[replacement], values[index]);
        }

        return values;
    }

    private List<CatalogueSearchCandidate> Order(
        List<CatalogueSearchCandidate> candidates,
        Func<CatalogueSearchCandidate, double>? weight) =>
        weight is null
            ? Shuffle(candidates)
            : WeightedOrder(candidates, weight);

    private List<T> WeightedOrder<T>(
        List<T> values,
        Func<T, double> weight) =>
        values
            .Select(value => new
            {
                Value = value,
                Key = WeightedKey(weight(value))
            })
            .OrderBy(item => item.Key)
            .Select(item => item.Value)
            .ToList();

    private double WeightedKey(double weight)
    {
        if (!double.IsFinite(weight) || weight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight),
                weight,
                "Candidate weight must be finite and greater than zero.");
        }

        return -Math.Log(1 - random.NextDouble()) / weight;
    }
}

internal sealed class SearchCandidateReservoir
{
    private readonly int capacity;
    private readonly Random random;
    private readonly List<CatalogueSearchCandidate> candidates;
    private int observedCount;

    public SearchCandidateReservoir(int capacity, Random random)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, null);
        }

        this.capacity = capacity;
        this.random = random;
        candidates = new List<CatalogueSearchCandidate>(capacity);
    }

    public IReadOnlyList<CatalogueSearchCandidate> Candidates => candidates;

    public void Consider(CatalogueSearchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        observedCount++;
        if (candidates.Count < capacity)
        {
            candidates.Add(candidate);
            return;
        }

        var replacement = random.Next(observedCount);
        if (replacement < capacity)
        {
            candidates[replacement] = candidate;
        }
    }
}
