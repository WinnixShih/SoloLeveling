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

    /// <inheritdoc />
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
