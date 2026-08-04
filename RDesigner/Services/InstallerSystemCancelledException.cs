using System;

namespace Pyramid.Services;

public sealed class InstallerSystemCancelledException : Exception
{
    public InstallerSystemCancelledException(string message)
        : base(message)
    {
    }
}
