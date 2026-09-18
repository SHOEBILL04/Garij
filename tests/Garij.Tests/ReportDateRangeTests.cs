using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Web.Controllers;
using Garij.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Garij.Tests;

/// <summary>
/// Covers defect D-11 (Report 05): a start date later than the end date was silently swapped on the
/// Revenue and Part Consumption reports, and passed through unswapped on Mechanic Workload, which then
/// returned all-zero rows. All three now resolve their range through ReportDateRange.
///
/// These tests pin the shared rule itself and, through the controller with a recording reporting
/// service, the exact dates each report action runs its query with.
///
/// Covers TC-RPT-05 and TC-RPT-06.
/// </summary>
public class ReportDateRangeTests
{
    private static readonly DateTime March1 = new(2026, 3, 1);
    private static readonly DateTime March15 = new(2026, 3, 15);

    // =================================================================================
    // The shared rule.
    // =================================================================================

    [Fact]
    public void Resolve_SwapsAReversedRange_AndSaysWhatItDid()
    {
        var range = ReportDateRange.Resolve(March15, March1);

        Assert.True(range.WasReversed);
        Assert.Equal(March1, range.Start);
        Assert.Equal(March15, range.End);
        Assert.Equal(
            "The start date (15 Mar 2026) is after the end date (1 Mar 2026), so the two dates were swapped and this report was run from 1 Mar 2026 to 15 Mar 2026.",
            range.CorrectionMessage);
    }

    [Fact]
    public void Resolve_LeavesAnInOrderRangeAlone_WithNoMessage()
    {
        var range = ReportDateRange.Resolve(March1, March15);

        Assert.False(range.WasReversed);
        Assert.Equal(March1, range.Start);
        Assert.Equal(March15, range.End);
        Assert.Null(range.CorrectionMessage);
    }

    [Fact]
    public void Resolve_TreatsASingleDayRangeAsInOrder()
    {
        var range = ReportDateRange.Resolve(March1, March1);

        Assert.False(range.WasReversed);
        Assert.Null(range.CorrectionMessage);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Resolve_NeverReportsAnOpenEndedRangeAsReversed(bool hasStart, bool hasEnd)
    {
        var range = ReportDateRange.Resolve(hasStart ? March15 : null, hasEnd ? March1 : null);

        Assert.False(range.WasReversed);
        Assert.Equal(hasStart ? March15 : null, range.Start);
        Assert.Equal(hasEnd ? March1 : null, range.End);
    }

    // =================================================================================
    // Each date-driven report action runs with the corrected range and passes it to the view.
    // =================================================================================

    [Fact]
    public async Task Revenue_WithAReversedRange_QueriesTheCorrectedRange()
    {
        var reporting = new RecordingReportingService();
        var controller = new ReportController(reporting);

        var result = await controller.Revenue(March15, March1);

        Assert.Equal((March1, March15), reporting.LastRange);
        AssertViewCarriesReversedRange(controller, result);
    }

    [Fact]
    public async Task PartConsumption_WithAReversedRange_QueriesTheCorrectedRange()
    {
        var reporting = new RecordingReportingService();
        var controller = new ReportController(reporting);

        var result = await controller.PartConsumption(March15, March1);

        Assert.Equal((March1, March15), reporting.LastRange);
        AssertViewCarriesReversedRange(controller, result);
    }

    [Fact]
    public async Task MechanicWorkload_WithAReversedRange_QueriesTheCorrectedRange()
    {
        // TC-RPT-06: previously passed through unswapped, so the query matched nothing.
        var reporting = new RecordingReportingService();
        var controller = new ReportController(reporting);

        var result = await controller.MechanicWorkload(March15, March1);

        Assert.Equal((March1, March15), reporting.LastRange);
        AssertViewCarriesReversedRange(controller, result);
    }

    [Fact]
    public async Task EveryDateDrivenReport_WithAnInOrderRange_QueriesItAsGiven_AndCarriesNoCorrection()
    {
        foreach (var run in new Func<ReportController, Task<IActionResult>>[]
                 {
                     c => c.Revenue(March1, March15),
                     c => c.PartConsumption(March1, March15),
                     c => c.MechanicWorkload(March1, March15)
                 })
        {
            var reporting = new RecordingReportingService();
            var controller = new ReportController(reporting);

            await run(controller);

            Assert.Equal((March1, March15), reporting.LastRange);
            var range = Assert.IsType<ReportDateRange>(controller.ViewData[ReportDateRange.ViewDataKey]);
            Assert.False(range.WasReversed);
        }
    }

    [Fact]
    public async Task MechanicWorkload_WithNoDates_KeepsTheRangeOpen()
    {
        var reporting = new RecordingReportingService();
        var controller = new ReportController(reporting);

        await controller.MechanicWorkload(null, null);

        Assert.Equal((null, null), reporting.LastRange);
    }

    [Fact]
    public async Task Revenue_WithADateThatFailedToParse_FallsBackToTheDefaultRange()
    {
        // TC-RPT-07: "?start=banana" reaches the action as null, and the default range applies.
        var reporting = new RecordingReportingService();
        var controller = new ReportController(reporting);

        await controller.Revenue(null, null);

        Assert.Equal((new DateTime(DateTime.UtcNow.Year, 1, 1), DateTime.UtcNow.Date), reporting.LastRange);
        Assert.False(Assert.IsType<ReportDateRange>(controller.ViewData[ReportDateRange.ViewDataKey]).WasReversed);
    }

    private static void AssertViewCarriesReversedRange(ReportController controller, IActionResult result)
    {
        Assert.IsType<ViewResult>(result);
        var range = Assert.IsType<ReportDateRange>(controller.ViewData[ReportDateRange.ViewDataKey]);
        Assert.True(range.WasReversed);
        Assert.NotNull(range.CorrectionMessage);
    }

    /// <summary>Records the range each report was queried with.</summary>
    private sealed class RecordingReportingService : IReportingService
    {
        public (DateTime? Start, DateTime? End) LastRange { get; private set; }

        public Task<RevenueReportDto> GetRevenueReportAsync(DateTime periodStart, DateTime periodEnd)
        {
            LastRange = (periodStart, periodEnd);
            return Task.FromResult(new RevenueReportDto { PeriodStart = periodStart, PeriodEnd = periodEnd });
        }

        public Task<IEnumerable<PartsConsumptionReportDto>> GetPartsConsumptionReportAsync(DateTime periodStart, DateTime periodEnd)
        {
            LastRange = (periodStart, periodEnd);
            return Task.FromResult(Enumerable.Empty<PartsConsumptionReportDto>());
        }

        public Task<IEnumerable<MechanicWorkloadDto>> GetMechanicWorkloadReportAsync(DateTime? periodStart = null, DateTime? periodEnd = null)
        {
            LastRange = (periodStart, periodEnd);
            return Task.FromResult(Enumerable.Empty<MechanicWorkloadDto>());
        }

        public Task<IEnumerable<LowStockReportDto>> GetLowStockReportAsync() => throw new NotImplementedException();

        public Task<IEnumerable<ServiceJobDto>> GetCompletedJobsReportAsync(DateTime periodStart, DateTime periodEnd) => throw new NotImplementedException();
    }
}
