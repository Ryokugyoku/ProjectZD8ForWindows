using ProjectZD8.Domain.ValueObjects;

namespace ProjectZD8ForWindows.Platform.Windows.Features.DeviceConnection.Presentation;

/// <summary>通信方式の表示用のキーと記号です（iOS 版の <c>AdapterTransportMode</c> の private 拡張）。</summary>
public static class AdapterTransportModePresentation
{
    /// <summary>通信方式の名前のキーを返します。</summary>
    /// <remarks>責務: 通信方式を xcstrings のキーへ変換します。</remarks>
    /// <param name="mode">通信方式。</param>
    /// <returns>キー。</returns>
    public static string TitleKey(this AdapterTransportMode mode) =>
        mode == AdapterTransportMode.Usb ? "settings.adapter.transport.usb" : "settings.adapter.transport.bluetooth";

    /// <summary>通信方式の記号（Segoe Fluent Icons）を返します。</summary>
    /// <remarks>責務: 通信方式を記号の文字へ変換します。</remarks>
    /// <param name="mode">通信方式。</param>
    /// <returns>記号の文字。</returns>
    public static string Glyph(this AdapterTransportMode mode) =>
        mode == AdapterTransportMode.Usb ? "" : "";
}
