namespace WalletApi.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long Balance { get; set; } // minor units (cents) to avoid float errors
    public uint Version { get; set; } // optimistic concurrency token (Postgres xmin)
}

public class Transfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IdempotencyKey { get; set; } = "";
    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public long Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
