using System.Collections.Generic;

namespace KaoyanEnglishMod.Vocab;

public sealed class KaoyanQuestion
{
    public KaoyanQuestion(KaoyanWord word, List<string> options, int correctIndex)
    {
        Word = word;
        Options = options;
        CorrectIndex = correctIndex;
    }

    public KaoyanWord Word { get; }

    public List<string> Options { get; }

    public int CorrectIndex { get; }
}
