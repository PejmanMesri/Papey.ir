using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Papey.Data;

namespace Papey.Hubs;

[Authorize(Roles = "Admin")]
public sealed class PrintingHub(IDbContextFactory<AppDbContext> dbFactory) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync(Context.ConnectionAborted);
        if (!int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !await db.Users.AnyAsync(u => u.Id == userId && u.IsAdmin, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }
        // Clients must drain the durable queue on every connection/reconnection.
        await Clients.Caller.SendAsync("PrintJobsAvailable", cancellationToken: Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
