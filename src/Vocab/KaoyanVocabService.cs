using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace KaoyanEnglishMod.Vocab;

public sealed class KaoyanVocabService
{
    private const int HighFrequencyEdgeWeight = 3;
    private const int MiddleEffectiveWeight = 10;
    private const int LowFrequencyEdgeWeight = 4;
    private const double MiddlePeakPosition = 0.62d;
    private const int MistakeWeightMultiplier = 100;
    private const double DynamicDifficultyMaxSwing = 0.8d;
    private const double DynamicDifficultyMinMultiplier = 0.5d;
    private const double DynamicDifficultyMaxMultiplier = 1.8d;

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
            _words = BuildUsableWords(Array.Empty<KaoyanWord>());
            return;
        }

        var json = file.GetAsText(false);
        var words = DeserializeWordsSafely(json);
        _words = BuildUsableWords(words ?? new List<KaoyanWord>());
    }

    public KaoyanWord GetRandomWordByWeight(
        IReadOnlySet<string>? excludedWordIds = null,
        IReadOnlyDictionary<string, int>? mistakeCountsByWordId = null,
        string? forcedWordId = null,
        double difficultyBias = MiddlePeakPosition)
    {
        EnsureLoaded();

        var candidates = _words
            .Where(word => !IsExcludedWord(word, excludedWordIds))
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = _words;
        }

        if (!string.IsNullOrWhiteSpace(forcedWordId))
        {
            var forcedWord = candidates.FirstOrDefault(word => word.Id == forcedWordId);
            if (forcedWord != null)
            {
                return forcedWord;
            }
        }

        var rankedWords = candidates.Where(word => word.Rank > 0).ToList();
        var minRank = rankedWords.Count > 0 ? rankedWords.Min(word => word.Rank) : 0;
        var maxRank = rankedWords.Count > 0 ? rankedWords.Max(word => word.Rank) : 0;
        var totalWeight = candidates.Sum(word => CalculateSelectionWeight(
            word,
            minRank,
            maxRank,
            mistakeCountsByWordId,
            difficultyBias));
        if (totalWeight <= 0)
        {
            return candidates[_random.Next(candidates.Count)];
        }

        var roll = _random.Next(totalWeight);
        var accumulatedWeight = 0;
        foreach (var word in candidates)
        {
            accumulatedWeight += CalculateSelectionWeight(
                word,
                minRank,
                maxRank,
                mistakeCountsByWordId,
                difficultyBias);
            if (roll < accumulatedWeight)
            {
                return word;
            }
        }

        return candidates[^1];
    }

    public KaoyanQuestion CreateQuestion(
        IReadOnlySet<string>? excludedWordIds = null,
        IReadOnlyDictionary<string, int>? mistakeCountsByWordId = null,
        string? forcedWordId = null,
        double difficultyBias = MiddlePeakPosition)
    {
        EnsureLoaded();

        var correctWord = GetRandomWordByWeight(
            excludedWordIds,
            mistakeCountsByWordId,
            forcedWordId,
            difficultyBias);
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
            return HighFrequencyEdgeWeight;
        }

        if (minRank == maxRank)
        {
            return MiddleEffectiveWeight;
        }

        var position = Math.Clamp((double)(word.Rank - minRank) / (maxRank - minRank), 0d, 1d);
        double weight;
        if (position <= MiddlePeakPosition)
        {
            var boost = position / MiddlePeakPosition;
            weight = HighFrequencyEdgeWeight + (MiddleEffectiveWeight - HighFrequencyEdgeWeight) * boost;
        }
        else
        {
            var drop = (position - MiddlePeakPosition) / (1d - MiddlePeakPosition);
            weight = MiddleEffectiveWeight - (MiddleEffectiveWeight - LowFrequencyEdgeWeight) * drop;
        }

        var roundedWeight = (int)Math.Round(weight, MidpointRounding.AwayFromZero);
        return Math.Clamp(roundedWeight, HighFrequencyEdgeWeight, MiddleEffectiveWeight);
    }

    private static int CalculateSelectionWeight(
        KaoyanWord word,
        int minRank,
        int maxRank,
        IReadOnlyDictionary<string, int>? mistakeCountsByWordId,
        double difficultyBias)
    {
        var baseWeight = CalculateEffectiveWeight(word, minRank, maxRank);
        var dynamicMultiplier = CalculateDynamicDifficultyMultiplier(word, minRank, maxRank, difficultyBias);
        var mistakeMultiplier = CalculateMistakeMultiplier(word, mistakeCountsByWordId);
        var weightedValue = baseWeight * dynamicMultiplier * mistakeMultiplier;
        return Math.Max(1, (int)Math.Round(weightedValue, MidpointRounding.AwayFromZero));
    }

    private static double CalculateDynamicDifficultyMultiplier(
        KaoyanWord word,
        int minRank,
        int maxRank,
        double difficultyBias)
    {
        if (word.Rank <= 0 || minRank <= 0 || maxRank <= 0 || minRank == maxRank)
        {
            return 1d;
        }

        var position = Math.Clamp((double)(word.Rank - minRank) / (maxRank - minRank), 0d, 1d);
        var targetPosition = Math.Clamp(difficultyBias, 0d, 1d);
        var shift = targetPosition - MiddlePeakPosition;
        if (Math.Abs(shift) < 0.001d)
        {
            return 1d;
        }

        var directionScore = shift > 0d
            ? (position - MiddlePeakPosition) / (1d - MiddlePeakPosition)
            : (MiddlePeakPosition - position) / MiddlePeakPosition;
        directionScore = Math.Clamp(directionScore, -1d, 1d);

        var maxShift = shift > 0d ? 1d - MiddlePeakPosition : MiddlePeakPosition;
        var intensity = Math.Clamp(Math.Abs(shift) / maxShift, 0d, 1d);
        var multiplier = 1d + directionScore * intensity * DynamicDifficultyMaxSwing;
        return Math.Clamp(multiplier, DynamicDifficultyMinMultiplier, DynamicDifficultyMaxMultiplier);
    }

    private static int CalculateMistakeMultiplier(
        KaoyanWord word,
        IReadOnlyDictionary<string, int>? mistakeCountsByWordId)
    {
        if (mistakeCountsByWordId == null
            || !mistakeCountsByWordId.TryGetValue(word.Id, out var mistakeCount)
            || mistakeCount <= 0)
        {
            return 1;
        }

        return MistakeWeightMultiplier * mistakeCount;
    }

    private static bool IsExcludedWord(KaoyanWord word, IReadOnlySet<string>? excludedWordIds)
    {
        return excludedWordIds != null && excludedWordIds.Contains(word.Id);
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
