using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Application.Transfers;

namespace WalletApi.Api.Controllers;

public record TransferRequest(Guid FromAccountId, Guid ToAccountId, long Amount);
public record DepositRequest(long Amount);

[ApiController, Authorize, Route("api/accounts")]
public class AccountsController(IWalletDb db, TransferService transfers) : ControllerBase
{
    Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Mine() =>
        Ok(await db.Accounts.Where(a => a.UserId == UserId).Select(a => new { a.Id, a.Balance }).ToListAsync());

    [HttpPost("{id:guid}/deposit")]
    public async Task<IActionResult> Deposit(Guid id, DepositRequest r)
    {
        var (acc, error) = await transfers.DepositAsync(UserId, id, r.Amount);
        if (error is null) return Ok(new { acc!.Id, acc.Balance });
        return error == "Account not found" ? NotFound() : error == TransferService.ConcurrentError ? Conflict(error) : BadRequest(error);
    }

    [HttpPost("transfers")]
    public async Task<IActionResult> Transfer([FromHeader(Name = "Idempotency-Key")] string? key, TransferRequest r)
    {
        if (string.IsNullOrWhiteSpace(key)) return BadRequest("Idempotency-Key header required");
        var res = await transfers.TransferAsync(UserId, r.FromAccountId, r.ToAccountId, r.Amount, key);
        if (res.Error is not null) return res.Error == TransferService.ConcurrentError ? Conflict(res.Error) : BadRequest(res.Error);
        return res.Replayed ? Ok(res.Transfer) : Created($"api/accounts/transfers/{res.Transfer!.Id}", res.Transfer);
    }
}
