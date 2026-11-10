using System;

namespace ProjectZD8ForWindows.Platform.Windows.AppShell;

/// <summary>
/// 画面の中に階層（一覧 → 詳細 → 分析など）を持ち、シェルの「戻る」で 1 段戻れる画面です。
/// </summary>
/// <remarks>シェルは、ナビゲーションの戻るボタン・Alt+← キー・マウスの戻るボタンを、この画面の <see cref="GoBack"/> へ渡します。</remarks>
public interface IBackNavigable
{
    /// <summary>1 段戻れるかどうかです。</summary>
    bool CanGoBack { get; }

    /// <summary>戻れるかどうかが変わったときに起きます。</summary>
    event EventHandler? CanGoBackChanged;

    /// <summary>1 段戻ります。</summary>
    void GoBack();
}
