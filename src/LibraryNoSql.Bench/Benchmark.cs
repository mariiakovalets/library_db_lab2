using System.Diagnostics;
using System.Text.Json;

namespace LibraryNoSql.Bench;

public record Measurement(string Store, int Iterations, double MedianMs, double P95Ms, double MeanMs);

public record ScenarioResult(string Scenario, string Why, Measurement Sql, Measurement Mongo);

/// <summary>
/// Runs one scenario on both stores with the same inputs: a warm-up first (not counted),
/// then many timed calls. Every call is timed separately, the report uses the median and the 95th percentile.
/// </summary>
public static class Benchmark
{
    public static async Task<ScenarioResult> RunAsync(
        string scenario, string why,
        ICatalogStore sql, ICatalogStore mongo,
        int warmup, int iterations,
        Func<ICatalogStore, int, Task> operation)
    {
        Console.Write($"  {scenario}: ");
        var sqlResult = await MeasureAsync(sql, warmup, iterations, operation);
        Console.Write($"{sql.Name} {sqlResult.MedianMs:0.000} ms, ");
        var mongoResult = await MeasureAsync(mongo, warmup, iterations, operation);
        Console.WriteLine($"{mongo.Name} {mongoResult.MedianMs:0.000} ms (median)");

        return new ScenarioResult(scenario, why, sqlResult, mongoResult);
    }

    private static async Task<Measurement> MeasureAsync(
        ICatalogStore store, int warmup, int iterations, Func<ICatalogStore, int, Task> operation)
    {
        // call i always gets input i, so both stores see exactly the same sequence of inputs
        for (var i = 0; i < warmup; i++)
        {
            await operation(store, i);
        }

        var times = new double[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var start = Stopwatch.GetTimestamp();
            await operation(store, warmup + i);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(times);
        return new Measurement(
            store.Name,
            iterations,
            Percentile(times, 0.50),
            Percentile(times, 0.95),
            times.Average());
    }

    private static double Percentile(double[] sorted, double p)
    {
        var index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

/// <summary>
/// Before timing anything, checks that both stores give the same answers to the same questions.
/// Otherwise the comparison would measure two different things.
/// </summary>
public static class ResultCheck
{
    public static async Task<(string What, int Checked, int Mismatched)> CompareAsync(
        string what, int count, Func<int, ICatalogStore, Task<object?>> query, ICatalogStore sql, ICatalogStore mongo)
    {
        var mismatched = 0;
        for (var i = 0; i < count; i++)
        {
            var a = JsonSerializer.Serialize(await query(i, sql));
            var b = JsonSerializer.Serialize(await query(i, mongo));
            if (a == b)
            {
                continue;
            }

            if (mismatched++ == 0)
            {
                Console.WriteLine($"    {what} differs:\n      {sql.Name}: {a}\n      {mongo.Name}: {b}");
            }
        }

        Console.WriteLine($"  {what}: {count - mismatched}/{count} same");
        return (what, count, mismatched);
    }
}
