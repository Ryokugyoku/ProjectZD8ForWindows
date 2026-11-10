using System.Globalization;
using System.Text;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.Errors;

namespace ProjectZD8.Domain.Policies;

/// <summary>保存された制限付きPID数式を応答バイトへ適用します。</summary>
public sealed class OBDPIDFormulaEvaluator
{
    /// <summary>PID定義の数式を指定バイト列で評価します。</summary>
    /// <remarks>責務: 1件の検証対象PID式を副作用なく有限数値へ変換します。</remarks>
    /// <param name="definition">評価する変換式と必要バイト数を持つPID定義。</param>
    /// <param name="bytes">OBD応答からServiceとPIDを除いた未加工データバイト。</param>
    /// <returns>定義式を適用した有限数値。</returns>
    /// <exception cref="OBDPIDFormulaException">バイト不足、不正構文、未提供変数、ゼロ除算、非有限結果の場合。</exception>
    public double Evaluate(OBDPIDDefinition definition, IReadOnlyList<byte> bytes)
    {
        if (definition.RequiredByteCount is not { } requiredByteCount || definition.Formula is not { } formula)
        {
            throw FormulaErrors.InvalidExpression();
        }
        if (bytes.Count < requiredByteCount)
        {
            throw FormulaErrors.InsufficientBytes(requiredByteCount, bytes.Count);
        }
        var parser = new Parser(formula, bytes);
        var value = parser.Parse();
        if (!double.IsFinite(value))
        {
            throw FormulaErrors.NonFiniteResult();
        }
        return value;
    }
}

/// <summary>数式評価で投げる <see cref="OBDPIDFormulaException"/> を生成します。</summary>
file static class FormulaErrors
{
    /// <summary>不正構文の例外を生成します。</summary>
    /// <remarks>責務: <c>OBDPIDFormulaError.invalidExpression</c> に対応する例外を1か所で組み立てます。</remarks>
    /// <returns>不正構文を表す例外。</returns>
    public static OBDPIDFormulaException InvalidExpression() =>
        new(new OBDPIDFormulaError.InvalidExpression());

    /// <summary>バイト不足の例外を生成します。</summary>
    /// <remarks>責務: <c>OBDPIDFormulaError.insufficientBytes</c> に対応する例外を1か所で組み立てます。</remarks>
    /// <param name="required">定義が要求するバイト数。</param>
    /// <param name="actual">実際のバイト数。</param>
    /// <returns>バイト不足を表す例外。</returns>
    public static OBDPIDFormulaException InsufficientBytes(int required, int actual) =>
        new(new OBDPIDFormulaError.InsufficientBytes(required, actual));

    /// <summary>未提供変数の例外を生成します。</summary>
    /// <remarks>責務: <c>OBDPIDFormulaError.unavailableVariable</c> に対応する例外を1か所で組み立てます。</remarks>
    /// <param name="variable">応答バイトが存在しない変数文字。</param>
    /// <returns>未提供変数を表す例外。</returns>
    public static OBDPIDFormulaException UnavailableVariable(char variable) =>
        new(new OBDPIDFormulaError.UnavailableVariable(variable));

    /// <summary>ゼロ除算の例外を生成します。</summary>
    /// <remarks>責務: <c>OBDPIDFormulaError.divisionByZero</c> に対応する例外を1か所で組み立てます。</remarks>
    /// <returns>ゼロ除算を表す例外。</returns>
    public static OBDPIDFormulaException DivisionByZero() =>
        new(new OBDPIDFormulaError.DivisionByZero());

    /// <summary>非有限結果の例外を生成します。</summary>
    /// <remarks>責務: <c>OBDPIDFormulaError.nonFiniteResult</c> に対応する例外を1か所で組み立てます。</remarks>
    /// <returns>非有限結果を表す例外。</returns>
    public static OBDPIDFormulaException NonFiniteResult() =>
        new(new OBDPIDFormulaError.NonFiniteResult());
}

/// <summary>PID式の許可済みトークンだけを再帰下降で評価します。</summary>
/// <remarks>
/// Swift の <c>Character</c>（拡張書記素クラスタ）単位の走査を再現するため、
/// 文字列を <see cref="StringInfo"/> のテキスト要素へ分割して1要素を1文字として扱います。
/// </remarks>
file sealed class Parser
{
    /// <summary>評価対象の文字（書記素クラスタ）配列です。</summary>
    private readonly string[] _characters;

    /// <summary><c>A</c>から<c>H</c>へ対応する応答バイトです。</summary>
    private readonly IReadOnlyList<byte> _bytes;

    /// <summary>次に解析する文字位置です。</summary>
    private int _index;

    /// <summary>数式文字列と応答バイトを使ってパーサーを生成します。</summary>
    /// <remarks>責務: 1件の式解析に必要な入力と読取位置を初期化します。</remarks>
    /// <param name="expression">解析対象の制限付き数式。</param>
    /// <param name="bytes">変数へ割り当てる応答バイト。</param>
    public Parser(string expression, IReadOnlyList<byte> bytes)
    {
        _characters = SplitCharacters(expression);
        _bytes = bytes;
    }

    /// <summary>式全体を解析して末尾まで消費した値を返します。</summary>
    /// <remarks>責務: 1件の数式全体が許可済み構文だけで完結することを確認します。</remarks>
    /// <returns>式全体の計算結果。</returns>
    /// <exception cref="OBDPIDFormulaException">構文または計算が不正な場合。</exception>
    public double Parse()
    {
        var value = ParseExpression();
        SkipWhitespace();
        if (_index != _characters.Length)
        {
            throw FormulaErrors.InvalidExpression();
        }
        return value;
    }

    /// <summary>加算と減算を左結合で解析します。</summary>
    /// <remarks>責務: 乗除算項を加減算演算子で結合した値を返します。</remarks>
    /// <returns>加減算式の値。</returns>
    /// <exception cref="OBDPIDFormulaException">子構文が不正な場合。</exception>
    private double ParseExpression()
    {
        var value = ParseTerm();
        while (ConsumeOne("+", "-") is { } operation)
        {
            var right = ParseTerm();
            value = operation == "+" ? value + right : value - right;
        }
        return value;
    }

    /// <summary>乗算と除算を左結合で解析します。</summary>
    /// <remarks>責務: 単項値を乗除算演算子で結合した値を返します。</remarks>
    /// <returns>乗除算式の値。</returns>
    /// <exception cref="OBDPIDFormulaException">子構文不正またはゼロ除算の場合。</exception>
    private double ParseTerm()
    {
        var value = ParseUnary();
        while (ConsumeOne("*", "/") is { } operation)
        {
            var right = ParseUnary();
            if (operation == "/" && right == 0)
            {
                throw FormulaErrors.DivisionByZero();
            }
            value = operation == "*" ? value * right : value / right;
        }
        return value;
    }

    /// <summary>任意個の単項符号を解析します。</summary>
    /// <remarks>責務: 1件の基本値へ先頭の正負符号を適用します。</remarks>
    /// <returns>単項符号適用後の値。</returns>
    /// <exception cref="OBDPIDFormulaException">後続構文が不正な場合。</exception>
    private double ParseUnary()
    {
        if (Consume("+")) { return ParseUnary(); }
        if (Consume("-")) { return -ParseUnary(); }
        return ParsePrimary();
    }

    /// <summary>数値、バイト変数、括弧式のいずれかを解析します。</summary>
    /// <remarks>責務: 1件の最小式要素を数値へ変換します。</remarks>
    /// <returns>最小式要素の値。</returns>
    /// <exception cref="OBDPIDFormulaException">未提供変数または不正構文の場合。</exception>
    private double ParsePrimary()
    {
        SkipWhitespace();
        if (Consume("("))
        {
            var value = ParseExpression();
            if (!Consume(")"))
            {
                throw FormulaErrors.InvalidExpression();
            }
            return value;
        }
        if (_index < _characters.Length && VariableIndex(_characters[_index]) is { } variableIndex)
        {
            var variable = _characters[_index][0];
            _index += 1;
            if (variableIndex >= _bytes.Count)
            {
                throw FormulaErrors.UnavailableVariable(variable);
            }
            return _bytes[variableIndex];
        }
        return ParseNumber();
    }

    /// <summary>10進整数または小数を解析します。</summary>
    /// <remarks>責務: 連続する10進数字と小数点を1件の定数へ変換します。</remarks>
    /// <returns>解析した10進定数。</returns>
    /// <exception cref="OBDPIDFormulaException">定数表現が不正な場合（<c>InvalidExpression</c>）。</exception>
    private double ParseNumber()
    {
        SkipWhitespace();
        var start = _index;
        var decimalPointCount = 0;
        while (_index < _characters.Length)
        {
            var character = _characters[_index];
            if (character == ".")
            {
                decimalPointCount += 1;
                if (decimalPointCount > 1) { break; }
                _index += 1;
            }
            else if (IsNumber(character))
            {
                _index += 1;
            }
            else
            {
                break;
            }
        }
        if (start >= _index || !TryParseSwiftDouble(string.Concat(_characters[start.._index]), out var value))
        {
            throw FormulaErrors.InvalidExpression();
        }
        return value;
    }

    /// <summary>現在位置の文字が指定候補に一致すれば消費します。</summary>
    /// <remarks>責務: 1文字演算子候補から現在位置に一致する値を返します。</remarks>
    /// <param name="candidates">許可する1文字演算子。</param>
    /// <returns>消費した演算子。どれにも一致しない場合は <see langword="null"/>。</returns>
    private string? ConsumeOne(params string[] candidates)
    {
        SkipWhitespace();
        if (_index >= _characters.Length || !candidates.Contains(_characters[_index], StringComparer.Ordinal))
        {
            return null;
        }
        var operation = _characters[_index];
        _index += 1;
        return operation;
    }

    /// <summary>現在位置の指定文字を一致時だけ消費します。</summary>
    /// <remarks>責務: 1件の期待文字を現在位置と照合して読取位置を進めます。</remarks>
    /// <param name="expected">期待する1文字。</param>
    /// <returns>一致して消費した場合は <see langword="true"/>。</returns>
    private bool Consume(string expected)
    {
        SkipWhitespace();
        if (_index >= _characters.Length || !string.Equals(_characters[_index], expected, StringComparison.Ordinal))
        {
            return false;
        }
        _index += 1;
        return true;
    }

    /// <summary>空白文字を読み飛ばします。</summary>
    /// <remarks>責務: 数式の意味に影響しない連続空白だけを現在位置から消費します。</remarks>
    private void SkipWhitespace()
    {
        while (_index < _characters.Length && IsWhitespace(_characters[_index])) { _index += 1; }
    }

    /// <summary>許可済み変数を応答バイト位置へ変換します。</summary>
    /// <remarks>責務: <c>A</c>から<c>H</c>の1文字を0始まりバイト位置へ写像します。</remarks>
    /// <param name="character">数式内の変数候補。</param>
    /// <returns>許可済み変数の位置。それ以外は <see langword="null"/>。</returns>
    private static int? VariableIndex(string character)
    {
        // Swift の Character.asciiValue は単一スカラーのASCII文字だけが値を持つ（"\r\n" は 0x0A で範囲外）。
        if (character.Length != 1 || character[0] < 'A' || character[0] >= 'A' + 8)
        {
            return null;
        }
        return character[0] - 'A';
    }

    /// <summary>文字列をSwiftの <c>Character</c> 相当の書記素クラスタへ分割します。</summary>
    /// <remarks>責務: <c>Array(expression)</c> と同じ単位で走査できる配列を作ります。</remarks>
    /// <param name="expression">分割する数式。</param>
    /// <returns>書記素クラスタの配列。</returns>
    private static string[] SplitCharacters(string expression)
    {
        var characters = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(expression);
        while (enumerator.MoveNext())
        {
            characters.Add(enumerator.GetTextElement());
        }
        return characters.ToArray();
    }

    /// <summary>Swift の <c>Character.isWhitespace</c> を再現します。</summary>
    /// <remarks>責務: 先頭スカラーが Unicode の White_Space 特性を持つかを判定します。</remarks>
    /// <param name="character">判定する書記素クラスタ。</param>
    /// <returns>空白の場合は <see langword="true"/>。</returns>
    private static bool IsWhitespace(string character) =>
        Rune.IsWhiteSpace(Rune.GetRuneAt(character, 0));

    /// <summary>Swift の <c>Character.isNumber</c> を再現します。</summary>
    /// <remarks>責務: 先頭スカラーが数値型（Numeric_Type が None 以外）を持つかを判定します。</remarks>
    /// <param name="character">判定する書記素クラスタ。</param>
    /// <returns>数値文字の場合は <see langword="true"/>。</returns>
    private static bool IsNumber(string character)
    {
        var rune = Rune.GetRuneAt(character, 0);
        // GetNumericValue は数値を持たないスカラーで -1 を返す（負の数値を持つスカラーは -0.5 の1件だけ）。
        return Rune.GetNumericValue(rune) != -1;
    }

    /// <summary>Swift の <c>Double(String)</c> を、数字と小数点だけからなる入力について再現します。</summary>
    /// <remarks>責務: ASCII数字と高々1個の小数点だけを受け付け、正しく丸めた倍精度値へ変換します。</remarks>
    /// <param name="text">変換する文字列。</param>
    /// <param name="value">変換結果。</param>
    /// <returns>Swift の <c>Double(String)</c> が値を返す場合は <see langword="true"/>。</returns>
    private static bool TryParseSwiftDouble(string text, out double value)
    {
        // Swift は C ロケールの strtod 相当で解析するため、ASCII 以外の数字（全角や他文字体系）は失敗する。
        var hasDigit = false;
        foreach (var c in text)
        {
            if (c >= '0' && c <= '9')
            {
                hasDigit = true;
            }
            else if (c != '.')
            {
                value = 0;
                return false;
            }
        }
        if (!hasDigit)
        {
            value = 0;
            return false;
        }
        return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }
}
