namespace CampaignEngine.Core.Evaluation;

public sealed class EngineOptions
{
    /// <summary>Decimals of the currency; amounts are rounded half away from zero.</summary>
    public int Decimals { get; set; } = 2;

    public PlanSelection Selection { get; set; } = PlanSelection.BestForCustomer;

    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

/// <summary>How the engine chooses between exclusive campaigns and the combination of stackable ones.</summary>
public enum PlanSelection
{
    /// <summary>The plan with the largest total discount wins.</summary>
    BestForCustomer,

    /// <summary>The plan containing the highest-priority campaign wins.</summary>
    HighestPriority,
}
