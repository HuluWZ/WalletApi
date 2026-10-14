namespace WalletApi.Domain;

public enum Interval { Daily, Weekly, Monthly }

public class ScheduledTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public long Amount { get; set; }
    public Interval Interval { get; set; }
    public DateTime NextRunAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? LastError { get; set; }

    public void Advance() => NextRunAt = Interval switch
    {
        Interval.Daily => NextRunAt.AddDays(1),
        Interval.Weekly => NextRunAt.AddDays(7),
        Interval.Monthly => NextRunAt.AddMonths(1),
        _ => throw new InvalidOperationException($"Unknown interval {Interval}"),
    };
}
