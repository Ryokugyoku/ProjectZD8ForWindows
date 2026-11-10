using System.Text;
using ProjectZD8.Application.VehicleManagement.Ports;
using ProjectZD8.Data.Devices.OBD;

namespace ProjectZD8.Data.Tests.Devices.OBD;

/// <summary>ELMコマンド直列化境界の応答保持と境界喪失を検証します。</summary>
public sealed class SerializedELMCommandChannelTests
{
    /// <summary>プロンプトだけを除いて加工前応答を返します。</summary>
    /// <remarks>責務: 許可済みコマンドが1回送信され応答本文が保持されることを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ExecuteWritesCommandAndReturnsResponseWithoutPrompt()
    {
        var transport = new SerializedChannelTransportFake(() => Task.FromResult(Encoding.UTF8.GetBytes("OK\r>")));
        var channel = new SerializedELMCommandChannel(transport);

        var response = await channel.Execute(ELM327Command.EchoOff);

        Assert.Equal("OK\r", response);
        Assert.Equal(["ATE0\r"], transport.Commands);
    }

    /// <summary>応答境界を失った接続を暗黙再利用しません。</summary>
    /// <remarks>責務: 最初のread失敗後は後続コマンドをTransportへ書き込まないことを確認します。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ExecuteRejectsSubsequentCommandAfterReadFailure()
    {
        var transport = new SerializedChannelTransportFake(() =>
            Task.FromException<byte[]>(new VehicleIdentificationException(new VehicleIdentificationError.ResponseTimedOut())));
        var channel = new SerializedELMCommandChannel(transport);

        var first = await Assert.ThrowsAsync<VehicleIdentificationException>(() => channel.Execute(ELM327Command.EchoOff));
        Assert.IsType<VehicleIdentificationError.ResponseTimedOut>(first.Reason);

        var second = await Assert.ThrowsAsync<VehicleIdentificationException>(() => channel.Execute(ELM327Command.LinefeedsOff));
        Assert.IsType<VehicleIdentificationError.ConnectionFailed>(second.Reason);

        Assert.Equal(["ATE0\r"], transport.Commands);
    }

    /// <summary>UTF-8として読めない応答を形式不正にします。</summary>
    /// <remarks>責務: 不正なバイト列を置換文字で成功扱いせず <c>MalformedResponse</c> にすることを確認します（Swift の <c>String(data:encoding:)</c> が nil になる場合と同じ）。</remarks>
    /// <returns>テストの完了。</returns>
    [Fact]
    public async Task ExecuteRejectsInvalidUTF8Response()
    {
        var transport = new SerializedChannelTransportFake(() => Task.FromResult(new byte[] { 0xFF, 0x3E }));
        var channel = new SerializedELMCommandChannel(transport);

        var error = await Assert.ThrowsAsync<VehicleIdentificationException>(() => channel.Execute(ELM327Command.EchoOff));

        Assert.IsType<VehicleIdentificationError.MalformedResponse>(error.Reason);
    }
}

/// <summary>直列チャネルへ固定応答または固定エラーを返すTransportです。</summary>
file sealed class SerializedChannelTransportFake : IOBDCommandTransport
{
    /// <summary>read時に返す固定結果です。</summary>
    private readonly Func<Task<byte[]>> result;

    /// <summary>read結果を固定して生成します。</summary>
    /// <remarks>責務: 1件の送受信シナリオへread結果を固定します。</remarks>
    /// <param name="result">read時に返すデータまたはエラー。</param>
    public SerializedChannelTransportFake(Func<Task<byte[]>> result)
    {
        this.result = result;
    }

    /// <summary>書込み済みASCIIコマンドです。</summary>
    public List<string> Commands { get; } = [];

    /// <summary>Fakeでは追加資源を確保せずopenを完了します。</summary>
    /// <remarks>責務: 直列チャネルテスト用Transportを利用可能状態として扱います。</remarks>
    /// <returns>完了済みのタスク。</returns>
    public Task Open() => Task.CompletedTask;

    /// <summary>ASCIIコマンドを履歴へ記録します。</summary>
    /// <remarks>責務: 1件の書込みデータを検証可能な文字列として保存します。</remarks>
    /// <param name="data">送信されたASCIIデータ。</param>
    /// <returns>完了済みのタスク。</returns>
    public Task Write(byte[] data)
    {
        Commands.Add(Encoding.UTF8.GetString(data));
        return Task.CompletedTask;
    }

    /// <summary>固定したread結果を返します。</summary>
    /// <remarks>責務: 現在シナリオの応答または失敗を1件返します。</remarks>
    /// <returns>プロンプトを含む加工前応答。</returns>
    public Task<byte[]> ReadUntilPrompt() => result();

    /// <summary>Fakeでは資源を持たずcloseを完了します。</summary>
    /// <remarks>責務: 直列チャネルテスト用Transportを終了済みとして扱います。</remarks>
    /// <returns>完了済みのタスク。</returns>
    public Task Close() => Task.CompletedTask;
}
