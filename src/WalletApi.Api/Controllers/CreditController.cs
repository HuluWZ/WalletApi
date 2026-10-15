using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletApi.Application.Credit;

namespace WalletApi.Api.Controllers;

[ApiController, Authorize, Route("api/credit")]
public class CreditController(CreditScoreService scores) : ControllerBase
{
    Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Current score (300-850) with a per-factor explanation. Also records today's snapshot for the trend chart.</summary>
    [HttpGet("score")]
    public async Task<CreditScore> Score()
    {
        var s = await scores.ComputeAsync(UserId, DateTime.UtcNow);
        await scores.RecordSnapshotAsync(UserId, s.Score, DateTime.UtcNow);
        return s;
    }

    /// <summary>Daily score snapshots for charting.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History() =>
        Ok((await scores.HistoryAsync(UserId)).Select(h => new { h.Day, h.Score }));
}
