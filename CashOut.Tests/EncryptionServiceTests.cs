using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CashOut.Tests;

[TestClass]
public class EncryptionServiceTests
{
    private static EncryptionService BuildService(string? base64Key = null)
    {
        base64Key ??= Convert.ToBase64String(new byte[32]
        {
            1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,
            17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ENCRYPTION_KEY"] = base64Key
            })
            .Build();

        return new EncryptionService(config);
    }

    [TestMethod]
    public void RoundTrip_Plaintext_IsRecovered()
    {
        var svc = BuildService();
        var original = "access-sandbox-abc123";

        var encrypted = svc.Encrypt(original);
        var decrypted = svc.Decrypt(encrypted);

        Assert.AreEqual(original, decrypted);
    }

    [TestMethod]
    public void Encrypt_ProducesThreeDotSeparatedSegments()
    {
        var svc = BuildService();
        var payload = svc.Encrypt("test");
        var parts = payload.Split('.');
        Assert.AreEqual(3, parts.Length);
    }

    [TestMethod]
    public void Encrypt_TwiceSamePlaintext_ProducesDifferentCiphertext()
    {
        // AES-GCM uses a random nonce, so every encryption call is unique
        var svc = BuildService();
        var a = svc.Encrypt("same-input");
        var b = svc.Encrypt("same-input");
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void Decrypt_TamperedPayload_Throws()
    {
        var svc = BuildService();
        var encrypted = svc.Encrypt("secret");

        var parts = encrypted.Split('.');
        parts[2] = Convert.ToBase64String(new byte[32]); // wrong bytes
        var tampered = string.Join('.', parts);

        Assert.ThrowsExactly<System.Security.Cryptography.AuthenticationTagMismatchException>(() => svc.Decrypt(tampered));
    }

    [TestMethod]
    public void Decrypt_MalformedPayload_ThrowsFormatException()
    {
        var svc = BuildService();
        Assert.ThrowsExactly<FormatException>(() => svc.Decrypt("not.valid"));
    }

    [TestMethod]
    public void Constructor_WrongKeyLength_Throws()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        Assert.ThrowsExactly<InvalidOperationException>(() => BuildService(shortKey));
    }

    [TestMethod]
    public void RoundTrip_EmptyString_Works()
    {
        var svc = BuildService();
        Assert.AreEqual("", svc.Decrypt(svc.Encrypt("")));
    }

    [TestMethod]
    public void RoundTrip_UnicodeString_Works()
    {
        var svc = BuildService();
        var text = "café ☕ résumé — 日本語";
        Assert.AreEqual(text, svc.Decrypt(svc.Encrypt(text)));
    }
}