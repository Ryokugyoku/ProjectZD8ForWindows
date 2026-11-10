// iOS 版の Localizable.xcstrings を、WinUI の .resw（日本語・英語・スペイン語）に変換します。
//
// 使い方（リポジトリのルートで）:
//   dotnet run scripts/ConvertXcstrings.cs -- ..\ProjectZD8\ProjectZD8\Resources\Localization\Localizable.xcstrings Strings
//
// - .resw の Name は Platform/Windows/Localization/LocalizedText.cs の ResourceName と同じ規則で作る（下の ResourceName）。
// - ある言語の訳がなければ、元の言語（xcstrings の sourceLanguage）の値、それもなければキーそのものを使う。
// - 値の printf 形式（%@、%lld、%1$@）はそのまま残す。アプリ側の LocalizedText.Format が埋め込む。
// - 変換後の Name がぶつかったら、エラーで止まる。
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dotnet run --file scripts/ConvertXcstrings.cs -- <Localizable.xcstrings> <出力フォルダ> [追加の xcstrings ...]");
    return 2;
}

var inputPath = args[0];
var outputRoot = args[1];

// xcstrings の言語コード → Strings/ の下のフォルダ名（WinUI の言語タグ）
var languages = new (string Source, string Folder)[]
{
    ("ja", "ja-JP"),
    ("en", "en-US"),
    ("es", "es-ES"),
};

var inputPaths = new[] { inputPath }.Concat(args.Skip(2)).ToArray();
string? sourceLanguage = null;
var names = new Dictionary<string, string>(StringComparer.Ordinal);
var entries = new List<(string Name, string Key, Dictionary<string, string> Values)>();
foreach (var path in inputPaths)
{
    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
    var root = document.RootElement;
    sourceLanguage ??= root.GetProperty("sourceLanguage").GetString() ?? "ja";
    var strings = root.GetProperty("strings");
    foreach (var item in strings.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
    {
        var key = item.Name;
        if (key.Length == 0)
        {
            continue;
        }

        var name = ResourceName(key);
        if (names.TryGetValue(name, out var other))
        {
            Console.Error.WriteLine($"Name が衝突しました: \"{key}\" と \"{other}\" → {name}（{path}）");
            return 1;
        }

        names[name] = key;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.Value.TryGetProperty("localizations", out var localizations))
        {
            foreach (var localization in localizations.EnumerateObject())
            {
                if (localization.Value.TryGetProperty("stringUnit", out var unit)
                    && unit.TryGetProperty("value", out var value)
                    && value.GetString() is { } text)
                {
                    values[localization.Name] = text;
                }
            }
        }

        entries.Add((name, key, values));
    }
}

sourceLanguage ??= "ja";

foreach (var (source, folder) in languages)
{
    var directory = Path.Combine(outputRoot, folder);
    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "Resources.resw");
    var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
    using var writer = XmlWriter.Create(path, settings);
    writer.WriteStartDocument();
    writer.WriteComment(" scripts/ConvertXcstrings.cs が iOS 版の Localizable.xcstrings から生成。手で編集しない。 ");
    writer.WriteStartElement("root");
    WriteHeader(writer);
    var translated = 0;
    foreach (var (name, key, values) in entries)
    {
        string value;
        if (values.TryGetValue(source, out var exact))
        {
            value = exact;
            translated++;
        }
        else if (values.TryGetValue(sourceLanguage, out var fallback))
        {
            value = fallback;
        }
        else
        {
            value = key;
        }

        writer.WriteStartElement("data");
        writer.WriteAttributeString("name", name);
        writer.WriteAttributeString("xml", "space", null, "preserve");
        writer.WriteElementString("value", value);
        writer.WriteElementString("comment", key);
        writer.WriteEndElement();
    }

    writer.WriteEndElement();
    writer.WriteEndDocument();
    Console.WriteLine($"{folder}: {entries.Count} 件（訳あり {translated} 件） → {path}");
}

return 0;

// LocalizedText.ResourceName と同じ規則。片方を変えたら、もう片方も変える。
static string ResourceName(string key)
{
    var builder = new StringBuilder(key.Length + 12);
    var needsHash = key.Length == 0;
    foreach (var character in key)
    {
        if (char.IsAsciiLetterOrDigit(character))
        {
            builder.Append(character);
        }
        else
        {
            builder.Append('_');
            if (character is not ('.' or '_' or '-'))
            {
                needsHash = true;
            }
        }
    }

    if (needsHash)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        builder.Append("_x").Append(Convert.ToHexStringLower(hash, 0, 4));
    }

    if (builder.Length == 0 || char.IsAsciiDigit(builder[0]) || builder[0] == '_')
    {
        builder.Insert(0, "k_");
    }

    return builder.ToString();
}

// .resw の決まったヘッダー（resheader）を書く。
static void WriteHeader(XmlWriter writer)
{
    void Header(string name, string value)
    {
        writer.WriteStartElement("resheader");
        writer.WriteAttributeString("name", name);
        writer.WriteElementString("value", value);
        writer.WriteEndElement();
    }

    Header("resmimetype", "text/microsoft-resx");
    Header("version", "2.0");
    Header("reader", "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
    Header("writer", "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
}
