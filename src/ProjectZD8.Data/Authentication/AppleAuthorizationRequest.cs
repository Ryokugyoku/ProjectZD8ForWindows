using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace ProjectZD8.Data.Authentication;

/// <summary>
/// Sign in with Apple（Web）の認可要求の URL です（<c>https://appleid.apple.com/auth/authorize</c>）。
/// </summary>
/// <remarks>
/// <para>iOS 版と同じくスコープ（名前・メール）を要求しないので、Apple の規則により <c>response_mode=fragment</c> を使えます
/// （スコープを要求すると <c>form_post</c> が必須になり、静的なページでは受けられません）。</para>
/// <para>response_type は <c>code id_token</c>。この実装は id_token だけを使い、code は使いません（client secret を持つサーバーがないため）。</para>
/// </remarks>
/// <param name="Uri">ブラウザーで開く URL。</param>
/// <param name="State">要求と戻りを結び付ける推測できない値。</param>
/// <param name="Nonce">ID トークンに入る推測できない値。</param>
public sealed record AppleAuthorizationRequest(Uri Uri, string State, string Nonce)
{
    /// <summary>Apple の認可の URL です。</summary>
    public static readonly Uri AuthorizeEndpoint = new("https://appleid.apple.com/auth/authorize");

    /// <summary>
    /// 新しい state と nonce で要求を作ります。
    /// </summary>
    /// <remarks>責務: 1 回のサインイン用に、推測できない state と nonce を付けた URL を作ります。</remarks>
    /// <param name="servicesId">Services ID（client_id）。</param>
    /// <param name="redirectUri">Services ID に登録した https のページ。</param>
    /// <param name="randomToken">推測できない値を作る処理。<c>null</c> なら 32 バイトの乱数の base64url。</param>
    /// <returns>認可要求。</returns>
    public static AppleAuthorizationRequest Create(string servicesId, Uri redirectUri, Func<string>? randomToken = null)
    {
        var random = randomToken ?? NewRandomToken;
        var state = random();
        var nonce = random();
        var query = new StringBuilder();
        Append(query, "client_id", servicesId);
        Append(query, "redirect_uri", redirectUri.AbsoluteUri);
        Append(query, "response_type", "code id_token");
        Append(query, "response_mode", "fragment");
        Append(query, "state", state);
        Append(query, "nonce", nonce);
        return new AppleAuthorizationRequest(new Uri($"{AuthorizeEndpoint.AbsoluteUri}?{query}"), state, nonce);
    }

    /// <summary>
    /// 32 バイトの乱数を base64url にします。
    /// </summary>
    /// <remarks>責務: 推測できない state と nonce を 1 つ作ります。</remarks>
    /// <returns>乱数の文字列。</returns>
    public static string NewRandomToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// クエリーにパラメーターを加えます。
    /// </summary>
    /// <remarks>責務: 名前と値を URL エンコードして <c>&amp;</c> でつなぎます（空白は <c>%20</c>）。</remarks>
    /// <param name="query">クエリー。</param>
    /// <param name="name">名前。</param>
    /// <param name="value">値。</param>
    private static void Append(StringBuilder query, string name, string value)
    {
        if (query.Length > 0)
        {
            query.Append('&');
        }
        query.Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
    }
}
