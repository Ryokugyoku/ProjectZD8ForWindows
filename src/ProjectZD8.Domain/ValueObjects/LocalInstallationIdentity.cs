namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// CloudKit受領証へ使用する現在アプリインストールの識別情報です。
/// </summary>
public sealed record LocalInstallationIdentity
{
    /// <summary>
    /// 再起動後も維持する不透明なインストール識別子です。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// ユーザーが取込先を判別する端末表示名です。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 不透明識別子と端末表示名を固定して生成します。
    /// </summary>
    /// <remarks>責務: 1件のアプリインストールを受領証向け識別情報へまとめます。</remarks>
    /// <param name="id">再起動後も維持する不透明識別子。</param>
    /// <param name="displayName">ユーザー向け端末表示名。</param>
    public LocalInstallationIdentity(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}
