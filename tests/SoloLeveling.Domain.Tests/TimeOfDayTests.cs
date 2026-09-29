using FluentAssertions;
using SoloLeveling.Domain;
using SoloLeveling.Domain.Rules;

namespace SoloLeveling.Domain.Tests;

public class TimeOfDayTests
{
    [Theory]
    [InlineData("12:00", 0)]
    [InlineData("23:30", 690)]
    [InlineData("00:00", 720)]
    [InlineData("01:00", 780)]
    [InlineData("07:00", 1140)]
    [InlineData("08:00", 1200)]
    [InlineData("11:59", 1439)]
    public void Parse_HHMM_轉成距中午的分鐘數(string text, int expected)
    {
        TimeOfDay.Parse(text).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "12:00")]
    [InlineData(690, "23:30")]
    [InlineData(720, "00:00")]
    [InlineData(780, "01:00")]
    [InlineData(1200, "08:00")]
    [InlineData(1439, "11:59")]
    public void Format_分鐘數_轉回HHMM(int minutes, string expected)
    {
        TimeOfDay.Format(minutes).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1:00")]
    [InlineData("25:00")]
    [InlineData("12:60")]
    [InlineData("abc")]
    public void Parse_格式錯誤_丟DomainValidationException(string text)
    {
        var act = () => TimeOfDay.Parse(text);
        act.Should().Throw<DomainValidationException>().Which.Code.Should().Be("InvalidTime");
    }

    [Theory]
    [InlineData("18:00", 0)]
    [InlineData("07:00", 780)]
    [InlineData("12:30", 1110)]
    public void Parse_以18點為基準_轉成距18點的分鐘數(string text, int expected)
    {
        TimeOfDay.Parse(text, 18).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "18:00")]
    [InlineData(780, "07:00")]
    [InlineData(1110, "12:30")]
    public void Format_以18點為基準_轉回HHMM(int minutes, string expected)
    {
        TimeOfDay.Format(minutes, 18).Should().Be(expected);
    }
}
