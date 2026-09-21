using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Infrastructure.ExternalServices.Gemini;
using Garij.Infrastructure.ExternalServices.Groq;
using Garij.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Garij.UnitTests;

public class IntakeAssistantTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GarijDbContext _context;

    public IntakeAssistantTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new GarijDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private class FakeLlmClient : ILlmClient
    {
        private readonly string _responseJson;

        public FakeLlmClient(string responseJson)
        {
            _responseJson = responseJson;
        }

        public Task<string> GenerateJsonContentAsync(string systemPrompt, string userPrompt, string jsonResponseSchema, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_responseJson);
        }
    }

    [Fact]
    public async Task SuggestServicesForIntakeAsync_DropsHallucinatedServiceId_AndSurfacesWarning()
    {
        // Arrange: Seed real catalog in database with ID 1
        var catalogItem = new ServiceCatalog
        {
            Id = 1,
            Name = "Brake Pad Replacement",
            Description = "Replace front or rear brake pads and inspect rotors",
            BasePrice = 120.00m,
            EstimatedDurationMinutes = 90
        };
        _context.ServiceCatalogs.Add(catalogItem);
        await _context.SaveChangesAsync();

        // Fixture JSON with 1 valid ID (1) and 1 hallucinated ID (999)
        const string fixtureJson = @"{
  ""summary"": ""Brake system inspection required due to squealing sound."",
  ""suggestions"": [
    {
      ""serviceCatalogId"": 1,
      ""serviceName"": ""Brake Pad Replacement"",
      ""reason"": ""Customer reports high-pitched squeal when braking."",
      ""confidence"": 0.95
    },
    {
      ""serviceCatalogId"": 999,
      ""serviceName"": ""Fictional Jet Engine Overhaul"",
      ""reason"": ""Hallucinated item not present in Garij catalog."",
      ""confidence"": 0.85
    }
  ],
  ""clarifyingQuestions"": [
    ""Does the squeal occur on light braking or heavy braking?"",
    ""Is there any steering wheel pulsation while stopping?""
  ]
}";

        var mockLlm = new FakeLlmClient(fixtureJson);
        var settings = Options.Create(new GeminiSettings
        {
            ApiKey = "test-fake-key",
            Model = "gemini-2.5-flash"
        });

        var service = new IntelligenceService(_context, mockLlm, settings, NullLogger<IntelligenceService>.Instance);

        // Act
        var result = await service.SuggestServicesForIntakeAsync("High pitched squealing sound coming from front wheels when I press the brakes.");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Brake system inspection required due to squealing sound.", result.Summary);

        // Crucial validation assert: Exactly 1 suggestion retained, invalid ID 999 dropped
        Assert.Single(result.Suggestions);
        Assert.Equal(1, result.Suggestions[0].ServiceCatalogId);
        Assert.Equal("Brake Pad Replacement", result.Suggestions[0].ServiceName);
        Assert.Equal(120.00m, result.Suggestions[0].BasePrice);
        Assert.Equal(90, result.Suggestions[0].EstimatedDurationMinutes);

        // Discarded warning verification
        Assert.Equal(1, result.DiscardedInvalidCount);
        Assert.Single(result.Warnings);
        Assert.Contains("1 unverified suggestion", result.Warnings[0]);

        // Clarifying questions verification
        Assert.Equal(2, result.ClarifyingQuestions.Count);

        // Totals
        Assert.Equal(120.00m, result.EstimatedTotalCost);
        Assert.Equal(90, result.EstimatedTotalDurationMinutes);

        // Persistence in AiRequestLogs table
        var log = await _context.AiRequestLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("SmartIntakeAssistant", log.FeatureName);
        Assert.True(log.IsSuccess);
        Assert.Null(log.ErrorMessage);
    }

    [Fact]
    public async Task SuggestServicesForIntakeAsync_WhenApiKeyMissing_DegradesGracefullyWithoutThrowing()
    {
        // Arrange
        var settings = Options.Create(new GeminiSettings
        {
            ApiKey = "", // Missing API key
            Model = "gemini-2.5-flash"
        });

        var mockLlm = new FakeLlmClient("{}");
        var service = new IntelligenceService(_context, mockLlm, settings, NullLogger<IntelligenceService>.Instance);

        // Act
        var result = await service.SuggestServicesForIntakeAsync("Battery died this morning.");

        // Assert
        Assert.False(result.Success);
        Assert.False(result.IsConfigured);
        Assert.Contains("Gemini API key is not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task SuggestServicesForIntakeAsync_WhenGroqApiKeyMissing_DegradesGracefullyWithoutThrowing()
    {
        // Arrange
        var groqSettings = Options.Create(new GroqSettings
        {
            ApiKey = "", // Missing API key
            Model = "openai/gpt-oss-120b"
        });

        var mockLlm = new FakeLlmClient("{}");
        var service = new IntelligenceService(_context, mockLlm, groqSettings, null, NullLogger<IntelligenceService>.Instance);

        // Act
        var result = await service.SuggestServicesForIntakeAsync("Alternator whining noise.");

        // Assert
        Assert.False(result.Success);
        Assert.False(result.IsConfigured);
        Assert.Contains("Groq API key is not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task SuggestServicesForIntakeAsync_WithGroqConfigured_ReturnsGroundedSuggestions()
    {
        // Arrange
        var catalogItem = new ServiceCatalog
        {
            Id = 5,
            Name = "Synthetic Oil & Filter Change",
            Description = "Full synthetic motor oil replacement",
            BasePrice = 85.00m,
            EstimatedDurationMinutes = 45
        };
        _context.ServiceCatalogs.Add(catalogItem);
        await _context.SaveChangesAsync();

        const string fixtureJson = @"{
  ""summary"": ""Routine synthetic oil service recommended."",
  ""suggestions"": [
    {
      ""serviceCatalogId"": 5,
      ""serviceName"": ""Synthetic Oil & Filter Change"",
      ""reason"": ""Oil service interval reached."",
      ""confidence"": 0.98
    }
  ],
  ""clarifyingQuestions"": [
    ""When was your last oil change?""
  ]
}";

        var mockLlm = new FakeLlmClient(fixtureJson);
        var groqSettings = Options.Create(new GroqSettings
        {
            ApiKey = "test-groq-key",
            Model = "openai/gpt-oss-120b"
        });

        var service = new IntelligenceService(_context, mockLlm, groqSettings, null, NullLogger<IntelligenceService>.Instance);

        // Act
        var result = await service.SuggestServicesForIntakeAsync("Engine oil light is flickering.");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Routine synthetic oil service recommended.", result.Summary);
        Assert.Single(result.Suggestions);
        Assert.Equal(5, result.Suggestions[0].ServiceCatalogId);
        Assert.Equal("Synthetic Oil & Filter Change", result.Suggestions[0].ServiceName);
        Assert.Equal(85.00m, result.Suggestions[0].BasePrice);
        Assert.Single(result.ClarifyingQuestions);
    }

    [Fact]
    public async Task GroqClient_LiveApiCheck_ReturnsValidResponse()
    {
        var apiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return; // Skip live network call when key is not set in environment
        }

        var settings = Options.Create(new GroqSettings
        {
            ApiKey = apiKey,
            Model = "openai/gpt-oss-120b"
        });
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new GroqClient(httpClient, settings, NullLogger<GroqClient>.Instance);

        var result = await client.GenerateJsonContentAsync(
            "You are a helpful assistant. Output JSON.",
            "Return JSON with key result equal to ok.",
            "{}");

        Assert.Contains("ok", result);
    }
}
