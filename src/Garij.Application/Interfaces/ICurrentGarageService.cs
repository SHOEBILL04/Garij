namespace Garij.Application.Interfaces;

/// <summary>
/// Provides the current active garage identifier for the request context.
/// </summary>
public interface ICurrentGarageService
{
    Task<string> GetCurrentGarageIdAsync();
}
