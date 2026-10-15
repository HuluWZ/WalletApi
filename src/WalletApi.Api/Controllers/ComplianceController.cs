using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletApi.Application.Compliance;
using WalletApi.Domain;

namespace WalletApi.Api.Controllers;

/// <summary>Regulator-only view of flagged activity. Thresholds come from the "Compliance" config section.</summary>
[ApiController, Authorize(Roles = nameof(Role.Regulator)), Route("api/compliance")]
public class ComplianceController(ComplianceService compliance) : ControllerBase
{
    [HttpGet("flags")]
    public async Task<IActionResult> Flags([FromQuery] FlagStatus? status) => Ok(await compliance.ListAsync(status));

    [HttpPost("flags/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, FlagReview r)
    {
        if (r.Status == FlagStatus.Open) return BadRequest("Review status must be Dismissed or Escalated");
        var reviewer = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return await compliance.ReviewAsync(id, reviewer, r, DateTime.UtcNow) is { } f ? Ok(f) : NotFound();
    }

    /// <summary>Per-day volume, transaction count and flag count (chart data).</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery] int days = 30) =>
        Ok(await compliance.StatsAsync(Math.Clamp(days, 1, 365), DateTime.UtcNow));

    /// <summary>Account-to-account transfer graph (nodes and weighted edges); flagged accounts/edges highlighted.</summary>
    [HttpGet("network")]
    public async Task<ComplianceService.Network> Network([FromQuery] int days = 30) =>
        await compliance.NetworkAsync(Math.Clamp(days, 1, 365), DateTime.UtcNow);
}
