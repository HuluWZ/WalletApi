using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WalletApi.Application.Scheduling;
using WalletApi.Domain;

namespace WalletApi.Api.Controllers;

public record CreateScheduleRequest(Guid FromAccountId, Guid ToAccountId, long Amount, Interval Interval, DateTime FirstRunAt);

[ApiController, Authorize, Route("api/scheduled-transfers")]
public class ScheduledTransfersController(ScheduledTransferService schedules) : ControllerBase
{
    Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<IActionResult> Create(CreateScheduleRequest r)
    {
        var (item, error) = await schedules.CreateAsync(UserId, r.FromAccountId, r.ToAccountId, r.Amount, r.Interval, r.FirstRunAt);
        return error is null ? Created($"api/scheduled-transfers/{item!.Id}", item) : BadRequest(error);
    }

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await schedules.ListAsync(UserId));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id) => await schedules.CancelAsync(UserId, id) ? NoContent() : NotFound();
}
