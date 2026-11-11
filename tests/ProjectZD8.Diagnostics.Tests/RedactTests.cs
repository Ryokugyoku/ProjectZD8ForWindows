namespace ProjectZD8.Diagnostics.Tests;

/// <summary>個人を特定する値を伏せる処理（docs/LOGGING_STRATEGY.md の 4）を確かめます。</summary>
public sealed class RedactTests
{
    /// <summary>VIN は末尾 4 桁だけ。</summary>
    [Fact]
    public void Vin_KeepsLastFourCharacters()
    {
        Assert.Equal("…1234", Redact.Vin("JN1AZ4EH8DM881234"));
        Assert.Equal("…12", Redact.Vin("12"));
        Assert.Equal(string.Empty, Redact.Vin(null));
        Assert.Equal(string.Empty, Redact.Vin("  "));
    }

    /// <summary>アカウント ID は SHA-256 の先頭 8 桁（同じ ID なら同じ値、元の値は残らない）。</summary>
    [Fact]
    public void AccountId_IsFirstEightHexOfSha256()
    {
        // SHA-256("abc") = ba7816bf8f01cfea...
        Assert.Equal("ba7816bf", Redact.AccountId("abc"));
        var subject = "001234.abcdef0123456789.0123";
        Assert.Equal(Redact.AccountId(subject), Redact.AccountId(subject));
        Assert.DoesNotContain("abcdef", Redact.AccountId(subject), StringComparison.Ordinal);
        Assert.Equal(8, Redact.AccountId(subject).Length);
        Assert.Equal(string.Empty, Redact.AccountId(null));
    }

    /// <summary>Bluetooth のアドレスは末尾 2 バイトだけ（数値・文字列・機器 ID の中）。</summary>
    [Fact]
    public void BluetoothAddress_KeepsLastTwoBytes()
    {
        Assert.Equal("…:EE:FF", Redact.BluetoothAddress(0xAABBCCDDEEFFUL));
        Assert.Equal("…:EE:FF", Redact.BluetoothAddress("aa:bb:cc:dd:ee:ff"));
        Assert.Equal("…:0A:1B", Redact.BluetoothAddress("00-11-22-33-0A-1B"));
        Assert.Equal(
            "BluetoothLE#BluetoothLE…:9A:BC-…:EE:FF",
            Redact.BluetoothAddress("BluetoothLE#BluetoothLEb8:31:b5:12:9a:bc-aa:bb:cc:dd:ee:ff"));
        Assert.Equal("OBDLink EX", Redact.BluetoothAddress("OBDLink EX"));
    }

    /// <summary>シリアル番号は末尾 2 バイト（4 文字）だけ。</summary>
    [Fact]
    public void SerialNumber_KeepsLastFourCharacters() =>
        Assert.Equal("…A1B2", Redact.SerialNumber("STN2255-00A1B2"));

    /// <summary>URL はクエリ・フラグメント・ユーザー情報を落とす。</summary>
    [Fact]
    public void Url_DropsQueryFragmentAndUserInfo()
    {
        Assert.Equal(
            "https://api.apple-cloudkit.com/database/1/iCloud.x/production/private/records/query",
            Redact.Url("https://api.apple-cloudkit.com/database/1/iCloud.x/production/private/records/query?ckAPIToken=SECRET&ckWebAuthToken=SECRET"));
        Assert.Equal("revtorque-insight://auth/apple", Redact.Url(new Uri("revtorque-insight://auth/apple?code=SECRET#state=SECRET")));
        Assert.Equal("https://example.com/a", Redact.Url("https://user:password@example.com/a?x=1"));
        Assert.Equal("relative/path", Redact.Url("relative/path?token=SECRET"));
        Assert.Equal(string.Empty, Redact.Url((string?)null));
    }

    /// <summary>パスはデータのフォルダーからの相対。外ならファイル名だけ。</summary>
    [Fact]
    public void Path_IsRelativeToDataDirectory()
    {
        var data = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "user-name", "LocalState");
        var inside = System.IO.Path.Combine(data, "logs", "session", "raw.bin");
        var outside = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "user-name", "Pictures", "car.jpg");

        Assert.Equal(System.IO.Path.Combine("logs", "session", "raw.bin"), Redact.Path(inside, data));
        Assert.Equal("…" + System.IO.Path.DirectorySeparatorChar + "car.jpg", Redact.Path(outside, data));
        Assert.DoesNotContain("user-name", Redact.Path(outside, data), StringComparison.Ordinal);
        Assert.Equal(System.IO.Path.Combine("logs", "a.bin"), Redact.Path(System.IO.Path.Combine("logs", "a.bin"), data));
        Assert.Equal(string.Empty, Redact.Path(null, data));
    }
}
