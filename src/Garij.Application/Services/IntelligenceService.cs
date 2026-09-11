using System.Diagnostics;
using System.Text.Json;
using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.ExternalServices.Gemini;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Garij.Application.Services;

public class IntelligenceService : IIntelligenceService
{
    private readonly GarijDbContext? _context;
    private readonly ILlmClient? _llmClient;
    private readonly GeminiSettings _settings;
    private readonly ILogger<IntelligenceService> _logger;
    private readonly IVehicleRepository _vehicleRepository;
    private readonly IServiceJobRepository _serviceJobRepository;
    private readonly ICurrentGarageService? _currentGarageService;

    public IntelligenceService(
        GarijDbContext context,
        ILlmClient? llmClient = null,
        IOptions<GeminiSettings>? settings = null,
        ILogger<IntelligenceService>? logger = null,
        ICurrentGarageService? currentGarageService = null)
    {
        _context = context;
        _llmClient = llmClient;
        _settings = settings?.Value ?? new GeminiSettings();
        _logger = logger ?? NullLogger<IntelligenceService>.Instance;
        _vehicleRepository = new VehicleRepository(context);
        _serviceJobRepository = new ServiceJobRepository(context);
        _currentGarageService = currentGarageService;
    }

    private async Task<string> ResolveGarageIdAsync(string? explicitGarageId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitGarageId))
        {
            return explicitGarageId;
        }

        if (_currentGarageService != null)
        {
            var id = await _currentGarageService.GetCurrentGarageIdAsync();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return "default-garij-master";
    }

    public async Task<IEnumerable<VehicleDto>> PredictMaintenanceDueAsync()
    {
        var predictions = await FlagVehiclesDueForServiceAsync();
        return predictions.Cast<VehicleDto>();
    }

    public async Task<IEnumerable<VehicleMaintenancePredictionDto>> FlagVehiclesDueForServiceAsync()
    {
        var garageId = await ResolveGarageIdAsync();
        var allVehicles = await _vehicleRepository.GetAllWithCustomersAsync();
        var vehicles = allVehicles.Where(v => (v.GarageId ?? "default-garij-master") == garageId).ToList();

        var allJobsRaw = await _serviceJobRepository.GetAllWithDetailsAsync();
        var allJobs = allJobsRaw.Where(j => (j.GarageId ?? "default-garij-master") == garageId).ToList();

        var jobLookup = allJobs.GroupBy(j => j.VehicleId)
                               .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<VehicleMaintenancePredictionDto>();
        var now = DateTime.UtcNow;

        foreach (var vehicle in vehicles)
        {
            jobLookup.TryGetValue(vehicle.Id, out var vehicleJobs);
            vehicleJobs ??= new List<Domain.Entities.ServiceJob>();

            // Check if vehicle currently has an active, open job in progress
            bool hasActiveJob = vehicleJobs.Any(j => j.Status != JobStatus.Completed && j.Status != JobStatus.Cancelled);

            // If the vehicle is currently in the workshop for an active job, skip from service-due queue
            if (hasActiveJob)
            {
                continue;
            }

            // Completed jobs sorted chronologically descending
            var completedJobs = vehicleJobs
                .Where(j => j.Status == JobStatus.Completed)
                .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
                .ToList();

            DateTime? lastServiceDate = null;
            int? daysSinceLastService = null;
            DateTime predictedDueDate;
            int? averageIntervalDays = null;
            string recommendedService;
            string predictionReason;
            string urgencyLevel;
            int daysOverdue = 0;
            int estimatedDaysUntilDue = 0;

            if (completedJobs.Count == 0)
            {
                // No completed service recorded yet: vehicle needs initial checkup
                lastServiceDate = null;
                daysSinceLastService = null;
                predictedDueDate = now.Date;
                daysOverdue = 14;
                estimatedDaysUntilDue = -daysOverdue;
                urgencyLevel = "Overdue";
                recommendedService = "Initial Multi-Point Inspection & Routine Service";
                predictionReason = "No prior service history recorded. Initial baseline maintenance and safety inspection required.";
            }
            else
            {
                var latestJob = completedJobs.First();
                lastServiceDate = latestJob.CompletedAt ?? latestJob.CreatedAt;
                daysSinceLastService = Math.Max(0, (int)(now.Date - lastServiceDate.Value.Date).TotalDays);

                if (completedJobs.Count >= 2)
                {
                    // Compute average interval between historical services
                    var intervals = new List<double>();
                    for (int i = 0; i < completedJobs.Count - 1; i++)
                    {
                        var current = completedJobs[i].CompletedAt ?? completedJobs[i].CreatedAt;
                        var previous = completedJobs[i + 1].CompletedAt ?? completedJobs[i + 1].CreatedAt;
                        var diffDays = (current - previous).TotalDays;
                        if (diffDays > 0)
                        {
                            intervals.Add(diffDays);
                        }
                    }

                    if (intervals.Count > 0)
                    {
                        var avg = (int)Math.Round(intervals.Average());
                        averageIntervalDays = Math.Clamp(avg, 30, 180);
                    }
                    else
                    {
                        averageIntervalDays = 90;
                    }
                }
                else
                {
                    // Single completed service: standard 90-day maintenance interval
                    averageIntervalDays = 90;
                }

                predictedDueDate = lastServiceDate.Value.Date.AddDays(averageIntervalDays.Value);
                estimatedDaysUntilDue = (int)(predictedDueDate.Date - now.Date).TotalDays;

                if (estimatedDaysUntilDue < 0)
                {
                    urgencyLevel = "Overdue";
                    daysOverdue = Math.Abs(estimatedDaysUntilDue);
                    predictionReason = $"Service is overdue by {daysOverdue} day{(daysOverdue == 1 ? "" : "s")} based on vehicle's {averageIntervalDays}-day service interval.";
                }
                else if (estimatedDaysUntilDue <= 14)
                {
                    urgencyLevel = "Due Soon";
                    daysOverdue = 0;
                    predictionReason = $"Service is due in {estimatedDaysUntilDue} day{(estimatedDaysUntilDue == 1 ? "" : "s")} based on regular {averageIntervalDays}-day maintenance cadence.";
                }
                else
                {
                    urgencyLevel = "Upcoming";
                    daysOverdue = 0;
                    predictionReason = $"Next routine maintenance forecasted in {estimatedDaysUntilDue} days.";
                }

                // Determine recommended service based on history
                if (daysSinceLastService > 180)
                {
                    recommendedService = "Major Periodic Service & Multi-Point Inspection";
                }
                else if (latestJob.JobType == JobType.RoutineService)
                {
                    recommendedService = "Routine Service, Fluid Top-Up & Oil Change";
                }
                else if (latestJob.JobType == JobType.Repair)
                {
                    recommendedService = "Post-Repair Safety Follow-up & Routine Service";
                }
                else
                {
                    recommendedService = "Scheduled Diagnostic & Preventative Maintenance";
                }
            }

            var dto = new VehicleMaintenancePredictionDto
            {
                Id = vehicle.Id,
                CustomerId = vehicle.CustomerId,
                CustomerName = vehicle.Customer?.FullName ?? "Unknown",
                CustomerPhoneNumber = vehicle.Customer?.PhoneNumber ?? string.Empty,
                CustomerEmail = vehicle.Customer?.Email ?? string.Empty,
                LicensePlateNumber = vehicle.LicensePlateNumber,
                Make = vehicle.Make,
                Model = vehicle.Model,
                Year = vehicle.Year,
                Vin = vehicle.Vin,
                Color = vehicle.Color,
                LastServiceDate = lastServiceDate,
                DaysSinceLastService = daysSinceLastService,
                PredictedDueDate = predictedDueDate,
                DaysOverdue = daysOverdue,
                EstimatedDaysUntilDue = estimatedDaysUntilDue,
                AverageIntervalDays = averageIntervalDays,
                TotalServicesCompleted = completedJobs.Count,
                UrgencyLevel = urgencyLevel,
                RecommendedService = recommendedService,
                PredictionReason = predictionReason,
                HasActiveJob = hasActiveJob,
                LastJobType = completedJobs.FirstOrDefault()?.JobType.ToString()
            };

            result.Add(dto);
        }

        // Return vehicles that require attention (Overdue or Due Soon), ordered by urgency
        return result
            .Where(r => r.UrgencyLevel == "Overdue" || r.UrgencyLevel == "Due Soon")
            .OrderBy(r => r.UrgencyLevel == "Overdue" ? 0 : 1)
            .ThenBy(r => r.EstimatedDaysUntilDue)
            .ToList();
    }

    public async Task<IntakeSuggestionResponseDto> SuggestServicesForIntakeAsync(
        string complaintText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(complaintText))
        {
            return new IntakeSuggestionResponseDto
            {
                Success = false,
                ErrorMessage = "Please provide customer complaint or vehicle symptoms."
            };
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            return new IntakeSuggestionResponseDto
            {
                Success = false,
                IsConfigured = false,
                ErrorMessage = "Gemini API key is not configured. Suggestions are currently disabled."
            };
        }

        if (_context == null || _llmClient == null)
        {
            return new IntakeSuggestionResponseDto
            {
                Success = false,
                ErrorMessage = "Intelligence service context or LLM client not initialized."
            };
        }

        var catalogList = await _context.ServiceCatalogs.AsNoTracking().ToListAsync(cancellationToken);
        if (catalogList.Count == 0)
        {
            return new IntakeSuggestionResponseDto
            {
                Success = false,
                ErrorMessage = "No service catalog entries found in the system for grounding."
            };
        }

        var catalogJson = JsonSerializer.Serialize(catalogList.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            description = c.Description,
            basePrice = c.BasePrice,
            estimatedDurationMinutes = c.EstimatedDurationMinutes
        }));

        var systemPrompt = $@"You are the expert automotive service intake intelligence assistant for Garij Workshop.
Your role is to analyze vehicle symptoms and customer complaints, and recommend relevant service items strictly from the workshop's catalog.

GROUNDING REQUIREMENT (MANDATORY):
You may ONLY suggest services from the following verified Service Catalog JSON array:
{catalogJson}

STRICT CONSTRAINTS:
1. Every item in 'suggestions' MUST contain a valid 'serviceCatalogId' matching one of the 'id' fields from the catalog above. Do NOT invent IDs or services.
2. If the customer complaint does not match any catalog item, leave 'suggestions' empty and explain why in 'summary'.
3. Include clear clinical diagnostic 'reason' for each recommended service and a 'confidence' score between 0.0 and 1.0.
4. Formulate 1 to 3 targeted 'clarifyingQuestions' that front-desk staff should ask the customer to help mechanics diagnose the fault accurately.";

        var userPrompt = $"Customer Complaint & Vehicle Symptoms:\n{complaintText}";

        const string responseSchema = @"{
  ""type"": ""OBJECT"",
  ""properties"": {
    ""summary"": { ""type"": ""STRING"" },
    ""suggestions"": {
      ""type"": ""ARRAY"",
      ""items"": {
        ""type"": ""OBJECT"",
        ""properties"": {
          ""serviceCatalogId"": { ""type"": ""INTEGER"" },
          ""serviceName"": { ""type"": ""STRING"" },
          ""reason"": { ""type"": ""STRING"" },
          ""confidence"": { ""type"": ""NUMBER"" }
        },
        ""required"": [""serviceCatalogId"", ""serviceName"", ""reason"", ""confidence""]
      }
    },
    ""clarifyingQuestions"": {
      ""type"": ""ARRAY"",
      ""items"": { ""type"": ""STRING"" }
    }
  },
  ""required"": [""summary"", ""suggestions"", ""clarifyingQuestions""]
}";

        var stopwatch = Stopwatch.StartNew();
        string rawResponse = string.Empty;

        try
        {
            rawResponse = await _llmClient.GenerateJsonContentAsync(systemPrompt, userPrompt, responseSchema, cancellationToken);
            stopwatch.Stop();

            using var doc = JsonDocument.Parse(rawResponse);
            var root = doc.RootElement;

            var summary = root.TryGetProperty("summary", out var sEl) ? sEl.GetString() ?? string.Empty : string.Empty;

            var rawSuggestions = new List<(int CatalogId, string Name, string Reason, double Confidence)>();
            if (root.TryGetProperty("suggestions", out var sugArr) && sugArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in sugArr.EnumerateArray())
                {
                    var id = item.TryGetProperty("serviceCatalogId", out var idEl) ? idEl.GetInt32() : 0;
                    var name = item.TryGetProperty("serviceName", out var nEl) ? nEl.GetString() ?? string.Empty : string.Empty;
                    var reason = item.TryGetProperty("reason", out var rEl) ? rEl.GetString() ?? string.Empty : string.Empty;
                    var conf = item.TryGetProperty("confidence", out var cEl) ? cEl.GetDouble() : 0.5;

                    rawSuggestions.Add((id, name, reason, conf));
                }
            }

            var questions = new List<string>();
            if (root.TryGetProperty("clarifyingQuestions", out var qArr) && qArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in qArr.EnumerateArray())
                {
                    var str = q.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        questions.Add(str);
                    }
                }
            }

            // SAFETY & GROUNDING VALIDATION: Validate returned IDs against actual database catalog
            var catalogById = catalogList.ToDictionary(c => c.Id);
            var validatedList = new List<ServiceSuggestionItemDto>();
            int discardedCount = 0;

            foreach (var raw in rawSuggestions)
            {
                if (catalogById.TryGetValue(raw.CatalogId, out var catalogItem))
                {
                    validatedList.Add(new ServiceSuggestionItemDto
                    {
                        ServiceCatalogId = catalogItem.Id,
                        ServiceName = catalogItem.Name,
                        Reason = raw.Reason,
                        Confidence = Math.Clamp(raw.Confidence, 0.0, 1.0),
                        BasePrice = catalogItem.BasePrice,
                        EstimatedDurationMinutes = catalogItem.EstimatedDurationMinutes
                    });
                }
                else
                {
                    discardedCount++;
                    _logger.LogWarning("Discarded ungrounded/hallucinated ServiceCatalogId {Id} from LLM response.", raw.CatalogId);
                }
            }

            var warnings = new List<string>();
            if (discardedCount > 0)
            {
                warnings.Add($"{discardedCount} unverified suggestion(s) were discarded because they did not match the current Service Catalog.");
            }

            await LogAiRequestAsync("SmartIntakeAssistant", userPrompt, rawResponse, true, null, stopwatch.ElapsedMilliseconds);

            return new IntakeSuggestionResponseDto
            {
                Success = true,
                Summary = summary,
                Suggestions = validatedList,
                ClarifyingQuestions = questions,
                DiscardedInvalidCount = discardedCount,
                Warnings = warnings,
                EstimatedTotalCost = validatedList.Sum(v => v.BasePrice),
                EstimatedTotalDurationMinutes = validatedList.Sum(v => v.EstimatedDurationMinutes),
                IsConfigured = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to generate AI intake suggestions.");

            await LogAiRequestAsync("SmartIntakeAssistant", userPrompt, rawResponse, false, ex.Message, stopwatch.ElapsedMilliseconds);

            return new IntakeSuggestionResponseDto
            {
                Success = false,
                ErrorMessage = $"Smart Intake error: {ex.Message}",
                IsConfigured = !string.IsNullOrWhiteSpace(_settings.ApiKey)
            };
        }
    }

    private async Task LogAiRequestAsync(
        string featureName,
        string promptText,
        string responseText,
        bool success,
        string? error,
        long responseTimeMs)
    {
        if (_context == null)
        {
            return;
        }

        try
        {
            var log = new AiRequestLog
            {
                FeatureName = featureName.Length > 100 ? featureName.Substring(0, 100) : featureName,
                PromptText = promptText,
                ResponseText = responseText,
                IsSuccess = success,
                ErrorMessage = error != null && error.Length > 500 ? error.Substring(0, 500) : error,
                RequestedAt = DateTime.UtcNow,
                ResponseTimeMs = responseTimeMs
            };

            _context.AiRequestLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception logEx)
        {
            _logger.LogError(logEx, "Failed to persist AiRequestLog.");
        }
    }

    public Task<TimeSpan> EstimateJobDurationAsync(int serviceJobId) => throw new NotImplementedException();

    public Task<IEnumerable<PartDto>> PredictPartsShortageAsync() => throw new NotImplementedException();

    public Task<IEnumerable<ServiceCatalogDto>> SuggestServicesForVehicleAsync(int vehicleId) => throw new NotImplementedException();
}
