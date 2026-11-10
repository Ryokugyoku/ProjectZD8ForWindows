using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ProjectZD8.Data.Authentication;

namespace ProjectZD8.Data.Windows.Authentication;

/// <summary>
/// DPAPI（<c>CryptProtectData</c>、現在のユーザーの鍵）で暗号化したファイルに秘密を保存します。資格情報マネージャーを使えないときの代わりです。
/// </summary>
/// <remarks>項目ごとに、項目名の SHA-256 をファイル名にした <c>.bin</c> を、渡されたフォルダーに置きます。</remarks>
public sealed class DpapiFileSecretStore : ISecretStore
{
    /// <summary>このアプリ用の追加のエントロピーです（同じユーザーのほかのアプリと区別するため）。</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Ryokugyoku.ProjectZD8.secrets.v1");

    /// <summary>保存先のフォルダーです。</summary>
    private readonly string _directory;

    /// <summary>
    /// 保存先のフォルダーを指定して作ります。
    /// </summary>
    /// <remarks>責務: 秘密のファイルの置き場を固定します。フォルダーは組み立てが渡します。</remarks>
    /// <param name="directory">保存先のフォルダー（例：LocalFolder の <c>secrets</c>）。</param>
    public DpapiFileSecretStore(string directory)
    {
        _directory = directory;
    }

    /// <inheritdoc />
    public string? Read(string name)
    {
        var path = PathFor(name);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            return Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            throw new SecretStoreException("秘密のファイルを読めません。", exception);
        }
    }

    /// <inheritdoc />
    public void Write(string name, string value)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(name);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, Protect(Encoding.UTF8.GetBytes(value)));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            throw new SecretStoreException("秘密のファイルに保存できません。", exception);
        }
    }

    /// <inheritdoc />
    public void Delete(string name)
    {
        try
        {
            File.Delete(PathFor(name));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SecretStoreException("秘密のファイルを消せません。", exception);
        }
    }

    /// <summary>
    /// 項目名からファイルのパスを作ります。
    /// </summary>
    /// <remarks>責務: 項目名を、ファイル名に使える不可逆な名前へ変換します。</remarks>
    /// <param name="name">項目名。</param>
    /// <returns>ファイルのパス。</returns>
    private string PathFor(string name) =>
        Path.Combine(_directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name))) + ".bin");

    /// <summary>
    /// 現在のユーザーの鍵で暗号化します。
    /// </summary>
    /// <remarks>責務: 1 件のバイト列を DPAPI で保護します。</remarks>
    /// <param name="plain">平文。</param>
    /// <returns>暗号文。</returns>
    private static byte[] Protect(byte[] plain) => Transform(plain, protect: true);

    /// <summary>
    /// 現在のユーザーの鍵で復号します。
    /// </summary>
    /// <remarks>責務: 1 件の DPAPI の暗号文を平文へ戻します。</remarks>
    /// <param name="cipher">暗号文。</param>
    /// <returns>平文。</returns>
    private static byte[] Unprotect(byte[] cipher) => Transform(cipher, protect: false);

    /// <summary>
    /// DPAPI で暗号化または復号します。
    /// </summary>
    /// <remarks>責務: アンマネージドのメモリを確保・解放して <c>CryptProtectData</c> / <c>CryptUnprotectData</c> を呼びます。</remarks>
    /// <param name="input">入力。</param>
    /// <param name="protect">暗号化なら <c>true</c>。</param>
    /// <returns>出力。</returns>
    /// <exception cref="Win32Exception">DPAPI が失敗した場合。</exception>
    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputBlob = new NativeMethods.DataBlob();
        var entropyBlob = new NativeMethods.DataBlob();
        var outputBlob = new NativeMethods.DataBlob();
        try
        {
            inputBlob = NativeMethods.DataBlob.Allocate(input);
            entropyBlob = NativeMethods.DataBlob.Allocate(Entropy);
            var ok = protect
                ? NativeMethods.CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, NativeMethods.UiForbidden, ref outputBlob)
                : NativeMethods.CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, NativeMethods.UiForbidden, ref outputBlob);
            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
            var output = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            inputBlob.FreeHGlobal();
            entropyBlob.FreeHGlobal();
            if (outputBlob.Data != IntPtr.Zero)
            {
                NativeMethods.LocalFree(outputBlob.Data);
            }
        }
    }

    /// <summary>
    /// DPAPI の P/Invoke の宣言です。
    /// </summary>
    private static class NativeMethods
    {
        /// <summary>UI を出さない指定（CRYPTPROTECT_UI_FORBIDDEN）です。</summary>
        public const int UiForbidden = 0x1;

        /// <summary>
        /// データを暗号化します。
        /// </summary>
        /// <remarks>責務: crypt32 の CryptProtectData を呼びます。</remarks>
        /// <param name="dataIn">入力。</param>
        /// <param name="description">説明。</param>
        /// <param name="entropy">追加のエントロピー。</param>
        /// <param name="reserved">予約。</param>
        /// <param name="prompt">プロンプト。</param>
        /// <param name="flags">フラグ。</param>
        /// <param name="dataOut">出力。</param>
        /// <returns>成功なら <c>true</c>。</returns>
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? description,
            ref DataBlob entropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            ref DataBlob dataOut);

        /// <summary>
        /// データを復号します。
        /// </summary>
        /// <remarks>責務: crypt32 の CryptUnprotectData を呼びます。</remarks>
        /// <param name="dataIn">入力。</param>
        /// <param name="description">説明（受け取らない）。</param>
        /// <param name="entropy">追加のエントロピー。</param>
        /// <param name="reserved">予約。</param>
        /// <param name="prompt">プロンプト。</param>
        /// <param name="flags">フラグ。</param>
        /// <param name="dataOut">出力。</param>
        /// <returns>成功なら <c>true</c>。</returns>
        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            ref DataBlob entropy,
            IntPtr reserved,
            IntPtr prompt,
            int flags,
            ref DataBlob dataOut);

        /// <summary>
        /// DPAPI が確保したメモリを解放します。
        /// </summary>
        /// <remarks>責務: kernel32 の LocalFree を呼びます。</remarks>
        /// <param name="handle">解放するメモリ。</param>
        /// <returns>成功なら <see cref="IntPtr.Zero"/>。</returns>
        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr handle);

        /// <summary>
        /// DPAPI のバイト列（DATA_BLOB）です。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DataBlob
        {
            /// <summary>バイト数。</summary>
            public int Length;

            /// <summary>先頭の位置。</summary>
            public IntPtr Data;

            /// <summary>
            /// バイト列をアンマネージドのメモリへ写して作ります。
            /// </summary>
            /// <remarks>責務: 1 件のバイト列を DATA_BLOB にします。</remarks>
            /// <param name="bytes">バイト列。</param>
            /// <returns>DATA_BLOB。</returns>
            public static DataBlob Allocate(byte[] bytes)
            {
                var pointer = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                return new DataBlob { Length = bytes.Length, Data = pointer };
            }

            /// <summary>
            /// <see cref="Allocate"/> で確保したメモリを解放します。
            /// </summary>
            /// <remarks>責務: アンマネージドのメモリを返します。</remarks>
            public readonly void FreeHGlobal()
            {
                if (Data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(Data);
                }
            }
        }
    }
}
