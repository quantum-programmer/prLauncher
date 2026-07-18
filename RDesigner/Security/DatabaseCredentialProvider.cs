using System;
using System.Runtime.InteropServices;

namespace Pyramid.Security;

public static class DatabaseCredentialProvider
{
    public static IDatabaseCredentialProvider Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialProvider();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxCredentialProvider();
        }

        throw new PlatformNotSupportedException("Secure database credential storage is supported only on Windows and Linux.");
    }
}
