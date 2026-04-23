using BackendApi.Modules.Search.Primitives.Normalization;
using FluentAssertions;

namespace Search.Tests.Unit;

public sealed class ArabicNormalizerTests
{
    private static readonly ArabicNormalizer Sut = new();

    [Theory]
    [InlineData("آسنان", "اسنان")]
    [InlineData("إسنان", "اسنان")]
    [InlineData("أسنان", "اسنان")]
    [InlineData("ٱسنان", "اسنان")]
    [InlineData("هديه", "هديه")]
    [InlineData("هيه", "هيه")]
    [InlineData("يوم", "يوم")]
    [InlineData("ئيم", "ييم")]
    [InlineData("ىوم", "يوم")]
    [InlineData("سُكَّر", "سكر")]
    [InlineData("قِفَاز", "قفاز")]
    [InlineData("مُعَقَّم", "معقم")]
    [InlineData("ســــنان", "سنان")]
    [InlineData("١٢٣٤٥٦٧٨٩٠", "1234567890")]
    [InlineData("رقم ٥٠٠", "رقم 500")]
    [InlineData("قفازات، جراحية", "قفازات جراحيه")]
    [InlineData("قفازات;جراحية", "قفازات جراحيه")]
    [InlineData("قفازات:جراحية", "قفازات جراحيه")]
    [InlineData("قفازات!جراحية", "قفازات جراحيه")]
    [InlineData("قفازات؟جراحية", "قفازات جراحيه")]
    [InlineData("قفازات?جراحية", "قفازات جراحيه")]
    [InlineData("قفازات-جراحية", "قفازات جراحيه")]
    [InlineData("قفازات_جراحية", "قفازات جراحيه")]
    [InlineData("قفازات/جراحية", "قفازات جراحيه")]
    [InlineData("قفازات\\جراحية", "قفازات جراحيه")]
    [InlineData("(قفازات)", "قفازات")]
    [InlineData("[قفازات]", "قفازات")]
    [InlineData("{قفازات}", "قفازات")]
    [InlineData("\"قفازات\"", "قفازات")]
    [InlineData("'قفازات'", "قفازات")]
    [InlineData("«قفازات»", "قفازات")]
    [InlineData("“قفازات”", "قفازات")]
    [InlineData("قفازات   جراحية", "قفازات جراحيه")]
    [InlineData("  قفازات\t\nجراحية  ", "قفازات جراحيه")]
    [InlineData("Surgical Gloves", "surgical gloves")]
    [InlineData("MIXED قفَّازات ١٠", "mixed قفازات 10")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("العَرَبِيَّة", "العربيه")]
    [InlineData("هـــة", "هه")]
    [InlineData("ي ى ئ", "ي ي ي")]
    [InlineData("إ-أ-آ-ا", "ا ا ا ا")]
    [InlineData("منتج رقم (٠١)", "منتج رقم 01")]
    [InlineData("سعره ٩٩٫٩", "سعره 99٫9")]
    public void Normalize_FoldsArabicAndSymbols(string input, string expected)
    {
        Sut.Normalize(input).Should().Be(expected);
    }
}
