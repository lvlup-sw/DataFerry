using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace lvlup.DataFerry.Caching.Options;

/// <summary>
/// Validation attribute to ensure a TimeSpan property meets a minimum value requirement.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
internal class TimeSpanAttribute : ValidationAttribute
{
    /// <summary>
    /// Gets the minimum allowed TimeSpan value.
    /// </summary>
    public TimeSpan MinValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeSpanAttribute"/> class with a minimum value specified in milliseconds.
    /// </summary>
    /// <param name="minMs">The minimum allowed value in milliseconds. Must be non-negative.</param>
    public TimeSpanAttribute(long minMs = 0)
        : base()
    {
        if (minMs < 0)
        {
            // Or throw ArgumentOutOfRangeException depending on desired behavior for invalid attribute usage
            minMs = 0;
        }

        MinValue = TimeSpan.FromMilliseconds(minMs);

        ErrorMessage ??= $"The field {{0}} must be a TimeSpan with a value of {MinValue} or greater.";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeSpanAttribute"/> class with a minimum value specified as a standard TimeSpan string.
    /// </summary>
    /// <param name="min">The minimum allowed value as a string (e.g., "00:00:05"). Must be non-negative.</param>
    public TimeSpanAttribute(string min)
        : base()
    {
        if (TimeSpan.TryParse(min, CultureInfo.InvariantCulture, out TimeSpan minTimeSpan) && minTimeSpan >= TimeSpan.Zero)
        {
            MinValue = minTimeSpan;
        }
        else
        {
            MinValue = TimeSpan.Zero;
        }

        ErrorMessage ??= $"The field {{0}} must be a TimeSpan with a value of {MinValue} or greater.";
    }

    /// <summary>
    /// Determines whether the specified value of the object is valid.
    /// </summary>
    /// <param name="value">The value of the object to validate.</param>
    /// <returns>true if the specified value is valid; otherwise, false.</returns>
    public override bool IsValid(object? value)
    {
        // Automatically pass if value is null; RequiredAttribute should be used for null checks.
        return value switch
        {
            null => true,
            TimeSpan tsValue => tsValue >= MinValue,
            _ => false
        };
    }
}