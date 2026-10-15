namespace WalletApi.Domain;

public enum FlagRule { LargeTransaction, DailyTotal, Structuring, RapidPassThrough }

public enum FlagStatus { Open, Dismissed, Escalated }

public class ComplianceFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TransferId { get; set; }
    public Guid AccountId { get; set; }
    public FlagRule Rule { get; set; }
    public string Details { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public FlagStatus Status { get; set; } = FlagStatus.Open;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
}
