namespace WalletApi.Domain;

public enum Role { User, Regulator }

public enum TxType { Transfer, Deposit, LoanDisbursement, LoanRepayment }

public static class Ids
{
    /// <summary>The lender's pool account; loans are disbursed from it and repaid into it. It may go negative (it is a ledger, not a customer).</summary>
    public static readonly Guid LenderPool = new("00000000-0000-0000-0000-000000000001");
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; } = Role.User;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public long Balance { get; set; } // minor units (cents) to avoid float errors
    public uint Version { get; set; } // optimistic concurrency token (Postgres xmin)
}

/// <summary>Ledger entry. FromAccountId is null for cash-in deposits.</summary>
public class Transfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IdempotencyKey { get; set; } = "";
    public Guid? FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public long Amount { get; set; }
    public TxType Type { get; set; } = TxType.Transfer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
