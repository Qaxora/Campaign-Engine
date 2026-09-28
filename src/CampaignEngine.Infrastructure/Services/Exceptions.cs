using CampaignEngine.Core.Conflicts;

namespace CampaignEngine.Infrastructure.Services;

/// <summary>Base for errors the API turns into 4xx responses.</summary>
public abstract class CampaignEngineException(string message) : Exception(message);

public sealed class NotFoundException(string message) : CampaignEngineException(message);

/// <summary>Duplicate code, stale version, invalid state transition, …</summary>
public sealed class ConflictException(string message) : CampaignEngineException(message);

public sealed class ValidationException(IReadOnlyList<string> errors)
    : CampaignEngineException("Validation failed: " + string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Activation refused because of error-level conflicts; retry with <c>force</c> to override.</summary>
public sealed class ActivationBlockedException(IReadOnlyList<CampaignConflict> conflicts)
    : CampaignEngineException("The campaign conflicts with live campaigns. Fix the conflicts or activate with force=true.")
{
    public IReadOnlyList<CampaignConflict> Conflicts { get; } = conflicts;
}
