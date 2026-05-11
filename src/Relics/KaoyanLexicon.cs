using System;
using System.Linq;
using System.Threading.Tasks;
using KaoyanEnglishMod.UI;
using KaoyanEnglishMod.Vocab;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace KaoyanEnglishMod.Relics;

public sealed class KaoyanLexicon : RelicModel
{
    private enum KaoyanChallengeMode
    {
        Undecided,
        Challenge,
        Declined,
    }

    private CombatState? _currentCombatState;
    private KaoyanChallengeMode _challengeMode = KaoyanChallengeMode.Undecided;
    private int _lastQuestionRound = -1;
    private int _lastExtraDrawRound = -1;
    private int _lastAnsweredRound = -1;
    private bool _declinedRewardApplied;
    private KaoyanVocabService? _vocabService;
    private bool _vocabLoaded;

    public override RelicRarity Rarity => RelicRarity.Starter;

    public override string PackedIconPath =>
        "res://KaoyanEnglishMod/images/atlases/relic_atlas.sprites/kaoyan_lexicon.tres";

    protected override string PackedIconOutlinePath =>
        "res://KaoyanEnglishMod/images/atlases/relic_outline_atlas.sprites/kaoyan_lexicon.tres";

    protected override string BigIconPath =>
        "res://KaoyanEnglishMod/images/packed/relics/kaoyan_lexicon.png";

    public override async Task AfterSideTurnStart(CombatSide side, CombatState combatState)
    {
        if (side != Owner.Creature.Side)
        {
            return;
        }

        if (!ReferenceEquals(_currentCombatState, combatState))
        {
            _currentCombatState = combatState;
            _challengeMode = KaoyanChallengeMode.Undecided;
            _lastQuestionRound = -1;
            _lastExtraDrawRound = -1;
            _lastAnsweredRound = -1;
            _declinedRewardApplied = false;
            _vocabLoaded = false;
            Log.Warn("[KaoyanEnglishMod] New combat challenge state initialized.");
        }

        if (combatState.RoundNumber == 1 && _challengeMode == KaoyanChallengeMode.Undecided)
        {
            try
            {
                Flash();
                KaoyanChallengePopup.ShowChallenge(choice => HandleChallengeChoice(choice, combatState));
            }
            catch (Exception exception)
            {
                Log.Error("[KaoyanEnglishMod] Failed to show challenge popup.");
                Log.Error(exception.ToString());
            }

            return;
        }

        if (_challengeMode != KaoyanChallengeMode.Challenge
            || (_lastQuestionRound == combatState.RoundNumber && _lastExtraDrawRound == combatState.RoundNumber))
        {
            return;
        }

        await HandleChallengeRoundStartSafelyAsync(combatState.RoundNumber, combatState);
    }

    private void HandleChallengeChoice(bool isChallengeSelected, CombatState combatState)
    {
        if (!ReferenceEquals(_currentCombatState, combatState) || _challengeMode != KaoyanChallengeMode.Undecided)
        {
            return;
        }

        if (!isChallengeSelected)
        {
            _challengeMode = KaoyanChallengeMode.Declined;
            Log.Warn("[KaoyanEnglishMod] Challenge declined.");
            _ = ApplyDeclinedRewardSafelyAsync(combatState);
            return;
        }

        _challengeMode = KaoyanChallengeMode.Challenge;
        Log.Warn("[KaoyanEnglishMod] Challenge selected.");
        _ = HandleChallengeRoundStartSafelyAsync(combatState.RoundNumber, combatState);
    }

    private async Task HandleChallengeRoundStartSafelyAsync(int roundNumber, CombatState combatState)
    {
        try
        {
            Flash();
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to flash challenge relic.");
            Log.Error(exception.ToString());
        }

        await DrawExtraCardForChallengeSafelyAsync(roundNumber);
        GenerateLogAndShowQuestionSafely(roundNumber, combatState);
    }

    private async Task DrawExtraCardForChallengeSafelyAsync(int roundNumber)
    {
        if (_lastExtraDrawRound == roundNumber)
        {
            return;
        }

        _lastExtraDrawRound = roundNumber;

        try
        {
            Log.Warn($"[KaoyanEnglishMod] Drawing 1 extra card for challenge mode. Round {roundNumber}.");
            await CardPileCmd.Draw(new BlockingPlayerChoiceContext(), 1m, Owner);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to draw extra challenge card.");
            Log.Error(exception.ToString());
        }
    }

    private async Task ApplyDeclinedRewardSafelyAsync(CombatState combatState)
    {
        if (!ReferenceEquals(_currentCombatState, combatState) || _declinedRewardApplied)
        {
            return;
        }

        _declinedRewardApplied = true;

        try
        {
            Flash();
            Log.Warn("[KaoyanEnglishMod] Applying declined reward: +1 Strength, +1 Dexterity.");
            await PowerCmd.Apply<StrengthPower>(Owner.Creature, 1m, Owner.Creature, null);
            await PowerCmd.Apply<DexterityPower>(Owner.Creature, 1m, Owner.Creature, null);
            Log.Warn("[KaoyanEnglishMod] Declined reward applied.");
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to apply declined reward.");
            Log.Error(exception.ToString());
        }
    }

    private void GenerateLogAndShowQuestionSafely(int roundNumber, CombatState combatState)
    {
        if (_lastQuestionRound == roundNumber)
        {
            return;
        }

        try
        {
            var question = GenerateAndLogQuestion(roundNumber);
            var popupShown = KaoyanQuestionPopup.ShowQuestion(question, answerIndex =>
            {
                HandleQuestionAnswer(roundNumber, question, answerIndex, combatState);
            });

            if (!popupShown)
            {
                Log.Warn("[KaoyanEnglishMod] Failed to show question popup; question was logged only.");
            }
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to create round question.");
            Log.Error(exception.ToString());
        }
    }

    private KaoyanQuestion GenerateAndLogQuestion(int roundNumber)
    {
        _lastQuestionRound = roundNumber;

        _vocabService ??= new KaoyanVocabService();
        if (!_vocabLoaded)
        {
            _vocabService.LoadFromGodotResource();
            _vocabLoaded = true;
        }

        var question = _vocabService.CreateQuestion();

        Log.Warn($"[KaoyanEnglishMod] Round {roundNumber} question:");
        Log.Warn($"[KaoyanEnglishMod] Word: {question.Word.Word}");
        Log.Warn($"[KaoyanEnglishMod] A. {question.Options[0]}");
        Log.Warn($"[KaoyanEnglishMod] B. {question.Options[1]}");
        Log.Warn($"[KaoyanEnglishMod] C. {question.Options[2]}");
        Log.Warn($"[KaoyanEnglishMod] D. {question.Options[3]}");
        Log.Warn($"[KaoyanEnglishMod] Correct: {question.Options[question.CorrectIndex]}");

        return question;
    }

    private void HandleQuestionAnswer(int roundNumber, KaoyanQuestion question, int? answerIndex, CombatState combatState)
    {
        if (_lastAnsweredRound == roundNumber)
        {
            return;
        }

        _lastAnsweredRound = roundNumber;

        if (answerIndex == null)
        {
            Log.Warn($"[KaoyanEnglishMod] Question skipped. Round {roundNumber}.");
            return;
        }

        if (answerIndex.Value == question.CorrectIndex)
        {
            Log.Warn($"[KaoyanEnglishMod] Correct answer selected. Round {roundNumber}.");
            ShowRewardChoiceSafely(roundNumber);
            return;
        }

        Log.Warn(
            $"[KaoyanEnglishMod] Wrong answer selected. Round {roundNumber}. Selected {GetOptionLabel(answerIndex.Value)}, correct {GetOptionLabel(question.CorrectIndex)}.");
        _ = ApplyWrongAnswerPenaltySafelyAsync(roundNumber, combatState);
    }

    private void ShowRewardChoiceSafely(int roundNumber)
    {
        try
        {
            Log.Warn($"[KaoyanEnglishMod] Showing reward choice. Round {roundNumber}.");
            var popupShown = KaoyanRewardChoicePopup.ShowRewardChoice(choice =>
            {
                HandleRewardChoice(roundNumber, choice);
            });

            if (!popupShown)
            {
                Log.Warn("[KaoyanEnglishMod] Failed to show reward choice popup; reward skipped in 4H.");
            }
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to show reward choice popup.");
            Log.Error(exception.ToString());
        }
    }

    private void HandleRewardChoice(int roundNumber, KaoyanRewardChoice choice)
    {
        switch (choice)
        {
            case KaoyanRewardChoice.Replay:
                Log.Warn($"[KaoyanEnglishMod] Reward selected: Replay. Round {roundNumber}.");
                _ = SelectHandCardForRewardSafelyAsync(roundNumber, choice);
                break;
            case KaoyanRewardChoice.FreeThisTurn:
                Log.Warn($"[KaoyanEnglishMod] Reward selected: FreeThisTurn. Round {roundNumber}.");
                _ = SelectHandCardForRewardSafelyAsync(roundNumber, choice);
                break;
            default:
                Log.Warn($"[KaoyanEnglishMod] Unknown reward selected: {choice}. Round {roundNumber}.");
                break;
        }
    }

    private async Task SelectHandCardForRewardSafelyAsync(int roundNumber, KaoyanRewardChoice choice)
    {
        var rewardName = GetRewardLogName(choice);

        try
        {
            Log.Warn($"[KaoyanEnglishMod] Selecting a hand card for {rewardName} reward. Round {roundNumber}.");

            var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
            if (handCards.Count == 0)
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: hand is empty.");
                return;
            }

            var selectedCard = (await CardSelectCmd.FromHand(
                new BlockingPlayerChoiceContext(),
                Owner,
                new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1),
                null,
                null!)).FirstOrDefault();

            if (selectedCard == null)
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: no hand card selected.");
                return;
            }

            ApplyRewardToCardSafely(choice, selectedCard);
        }
        catch (Exception exception)
        {
            Log.Warn("[KaoyanEnglishMod] Official hand select UI unavailable; using temporary Kaoyan hand choice popup.");
            Log.Error(exception.ToString());
            SelectHandCardForRewardWithTemporaryPopupSafely(choice, rewardName);
        }
    }

    private void SelectHandCardForRewardWithTemporaryPopupSafely(KaoyanRewardChoice choice, string rewardName)
    {
        try
        {
            var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
            if (handCards.Count == 0)
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: hand is empty.");
                return;
            }

            var popupShown = KaoyanHandCardChoicePopup.ShowHandCardChoice(
                handCards,
                "\u9009\u62e9\u624b\u724c",
                GetRewardSelectionBody(choice),
                card => ApplyRewardToCardSafely(choice, card));

            if (!popupShown)
            {
                Log.Warn($"[KaoyanEnglishMod] Failed to show hand card choice popup; {rewardName} reward skipped.");
            }
        }
        catch (Exception fallbackException)
        {
            Log.Error("[KaoyanEnglishMod] Failed to apply reward.");
            Log.Error(fallbackException.ToString());
        }
    }

    private void ApplyRewardToCardSafely(KaoyanRewardChoice choice, CardModel card)
    {
        try
        {
            var rewardName = GetRewardLogName(choice);
            var handCards = PileType.Hand.GetPile(Owner).Cards;
            if (!handCards.Contains(card))
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: selected card is no longer in hand.");
                return;
            }

            var cardLabel = GetCardLogLabel(card);
            Log.Warn($"[KaoyanEnglishMod] Applying {rewardName} reward to card: {cardLabel}.");

            switch (choice)
            {
                case KaoyanRewardChoice.Replay:
                    card.BaseReplayCount++;
                    Log.Warn("[KaoyanEnglishMod] Replay reward applied.");
                    break;
                case KaoyanRewardChoice.FreeThisTurn:
                    card.SetToFreeThisTurn();
                    Log.Warn("[KaoyanEnglishMod] FreeThisTurn reward applied.");
                    break;
                default:
                    Log.Warn($"[KaoyanEnglishMod] Unknown reward selected: {choice}.");
                    break;
            }
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to apply reward.");
            Log.Error(exception.ToString());
        }
    }

    private static string GetRewardSelectionBody(KaoyanRewardChoice choice)
    {
        return choice switch
        {
            KaoyanRewardChoice.Replay => "\u9009\u62e9\u4e00\u5f20\u624b\u724c\uff0c\u672c\u573a\u6218\u6597\u6dfb\u52a0\u91cd\u653e 1",
            KaoyanRewardChoice.FreeThisTurn => "\u9009\u62e9\u4e00\u5f20\u624b\u724c\uff0c\u672c\u56de\u5408\u53ef\u4ee5\u514d\u8d39\u6253\u51fa",
            _ => "\u9009\u62e9\u4e00\u5f20\u624b\u724c",
        };
    }

    private static string GetRewardLogName(KaoyanRewardChoice choice)
    {
        return choice switch
        {
            KaoyanRewardChoice.Replay => "Replay",
            KaoyanRewardChoice.FreeThisTurn => "FreeThisTurn",
            _ => choice.ToString(),
        };
    }

    private async Task ApplyWrongAnswerPenaltySafelyAsync(int roundNumber, CombatState combatState)
    {
        try
        {
            if (!ReferenceEquals(_currentCombatState, combatState) || _challengeMode != KaoyanChallengeMode.Challenge)
            {
                return;
            }

            Log.Warn($"[KaoyanEnglishMod] Applying wrong answer penalty: exhaust 1 random hand card. Round {roundNumber}.");

            var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
            if (handCards.Count == 0)
            {
                Log.Warn("[KaoyanEnglishMod] Wrong answer penalty skipped: hand is empty.");
                return;
            }

            var cardToExhaust = Owner.RunState.Rng.CombatCardSelection.NextItem(handCards);
            if (cardToExhaust == null)
            {
                Log.Warn("[KaoyanEnglishMod] Wrong answer penalty skipped: hand is empty.");
                return;
            }

            var cardLabel = GetCardLogLabel(cardToExhaust);
            await CardCmd.Exhaust(new BlockingPlayerChoiceContext(), cardToExhaust);
            Log.Warn($"[KaoyanEnglishMod] Wrong answer penalty exhausted card: {cardLabel}.");
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to apply wrong answer penalty.");
            Log.Error(exception.ToString());
        }
    }

    private static string GetCardLogLabel(CardModel card)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(card.Title))
            {
                return card.Title;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(card.Id.Entry))
            {
                return card.Id.Entry;
            }
        }
        catch (Exception)
        {
        }

        return card.GetType().Name;
    }

    private static string GetOptionLabel(int index)
    {
        return index switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            _ => "?",
        };
    }
}
