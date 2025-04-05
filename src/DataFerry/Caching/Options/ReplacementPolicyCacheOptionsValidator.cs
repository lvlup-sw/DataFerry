namespace lvlup.DataFerry.Caching.Options;

using Microsoft.Extensions.Options;

public class ReplacementPolicyCacheOptionsValidator<TKey> : IValidateOptions<ReplacementPolicyCacheOptions<TKey>>
    where TKey : notnull
{
    public ValidateOptionsResult Validate(string? name, ReplacementPolicyCacheOptions<TKey> options)
    {
        List<string> failures = new();

        if (options.Capacity < 3)
        {
            failures.Add($"{nameof(options.Capacity)} must be 3 or greater.");
        }

        // Allow InfiniteTimeSpan maybe? Or require > Zero?
        if (options.DefaultTimeToEvict <= TimeSpan.Zero && options.DefaultTimeToEvict != Timeout.InfiniteTimeSpan)
        {
            failures.Add($"{nameof(options.DefaultTimeToEvict)} must be positive.");
        }

        if (options.ExtendedTimeToEvictAfterHit <= TimeSpan.Zero && options.ExtendTimeToEvictAfterHit)
        {
            failures.Add($"{nameof(options.ExtendedTimeToEvictAfterHit)} must be positive when {nameof(options.ExtendTimeToEvictAfterHit)} is true.");
        }

        if (options.ConcurrencyLevel < 1)
        {
            failures.Add($"{nameof(options.ConcurrencyLevel)} must be 1 or greater.");
        }

        if (options.MetricPublicationInterval < TimeSpan.FromSeconds(1))
        {
            failures.Add($"{nameof(options.MetricPublicationInterval)} must be at least 1 second.");
        }

        if (options.KeyComparer == null)
        {
            failures.Add($"{nameof(options.KeyComparer)} cannot be null.");
        }

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }
}