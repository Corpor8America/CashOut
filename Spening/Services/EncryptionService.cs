using System.Security.Cryptography;

public class EncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(IConfiguration config)
    {
        var raw = config["ENCRYPTION_KEY"]
            ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY")
            ?? throw new InvalidOperationException(
                "ENCRYPTION_KEY environment variable is required.");

        _key = Convert.FromBase64String(raw);

        if (_key.Length != 32)
            throw new InvalidOperationException(
                "ENCRYPTION_KEY must be a base64-encoded 32-byte value.");
    }

    public string Encrypt(string plaintext)
    {
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];   // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];       // 16 bytes

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Store as: base64(nonce) + "." + base64(tag) + "." + base64(ciphertext)
        return $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(ciphertext)}";
    }

    public string Decrypt(string payload)
    {
        var parts = payload.Split('.');
        if (parts.Length != 3)
            throw new FormatException("Invalid encrypted payload format.");

        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var ciphertext = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
