using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WalletApi.Domain;

namespace WalletApi.Infrastructure.Persistence;

public static class DbSeeder
{
    /// <summary>Creates the lender pool account and, if configured, a Regulator login (Seed:RegulatorEmail / Seed:RegulatorPassword).</summary>
    public static async Task SeedAsync(WalletDb db, IConfiguration cfg)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == Ids.LenderPool))
            db.Accounts.Add(new Account { Id = Ids.LenderPool, UserId = Guid.Empty });

        var email = cfg["Seed:RegulatorEmail"];
        var password = cfg["Seed:RegulatorPassword"];
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password) && !await db.Users.AnyAsync(u => u.Email == email))
            db.Users.Add(new User { Email = email, Role = Role.Regulator, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) });
        await db.SaveChangesAsync();
    }
}
