using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jackett.Common.Services.Interfaces;

namespace Jackett.Mobile.Core.Services
{
    // Encrypts the passwords saved in the indexer configuration files.
    // Replaces the ASP.NET DataProtection based service of the server. AES-CBC + HMAC-SHA256 is used
    // because it is available on every platform (AesGcm is not on all iOS versions).
    public class MobileProtectionService : IProtectionService
    {
        public const int KeyLength = 64; // 32 bytes AES key + 32 bytes HMAC key

        private readonly byte[] _encryptionKey;
        private readonly byte[] _macKey;

        public MobileProtectionService(byte[] key)
        {
            if (key == null || key.Length != KeyLength)
                throw new ArgumentException($"The key must be {KeyLength} bytes", nameof(key));

            _encryptionKey = key.Take(32).ToArray();
            _macKey = key.Skip(32).ToArray();
        }

        public static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(KeyLength);

        public string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();
            var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(plainText), aes.IV);

            var payload = aes.IV.Concat(cipher).ToArray();
            var mac = HMACSHA256.HashData(_macKey, payload);
            return Convert.ToBase64String(payload.Concat(mac).ToArray());
        }

        public string UnProtect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            var data = Convert.FromBase64String(plainText);
            if (data.Length < 16 + 16 + 32)
                throw new CryptographicException("Invalid protected data");

            var payload = data.Take(data.Length - 32).ToArray();
            var mac = data.Skip(data.Length - 32).ToArray();
            if (!CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(_macKey, payload)))
                throw new CryptographicException("Protected data was modified or encrypted with another key");

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            var iv = payload.Take(16).ToArray();
            var plain = aes.DecryptCbc(payload.Skip(16).ToArray(), iv);
            return Encoding.UTF8.GetString(plain);
        }

        // Fallback when the platform has no secure storage: a key file in the app data folder.
        public static byte[] LoadOrCreateKeyFile(string path)
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == KeyLength)
                    return existing;
            }

            var key = GenerateKey();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, key);
            return key;
        }
    }
}
