using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace KaoyanEnglishMod.Vocab;

public sealed class KaoyanVocabService
{
    private const int MinimumEffectiveWeight = 3;
    private const int MaximumEffectiveWeight = 10;

    public const string DefaultVocabPath = "res://KaoyanEnglishMod/data/kaoyan_words_mod.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly List<KaoyanWord> FallbackWords =
    [
        new()
        {
            Id = "fallback_analyze",
            Rank = 1,
            Word = "analyze",
            ZhMeaning = "\u5206\u6790",
            Difficulty = "fallback",
        },
        new()
        {
            Id = "fallback_evaluate",
            Rank = 2,
            Word = "evaluate",
            ZhMeaning = "\u8bc4\u4f30",
            Difficulty = "fallback",
        },
        new()
        {
            Id = "fallback_context",
            Rank = 3,
            Word = "context",
            ZhMeaning = "\u8bed\u5883",
            Difficulty = "fallback",
        },
        new()
        {
            Id = "fallback_contrast",
            Rank = 4,
            Word = "contrast",
            ZhMeaning = "\u5bf9\u6bd4",
            Difficulty = "fallback",
        },
    ];

    private readonly Random _random;
    private List<KaoyanWord> _words = [];

    public KaoyanVocabService()
        : this(Random.Shared)
    {
    }

    public KaoyanVocabService(Random random)
    {
        _random = random;
    }

    public IReadOnlyList<KaoyanWord> Words => _words;

    public void LoadFromGodotResource(string path = DefaultVocabPath)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            _words = BuildUsableWords([]);
            return;
        }

        var json = file.GetAsText();
        var words = DeserializeWordsSafely(json);
        _words = BuildUsableWords(words ?? []);
    }

    public KaoyanWord GetRandomWordByWeight()
    {
        EnsureLoaded();

        var rankedWords = _words.Where(word => word.Rank > 0).ToList();
        var minRank = rankedWords.Count > 0 ? rankedWords.Min(word => word.Rank) : 0;
        var maxRank = rankedWords.Count > 0 ? rankedWords.Max(word => word.Rank) : 0;
        var totalWeight = _words.Sum(word => CalculateEffectiveWeight(word, minRank, maxRank));
        if (totalWeight <= 0)
        {
            return _words[_random.Next(_words.Count)];
        }

        var roll = _random.Next(totalWeight);
        var accumulatedWeight = 0;
        foreach (var word in _words)
        {
            accumulatedWeight += CalculateEffectiveWeight(word, minRank, maxRank);
            if (roll < accumulatedWeight)
            {
                return word;
            }
        }

        return _words[^1];
    }

    public KaoyanQuestion CreateQuestion()
    {
        EnsureLoaded();

        var correctWord = GetRandomWordByWeight();
        var distractors = GetDistractors(correctWord, 3);
        var options = distractors
            .Select(word => word.ZhMeaning)
            .Append(correctWord.ZhMeaning)
            .ToList();

        Shuffle(options);

        var correctIndex = options.IndexOf(correctWord.ZhMeaning);
        if (correctIndex < 0)
        {
            throw new InvalidOperationException("Failed to place the correct answer in question options.");
        }

        return new KaoyanQuestion(correctWord, options, correctIndex);
    }

    private static bool IsUsableWord(KaoyanWord word)
    {
        return !string.IsNullOrWhiteSpace(word.Id)
            && !string.IsNullOrWhiteSpace(word.Word)
            && !string.IsNullOrWhiteSpace(word.ZhMeaning)
            && !string.IsNullOrWhiteSpace(word.Difficulty);
    }

    private static List<KaoyanWord>? DeserializeWordsSafely(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<KaoyanWord>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<KaoyanWord> BuildUsableWords(IEnumerable<KaoyanWord> words)
    {
        var usableWords = new List<KaoyanWord>();
        var usedMeanings = new HashSet<string>(StringComparer.Ordinal);

        AddUniqueUsableWords(words, usableWords, usedMeanings);

        if (usableWords.Count < 4)
        {
            AddUniqueUsableWords(FallbackWords, usableWords, usedMeanings);
        }

        return usableWords;
    }

    private static void AddUniqueUsableWords(
        IEnumerable<KaoyanWord> candidates,
        List<KaoyanWord> usableWords,
        HashSet<string> usedMeanings)
    {
        foreach (var candidate in candidates)
        {
            if (IsUsableWord(candidate) && usedMeanings.Add(candidate.ZhMeaning))
            {
                usableWords.Add(candidate);
            }
        }
    }

    private static int CalculateEffectiveWeight(KaoyanWord word, int minRank, int maxRank)
    {
        if (word.Rank <= 0 || minRank <= 0 || maxRank <= 0)
        {
            return MinimumEffectiveWeight;
        }

        if (minRank == maxRank)
        {
            return MaximumEffectiveWeight;
        }

        var position = (double)(word.Rank - minRank) / (maxRank - minRank);
        var distanceFromMiddle = Math.Abs(position - 0.5d) * 2d;
        var middleBoost = 1d - Math.Clamp(distanceFromMiddle, 0d, 1d);
        var weight = MinimumEffectiveWeight + (MaximumEffectiveWeight - MinimumEffectiveWeight) * middleBoost;

        return Math.Max(MinimumEffectiveWeight, (int)Math.Round(weight, MidpointRounding.AwayFromZero));
    }

    private List<KaoyanWord> GetDistractors(KaoyanWord correctWord, int count)
    {
        var selected = new List<KaoyanWord>(count);
        var usedMeanings = new HashSet<string>(StringComparer.Ordinal)
        {
            correctWord.ZhMeaning,
        };

        AddDistractors(
            _words.Where(word => word.Difficulty == correctWord.Difficulty),
            correctWord,
            count,
            selected,
            usedMeanings);

        if (selected.Count < count)
        {
            AddDistractors(_words, correctWord, count, selected, usedMeanings);
        }

        if (selected.Count < count)
        {
            throw new InvalidOperationException("Not enough unique wrong meanings to create a question.");
        }

        return selected;
    }

    private void AddDistractors(
        IEnumerable<KaoyanWord> candidates,
        KaoyanWord correctWord,
        int count,
        List<KaoyanWord> selected,
        HashSet<string> usedMeanings)
    {
        foreach (var candidate in candidates
            .Where(word => word.Id != correctWord.Id)
            .OrderBy(_ => _random.Next()))
        {
            if (selected.Count >= count)
            {
                return;
            }

            if (usedMeanings.Add(candidate.ZhMeaning))
            {
                selected.Add(candidate);
            }
        }
    }

    private void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swapIndex = _random.Next(index + 1);
            (items[index], items[swapIndex]) = (items[swapIndex], items[index]);
        }
    }

    private void EnsureLoaded()
    {
        if (_words.Count == 0)
        {
            throw new InvalidOperationException("Vocab has not been loaded. Call LoadFromGodotResource first.");
        }
    }
}
