using System.Net;
using System.Text;

namespace ProjectZD8.Data.Tests.TestSupport;

/// <summary>
/// 実際の Apple / CloudKit に接続せず、決めた応答を返す HTTP ハンドラーです。送られた要求を記録します。
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    /// <summary>要求から応答を作る処理です。</summary>
    private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;

    /// <summary>
    /// 応答を作る処理を指定して作ります。
    /// </summary>
    /// <remarks>責務: テストの筋書きに沿った応答を返せるようにします。</remarks>
    /// <param name="respond">要求と本文（文字列）から応答を作る処理。</param>
    public FakeHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        _respond = respond;
    }

    /// <summary>送られた要求（URL、メソッド、本文）です。</summary>
    public List<(HttpMethod Method, Uri Uri, string? Body, byte[]? Bytes)> Requests { get; } = [];

    /// <summary>
    /// JSON の応答を作ります。
    /// </summary>
    /// <remarks>責務: 状態と JSON の本文から応答を作ります。</remarks>
    /// <param name="status">HTTP の状態。</param>
    /// <param name="json">本文。</param>
    /// <returns>応答。</returns>
    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// 要求を記録して応答を返します。
    /// </summary>
    /// <remarks>責務: 1 件の要求を記録し、筋書きの応答を返します。</remarks>
    /// <param name="request">要求。</param>
    /// <param name="cancellationToken">取り消しの合図。</param>
    /// <returns>応答。</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? bytes = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var body = bytes is null ? null : Encoding.UTF8.GetString(bytes);
        Requests.Add((request.Method, request.RequestUri!, body, bytes));
        return _respond(request, body);
    }
}
