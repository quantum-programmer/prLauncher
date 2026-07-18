using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Pyramid.Security;

[SupportedOSPlatform("windows")]
internal sealed class WindowsCredentialProvider : IDatabaseCredentialProvider
{
    private const string TpmProviderName = "Microsoft Platform Crypto Provider";
    private const string KeyName = "Pyramid.Database.MachineCredentialKey.v1";
    private const string BuiltinUsersSid = "*S-1-5-32-545";

    private static readonly byte[] PayloadPrefix = Encoding.UTF8.GetBytes("cng-tpm-machine-rsa-oaep-sha256:v1:");
    private static readonly CngProvider TpmProvider = new(TpmProviderName);
    private static readonly Regex UnsafeFileNameChars = new(@"[^A-Za-z0-9_.-]", RegexOptions.Compiled);

    public string? GetPassword(string credentialId)
    {
        var path = GetCredentialPath(credentialId);
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedPayload = File.ReadAllBytes(path);
        var encryptedPayload = DecodePayload(protectedPayload);
        var decryptedPayload = DecryptWithTpmKey(encryptedPayload);
        return DecodePasswordPayload(decryptedPayload, credentialId);
    }

    public void SavePassword(string credentialId, string password)
    {
        var path = GetCredentialPath(credentialId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var plainPayload = EncodePasswordPayload(credentialId, password);
        var encryptedPayload = EncryptWithTpmKey(plainPayload);
        var payload = EncodePayload(encryptedPayload);

        File.WriteAllBytes(path, payload);
        SetMachineCredentialPermissions(path);
    }

    private static string GetCredentialPath(string credentialId)
    {
        var safeName = UnsafeFileNameChars.Replace(credentialId, "_");
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Pyramid",
            "Credentials",
            $"{safeName}.credential");
    }

    private static byte[] EncodePayload(byte[] encryptedPassword)
    {
        var payload = new byte[PayloadPrefix.Length + encryptedPassword.Length];
        Buffer.BlockCopy(PayloadPrefix, 0, payload, 0, PayloadPrefix.Length);
        Buffer.BlockCopy(encryptedPassword, 0, payload, PayloadPrefix.Length, encryptedPassword.Length);
        return payload;
    }

    private static byte[] DecodePayload(byte[] payload)
    {
        if (payload.Length <= PayloadPrefix.Length)
        {
            throw new InvalidOperationException("Database credential payload is empty.");
        }

        for (var i = 0; i < PayloadPrefix.Length; i++)
        {
            if (payload[i] != PayloadPrefix[i])
            {
                throw new InvalidOperationException("Unsupported database credential payload format. Re-run Pyramid to recreate the machine credential.");
            }
        }

        var encryptedPassword = new byte[payload.Length - PayloadPrefix.Length];
        Buffer.BlockCopy(payload, PayloadPrefix.Length, encryptedPassword, 0, encryptedPassword.Length);
        return encryptedPassword;
    }

    private static byte[] EncodePasswordPayload(string credentialId, string password)
    {
        var text = credentialId + "\n" + password;
        return Encoding.UTF8.GetBytes(text);
    }

    private static string DecodePasswordPayload(byte[] payload, string credentialId)
    {
        var text = Encoding.UTF8.GetString(payload);
        var separatorIndex = text.IndexOf('\n', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            throw new InvalidOperationException("Invalid database credential payload.");
        }

        var payloadCredentialId = text[..separatorIndex];
        if (!string.Equals(payloadCredentialId, credentialId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Database credential payload does not match requested CredentialID.");
        }

        return text[(separatorIndex + 1)..];
    }

    private static byte[] EncryptWithTpmKey(byte[] data)
    {
        using var key = OpenOrCreateTpmMachineKey();
        using var rsa = new RSACng(key);
        return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
    }

    private static byte[] DecryptWithTpmKey(byte[] data)
    {
        using var key = OpenTpmMachineKey();
        using var rsa = new RSACng(key);
        return rsa.Decrypt(data, RSAEncryptionPadding.OaepSHA256);
    }

    private static CngKey OpenOrCreateTpmMachineKey()
    {
        if (CngKey.Exists(KeyName, TpmProvider, CngKeyOpenOptions.MachineKey))
        {
            return CngKey.Open(KeyName, TpmProvider, CngKeyOpenOptions.MachineKey);
        }

        try
        {
            var creationParameters = new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None,
                KeyCreationOptions = CngKeyCreationOptions.MachineKey,
                KeyUsage = CngKeyUsages.Decryption,
                Provider = TpmProvider
            };
            creationParameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));

            return CngKey.Create(CngAlgorithm.Rsa, KeyName, creationParameters);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Failed to create TPM-backed machine CNG key. Check that TPM 2.0 is enabled and the Microsoft Platform Crypto Provider is available.",
                ex);
        }
    }

    private static CngKey OpenTpmMachineKey()
    {
        try
        {
            return CngKey.Open(KeyName, TpmProvider, CngKeyOpenOptions.MachineKey);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "TPM-backed machine CNG key was not found. Run Pyramid as administrator to create the database credential key.",
                ex);
        }
    }

    private static void SetMachineCredentialPermissions(string credentialPath)
    {
        var pyramidDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Pyramid");
        var credentialDirectory = Path.GetDirectoryName(credentialPath)!;

        RunIcacls(pyramidDirectory, "/grant", $"{BuiltinUsersSid}:(OI)(CI)(RX)");
        RunIcacls(credentialDirectory, "/grant", $"{BuiltinUsersSid}:(OI)(CI)(RX)");
        RunIcacls(credentialPath, "/grant", $"{BuiltinUsersSid}:(R)");

        using var key = OpenTpmMachineKey();
        if (string.IsNullOrWhiteSpace(key.UniqueName))
        {
            return;
        }

        var keyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Crypto",
            "Keys",
            key.UniqueName);

        if (File.Exists(keyPath))
        {
            RunIcacls(keyPath, "/grant", $"{BuiltinUsersSid}:(R)");
        }
    }

    private static void RunIcacls(string path, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "icacls",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(path);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start icacls.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to set permissions for '{path}' with icacls. {output} {error}".Trim());
        }
    }
}
