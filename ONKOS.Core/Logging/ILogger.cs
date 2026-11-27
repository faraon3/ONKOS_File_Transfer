using System;

namespace ONKOS.Core.Logging
{
    public enum LogLevel
    {
        Info,
        Warn,
        Error
    }

    public interface ILogger
    {
        void Info(string category, string message);
        void Warn(string category, string message);
        void Error(string category, string message, Exception ex = null);
    }
}
