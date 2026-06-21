using System;

namespace WzComparerR2.Cli
{
    internal sealed class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        {
        }
    }

    internal sealed class WzLoadException : Exception
    {
        public WzLoadException(string message)
            : base(message)
        {
        }

        public WzLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
