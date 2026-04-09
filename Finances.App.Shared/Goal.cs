using System.ComponentModel.DataAnnotations;

namespace Finances.App.Shared;

public class Goal
{
    public int Id { get; set; }

    [Required]
    public GoalType Type { get; set; } = GoalType.Revenue;

    [Range(0.01, 10_000_000)]
    public decimal TargetValue { get; set; }

    [Required]
    public GoalPeriod Period { get; set; } = GoalPeriod.Monthly;

    /// <summary>
    /// Optional — when set, the goal is scoped to a specific worker.
    /// </summary>
    public int? WorkerId { get; set; }

    [MaxLength(100)]
    public string? Label { get; set; }

    public bool IsActive { get; set; } = true;
}

public enum GoalType
{
    Revenue,
    ServiceCount,
    ProductSaleCount
}

public enum GoalPeriod
{
    Weekly,
    Monthly
}
