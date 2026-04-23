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
    [InlineData("هديه", "ةدىة")]
    [InlineData("هيه", "ةىة")]
    [InlineData("يوم", "ىوم")]
    [InlineData("ئيم", "ىىم")]
    [InlineData("ىوم", "ىوم")]
    [InlineData("سُكَّر", "سكر")]
    [InlineData("قِفَاز", "قفاز")]
    [InlineData("مُعَقَّم", "معقم")]
    [InlineData("ســــنان", "سنان")]
    [InlineData("١٢٣٤٥٦٧٨٩٠", "1234567890")]
    [InlineData("رقم ٥٠٠", "رقم 500")]
    [InlineData("قفازات، جراحية", "قفازات جراحىة")]
    [InlineData("قفازات;جراحية", "قفازات جراحىة")]
    [InlineData("قفازات:جراحية", "قفازات جراحىة")]
    [InlineData("قفازات!جراحية", "قفازات جراحىة")]
    [InlineData("قفازات؟جراحية", "قفازات جراحىة")]
    [InlineData("قفازات?جراحية", "قفازات جراحىة")]
    [InlineData("قفازات-جراحية", "قفازات جراحىة")]
    [InlineData("قفازات_جراحية", "قفازات جراحىة")]
    [InlineData("قفازات/جراحية", "قفازات جراحىة")]
    [InlineData("قفازات\\جراحية", "قفازات جراحىة")]
    [InlineData("(قفازات)", "قفازات")]
    [InlineData("[قفازات]", "قفازات")]
    [InlineData("{قفازات}", "قفازات")]
    [InlineData("\"قفازات\"", "قفازات")]
    [InlineData("'قفازات'", "قفازات")]
    [InlineData("«قفازات»", "قفازات")]
    [InlineData("“قفازات”", "قفازات")]
    [InlineData("قفازات   جراحية", "قفازات جراحىة")]
    [InlineData("  قفازات\t\nجراحية  ", "قفازات جراحىة")]
    [InlineData("Surgical Gloves", "surgical gloves")]
    [InlineData("MIXED قفَّازات ١٠", "mixed قفازات 10")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("العَرَبِيَّة", "العربىة")]
    [InlineData("هـــة", "ةة")]
    [InlineData("ي ى ئ", "ى ى ى")]
    [InlineData("إ-أ-آ-ا", "ا ا ا ا")]
    [InlineData("منتج رقم (٠١)", "منتج رقم 01")]
    [InlineData("سعره ٩٩٫٩", "سعرة 99٫9")]
    public void Normalize_FoldsArabicAndSymbols(string input, string expected)
    {
        Sut.Normalize(input).Should().Be(expected);
    }
}
