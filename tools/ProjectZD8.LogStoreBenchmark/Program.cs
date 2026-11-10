using System.Diagnostics;
using System.Globalization;
using ProjectZD8.Data.Persistence.LogStore;
using ProjectZD8.Data.Persistence.Sqlite.Database;
using ProjectZD8.Data.Persistence.Sqlite.Repositories;
using ProjectZD8.Domain.Entities;
using ProjectZD8.Domain.ValueObjects;

// LogStore の性能を測ります。
// 使い方: dotnet run -c Release --project tools/ProjectZD8.LogStoreBenchmark -- [件数=1000000] [作業ディレクトリー]
// 1 セッションへ生ログを件数分だけ（接続セッション保存一式の Append 経由で）書き、
// sequence 範囲と観測日時の範囲で読んで、スループットと遅延を表示します。
var count = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 1_000_000;
var root = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "ProjectZD8.LogStoreBenchmark", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Console.WriteLine($"ProjectZD8 LogStore benchmark  entries={count:N0}  dir={root}");
Console.WriteLine($"OS={Environment.OSVersion}  cores={Environment.ProcessorCount}  runtime={Environment.Version}  release={!IsDebug()}");
Console.WriteLine();

var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
const long spacingTicks = 10 * TimeSpan.TicksPerMillisecond; // 10 ms ごとの観測（約 100 応答/秒の計測を想定）
var session = new ConnectionSession("benchmark-account", startedAt: start);
long[] appendLatencies = new long[count];
TimeSpan appendElapsed;
TimeSpan disposeElapsed;
var database = SqliteDatabase.OpenFile(Path.Combine(root, SqliteDatabase.ApplicationDatabaseFileName));
var logDirectory = Path.Combine(root, SqliteConnectionSessionRepository.LogStoreDirectoryName);

using (var repository = new SqliteConnectionSessionRepository(database, ConnectionSessionLogStore.Open(logDirectory)))
{
    repository.Save(session);
    var payloads = Enumerable.Range(0, 256).Select(value => new byte[] { (byte)value, (byte)(255 - value), 0x10, 0x20 }).ToArray();
    var observations = new OBDRawResponseObservation[1024];
    for (var index = 0; index < observations.Length; index++)
    {
        observations[index] = new OBDRawResponseObservation(start, 0, new OBDPIDRequest(0x01, 0x0C), payloads[index % payloads.Length]);
    }

    // 書込み（計測スレッドから見た Append の所要時間）
    var total = Stopwatch.StartNew();
    for (var index = 0; index < count; index++)
    {
        var observation = new OBDRawResponseObservation(
            start.AddTicks(index * spacingTicks),
            (ulong)(index % 1000) * 1000,
            new OBDPIDRequest(0x01, (byte)(0x04 + index % 16)),
            observations[index % observations.Length].Payload);
        var before = Stopwatch.GetTimestamp();
        repository.Append(observation, session.Id);
        appendLatencies[index] = Stopwatch.GetTimestamp() - before;
    }
    appendElapsed = total.Elapsed;
    var drain = Stopwatch.StartNew();
    var last = repository.EntriesInSequenceRange(session.Id, count - 1, count);
    var drainElapsed = drain.Elapsed;
    if (last.Count != 1 || last[0].Sequence != count - 1)
    {
        throw new InvalidOperationException("Last entry was not readable");
    }

    Console.WriteLine("== Write (Append through SqliteConnectionSessionRepository) ==");
    Console.WriteLine($"  total {appendElapsed.TotalMilliseconds:N0} ms  throughput {count / appendElapsed.TotalSeconds:N0} entries/s");
    PrintLatency("  Append latency", appendLatencies);
    Console.WriteLine($"  background writer drain after last Append: {drainElapsed.TotalMilliseconds:N1} ms");
    Console.WriteLine();

    // sequence 範囲の読取
    var random = new Random(42);
    const int rangeQueries = 1_000;
    const int rangeLength = 1_000;
    var sequenceLatencies = new long[rangeQueries];
    long sequenceEntries = 0;
    var sequenceTotal = Stopwatch.StartNew();
    for (var query = 0; query < rangeQueries; query++)
    {
        var from = random.NextInt64(0, count - rangeLength);
        var before = Stopwatch.GetTimestamp();
        var entries = repository.EntriesInSequenceRange(session.Id, from, from + rangeLength);
        sequenceLatencies[query] = Stopwatch.GetTimestamp() - before;
        if (entries.Count != rangeLength || entries[0].Sequence != from)
        {
            throw new InvalidOperationException("Sequence range mismatch");
        }
        sequenceEntries += entries.Count;
    }
    sequenceTotal.Stop();
    Console.WriteLine($"== Read by sequence range ({rangeQueries:N0} random ranges x {rangeLength:N0} entries) ==");
    Console.WriteLine($"  throughput {sequenceEntries / sequenceTotal.Elapsed.TotalSeconds:N0} entries/s");
    PrintLatency("  query latency", sequenceLatencies);
    Console.WriteLine();

    // 観測日時の範囲の読取（10 秒の窓 = 約 1,000 件）
    var timeLatencies = new long[rangeQueries];
    long timeEntries = 0;
    var timeTotal = Stopwatch.StartNew();
    for (var query = 0; query < rangeQueries; query++)
    {
        var offset = random.NextInt64(0, (count - rangeLength) * spacingTicks);
        var from = start.AddTicks(offset);
        var to = from.AddSeconds(10);
        var before = Stopwatch.GetTimestamp();
        var entries = repository.EntriesObserved(session.Id, from, to);
        timeLatencies[query] = Stopwatch.GetTimestamp() - before;
        if (entries.Count == 0 || entries.Any(entry => entry.ObservedAt < from || entry.ObservedAt > to))
        {
            throw new InvalidOperationException("Time range mismatch");
        }
        timeEntries += entries.Count;
    }
    timeTotal.Stop();
    Console.WriteLine($"== Read by time window ({rangeQueries:N0} random 10 s windows) ==");
    Console.WriteLine($"  throughput {timeEntries / timeTotal.Elapsed.TotalSeconds:N0} entries/s  avg {timeEntries / (double)rangeQueries:N0} entries/query");
    PrintLatency("  query latency", timeLatencies);
    Console.WriteLine();

    // 全件読取
    var scan = Stopwatch.StartNew();
    var all = repository.Entries(session.Id);
    scan.Stop();
    Console.WriteLine($"== Full read ==  {all.Count:N0} entries in {scan.Elapsed.TotalMilliseconds:N0} ms ({all.Count / scan.Elapsed.TotalSeconds:N0} entries/s)");
    Console.WriteLine();

    repository.Save(session with { EndedAt = start.AddTicks(count * spacingTicks), EndReason = ConnectionSessionEndReason.UserDisconnected });
    var dispose = Stopwatch.StartNew();
    repository.Dispose();
    disposeElapsed = dispose.Elapsed;
}

var files = Directory.EnumerateFiles(logDirectory, "*", SearchOption.AllDirectories).Select(path => new FileInfo(path)).ToList();
Console.WriteLine("== Disk ==");
Console.WriteLine($"  close (fsync + index file) {disposeElapsed.TotalMilliseconds:N0} ms");
Console.WriteLine($"  segments: {files.Count(file => file.Extension == ".log")} plain, {files.Count(file => file.Extension == ".logz")} compressed;"
    + $" total {files.Sum(file => file.Length) / 1024.0 / 1024.0:N1} MiB ({files.Sum(file => file.Length) / (double)count:N1} bytes/entry, payload 4 bytes/entry)");
Console.WriteLine();

// 再オープン（索引ファイルあり）と最初の範囲読取
Reopen("Cold reopen with index file", deleteSidecar: false);
// 再オープン（索引ファイルなし = 異常終了後の全走査）
Reopen("Cold reopen without index file (crash recovery scan)", deleteSidecar: true);

database.Dispose();
try
{
    Directory.Delete(root, recursive: true);
}
catch (IOException)
{
}
return;

void Reopen(string title, bool deleteSidecar)
{
    if (deleteSidecar)
    {
        foreach (var sidecar in Directory.EnumerateFiles(logDirectory, "index.bin", SearchOption.AllDirectories))
        {
            File.Delete(sidecar);
        }
    }
    var watch = Stopwatch.StartNew();
    using var repository = new SqliteConnectionSessionRepository(database, ConnectionSessionLogStore.Open(logDirectory));
    var opened = watch.Elapsed;
    var middle = count / 2;
    var entries = repository.EntriesInSequenceRange(session.Id, middle, middle + 1_000);
    var firstRead = watch.Elapsed - opened;
    var listed = repository.Sessions("benchmark-account")[0];
    Console.WriteLine($"== {title} ==");
    Console.WriteLine($"  open {opened.TotalMilliseconds:N1} ms, first 1,000-entry range read (loads log) {firstRead.TotalMilliseconds:N1} ms, entries={entries.Count:N0}, summary count={listed.RawLogSummary.RecordCount:N0}");
    Console.WriteLine();
}

static void PrintLatency(string label, long[] samples)
{
    var sorted = samples.ToArray();
    Array.Sort(sorted);
    double Micro(long ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;
    long At(double percentile) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(percentile * sorted.Length) - 1)];
    Console.WriteLine(
        $"{label}: p50 {Micro(At(0.50)):N1} us  p99 {Micro(At(0.99)):N1} us  p99.9 {Micro(At(0.999)):N1} us  max {Micro(sorted[^1]):N1} us  mean {Micro((long)sorted.Average()):N2} us");
}

static bool IsDebug()
{
#if DEBUG
    return true;
#else
    return false;
#endif
}
