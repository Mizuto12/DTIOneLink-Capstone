using System.Security.Cryptography;

namespace DTIOneLink.Security
{
    /// <summary>
    /// Small PBKDF2 (SHA-256) password hasher — no external dependencies.
    /// Stored format: "{iterations}.{saltBase64}.{hashBase64}".
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;      // 128-bit
        private const int KeySize = 32;       // 256-bit
        private const int Iterations = 100_000;
        private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

        public static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string? stored)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return false;
            }

            var parts = stored.Split('.', 3);
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            {
                return false;
            }

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expected = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
