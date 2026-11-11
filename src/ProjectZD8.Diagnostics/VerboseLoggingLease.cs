using System.Globalization;
using System.Text.Json;

namespace ProjectZD8.Diagnostics;

/// <summary>
/// 起動時に「詳細ログ」がどうなったかです。
/// </summary>
public enum VerboseLoggingStartup
{
    /// <summary>入っていない。</summary>
    Off,

    /// <summary>入っていて、期限内。</summary>
    On,

    /// <summary>入っていたが期限（7 日）が過ぎたので切った。</summary>
    Expired,
}

/// <summary>
/// 「詳細ログ」スイッチを入れた日時を保存し、7 日たったら起動時に自動で切ります（入れっぱなしを防ぐ）。
/// </summary>
/// <remarks>
/// 保存先は診断ログのフォルダーの <c>verbose-logging.json</c> です（診断ログのファイル名ではないので、
/// 不具合レポートにも「診断ログを消す」にも含まれません）。
/// </remarks>
public sealed class VerboseLoggingLease
{
    /// <summary>保存するファイルの名前です。</summary>
    public const string FileName = "verbose-logging.json";

    /// <summary>詳細ログを入れたままにできる期間です。</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromDays(7);

    /// <summary>時計が大きく戻っていたとみなす差です（これより未来の日時は無効とする）。</summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromDays(1);

    /// <summary>保存先のファイルです。</summary>
    private readonly string _path;

    /// <summary>時計です。</summary>
    private readonly TimeProvider _clock;

    /// <summary>
    /// 作ります。
    /// </summary>
    /// <param name="dataDirectory">データのフォルダー（パッケージの LocalState）。</param>
    /// <param name="clock">時計。省略すると実時刻。</param>
    public VerboseLoggingLease(string dataDirectory, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        _path = Path.Combine(dataDirectory, DiagnosticSessionOptions.DirectoryName, FileName);
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>入れた日時です。入っていなければ null（期限が過ぎていても、まだ切っていなければ値を返す）。</summary>
    public DateTimeOffset? EnabledAt => Read();

    /// <summary>自動で切れる日時です。入っていなければ null。</summary>
    public DateTimeOffset? ExpiresAt => EnabledAt + Duration;

    /// <summary>
    /// 期限を調べ、過ぎていれば切ります。
    /// </summary>
    /// <remarks>責務: 起動時に呼び、診断ログを始める前に詳細ログを入れるかを決めます。壊れた・未来の日時は無効として消します。</remarks>
    /// <returns>起動時の状態。</returns>
    public VerboseLoggingStartup ApplyAtStartup()
    {
        var enabledAt = Read();
        if (enabledAt is null)
        {
            if (File.Exists(_path))
            {
                Delete();
            }

            return VerboseLoggingStartup.Off;
        }

        var age = _clock.GetUtcNow() - enabledAt.Value;
        if (age >= Duration || age < -ClockSkewTolerance)
        {
            Delete();
            return VerboseLoggingStartup.Expired;
        }

        return VerboseLoggingStartup.On;
    }

    /// <summary>
    /// 詳細ログを入れた日時を今にして保存します。
    /// </summary>
    /// <returns>保存できたら true。</returns>
    public bool Enable()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["enabledAt"] = _clock.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            });
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 詳細ログを切った記録（保存した日時）を消します。
    /// </summary>
    public void Disable() => Delete();

    /// <summary>保存した日時を読みます。</summary>
    /// <returns>日時。なければ・壊れていれば null。</returns>
    private DateTimeOffset? Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("enabledAt", out var value)
                && value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var enabledAt))
            {
                return enabledAt;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // 壊れている・読めないものは「入っていない」とみなす。
        }

        return null;
    }

    /// <summary>保存したファイルを消します。</summary>
    private void Delete()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 消せなければ次の起動でもう一度試す。
        }
    }
}
