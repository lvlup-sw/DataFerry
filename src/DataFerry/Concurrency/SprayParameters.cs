// ===========================================================================
// <copyright file="SprayParameters.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

namespace lvlup.DataFerry.Concurrency;

/// <summary>
/// Holds the calculated parameters for a SprayList probabilistic DeleteMin operation.
/// These parameters guide the random walk down the SkipList structure.
/// </summary>
internal readonly struct SprayParameters
{
    /// <summary>
    /// The calculated starting height (h ≈ log n + K) for the spray walk.
    /// Starting high allows the walk to potentially land anywhere lower down.
    /// Value is clamped to list bounds and adjusted to be divisible by DescentLength.
    /// </summary>
    public readonly int StartHeight;

    /// <summary>
    /// The maximum random horizontal jump length (y ≈ M * (log n)^3) at each level of the spray.
    /// Designed to jump past the absolute minimum but likely land near the beginning of the list.
    /// </summary>
    public readonly int MaxJumpLength;

    /// <summary>
    /// The number of levels to descend (d ≈ log * log n) after each horizontal jump phase.
    /// A gradual descent helps distribute landing points near the list's start.
    /// Value is at least 1.
    /// </summary>
    public readonly int DescentLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="SprayParameters"/> struct.
    /// </summary>
    /// <param name="startHeight">The calculated start height.</param>
    /// <param name="maxJumpLength">The calculated maximum jump length.</param>
    /// <param name="descentLength">The calculated descent length.</param>
    public SprayParameters(int startHeight, int maxJumpLength, int descentLength)
    {
        StartHeight = startHeight;
        MaxJumpLength = maxJumpLength;
        DescentLength = descentLength;
    }

    /// <summary>
    /// Calculates the parameters needed for the SprayList probabilistic DeleteMin operation
    /// based on the current list state and tuning constants.
    /// </summary>
    /// <param name="currentCount">The currently estimated count of items in the queue (must be > 1).</param>
    /// <param name="topLevel">The maximum level index allowed in the SkipList.</param>
    /// <param name="offsetK">A tuning constant (K) added to the height calculation.</param>
    /// <param name="offsetM">A tuning constant (M) multiplied by the jump length calculation.</param>
    /// <returns>A SprayParameters struct containing the calculated height, max jump length, and descent length.</returns>
    public static SprayParameters CalculateParameters(int currentCount, int topLevel, int offsetK, int offsetM)
    {
        // Base calculations (assuming currentCount > 1)
        double logN = Math.Log(currentCount);

        // Ensure inner Log argument is >= 1 for LogLogN calculation
        double logLogN = Math.Log(Math.Max(1.0, logN));

        // Calculate initial parameters
        int h = (int)logN + offsetK;
        int y = offsetM * (int)Math.Pow(Math.Max(1.0, logN), 3);
        int d = Math.Max(1, (int)logLogN);

        // Clamp height to list bounds and ensure non-negative
        int startHeight = Math.Min(topLevel, Math.Max(0, h));

        // Ensure startHeight is divisible by descentLength for clean loop steps
        if (startHeight >= d && startHeight % d != 0)
        {
            startHeight = d * (startHeight / d);
        }
        else if (startHeight < d)
        {
            startHeight = Math.Max(0, startHeight);
        }

        // Ensure jump length is non-negative
        int maxJumpLength = Math.Max(0, y);

        return new SprayParameters(startHeight, maxJumpLength, descentLength: d);
    }
}