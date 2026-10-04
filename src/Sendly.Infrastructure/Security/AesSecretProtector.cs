using System.Security.Cryptography;
using System.Text;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Security;

public class AesSecretProtector : ISecretProtector
{
    private readonly byte[] _key;

    public AesSecretProtector(IOptions<SecretProtectionOptions> options)
    {
        _key = ResolveKey(options.Value.EncryptionKey);
    }

    public string Protect(string plainText)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var payload = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, payload, nonce.Length + tag.Length, cipherBytes.Length);
        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedText)
    {
        var payload = Convert.FromBase64String(protectedText);
        if (payload.Length < 28)
        {
            throw new InvalidOperationException("Protected secret payload is invalid.");
        }

        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipherBytes = payload.AsSpan(28);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] ResolveKey(string configuredKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            var generated = SHA256.HashData(Encoding.UTF8.GetBytes("Sendly-Dev-Only-Key-Change-Me"));
            return generated;
        }

        try
        {
            var keyBytes = Convert.FromBase64String(configuredKey);
            if (keyBytes.Length == 32)
            {
                return keyBytes;
            }
        }
        catch (FormatException)
        {
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
    }
}
