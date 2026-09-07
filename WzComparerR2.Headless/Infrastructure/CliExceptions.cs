using System;

namespace WzComparerR2.Headless
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

        public WzLoadException(string message, Exception innerException, WzLoadDiagnostic diagnostic)
            : base(message, innerException)
        {
            this.Diagnostic = diagnostic;
        }

        public WzLoadDiagnostic Diagnostic { get; private set; }
    }

    internal sealed class CliErrorDto
    {
        public string Error { get; set; }
        public string Message { get; set; }
        public string InnerMessage { get; set; }
        public WzLoadDiagnostic Diagnostic { get; set; }
    }
}
