using System;
using Microsoft.UI.Xaml.Controls;

namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>右クリック（とキーボードのメニューキー・Shift+F10）で開くメニューを作ります。</summary>
public sealed class ContextMenu
{
    /// <summary>作っているメニューです。</summary>
    private readonly MenuFlyout _flyout = new();

    /// <summary>項目を足します。</summary>
    /// <remarks>責務: 記号つきの項目を足し、選んだら <paramref name="invoke"/> を呼びます。</remarks>
    /// <param name="glyph">記号。</param>
    /// <param name="text">文字。</param>
    /// <param name="invoke">選んだときの処理。</param>
    /// <param name="tone">記号の色。なければ既定の色。</param>
    /// <returns>このメニュー。</returns>
    public ContextMenu Item(string glyph, string text, Action invoke, Tone? tone = null)
    {
        var icon = new FontIcon { Glyph = glyph };
        if (tone is { } value)
        {
            icon.Foreground = DesignTokens.Foreground(value);
        }

        var item = new MenuFlyoutItem { Text = text, Icon = icon };
        item.Click += (_, _) => invoke();
        _flyout.Items.Add(item);
        return this;
    }

    /// <summary>区切り線を足します（前に項目があるときだけ）。</summary>
    /// <returns>このメニュー。</returns>
    public ContextMenu Separator()
    {
        if (_flyout.Items.Count > 0)
        {
            _flyout.Items.Add(new MenuFlyoutSeparator());
        }

        return this;
    }

    /// <summary>作ったメニューです。</summary>
    /// <returns>メニュー。</returns>
    public MenuFlyout Build() => _flyout;
}
