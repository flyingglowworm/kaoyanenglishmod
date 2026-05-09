using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace KaoyanEnglishMod.Vocab;

public sealed class KaoyanVocabService
{
    public const string DefaultVocabPath = "res://KaoyanEnglishMod/data/kaoyan_words_mod.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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
            var error = FileAccess.GetOpenError();
            throw new InvalidOperationException($"Failed to open vocab resource '{path}'. Godot error: {error}.");
        }

        var json = file.GetAsText();
        var words = JsonSerializer.Deserialize<List<KaoyanWord>>(json, JsonOptions);
        if (words is null || words.Count == 0)
        {
            throw new InvalidOperationException($"Vocab resource '{path}' did not contain any words.");
        }

        _words = words
            .Where(IsUsableWord)
            .ToList();

        if (_words.Count < 4)
        {
            throw new InvalidOperationException($"Vocab resource '{path}' must contain at least 4 usable words.");
        }
    }

    public KaoyanWord GetRandomWordByWeight()
    {
        EnsureLoaded();

        var totalWeight = _words.Sum(word => Math.Max(0, word.SpawnWeight));
        if (totalWeight <= 0)
        {
            return _words[_random.Next(_words.Count)];
        }

        var roll = _random.Next(totalWeight);
        var accumulatedWeight = 0;
        foreach (var word in _words)
        {
            accumulatedWeight += Math.Max(0, word.SpawnWeight);
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
