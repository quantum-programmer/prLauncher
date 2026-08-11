using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Pyramid.Services;

internal static class ConfigurationFileSafety
{
    public static bool RepairTrailingNullBytes(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var content = File.ReadAllBytes(path);
        var validLength = content.Length;
        while (validLength > 0 && content[validLength - 1] == 0)
        {
            validLength--;
        }

        if (validLength == content.Length)
        {
            return false;
        }

        using (JsonDocument.Parse(content.AsMemory(0, validLength)))
        {
            // Parsing proves that only the trailing NUL bytes are corrupt.
        }

        WriteAllBytesAtomic(path, content.AsSpan(0, validLength));
        return true;
    }

    public static void WriteAllTextAtomic(string path, string content, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        var encodedContent = encoding.GetBytes(content);
        var bytes = new byte[preamble.Length + encodedContent.Length];
        preamble.CopyTo(bytes, 0);
        encodedContent.CopyTo(bytes, preamble.Length);
        WriteAllBytesAtomic(path, bytes);
    }

    private static void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Configuration path has no directory: {path}");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
