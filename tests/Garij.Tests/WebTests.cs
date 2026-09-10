using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Garij.Web.Controllers;
using Garij.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Garij.Tests;

public class WebTests
{
    [Fact]
    public async Task DashboardController_Index_ReturnsView()
    {
        var controller = new DashboardController();

        var result = await controller.Index();

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.IsType<FrontDeskDashboardViewModel>(viewResult.Model);
    }

    [Fact]
    public async Task StatusLookup_Result_WithBookingReference_ReturnsCurrentJobAndHistory()
    {
        var services = new FakeLookupServices();
        var controller = new HomeController(services, services);

        var result = await controller.Index("grj-2026-0007", null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StatusLookupViewModel>(viewResult.Model);
        Assert.True(model.MatchedByBookingReference);
        Assert.Equal("GRJ-2026-0007", model.CurrentJob?.BookingReference);
        Assert.Equal("DHA-2026", model.Vehicle?.LicensePlateNumber);
        Assert.Equal(2, model.ServiceHistory.Count);
    }

    [Fact]
    public async Task StatusLookup_Result_WithPlateNumber_SelectsNewestActiveJob()
    {
        var services = new FakeLookupServices();
        var controller = new HomeController(services, services);

        var result = await controller.Index("DHA-2026", null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StatusLookupViewModel>(viewResult.Model);
        Assert.False(model.MatchedByBookingReference);
        Assert.Equal(JobStatus.InProgress, model.CurrentJob?.Status);
        Assert.Equal("GRJ-2026-0007", model.CurrentJob?.BookingReference);
    }

    [Fact]
    public async Task StatusLookup_Result_WithUnknownLookup_ReturnsFriendlyMessage()
    {
        var services = new FakeLookupServices();
        var controller = new HomeController(services, services);

        var result = await controller.Index("UNKNOWN", null, null);

        var viewResult = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StatusLookupViewModel>(viewResult.Model);
        Assert.Null(model.CurrentJob);
        Assert.Contains("No booking or vehicle", model.Message);
    }

    private sealed class FakeLookupServices : ICustomerVehicleService, IServiceJobService
    {
        private readonly VehicleDto _vehicle = new()
        {
            Id = 10,
            CustomerId = 5,
            CustomerName = "Nadia Rahman",
            LicensePlateNumber = "DHA-2026",
            Make = "Toyota",
            Model = "Corolla",
            Year = 2022,
            Vin = "VIN123",
            Color = "White"
        };

        private readonly ServiceJobDto _currentJob = new()
        {
            Id = 7,
            CustomerId = 5,
            CustomerName = "Nadia Rahman",
            VehicleId = 10,
            VehiclePlateNumber = "DHA-2026",
            VehicleDescription = "2022 Toyota Corolla",
            BookingReference = "GRJ-2026-0007",
            JobType = JobType.Repair,
            Status = JobStatus.InProgress,
            CreatedAt = new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc)
        };

        public Task<VehicleDto?> GetVehicleByIdAsync(int id) =>
            Task.FromResult(id == _vehicle.Id ? _vehicle : null);

        public Task<VehicleDto?> GetVehicleByLicensePlateAsync(string licensePlateNumber) =>
            Task.FromResult(string.Equals(licensePlateNumber, _vehicle.LicensePlateNumber, StringComparison.OrdinalIgnoreCase)
                ? _vehicle
                : null);

        public Task<IEnumerable<ServiceHistoryDto>> GetServiceHistoryByVehicleAsync(int vehicleId)
        {
            IEnumerable<ServiceHistoryDto> history = vehicleId == _vehicle.Id
                ? new[]
                {
                    new ServiceHistoryDto
                    {
                        ServiceJobId = _currentJob.Id,
                        BookingReference = _currentJob.BookingReference,
                        JobType = _currentJob.JobType,
                        Status = _currentJob.Status,
                        CreatedAt = _currentJob.CreatedAt,
                        VehiclePlate = _currentJob.VehiclePlateNumber,
                        VehicleDescription = _currentJob.VehicleDescription
                    },
                    new ServiceHistoryDto
                    {
                        ServiceJobId = 3,
                        BookingReference = "GRJ-2026-0003",
                        JobType = JobType.RoutineService,
                        Status = JobStatus.Completed,
                        CreatedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
                        CompletedAt = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc),
                        VehiclePlate = _vehicle.LicensePlateNumber,
                        VehicleDescription = "2022 Toyota Corolla"
                    }
                }
                : Array.Empty<ServiceHistoryDto>();

            return Task.FromResult(history);
        }

        public Task<ServiceJobDto?> GetServiceJobByBookingReferenceAsync(string bookingReference) =>
            Task.FromResult(string.Equals(bookingReference, _currentJob.BookingReference, StringComparison.OrdinalIgnoreCase)
                ? _currentJob
                : null);

        public Task<IEnumerable<CustomerDto>> GetAllCustomersAsync() => throw new NotImplementedException();
        public Task<CustomerDto?> GetCustomerByIdAsync(int id) => throw new NotImplementedException();
        public Task<CustomerDto> CreateCustomerAsync(CustomerDto customer) => throw new NotImplementedException();
        public Task<CustomerDto> UpdateCustomerAsync(CustomerDto customer) => throw new NotImplementedException();
        public Task DeleteCustomerAsync(int id) => throw new NotImplementedException();
        public Task<IEnumerable<VehicleDto>> GetVehiclesByCustomerAsync(int customerId) => throw new NotImplementedException();
        public Task<VehicleDto> AddVehicleAsync(VehicleDto vehicle) => throw new NotImplementedException();
        public Task<VehicleDto> UpdateVehicleAsync(VehicleDto vehicle) => throw new NotImplementedException();
        public Task DeleteVehicleAsync(int id) => throw new NotImplementedException();
        public Task<IEnumerable<ServiceJobDto>> GetAllServiceJobsAsync() => throw new NotImplementedException();
        public Task<IEnumerable<ServiceJobDto>> GetServiceJobsByStatusAsync(JobStatus status) => throw new NotImplementedException();
        public Task<IEnumerable<ServiceJobDto>> GetFilteredServiceJobsAsync(JobStatus? status = null, int? mechanicId = null, string? sortBy = null, string? searchTerm = null) => throw new NotImplementedException();
        public Task<ServiceJobDto?> GetServiceJobByIdAsync(int id) => throw new NotImplementedException();
        public Task<ServiceJobDto> CreateServiceJobAsync(ServiceJobDto serviceJob) => throw new NotImplementedException();
        public Task<ServiceJobDto> UpdateServiceJobAsync(ServiceJobDto serviceJob) => throw new NotImplementedException();
        public Task<ServiceJobDto> UpdateServiceJobStatusAsync(int id, JobStatus status) => throw new NotImplementedException();
        public Task DeleteServiceJobAsync(int id) => throw new NotImplementedException();
        public Task<MechanicAssignmentDto> AssignMechanicAsync(int serviceJobId, int userId, RoleInJob roleInJob) => throw new NotImplementedException();

        public Task<MechanicAssignmentDto> UpdateMechanicAssignmentRoleAsync(int assignmentId, RoleInJob roleInJob) => throw new NotImplementedException();
        public Task RemoveMechanicAssignmentAsync(int assignmentId) => throw new NotImplementedException();
        public Task<IEnumerable<MechanicAssignmentDto>> GetAssignmentsByServiceJobAsync(int serviceJobId) => throw new NotImplementedException();
        public Task<IEnumerable<ServiceJobDto>> GetJobsByMechanicAsync(int mechanicUserId) => throw new NotImplementedException();
        public Task<ServiceJobDto> SaveDiagnosticNotesAsync(int serviceJobId, string notes) => throw new NotImplementedException();
    }
}
