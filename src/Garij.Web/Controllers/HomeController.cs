using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Enums;
using Garij.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garij.Web.Controllers;

[AllowAnonymous]
public class HomeController : Controller
{
    private readonly ICustomerVehicleService _customerVehicleService;
    private readonly IServiceJobService _serviceJobService;

    public HomeController(
        ICustomerVehicleService customerVehicleService,
        IServiceJobService serviceJobService)
    {
        _customerVehicleService = customerVehicleService;
        _serviceJobService = serviceJobService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? query, string? plateNumber, string? bookingReference)
    {
        var lookupValue = FirstProvided(query, bookingReference, plateNumber);
        var model = new StatusLookupViewModel
        {
            Query = lookupValue,
            WasSearched = !string.IsNullOrWhiteSpace(lookupValue)
        };

        if (string.IsNullOrWhiteSpace(lookupValue))
        {
            return View(model);
        }

        var job = await _serviceJobService.GetServiceJobByBookingReferenceAsync(lookupValue);
        if (job is not null)
        {
            model.MatchedByBookingReference = true;
            model.CurrentJob = job;
            model.Vehicle = await _customerVehicleService.GetVehicleByIdAsync(job.VehicleId);
            model.ServiceHistory = (await _customerVehicleService.GetServiceHistoryByVehicleAsync(job.VehicleId)).ToList();
            return View(model);
        }

        var vehicle = await _customerVehicleService.GetVehicleByLicensePlateAsync(lookupValue);
        if (vehicle is null)
        {
            model.Message = $"No booking or vehicle was found for \"{lookupValue}\".";
            return View(model);
        }

        var history = (await _customerVehicleService.GetServiceHistoryByVehicleAsync(vehicle.Id)).ToList();
        model.Vehicle = vehicle;
        model.ServiceHistory = history;
        model.CurrentJob = SelectCurrentJob(history);

        return View(model);
    }

    [HttpGet]
    [Route("About")]
    [Route("Home/About")]
    public IActionResult About() => View();

    [HttpGet]
    [Route("Services")]
    [Route("Home/Services")]
    public IActionResult Services() => View();

    [HttpGet]
    [Route("Testimonials")]
    [Route("Home/Testimonials")]
    public IActionResult Testimonials() => View();

    [HttpGet]
    [Route("Pricing")]
    [Route("Home/Pricing")]
    public IActionResult Pricing() => View();

    private static string FirstProvided(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static ServiceJobDto? SelectCurrentJob(IReadOnlyList<ServiceHistoryDto> history)
    {
        var current = history.FirstOrDefault(job => job.Status is not JobStatus.Completed and not JobStatus.Cancelled)
            ?? history.FirstOrDefault();

        if (current is null)
        {
            return null;
        }

        return new ServiceJobDto
        {
            Id = current.ServiceJobId,
            VehiclePlateNumber = current.VehiclePlate,
            VehicleDescription = current.VehicleDescription,
            BookingReference = current.BookingReference,
            JobType = current.JobType,
            Status = current.Status,
            CreatedAt = current.CreatedAt,
            CompletedAt = current.CompletedAt
        };
    }
}
