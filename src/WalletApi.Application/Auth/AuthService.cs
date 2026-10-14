using Microsoft.EntityFrameworkCore;
using WalletApi.Application.Abstractions;
using WalletApi.Domain;

namespace WalletApi.Application.Auth;

public class AuthService(IWalletDb db, ITokenService tokens)
{
    public async Task<(string? Token, string? Error)> RegisterAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || password.Length < 8) return (null, "Valid email and 8+ char password required");
        if (await db.Users.AnyAsync(u => u.Email == email)) return (null, "Email already registered");
        var user = new User { Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password) };
        db.Users.Add(user);
        db.Accounts.Add(new Account { UserId = user.Id });
        await db.SaveChangesAsync();
        return (tokens.Create(user.Id, user.Email), null);
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        return user is not null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)
            ? tokens.Create(user.Id, user.Email) : null;
    }
}
