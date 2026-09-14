using KeySwitcher.Core.Analysis;
using KeySwitcher.Core.Layout;

namespace KeySwitcher.Tests;

/// <summary>
/// The user's own word list (<c>custom-words.txt</c>): it is edited by hand as often as from the dialog,
/// so reading must survive comments, blank lines and duplicates, and writing must produce a file that
/// reads back identically.
/// </summary>
public class PersonalDictionaryTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"keyswitcher-words-{Guid.NewGuid():N}.txt");

    [Fact]
    public void Load_MissingFile_IsAnEmptyList()
    {
        Assert.Empty(PersonalDictionary.Load(TempFile()));
    }

    [Fact]
    public void Load_TrimsWords_AndSkipsCommentsAndBlanks()
    {
        string path = TempFile();
        try
        {
            File.WriteAllLines(path, ["# коментар", "", "  Згурський  ", "bilous", "   "]);

            Assert.Equal(["Згурський", "bilous"], PersonalDictionary.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_DropsDuplicates_CaseInsensitively()
    {
        string path = TempFile();
        try
        {
            File.WriteAllLines(path, ["Word", "word", "WORD"]);

            Assert.Single(PersonalDictionary.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_ThenLoad_ReturnsTheSameWords()
    {
        string path = TempFile();
        try
        {
            PersonalDictionary.Save(["Згурський", "bilous"], path);

            Assert.Equal(["Згурський", "bilous"], PersonalDictionary.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_TrimsAndDropsEmptyEntries()
    {
        string path = TempFile();
        try
        {
            PersonalDictionary.Save(["  Згурський  ", "", "   "], path);

            Assert.Equal(["Згурський"], PersonalDictionary.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SplitByScript_SendsCyrillicToUkrainian_AndLatinToEnglish()
    {
        (IReadOnlyList<string> ukrainian, IReadOnlyList<string> english) =
            PersonalDictionary.SplitByScript(["Згурський", "bilous", "Ґанжа", "KeySwitcher"]);

        Assert.Equal(["Згурський", "Ґанжа"], ukrainian);
        Assert.Equal(["bilous", "KeySwitcher"], english);
    }

    [Fact]
    public void SplitByScript_DropsWordsWithoutLetters()
    {
        (IReadOnlyList<string> ukrainian, IReadOnlyList<string> english) =
            PersonalDictionary.SplitByScript(["2026", "-", "!!!"]);

        Assert.Empty(ukrainian);
        Assert.Empty(english);
    }

    [Theory]
    [InlineData("Згурський", KeyboardLanguage.Ukrainian)]
    [InlineData("працює", KeyboardLanguage.Ukrainian)]
    [InlineData("bilous", KeyboardLanguage.English)]
    [InlineData("KeySwitcher", KeyboardLanguage.English)]
    public void LanguageOf_ComesFromTheFirstLetter(string word, KeyboardLanguage expected) =>
        Assert.Equal(expected, PersonalDictionary.LanguageOf(word));

    [Theory]
    [InlineData("2026")]
    [InlineData("...")]
    [InlineData("")]
    public void LanguageOf_IsNullForWordsWithNoLetters(string word) =>
        Assert.Null(PersonalDictionary.LanguageOf(word));

    [Fact]
    public void DefaultPath_LivesNextToTheSettings()
    {
        Assert.EndsWith(Path.Combine("KeySwitcher", "custom-words.txt"), PersonalDictionary.DefaultPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            PersonalDictionary.DefaultPath, StringComparison.OrdinalIgnoreCase);
    }
}
