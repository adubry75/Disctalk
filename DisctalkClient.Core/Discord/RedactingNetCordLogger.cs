using DisctalkClient.Core.Security;
using Microsoft.Extensions.Logging;
using NetCordLogLevel = NetCord.Logging.LogLevel;

namespace DisctalkClient.Core.Discord;

/// <summary>Forwards NetCord's gateway/REST logs to Microsoft.Extensions.Logging with secrets scrubbed.</summary>
internal sealed class RedactingNetCordLogger(ILogger inner) : NetCord.Logging.IGatewayLogger, NetCord.Logging.IRestLogger
{
    public bool IsEnabled(NetCordLogLevel logLevel) => inner.IsEnabled(Map(logLevel));

    public void Log<TState>(NetCordLogLevel logLevel, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var level = Map(logLevel);
        if (!inner.IsEnabled(level)) return;
        var text = SecretRedactor.Redact(formatter(state, exception));
        // The exception object itself is not passed on: its message/stack could carry a URL with a webhook token.
        if (exception is not null) text += " | " + exception.GetType().Name + ": " + SecretRedactor.Redact(exception.Message);
        inner.Log(level, "{Message}", text);
    }

    static LogLevel Map(NetCordLogLevel level) => level switch
    {
        NetCordLogLevel.Trace => LogLevel.Trace,
        NetCordLogLevel.Debug => LogLevel.Debug,
        NetCordLogLevel.Information => LogLevel.Information,
        NetCordLogLevel.Warning => LogLevel.Warning,
        NetCordLogLevel.Error => LogLevel.Error,
        NetCordLogLevel.Critical => LogLevel.Critical,
        _ => LogLevel.None,
    };
}
