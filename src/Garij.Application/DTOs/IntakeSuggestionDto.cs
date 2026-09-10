namespace Garij.Application.DTOs;

public class IntakeSuggestionRequestDto
{
    public string ComplaintText { get; set; } = string.Empty;
}

public class ServiceSuggestionItemDto
{
    public int ServiceCatalogId { get; set; }

    public string ServiceName { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public decimal BasePrice { get; set; }

    public int EstimatedDurationMinutes { get; set; }
}

public class IntakeSuggestionResponseDto
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string Summary { get; set; } = string.Empty;

    public decimal EstimatedTotalCost { get; set; }

    public int EstimatedTotalDurationMinutes { get; set; }

    public List<ServiceSuggestionItemDto> Suggestions { get; set; } = new();

    public List<string> ClarifyingQuestions { get; set; } = new();

    public int DiscardedInvalidCount { get; set; }

    public List<string> Warnings { get; set; } = new();

    public bool IsConfigured { get; set; } = true;
}
