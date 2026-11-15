using System;
using System.Threading;
using System.Threading.Tasks;
using Trazal.Identity.Domain;

namespace Trazal.Identity.Application
{
    public interface IIdentityProvider
    {
        Task<IdentityTokens> SignInAsync(string email, string password, CancellationToken cancellation);
        Task<IdentityTokens> RegisterAsync(string email, string password, CancellationToken cancellation);
        Task<IdentityTokens> RefreshAsync(string refreshToken, CancellationToken cancellation);
        Task<AccountIdentity> GetIdentityAsync(string idToken, CancellationToken cancellation);
        Task SendVerificationAsync(string idToken, CancellationToken cancellation);
        Task SendPasswordResetAsync(string email, CancellationToken cancellation);
    }

    public interface ISessionStore
    {
        RememberedSession Read();
        void Save(RememberedSession session);
        void Clear();
    }

    public interface IClock { DateTime UtcNow { get; } }
    public sealed class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
}
