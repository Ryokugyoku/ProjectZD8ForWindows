namespace ProjectZD8.Domain.ValueObjects;

/// <summary>
/// OBDのServiceとPIDを組にした読取要求識別子です。
/// </summary>
/// <remarks>
/// ServiceとPIDを複合識別子へ固定します。
/// 責務: 1件のOBD読取対象をService/PIDの組として保持します。
/// </remarks>
/// <param name="Service">OBD Service番号です。</param>
/// <param name="Pid">Service内PID番号です。</param>
public readonly record struct OBDPIDRequest(byte Service, byte Pid);
