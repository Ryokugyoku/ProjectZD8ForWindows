using System.Net;

namespace ProjectZD8.Data.Network.CloudKit;

/// <summary>
/// CloudKit Web Services がエラーを返したときの例外です。
/// </summary>
public class CloudKitException : Exception
{
    /// <summary>
    /// HTTP の状態とサーバーのエラーコードから作ります。
    /// </summary>
    /// <remarks>責務: 1 件の CloudKit のエラーを、呼び出し側が判定できる形にします。</remarks>
    /// <param name="statusCode">HTTP の状態（レコード単位のエラーなら 200 のこともあります）。</param>
    /// <param name="serverErrorCode">サーバーのエラーコード（<c>NOT_FOUND</c>、<c>CONFLICT</c> など）。</param>
    /// <param name="reason">サーバーが返した理由。</param>
    public CloudKitException(HttpStatusCode statusCode, string? serverErrorCode, string? reason)
        : base($"CloudKit のエラー: {(int)statusCode} {serverErrorCode} {reason}")
    {
        StatusCode = statusCode;
        ServerErrorCode = serverErrorCode;
        Reason = reason;
    }

    /// <summary>HTTP の状態。</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>サーバーのエラーコード。</summary>
    public string? ServerErrorCode { get; }

    /// <summary>サーバーが返した理由。</summary>
    public string? Reason { get; }

    /// <summary>レコードがない（iOS の <c>CKError.unknownItem</c>）かどうか。</summary>
    public bool IsNotFound => string.Equals(ServerErrorCode, "NOT_FOUND", StringComparison.Ordinal);
}

/// <summary>
/// CloudKit のサインイン（ckWebAuthToken）が必要なときの例外です（HTTP 421 <c>AUTHENTICATION_REQUIRED</c>）。
/// </summary>
public sealed class CloudKitAuthenticationRequiredException : CloudKitException
{
    /// <summary>
    /// サインイン用のページの URL から作ります。
    /// </summary>
    /// <remarks>責務: サインインをやり直すための URL を呼び出し側へ渡します。</remarks>
    /// <param name="redirectUrl">応答の <c>redirectURL</c>（ブラウザーで開くとサインイン画面になります）。</param>
    /// <param name="reason">サーバーが返した理由。</param>
    public CloudKitAuthenticationRequiredException(Uri? redirectUrl, string? reason)
        : base((HttpStatusCode)421, "AUTHENTICATION_REQUIRED", reason)
    {
        RedirectUrl = redirectUrl;
    }

    /// <summary>ブラウザーで開くサインインの URL。</summary>
    public Uri? RedirectUrl { get; }
}

/// <summary>
/// CloudKit を使えない（設定がない、またはサインインしていない）ときの例外です。
/// </summary>
public sealed class CloudKitUnavailableException : Exception
{
    /// <summary>
    /// 使えない理由から作ります。
    /// </summary>
    /// <remarks>責務: CloudKit を使えない状態を呼び出し側へ伝えます。</remarks>
    /// <param name="availability">現在の状態。</param>
    public CloudKitUnavailableException(CloudKitAvailability availability)
        : base($"CloudKit を使えません: {availability}")
    {
        Availability = availability;
    }

    /// <summary>現在の状態。</summary>
    public CloudKitAvailability Availability { get; }
}
