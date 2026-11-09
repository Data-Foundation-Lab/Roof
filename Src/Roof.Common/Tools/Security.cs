using System.Buffers;
using System.Security.Cryptography;
using System.IO.Hashing;
using System.Text;
using System.Text.RegularExpressions;

namespace Roof.Common.Tools
{
    public static class Security
    {
        public static bool Is128BitHash(string input) => new Regex("^[a-fA-F0-9]{32}$").IsMatch(input);

        public class AES256
        {
            private static readonly Regex Base64Regex = new(@"^[a-zA-Z0-9+/]*={0,2}$", RegexOptions.None);
            public static (string, string) GenerateAESIVAndKey()
            {
                using (Aes aes = Aes.Create())
                {
                    aes.GenerateIV();
                    aes.GenerateKey();
                    return (Convert.ToBase64String(aes.IV), Convert.ToBase64String(aes.Key));
                }
            }

            public static void Encrypt(byte[] key, byte[] iv, byte[] data, out byte[] encryptedData)
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Padding = PaddingMode.PKCS7;  // Ensure padding is used

                    ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                    encryptedData = encryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }

            public static void Decrypt(byte[] key, byte[] iv, byte[] data, out byte[] decryptedData)
            {
                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Padding = PaddingMode.PKCS7;  // Ensure padding is used

                    ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                    decryptedData = decryptor.TransformFinalBlock(data, 0, data.Length);
                }
            }

            /*public static void ManagedEncrypt(string key, string iv, string data, out string encryptedData)
            {
                byte[] keyBuffer = Convert.FromBase64String(key);
                byte[] ivBuffer = Convert.FromBase64String(iv);
                byte[] messageBuffer = Encoding.ASCII.GetBytes(data);

                byte[] encryptedBuffer = new byte[] { };
                Encrypt(keyBuffer, ivBuffer, messageBuffer, out encryptedBuffer);

                encryptedData = Convert.ToBase64String(encryptedBuffer);
            }

            public static void ManagedDecrypt(string key, string iv, string data, out string decryptedData)
            {
                byte[] keyBuffer = Convert.FromBase64String(key);
                byte[] ivBuffer = Convert.FromBase64String(iv);
                byte[] messageBuffer = Convert.FromBase64String(data);

                byte[] decryptedBuffer = new byte[] { };
                Decrypt(keyBuffer, ivBuffer, messageBuffer, out decryptedBuffer);

                decryptedData = Convert.ToBase64String(decryptedBuffer);
            }*/

            public static byte[] ManagedNetworkEncrypt(string key, string iv, string data)
            {
                byte[] keyBuffer = Convert.FromBase64String(key);
                byte[] ivBuffer = Convert.FromBase64String(iv);

                byte[] preprocessedUTF8Buffer = Encoding.UTF8.GetBytes(data);

                byte[] encryptedBuffer = new byte[] { };
                Encrypt(keyBuffer, ivBuffer, preprocessedUTF8Buffer, out encryptedBuffer);

                string postProcessedBase64String = Convert.ToBase64String(encryptedBuffer);

                byte[] networkBufferSendingFormat = Encoding.UTF8.GetBytes(postProcessedBase64String);
                return networkBufferSendingFormat;
            }

            public static byte[] ManagedNetworkEncrypt(string key, string iv, byte[] data, int dataLength)
            {
                string networkBufferReceivingFormat = Encoding.UTF8.GetString(data, 0, dataLength);

                byte[] keyBuffer = Convert.FromBase64String(key);
                byte[] ivBuffer = Convert.FromBase64String(iv);

                byte[] preprocessedUTF8Buffer = Encoding.UTF8.GetBytes(networkBufferReceivingFormat);

                byte[] encryptedBuffer = new byte[] { };
                Encrypt(keyBuffer, ivBuffer, preprocessedUTF8Buffer, out encryptedBuffer);

                string postProcessedBase64String = Convert.ToBase64String(encryptedBuffer);

                byte[] networkBufferSendingFormat = Encoding.UTF8.GetBytes(postProcessedBase64String);
                return networkBufferSendingFormat;
            }

            public static string ManagedNetworkDecrypt(string key, string iv, byte[] data, int dataLength)
            {
                string networkBufferReceivingFormat = Encoding.UTF8.GetString(data, 0, dataLength);

                byte[] keyBuffer = Convert.FromBase64String(key);
                byte[] ivBuffer = Convert.FromBase64String(iv);

                byte[] preProcessedBuffer = Convert.FromBase64String(networkBufferReceivingFormat);

                byte[] decryptedBuffer = new byte[] { };
                Decrypt(keyBuffer, ivBuffer, preProcessedBuffer, out decryptedBuffer);

                return Encoding.UTF8.GetString(decryptedBuffer);
            }

            public static bool IsValidBase64AES256Key(string base64Key)
            {
                if (base64Key.Length == 44 && Base64Regex.IsMatch(base64Key))
                {
                    try
                    {
                        byte[] keyBytes = Convert.FromBase64String(base64Key);
                        return keyBytes.Length == 32;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                }

                return false;
            }

            public static bool IsValidBase64AESIV(string base64IV)
            {
                if (Base64Regex.IsMatch(base64IV))
                {
                    try
                    {
                        byte[] ivBytes = Convert.FromBase64String(base64IV);
                        return ivBytes.Length == 16;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                }

                return false;
            }
        }

        public class SHA256
        {
            public static string ComputeSha256Hash(string input)
            {
                using (System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(input);
                    byte[] hash = sha256.ComputeHash(bytes);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }

            public static string ComputeSha256Hash(byte[] data)
            {
                using (System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(data);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
        }

        public class XxHash64
        {
            public static string ComputeHash(byte[] input)
            {
                // Each call owns its hasher; callers include concurrent filesystem managers.
                var hasher = new System.IO.Hashing.XxHash64();
                hasher.Append(input);
                byte[] hashBytes = hasher.GetCurrentHash();

                // Convert the hash bytes to a hexadecimal string
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            
            /// <summary>
            /// Generates the hashed path using System.IO.Hashing.XxHash64.
            /// </summary>
            /// <param name="input">The raw file path to hash.</param>
            public static string GenerateHashedPath(string input)
            {
                // Convert the input string to bytes
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);

                // Each call owns its hasher; callers include concurrent filesystem managers.
                var hasher = new System.IO.Hashing.XxHash64();
                hasher.Append(inputBytes);
                byte[] hashBytes = hasher.GetCurrentHash();

                // Convert the hash bytes to a hexadecimal string
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
            
            /// <summary>
            /// Compute xxHash64 for the data in <paramref name="stream"/> starting at its current position.
            /// By default, the original stream position is restored after hashing.
            /// </summary>
            public static string ComputeHash(Stream stream, bool preservePosition = true)
            {
                if (stream is null)
                    throw new ArgumentNullException(nameof(stream));
                if (!stream.CanRead)
                    throw new ArgumentException("Stream must be readable.", nameof(stream));

                long originalPos = 0;
                if (preservePosition && stream.CanSeek)
                    originalPos = stream.Position;

                byte[]? rented = null;

                try
                {
                    // Rent a buffer (128 KiB is a good default).
                    rented = ArrayPool<byte>.Shared.Rent(128 * 1024);

                    // Local hasher instance to avoid cross-call/thread interference.
                    var hasher = new System.IO.Hashing.XxHash64();
                    
                    int read;
                    while ((read = stream.Read(rented, 0, rented.Length)) > 0)
                    {
                        hasher.Append(new ReadOnlySpan<byte>(rented, 0, read));
                    }
                    var hashBytes = hasher.GetCurrentHash(); // 8 bytes
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
                finally
                {
                    if (preservePosition && stream.CanSeek)
                        stream.Position = originalPos;

                    if (rented is not null)
                        ArrayPool<byte>.Shared.Return(rented);
                }
            }

            /// <summary>
            /// Convenience overload for FileStream. Hashes from current position and restores it by default.
            /// </summary>
            public static string ComputeHash(FileStream fileStream, bool preservePosition = true)
                => ComputeHash((Stream)fileStream, preservePosition);

            public static bool VerifyHashedPath(string input, string expectedHash)
            {
                // Generate hash for the given input
                string generatedHash = GenerateHashedPath(input);

                // Compare the generated hash with the expected hash
                return string.Equals(generatedHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            }

            public static bool IsValidXxHash64Format(string hash)
            {
                // Check if the hash length is exactly 16 characters
                if (hash.Length != 16)
                    return false;

                // Check if all characters are valid hexadecimal (0-9, a-f)
                return System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-fA-F0-9]{16}$");
            }
        }

        public class Random
        {
            private const int DEFAULT_NONCE_LENGTH_BYTES = 32;
            public static string GenerateNonce(int length = DEFAULT_NONCE_LENGTH_BYTES)
            {
                byte[] nonceBytes = new byte[length];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(nonceBytes);
                }
                return Convert.ToBase64String(nonceBytes);
            }

            public static string GenerateHexNonce(int length = DEFAULT_NONCE_LENGTH_BYTES)
            {
                byte[] nonceBytes = new byte[length];
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(nonceBytes);
                }
                return BitConverter.ToString(nonceBytes).Replace("-", "").ToLower();
            }
        }

        public static string GenerateMD5Hash(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] data = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    sb.Append(data[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }

        public static string GenerateStrongPassword(int length)
        {
            // Omit colons for compatibility with older Engine password validators.
            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()_+-=[]{}|;,.<>?";
            StringBuilder password = new StringBuilder();
            byte[] randomBytes = new byte[length];

            RandomNumberGenerator.Create().GetBytes(randomBytes);

            foreach (byte b in randomBytes)
            {
                password.Append(validChars[b % validChars.Length]);
            }

            return password.ToString();
        }
    }
}