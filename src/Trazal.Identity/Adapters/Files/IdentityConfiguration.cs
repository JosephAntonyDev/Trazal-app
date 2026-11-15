using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Trazal.Identity.Adapters.Files
{
    [DataContract]
    public sealed class IdentityConfiguration
    {
        [DataMember(Name = "apiKey")] public string ApiKey { get; set; }
        [DataMember(Name = "projectId")] public string ProjectId { get; set; }
        [DataMember(Name = "apiOrigin")] public string ApiOrigin { get; set; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ProjectId);
        public static IdentityConfiguration Load(string file)
        {
            var config = new IdentityConfiguration();
            if (File.Exists(file)) using (var stream = File.OpenRead(file)) config = (IdentityConfiguration)new DataContractJsonSerializer(typeof(IdentityConfiguration)).ReadObject(stream);
            config.ApiKey = Environment.GetEnvironmentVariable("TRAZAL_FIREBASE_API_KEY") ?? config.ApiKey;
            config.ProjectId = Environment.GetEnvironmentVariable("TRAZAL_FIREBASE_PROJECT_ID") ?? config.ProjectId;
            config.ApiOrigin = Environment.GetEnvironmentVariable("TRAZAL_API_ORIGIN") ?? config.ApiOrigin;
            if (config.IsConfigured && !Regex.IsMatch(config.ProjectId, "^[a-z][a-z0-9-]{4,62}$")) throw new ArgumentException("El projectId de Firebase no es válido.");
            if (!string.IsNullOrWhiteSpace(config.ApiOrigin))
            {
                Uri uri;
                if (!Uri.TryCreate(config.ApiOrigin, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo != "" || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "")
                    throw new ArgumentException("apiOrigin debe ser el origen HTTPS del Worker, sin rutas ni credenciales.");
            }
            return config;
        }
    }

    public static class AccountWorkspace
    {
        public static string DatabasePath(string root, string project, string uid)
        {
            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(uid) || uid.Length > 128) throw new ArgumentException("Cuenta inválida.");
            using (var sha = SHA256.Create())
            {
                var key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(project + "\n" + uid))).Replace("-", "").ToLowerInvariant();
                return Path.Combine(Path.GetFullPath(root), "accounts", key, "trazal.db");
            }
        }
    }
}
