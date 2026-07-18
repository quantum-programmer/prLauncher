using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pyramid.Security;

internal sealed class LinuxCredentialProvider : IDatabaseCredentialProvider
{
    private const string CredentialDirectory = "/etc/pyramid/credentials";
    private const string MachineKeyPath = "/etc/pyramid/credentials/pyramid-machine-key.v1";
    private const string PayloadPrefix = "pyramid-linux-machine-aes-gcm:v1:";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly Regex SafeNameRegex = new(@"[^A-Za-z0-9_.-]", RegexOptions.Compiled);

    public string? GetPassword(string credentialId)
    {
        var path = GetCredentialPath(credentialId);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path, Encoding.UTF8).Trim();
        if (!text.StartsWith(PayloadPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Linux database credential '{credentialId}' was created by an older systemd-creds provider. Recreate it with Pyramid.");
        }

        var payload = JsonSerializer.Deserialize<EncryptedCredential>(
            Encoding.UTF8.GetString(Convert.FromBase64String(text[PayloadPrefix.Length..])))
            ?? throw new InvalidOperationException($"Linux database credential '{credentialId}' payload is invalid.");

        if (!string.Equals(payload.CredentialID, credentialId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Database credential payload does not match requested CredentialID.");
        }

        var key = ReadMachineKey();
        var nonce = Convert.FromBase64String(payload.Nonce);
        var tag = Convert.FromBase64String(payload.Tag);
        var ciphertext = Convert.FromBase64String(payload.Ciphertext);
        var plaintext = new byte[ciphertext.Length];
        var associatedData = Encoding.UTF8.GetBytes(credentialId);

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return Encoding.UTF8.GetString(plaintext);
    }

    public void SavePassword(string credentialId, string password)
    {
        Directory.CreateDirectory(CredentialDirectory);
        var key = EnsureMachineKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = Encoding.UTF8.GetBytes(password);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var associatedData = Encoding.UTF8.GetBytes(credentialId);

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        var payload = new EncryptedCredential(
            credentialId,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(ciphertext));

        var serializedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        var path = GetCredentialPath(credentialId);
        File.WriteAllText(path, PayloadPrefix + serializedPayload, new UTF8Encoding(false));
        TrySetMachineCredentialPermissions(path);
    }

    private static string GetCredentialPath(string credentialId)
    {
        var safeName = SafeNameRegex.Replace(credentialId, "_");
        return Path.Combine(CredentialDirectory, $"{safeName}.credential");
    }

    private static byte[] EnsureMachineKey()
    {
        if (File.Exists(MachineKeyPath))
        {
            return ReadMachineKey();
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllText(MachineKeyPath, Convert.ToBase64String(key), new UTF8Encoding(false));
        TrySetMachineCredentialPermissions(MachineKeyPath);
        return key;
    }

    private static byte[] ReadMachineKey()
    {
        if (!File.Exists(MachineKeyPath))
        {
            throw new FileNotFoundException(
                "Linux machine credential key was not found. Run Pyramid installation to create credentials.",
                MachineKeyPath);
        }

        var key = Convert.FromBase64String(File.ReadAllText(MachineKeyPath, Encoding.UTF8).Trim());
        if (key.Length != KeySize)
        {
            throw new InvalidOperationException("Linux machine credential key has invalid size.");
        }

        return key;
    }

    private static void TrySetMachineCredentialPermissions(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CredentialDirectory);
            File.SetUnixFileMode(
                Path.GetDirectoryName(CredentialDirectory)!,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(
                CredentialDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead |
                UnixFileMode.OtherRead);
        }
        catch
        {
            // Best effort only. The credential payload still remains encrypted.
        }
    }

    private sealed record EncryptedCredential(
        string CredentialID,
        string Nonce,
        string Tag,
        string Ciphertext);
}
