// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace BedrockLan;

public enum LanLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Minimal logging hook so the library carries no logging dependency.</summary>
public delegate void LanLogger(LanLogLevel level, string message);

internal static class LanLoggerExtensions
{
    public static void Debug(this LanLogger? logger, string message) => logger?.Invoke(LanLogLevel.Debug, message);

    public static void Info(this LanLogger? logger, string message) => logger?.Invoke(LanLogLevel.Info, message);

    public static void Warn(this LanLogger? logger, string message) => logger?.Invoke(LanLogLevel.Warning, message);

    public static void Error(this LanLogger? logger, string message) => logger?.Invoke(LanLogLevel.Error, message);
}
