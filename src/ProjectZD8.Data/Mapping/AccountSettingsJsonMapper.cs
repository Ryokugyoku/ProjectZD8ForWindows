using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectZD8.Application.Settings.State;
using static ProjectZD8.Data.Mapping.SwiftJsonReader;

namespace ProjectZD8.Data.Mapping;

/// <summary>
/// アカウント設定と Swift の <c>AccountSettings</c>（合成 Codable）の JSON を相互に変換します。
/// </summary>
/// <remarks>3 つのキー（language、appearance、automaticSessionUploadEnabled）はすべて必須です。1 つでも欠けると読めません（Swift と同じ）。</remarks>
public static class AccountSettingsJsonMapper
{
    /// <summary>
    /// アカウント設定を JSON のバイト列にします。
    /// </summary>
    /// <remarks>責務: 1 件の設定を、キーを並べ替えた UTF-8 の JSON へ変換します（CloudKit の settingsData と同じ形）。</remarks>
    /// <param name="settings">アカウント設定。</param>
    /// <returns>UTF-8 の JSON。</returns>
    public static byte[] Encode(AccountSettings settings) =>
        SwiftJsonWriter.Write(ToJson(settings), SwiftJsonEncodingOptions.SortedDefault);

    /// <summary>
    /// JSON のバイト列からアカウント設定を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON を設定へ変換し、壊れているか不完全なら <c>null</c> を返します（Swift の <c>try?</c>）。</remarks>
    /// <param name="data">UTF-8 の JSON。<c>null</c> なら <c>null</c>。</param>
    /// <returns>復元できた設定。</returns>
    public static AccountSettings? TryDecode(byte[]? data)
    {
        if (data is null)
        {
            return null;
        }
        try
        {
            return Parse(data, FromJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// アカウント設定を JSON の木にします。
    /// </summary>
    /// <remarks>責務: 1 件の設定を宣言順のキーの JSON オブジェクトへ変換します。</remarks>
    /// <param name="settings">アカウント設定。</param>
    /// <returns>JSON のオブジェクト。</returns>
    public static JsonObject ToJson(AccountSettings settings) => new()
    {
        ["language"] = settings.Language.ToRawValue(),
        ["appearance"] = settings.Appearance.ToRawValue(),
        ["automaticSessionUploadEnabled"] = settings.AutomaticSessionUploadEnabled,
    };

    /// <summary>
    /// JSON からアカウント設定を復元します。
    /// </summary>
    /// <remarks>責務: 1 件の JSON オブジェクトを設定へ変換します。</remarks>
    /// <param name="element">JSON のオブジェクト。</param>
    /// <returns>アカウント設定。</returns>
    /// <exception cref="JsonException">キーが欠けているか、値が不明な場合。</exception>
    public static AccountSettings FromJson(JsonElement element) => new(
        language: Enum<SettingsLanguage>(Required(element, "language"), SettingsLanguageRawValue.TryParse),
        appearance: Enum<SettingsAppearance>(Required(element, "appearance"), SettingsAppearanceRawValue.TryParse),
        automaticSessionUploadEnabled: Bool(Required(element, "automaticSessionUploadEnabled")));
}
