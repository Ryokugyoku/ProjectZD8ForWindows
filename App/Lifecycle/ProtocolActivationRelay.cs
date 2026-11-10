using System;
using System.Collections.Generic;

namespace ProjectZD8ForWindows.AppRoot.Lifecycle;

/// <summary>
/// <c>revtorque-insight://</c> のプロトコル起動で届いた URI を、受け取り先へ渡す中継です。
/// </summary>
/// <remarks>
/// 受け取り先（リードがつなぐ <c>AuthorizationCallbackBroker.TryDeliver</c>）が決まる前に届いた URI は、ためておいて後で渡します。
/// UI スレッドから使います。
/// </remarks>
public sealed class ProtocolActivationRelay
{
    /// <summary>受け取り先が決まる前に届いた URI です。</summary>
    private readonly Queue<Uri> _pending = new();

    /// <summary>受け取り先です。</summary>
    private Action<Uri>? _receiver;

    /// <summary>URI の受け取り先です。設定すると、ためていた URI を順に渡します。</summary>
    public Action<Uri>? Receiver
    {
        get => _receiver;
        set
        {
            _receiver = value;
            while (_receiver is not null && _pending.Count > 0)
            {
                _receiver(_pending.Dequeue());
            }
        }
    }

    /// <summary>
    /// 届いた URI を受け取り先へ渡します。
    /// </summary>
    /// <remarks>責務: アプリのスキームの URI だけを受け付け、受け取り先がなければためておきます。</remarks>
    /// <param name="uri">届いた URI。</param>
    public void Deliver(Uri uri)
    {
        if (!string.Equals(uri.Scheme, "revtorque-insight", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_receiver is { } receiver)
        {
            receiver(uri);
        }
        else
        {
            _pending.Enqueue(uri);
        }
    }
}
