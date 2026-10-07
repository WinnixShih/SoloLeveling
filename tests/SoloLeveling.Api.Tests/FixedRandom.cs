namespace SoloLeveling.Api.Tests;

/// <summary>
/// 固定回傳 <see cref="Value"/>（超過上限時取最後一個索引），讓抽卡結果可預期；預設 0＝該等級目錄中的第一張卡。
/// </summary>
public sealed class FixedRandom : Random
{
    public int Value { get; set; }

    public override int Next(int maxValue)
    {
        return Math.Min(Value, maxValue - 1);
    }
}
