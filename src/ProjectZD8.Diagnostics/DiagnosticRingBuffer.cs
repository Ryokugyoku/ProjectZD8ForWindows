namespace ProjectZD8.Diagnostics;

/// <summary>
/// 直近のイベントを決まった件数だけ持つリングバッファです（Stable の「フライトレコーダー」）。
/// </summary>
/// <remarks>
/// 各イベントに「ファイルへ書いたか」を持たせ、Error のときの吐き出しでまだ書いていない行だけを返します（同じ行を二重に書かない）。
/// スレッド安全ではありません。出力先のロックの中で使います。
/// </remarks>
internal sealed class DiagnosticRingBuffer
{
    /// <summary>イベントと、ファイルへ書いたかです。</summary>
    private readonly (DiagnosticEvent Event, bool Written)[] _slots;

    /// <summary>次に入れる位置です。</summary>
    private int _next;

    /// <summary>入っている件数です。</summary>
    private int _count;

    /// <summary>
    /// 作ります。
    /// </summary>
    /// <param name="capacity">件数。</param>
    public DiagnosticRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _slots = new (DiagnosticEvent, bool)[capacity];
    }

    /// <summary>件数の上限です。</summary>
    public int Capacity => _slots.Length;

    /// <summary>入っている件数です。</summary>
    public int Count => _count;

    /// <summary>
    /// イベントを入れます。いっぱいなら最も古いものを上書きします。
    /// </summary>
    /// <param name="diagnosticEvent">イベント。</param>
    /// <param name="written">ファイルへ書く（書いた）なら true。</param>
    public void Add(DiagnosticEvent diagnosticEvent, bool written)
    {
        _slots[_next] = (diagnosticEvent, written);
        _next = (_next + 1) % _slots.Length;
        if (_count < _slots.Length)
        {
            _count++;
        }
    }

    /// <summary>
    /// まだファイルへ書いていないイベントを古い順に返し、書いたことにします。
    /// </summary>
    /// <returns>まだ書いていないイベント。</returns>
    public List<DiagnosticEvent> TakeUnwritten()
    {
        var result = new List<DiagnosticEvent>();
        var start = (_next - _count + _slots.Length) % _slots.Length;
        for (var offset = 0; offset < _count; offset++)
        {
            var index = (start + offset) % _slots.Length;
            if (!_slots[index].Written)
            {
                result.Add(_slots[index].Event);
                _slots[index].Written = true;
            }
        }

        return result;
    }

    /// <summary>
    /// 入っているイベントを全部、古い順に返します（クラッシュの記録用。書いたかは変えない）。
    /// </summary>
    /// <returns>イベント。</returns>
    public List<DiagnosticEvent> Snapshot()
    {
        var result = new List<DiagnosticEvent>(_count);
        var start = (_next - _count + _slots.Length) % _slots.Length;
        for (var offset = 0; offset < _count; offset++)
        {
            result.Add(_slots[(start + offset) % _slots.Length].Event);
        }

        return result;
    }
}
