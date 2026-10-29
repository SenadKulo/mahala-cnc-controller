namespace GrblCore;

/// <summary>
/// When the air compressors switch on: another Arduino starts them every <paramref name="Period"/>
/// (15 minutes), to the second, and their start can restart the Grbl board.
/// </summary>
/// <param name="KnownStart">Any moment a compressor switched on; the others are whole periods away from it.</param>
public sealed record CompressorSchedule(DateTime KnownStart, TimeSpan Period)
{
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromMinutes(15);

    /// <summary>Time from the compressors switching on until Grbl's banner arrives (the Arduino booting).</summary>
    public static readonly TimeSpan BootDelay = TimeSpan.FromSeconds(1);

    /// <summary>How long before a start the machine must stand still, so the estimate can be off a bit.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromSeconds(10);

    /// <summary>How long after a start to wait before the laser goes on again.</summary>
    public static readonly TimeSpan After = TimeSpan.FromSeconds(10);

    /// <summary>First start at or after <paramref name="time"/>.</summary>
    public DateTime NextStart(DateTime time)
    {
        if (Period <= TimeSpan.Zero)
            throw new InvalidOperationException("Period must be positive.");
        long periods = (long)Math.Ceiling((time - KnownStart) / Period);
        return KnownStart + periods * Period;
    }

    /// <summary>
    /// The start whose quiet window (from <see cref="Lead"/> before to <see cref="After"/> after it)
    /// is current or comes next at <paramref name="now"/>.
    /// </summary>
    public DateTime UpcomingStart(DateTime now) => NextStart(now - After);

    /// <summary>
    /// Should a job stop sending before a line that would keep the machine busy until <paramref name="busyUntil"/>?
    /// True if that runs into the quiet window of the coming start.
    /// </summary>
    public bool ShouldBreak(DateTime now, DateTime busyUntil)
    {
        var start = UpcomingStart(now);
        return busyUntil > start - Lead && now < start + After;
    }

    /// <summary>When to continue after a break that began at <paramref name="breakStarted"/>.</summary>
    public DateTime BreakEnd(DateTime breakStarted) => UpcomingStart(breakStarted) + After;

    /// <summary>The schedule after Grbl restarted on its own at <paramref name="bannerTime"/>: that restart marks a start.</summary>
    public static CompressorSchedule Learn(CompressorSchedule? current, DateTime bannerTime) =>
        new(bannerTime - BootDelay, current?.Period ?? DefaultPeriod);
}
