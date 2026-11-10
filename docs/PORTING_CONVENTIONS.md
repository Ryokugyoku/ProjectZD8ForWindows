# Swift → C# 移植規約

iOS 版（`..\ProjectZD8`、GitHub: Ryokugyoku/ProjectZD8）を C# に移すときの約束です。
iOS 版の `AGENTS.md`・`Documentation/CODING_STANDARDS.md`・`Documentation/PLACEMENT_RULES.md` の考え方（層の依存方向、1ファイル1責務、日本語ドキュメント）をそのまま引き継ぎます。

## 層とプロジェクト

| iOS 版のフォルダ | C# プロジェクト | 名前空間 |
|---|---|---|
| `ProjectZD8/Domain` | `src/ProjectZD8.Domain`（net10.0） | `ProjectZD8.Domain.<サブフォルダ>` |
| `ProjectZD8Tests/Domain` | `tests/ProjectZD8.Domain.Tests`（xUnit） | `ProjectZD8.Domain.Tests.<サブフォルダ>` |
| `ProjectZD8/Application/Features/<機能>` | `src/ProjectZD8.Application/Features/<機能>`（net10.0、Domain だけを参照） | `ProjectZD8.Application.<機能>.<サブフォルダ>`（Features は入れない） |
| `ProjectZD8Tests/Application/Features/<機能>` | `tests/ProjectZD8.Application.Tests/Features/<機能>` | `ProjectZD8.Application.Tests.<機能>.<サブフォルダ>` |

- フォルダ構成とファイル名は iOS 版と同じにする（`Domain/Entities/VehicleProfile.swift` → `src/ProjectZD8.Domain/Entities/VehicleProfile.cs`）。
- Domain は他のプロジェクトにも OS にも依存しない。`System.Text.Json` の属性も Domain には付けない（JSON の互換性は Data 層で扱う）。

## 名前

- 型名は Swift と同じ。メンバー名は PascalCase にする（`rawValue` → `RawValue`、`displayIdentifier` → `DisplayIdentifier`）。
- 列挙のケース名も PascalCase（`gasolineRegular` → `GasolineRegular`）。
- Swift のプロトコル `XxxRepository` は C# の `IXxxRepository` にする。

## 型の対応

| Swift | C# |
|---|---|
| `struct`（値型、Equatable/Hashable） | `public sealed record` または `public readonly record struct`（ID のような小さな値は record struct） |
| 1つの値を包む ID（`VehicleID` など） | `public readonly record struct VehicleID(Guid RawValue)` と `New()` ファクトリ |
| 関連値なしの `enum` | `public enum` |
| 関連値ありの `enum` | `public abstract record` と、各ケースの `public sealed record` |
| `String`/`Int` の raw value を持つ `enum` | `public enum` と、同じファイルの `XxxRawValue` 静的クラス（`ToRawValue(this Xxx)` と `TryParse(string, out Xxx)`）。raw value の文字列は Swift と完全に一致させる（明示がなければケース名そのまま、例 `"gasolineRegular"`） |
| `CaseIterable` | `XxxRawValue.AllCases`（宣言順の `IReadOnlyList<Xxx>`） |
| static 関数だけの `enum`（Policy） | `public static class` |
| `protocol` | `public interface` |
| `Error` の `enum` | `public sealed class XxxException : Exception` に `Reason` を持たせる（関連値ありなら abstract record の Reason）。Swift で `Equatable` なら Reason で比較できるようにする |
| `UUID` | `Guid` |
| `Date` | `DateTimeOffset` |
| `Data` | `byte[]`（等価性が必要な型では Equals/GetHashCode を自分で書く） |
| `[T]` | `IReadOnlyList<T>`（等価性が必要なら SequenceEqual で Equals を上書き） |
| `[K: V]` | `IReadOnlyDictionary<K, V>` |
| `Set<T>` | `IReadOnlySet<T>` |
| `Int` / `Int64` | `long`（`Int` が明らかに小さい件数・添字なら `int` でもよい） |
| `UInt8` / `[UInt8]` | `byte` / `IReadOnlyList<byte>` |
| `Double` | `double` |
| `T?` | `T?` |
| `async throws` | `Task` / `Task<T>`（`CancellationToken` は付けない。Swift 側にないため） |
| `throws`（同期） | 例外を投げる |
| `Character` | `char`（または `string`） |
| `@MainActor` / `Sendable` / `nonisolated` | 無視する |
| `Identifiable` の `id` | プロパティ `Id` |
| 既定引数つき `init` | 省略可能引数つきコンストラクタ（`Date()` や `UUID()` の既定値は `null` を受けて中で補う） |

- 計算プロパティは get だけのプロパティ、`mutating` を含む `var` は `{ get; init; }` または `with` で表す。
- Swift で `precondition` / `fatalError` しているところは `ArgumentException` / `InvalidOperationException`。
- 振る舞い（計算式、境界値、並び順、丸め）は Swift と完全に同じにする。文字列の比較は `StringComparison.Ordinal`。

## 移植中に決まった細かい約束（Domain 層の移植で確定）

- **エラー**：Swift のエラー型は同じ名前の C# の `enum`（関連値があれば `abstract record`）にし、`XxxException : Exception` で包んで `Reason` に持たせる。例：`throw new OBDPIDFormulaException(new OBDPIDFormulaError.InvalidExpression())`。
- **入れ子の型とプロパティの名前がぶつかるとき**（C# では同じ名前にできない）：型を外に出し、親の名前を前に付ける。例：`OBDConnectionEndpoint.Transport` は `OBDConnectionEndpointTransport`、`ConnectionSession.Status` は `ConnectionSessionStatus`。
- **列挙の計算プロパティやメソッド**：`XxxRawValue` か `XxxExtensions` の拡張メソッドにする。
- **引数の順番**：C# では省略できる引数を後ろに置く必要があるので、必須の引数を先に並べる。そのため Swift と順番が違うことがある。呼び出す側は名前付き引数を使う。
- **整数の幅**：件数・序数・版番号は `int`、`Int64` は `long`、`UInt` と `UInt64` は `ulong`。
- **検証付きの型**：`with` で検証を飛ばされないよう、プロパティを get 専用にする。Swift で `var` のものだけ `{ get; init; }` にする。
- **文字列の比較**：基本は Ordinal。ただし、Swift の `==` と同じ結果が必要な箇所（取得マニフェストの単位の比較）だけは、Unicode の正規化（FormD）をしてから比べる。

## Application 層の約束（Application の移植で確定）

- Port（`XxxPort` などの protocol）は `I` 付きの interface。ファイル名にも `I` を付ける。async メソッドに `Async` の接尾辞は付けない。
- State（`@Observable`）は UI に依存しない `public sealed class` で `INotifyPropertyChanged` を実装する。共通の基底クラスは作らない。`@MainActor` は無視し、「UI スレッドから呼ぶ前提」と XML ドキュメントに書く。
- Swift の `Task {}` は、内部の `CancellationTokenSource` と `Task` をフィールドに持って表す。C# の async は最初の await まで同期で走るので、状態遷移の順番が Swift と同じになるよう確かめる。`Task.Run` でスレッドを増やさない。
- `actor` は `lock` 付きのクラス。await をまたいでロックしない。
- テストの Fake はテストファイルの中に置く（`file` 型か private の入れ子クラス）。非同期の完了は `TestSupport/AsyncWait.UntilAsync` で待つ。

## ドキュメント

- すべての public / internal / private の宣言に XML ドキュメントコメントを日本語で書く。Swift の DocC の文面を基本的にそのまま訳す（すでに日本語なのでそのまま写す）。
- メソッドには `<remarks>責務: ...</remarks>` を必ず書く。
- `<param>` / `<returns>` / `<exception>` も Swift の記述に合わせる。

## テスト

- XCTest / Swift Testing のテストは xUnit に移す。テスト名とアサーションの意味を同じにする。
- `Codable` の往復テストは Data 層で JSON 互換性を作るときに移す（Domain のテストでは対象外）。
