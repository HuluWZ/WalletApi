using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletApi.Application.Lending;

namespace WalletApi.Api.Controllers;

public record ApplyLoanRequest(Guid AccountId, long Amount, int TermMonths);

[ApiController, Authorize, Route("api/loans")]
public class LoansController(LoanService loans) : ControllerBase
{
    Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>What the user qualifies for right now, based on their credit score.</summary>
    [HttpGet("offer")]
    public async Task<LoanOffer> Offer() => await loans.OfferAsync(UserId, DateTime.UtcNow);

    [HttpPost]
    public async Task<IActionResult> Apply(ApplyLoanRequest r)
    {
        var (loan, error) = await loans.ApplyAsync(UserId, r.AccountId, r.Amount, r.TermMonths, DateTime.UtcNow);
        return error is null ? Created($"api/loans/{loan!.Id}", loan) : BadRequest(error);
    }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await loans.ListAsync(UserId));

    /// <summary>Pay the next pending installment now (autopay also collects due installments).</summary>
    [HttpPost("{id:guid}/repay")]
    public async Task<IActionResult> Repay(Guid id)
    {
        var (paid, error) = await loans.RepayNextAsync(UserId, id, DateTime.UtcNow);
        return paid ? Ok() : error == "Loan not found" ? NotFound() : BadRequest(error);
    }
}
