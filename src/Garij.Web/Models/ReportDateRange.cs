using System.Globalization;

namespace Garij.Web.Models;

/// <summary>
/// The date range a report actually runs with. Every date-driven report resolves its range through
/// here, so a start date later than the end date is handled one way everywhere: the two dates are
/// swapped and the page says so. Before this, two reports swapped silently and one silently returned
/// nothing, depending only on which screen the user was on.
/// </summary>
public sealed class ReportDateRange
{
    /// <summary>ViewData key the report actions store the range under; read by _DateRangeFilterPartial.</summary>
    public const string ViewDataKey = nameof(ReportDateRange);

    /// <summary>
    /// Month spelled out so the message cannot be misread as day/month or month/day, which is exactly
    /// the kind of confusion a message about the order of two dates must not add to.
    /// </summary>
    private const string DisplayFormat = "d MMM yyyy";

    private ReportDateRange(DateTime? start, DateTime? end, bool wasReversed)
    {
        Start = start;
        End = end;
        WasReversed = wasReversed;
    }

    /// <summary>First day the report covers, or null for no lower bound.</summary>
    public DateTime? Start { get; }

    /// <summary>Last day the report covers, or null for no upper bound.</summary>
    public DateTime? End { get; }

    /// <summary>True when the start date came after the end date and the two were swapped.</summary>
    public bool WasReversed { get; }

    /// <summary>The message shown on the report when the range was swapped; null otherwise.</summary>
    public string? CorrectionMessage => WasReversed
        ? $"The start date ({Format(End)}) is after the end date ({Format(Start)}), so the two dates were swapped and this report was run from {Format(Start)} to {Format(End)}."
        : null;

    /// <summary>
    /// Resolves the range a report runs with. Pass the dates after the report's own defaults are applied,
    /// so a supplied date that lands on the wrong side of a default is corrected too. Only a range with
    /// both ends can be reversed; an open end means "from the beginning" or "with no end".
    /// </summary>
    public static ReportDateRange Resolve(DateTime? start, DateTime? end) =>
        start.HasValue && end.HasValue && start.Value > end.Value
            ? new ReportDateRange(end, start, wasReversed: true)
            : new ReportDateRange(start, end, wasReversed: false);

    private static string Format(DateTime? date) =>
        date?.ToString(DisplayFormat, CultureInfo.InvariantCulture) ?? string.Empty;
}
