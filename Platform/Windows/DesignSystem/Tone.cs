namespace ProjectZD8ForWindows.Platform.Windows.DesignSystem;

/// <summary>
/// 色の役割です。シアン（Accent）が主、オレンジ（Caution）は小さな強調だけに使います。
/// </summary>
public enum Tone
{
    /// <summary>主のアクセント（シアン）。選択・主の操作・分析の深さ。</summary>
    Accent,

    /// <summary>注意（オレンジ）。中断・確認が必要なもの。</summary>
    Caution,

    /// <summary>成功（緑）。アプリの状態が裏付ける「記録中」「完了」だけ。</summary>
    Success,

    /// <summary>危険（赤）。削除・失敗。</summary>
    Danger,

    /// <summary>解析（藍）。保存済みログの分析。</summary>
    Indigo,

    /// <summary>落ち着いた色（強調しない）。</summary>
    Neutral,
}
