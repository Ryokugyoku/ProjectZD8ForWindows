using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>接続の役割の表示用のキーと記号です（iOS 版の <c>AdapterConnectionRole+MacOSPresentation</c>）。</summary>
public static class AdapterConnectionRolePresentation
{
    /// <summary>役割の名前のキーを返します。</summary>
    /// <remarks>責務: 役割を xcstrings のキーへ変換します。</remarks>
    /// <param name="role">役割。</param>
    /// <returns>キー。</returns>
    public static string TitleKey(this AdapterConnectionRole role) =>
        role == AdapterConnectionRole.Primary ? "settings.adapter.primary" : "settings.adapter.secondary";

    /// <summary>役割のバッジのキーを返します。</summary>
    /// <remarks>責務: 役割を「必須」「受信専用」のキーへ変換します。</remarks>
    /// <param name="role">役割。</param>
    /// <returns>キー。</returns>
    public static string BadgeKey(this AdapterConnectionRole role) =>
        role == AdapterConnectionRole.Primary ? "settings.adapter.required" : "settings.adapter.receive_only";

    /// <summary>選択ダイアログの題のキーを返します。</summary>
    /// <remarks>責務: 役割を選択ダイアログの題のキーへ変換します。</remarks>
    /// <param name="role">役割。</param>
    /// <returns>キー。</returns>
    public static string SelectionTitleKey(this AdapterConnectionRole role) =>
        role == AdapterConnectionRole.Primary ? "settings.adapter.selector.title" : "settings.adapter.selector.secondary.title";

    /// <summary>役割の記号（Segoe Fluent Icons）を返します。</summary>
    /// <remarks>責務: 役割を記号の文字へ変換します。</remarks>
    /// <param name="role">役割。</param>
    /// <returns>記号の文字。</returns>
    public static string Glyph(this AdapterConnectionRole role) =>
        role == AdapterConnectionRole.Primary ? "" : "";
}
