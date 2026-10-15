namespace WalletApi.Domain;

public enum LoanStatus { Active, Repaid, Defaulted }

public enum InstallmentStatus { Pending, Paid }

public class Loan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid AccountId { get; set; }
    public long Principal { get; set; }
    public decimal AnnualRatePercent { get; set; }
    public int TermMonths { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<LoanInstallment> Installments { get; set; } = [];
}

public class LoanInstallment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanId { get; set; }
    public int Number { get; set; }
    public DateTime DueDate { get; set; }
    public long Amount { get; set; }
    public InstallmentStatus Status { get; set; } = InstallmentStatus.Pending;
    public DateTime? PaidAt { get; set; }
}

public class CreditScoreSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTime Day { get; set; }
    public int Score { get; set; }
}
