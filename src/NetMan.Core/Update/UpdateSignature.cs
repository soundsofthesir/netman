using System.Security.Cryptography;
using System.Text;

namespace NetMan.Core;

/// <summary>
/// ECDSA P-256 signatures over "NetMan {version}\n{sha256 of exe}". The release workflow signs with the private key;
/// the app holds the public key. Signing the version stops an older signed build from being installed as a newer one.
/// </summary>
public static class UpdateSignature
{
    public static byte[] Message(byte[] exe, Version version)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(exe));
        return Encoding.UTF8.GetBytes($"NetMan {AppVersion.Normalize(version)}\n{hash}");
    }

    /// <param name="signature">Base64 IEEE P1363 signature, as .NET's <c>ECDsa.SignData</c> produces.</param>
    /// <param name="publicKey">Base64 SubjectPublicKeyInfo.</param>
    public static bool Verify(byte[] exe, Version version, string signature, string publicKey)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return ecdsa.VerifyData(Message(exe, version), Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
