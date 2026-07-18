using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Pyramid.Security;

internal sealed class LinuxCredentialProvider : IDatabaseCredentialProvider
{
    private const string CredentialDirectory = "/etc/pyramid/credentials";

    private static readonly Regex SafeNameRegex = new(@"[^A-Za-z0-9_.-]", RegexOptions.Compiled);

    public string? GetPassword(string credentialId)
    {
        var path = GetCredentialPath(credentialId);
        if (!File.Exists(path))
        {
            return null;
        }

        var result = RunSystemdCreds("decrypt", path, "-");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to decrypt Linux database credential '{credentialId}': {result.Error.Trim()}");
        }

        return result.Output.TrimEnd('\r', '\n');
    }

    public void SavePassword(string credentialId, string password)
    {
        var path = GetCredentialPath(credentialId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var result = RunSystemdCreds("encrypt", "--with-key=tpm2", "-", path, password);
        if (result.ExitCode != 0)
        {
            result = RunSystemdCreds("encrypt", "--with-key=host", "-", path, password);
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to encrypt Linux database credential '{credentialId}' with systemd-creds: {result.Error.Trim()}");
        }

        TrySetMachineCredentialPermissions(path);
    }

    private static string GetCredentialPath(string credentialId)
    {
        var safeName = SafeNameRegex.Replace(credentialId, "_");
        return Path.Combine(CredentialDirectory, $"{safeName}.credential");
    }

    private static ProcessResult RunSystemdCreds(params string[] arguments) =>
        RunSystemdCreds(arguments, null);

    private static ProcessResult RunSystemdCreds(string arg1, string arg2, string arg3, string arg4, string? standardInput = null) =>
        RunSystemdCreds(new[] { arg1, arg2, arg3, arg4 }, standardInput);

    private static ProcessResult RunSystemdCreds(string arg1, string arg2, string arg3, string? standardInput = null) =>
        RunSystemdCreds(new[] { arg1, arg2, arg3 }, standardInput);

    private static ProcessResult RunSystemdCreds(string[] arguments, string? standardInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "systemd-creds",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start systemd-creds.");

            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output, error);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("systemd-creds was not found. Install systemd-creds or configure a supported Linux credential provider.", ex);
        }
    }

    private static void TrySetMachineCredentialPermissions(string path)
    {
        try
        {
            RunChmod("755", Path.GetDirectoryName(CredentialDirectory)!);
            RunChmod("755", CredentialDirectory);
            RunChmod("644", path);
        }
        catch
        {
            // Best effort only. systemd-creds still stores encrypted data.
        }
    }

    private static void RunChmod(string mode, string path)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "chmod",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { mode, path }
        });
        process?.WaitForExit();
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
