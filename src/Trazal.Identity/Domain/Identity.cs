using System;
using System.Net.Mail;

namespace Trazal.Identity.Domain
{
    public sealed class AccountIdentity
    {
        public string Uid { get; set; }
        public string Email { get; set; }
        public bool EmailVerified { get; set; }
    }

    public sealed class IdentityTokens
    {
        public AccountIdentity Identity { get; set; }
        public string IdToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public sealed class RememberedSession
    {
        public string Uid { get; set; }
        public string Email { get; set; }
        public string RefreshToken { get; set; }
    }

    public sealed class IdentityException : Exception
    {
        public string Code { get; }
        public IdentityException(string code, string message) : base(message) { Code = code; }
    }

    public static class Credentials
    {
        public static bool IsEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
            try { return new MailAddress(value.Trim()).Address == value.Trim(); }
            catch (FormatException) { return false; }
        }

        public static void Validate(string email, string password, bool registration)
        {
            if (!IsEmail(email)) throw new ArgumentException("Escribe un correo electrónico válido.");
            if (string.IsNullOrEmpty(password) || password.Length > 4096)
                throw new ArgumentException("Escribe tu contraseña.");
            // Firebase applies the project's complete password policy as well.
            if (registration && password.Length < 8)
                throw new ArgumentException("Usa al menos 8 caracteres para tu contraseña.");
        }
    }
}
