using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Trazal.Identity.Application;
using Trazal.Identity.Domain;

namespace Trazal.Identity.Adapters.Windows
{
    public sealed class ProtectedSessionStore : ISessionStore
    {
        private readonly string file;
        private readonly byte[] entropy;
        public ProtectedSessionStore(string file, string projectId)
        {
            this.file = Path.GetFullPath(file);
            entropy = Encoding.UTF8.GetBytes("Trazal.Identity.v1:" + projectId);
        }
        public RememberedSession Read()
        {
            if (!File.Exists(file)) return null;
            byte[] clear = null;
            try
            {
                if (new FileInfo(file).Length > 131072) throw new InvalidDataException();
                clear = ProtectedData.Unprotect(File.ReadAllBytes(file), entropy, DataProtectionScope.CurrentUser);
                using (var stream = new MemoryStream(clear)) using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadInt32() != 1) throw new InvalidDataException();
                    return new RememberedSession { Uid = reader.ReadString(), Email = reader.ReadString(), RefreshToken = reader.ReadString() };
                }
            }
            catch (CryptographicException) { throw new IdentityException("session_unreadable", "No se pudo abrir la sesión guardada en Windows. Inicia sesión de nuevo."); }
            catch (IOException) { throw new IdentityException("session_unreadable", "No se pudo leer la sesión guardada. Inicia sesión de nuevo."); }
            finally { if (clear != null) Array.Clear(clear, 0, clear.Length); }
        }
        public void Save(RememberedSession session)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            byte[] clear;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                { writer.Write(1); writer.Write(session.Uid); writer.Write(session.Email ?? ""); writer.Write(session.RefreshToken); }
                clear = stream.ToArray();
            }
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, ProtectedData.Protect(clear, entropy, DataProtectionScope.CurrentUser));
                if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
            }
            finally { Array.Clear(clear, 0, clear.Length); if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Clear() { if (File.Exists(file)) File.Delete(file); }
    }
}
