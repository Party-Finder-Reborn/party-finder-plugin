using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PartyFinderReborn.Utils;

internal static class ResourceDecoder
{
    private static string? _cachedResult;
    
    internal static string GetDecodedContent()
    {
        if (_cachedResult != null) return _cachedResult;
        
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resources = assembly.GetManifestResourceNames();
            
            // Look for any resource ending with auth.dat
            var target = resources.FirstOrDefault(r => r.EndsWith("auth.dat"));
            if (target == null) return string.Empty;
            
            using var stream = assembly.GetManifestResourceStream(target);
            if (stream == null) return string.Empty;
            
            var encrypted = new byte[stream.Length];
            stream.ReadExactly(encrypted);
            
            var key = DeriveKey();
            var iv = encrypted.Take(16).ToArray();
            var ciphertext = encrypted.Skip(16).ToArray();
            
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            
            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            
            // Remove PKCS7 padding
            var paddingLength = decrypted[decrypted.Length - 1];
            var unpaddedLength = decrypted.Length - paddingLength;
            var unpadded = new byte[unpaddedLength];
            Array.Copy(decrypted, 0, unpadded, 0, unpaddedLength);
            
            _cachedResult = Encoding.UTF8.GetString(unpadded);
            return _cachedResult;
        }
        catch
        {
            return string.Empty;
        }
    }
    
    private static byte[] DeriveKey()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetName().Name ?? "default";
        var version = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var constant = "PFR2024SecureKey";
        
        var combined = string.Join(":", name, version, constant);
        
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
    }
}
