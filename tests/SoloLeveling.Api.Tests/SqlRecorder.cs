using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SoloLeveling.Api.Tests;

/// <summary>
/// 記錄 EF 送出的查詢文字；測試用 <see cref="Count"/> 判斷某張表是否被讀過。
/// </summary>
public sealed class SqlRecorder : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    /// <summary>清掉已記錄的查詢。</summary>
    public void Clear()
    {
        _commands.Clear();
    }

    /// <summary>已記錄的查詢中含指定片段的筆數。</summary>
    /// <param name="fragment">SQL 片段。</param>
    /// <returns>筆數。</returns>
    public int Count(string fragment)
    {
        return _commands.Count(c => c.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// 在查詢送出前記下 SQL 文字，不改變執行結果。
    /// </summary>
    /// <param name="command">即將執行的命令。</param>
    /// <param name="eventData">事件資料。</param>
    /// <param name="result">攔截結果，原樣回傳。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>原攔截結果。</returns>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
