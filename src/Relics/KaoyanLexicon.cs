using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KaoyanEnglishMod.UI;
using KaoyanEnglishMod.Vocab;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;

namespace KaoyanEnglishMod.Relics;

public sealed class KaoyanLexicon : RelicModel
{
    private enum KaoyanChallengeMode
    {
        Undecided,
        Challenge,
        Declined,
    }

    private static readonly MethodInfo? WaitUntilQueueIsSafeMethod = typeof(CombatManager).GetMethod(
        "WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private CombatState? _currentCombatState;
    private KaoyanChallengeMode _challengeMode = KaoyanChallengeMode.Undecided;
    private int _lastQuestionRound = -1;
    private int _lastExtraDrawRound = -1;
    private int _lastAnsweredRound = -1;
    private int _pendingQuestionRound = -1;
    private int _pendingQuestionTaskRound = -1;
    private int _lastRewardChoiceRound = -1;
    private int _lastRewardAppliedRound = -1;
    private bool _declinedRewardApplied;
    private bool _challengePromptInProgress;
    private bool _questionInProgress;
    private bool _rewardChoiceInProgress;
    private bool _handChoiceInProgress;
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

        EnsureCombatState(combatState);
        await Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }

        var combatState = player.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        EnsureCombatState(combatState);

        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        if (combatState.RoundNumber == 1 && _challengeMode == KaoyanChallengeMode.Undecided)
        {
            ScheduleChallengePrompt(combatState.RoundNumber, combatState);
            return;
        }

        if (_challengeMode == KaoyanChallengeMode.Challenge)
        {
            ScheduleChallengeQuestion(combatState.RoundNumber, combatState);
        }

        await Task.CompletedTask;
    }

    public override async Task AfterCombatVictory(CombatRoom room)
    {
        CancelPendingKaoyanFlow();
        await Task.CompletedTask;
    }

    public override async Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (_currentCombatState == null || !IsKaoyanCombatStillActive(_currentCombatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        await Task.CompletedTask;
    }

    private void HandleChallengeChoice(bool isChallengeSelected, CombatState combatState)
    {
        if (!ReferenceEquals(_currentCombatState, combatState) || _challengeMode != KaoyanChallengeMode.Undecided)
        {
            return;
        }

        if (!isChallengeSelected)
        {
            _challengePromptInProgress = false;
            _challengeMode = KaoyanChallengeMode.Declined;
            Log.Warn("[KaoyanEnglishMod] Challenge declined.");
            _ = ApplyDeclinedRewardSafelyAsync(combatState);
            return;
        }

        _challengePromptInProgress = false;
        _challengeMode = KaoyanChallengeMode.Challenge;
        Log.Warn("[KaoyanEnglishMod] Challenge selected.");
        ScheduleChallengeQuestion(combatState.RoundNumber, combatState);
    }

    private void EnsureCombatState(CombatState combatState)
    {
        if (ReferenceEquals(_currentCombatState, combatState))
        {
            return;
        }

        _currentCombatState = combatState;
        _challengeMode = KaoyanChallengeMode.Undecided;
        ResetPerCombatFlowState();
        _declinedRewardApplied = false;
        _vocabLoaded = false;
        Log.Warn("[KaoyanEnglishMod] New combat challenge state initialized.");
    }

    private void ResetPerCombatFlowState()
    {
        _lastQuestionRound = -1;
        _lastExtraDrawRound = -1;
        _lastAnsweredRound = -1;
        _pendingQuestionRound = -1;
        _pendingQuestionTaskRound = -1;
        _lastRewardChoiceRound = -1;
        _lastRewardAppliedRound = -1;
        _challengePromptInProgress = false;
        _questionInProgress = false;
        _rewardChoiceInProgress = false;
        _handChoiceInProgress = false;
    }

    private void CancelPendingKaoyanFlow()
    {
        _pendingQuestionRound = -1;
        _pendingQuestionTaskRound = -1;
        _challengePromptInProgress = false;
        _questionInProgress = false;
        _rewardChoiceInProgress = false;
        _handChoiceInProgress = false;
    }

    private void ScheduleChallengePrompt(int roundNumber, CombatState combatState)
    {
        if (_challengePromptInProgress)
        {
            return;
        }

        _challengePromptInProgress = true;
        _ = ShowChallengePromptWhenSafeAsync(roundNumber, combatState);
    }

    private async Task ShowChallengePromptWhenSafeAsync(int roundNumber, CombatState combatState)
    {
        try
        {
            while (ReferenceEquals(_currentCombatState, combatState)
                && _challengeMode == KaoyanChallengeMode.Undecided
                && _challengePromptInProgress)
            {
                await WaitForCombatQueueToSettleAsync();

                if (!IsKaoyanCombatStillActive(combatState))
                {
                    CancelPendingKaoyanFlow();
                    return;
                }

                if (CanStartKaoyanQuestionNow(combatState, roundNumber, allowChallengePrompt: true))
                {
                    Flash();
                    KaoyanChallengePopup.ShowChallenge(choice => HandleChallengeChoice(choice, combatState));
                    return;
                }

                await DelayBeforeNextSafetyProbeAsync();
            }
        }
        catch (Exception exception)
        {
            _challengePromptInProgress = false;
            Log.Error("[KaoyanEnglishMod] Failed to show challenge popup.");
            Log.Error(exception.ToString());
        }
    }

    private void ScheduleChallengeQuestion(int roundNumber, CombatState combatState)
    {
        if (_lastQuestionRound == roundNumber
            || _pendingQuestionRound == roundNumber
            || _pendingQuestionTaskRound == roundNumber)
        {
            return;
        }

        _pendingQuestionRound = roundNumber;
        _pendingQuestionTaskRound = roundNumber;
        _ = ProcessPendingQuestionWhenSafeAsync(roundNumber, combatState);
    }

    private async Task ProcessPendingQuestionWhenSafeAsync(int roundNumber, CombatState combatState)
    {
        try
        {
            while (ReferenceEquals(_currentCombatState, combatState)
                && _pendingQuestionRound == roundNumber
                && _challengeMode == KaoyanChallengeMode.Challenge)
            {
                await WaitForCombatQueueToSettleAsync();

                if (!IsKaoyanCombatStillActive(combatState))
                {
                    CancelPendingKaoyanFlow();
                    return;
                }

                if (!CanStartKaoyanQuestionNow(combatState, roundNumber))
                {
                    await DelayBeforeNextSafetyProbeAsync();
                    continue;
                }

                await DrawExtraCardForChallengeSafelyAsync(roundNumber, combatState);

                await WaitForCombatQueueToSettleAsync();
                if (!CanStartKaoyanQuestionNow(combatState, roundNumber))
                {
                    await DelayBeforeNextSafetyProbeAsync();
                    continue;
                }

                GenerateLogAndShowQuestionSafely(roundNumber, combatState);
                return;
            }
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed while waiting for a safe question window.");
            Log.Error(exception.ToString());
        }
        finally
        {
            if (_pendingQuestionTaskRound == roundNumber)
            {
                _pendingQuestionTaskRound = -1;
            }
        }
    }

    private async Task WaitForCombatQueueToSettleAsync()
    {
        var combatManager = CombatManager.Instance;
        if (combatManager == null)
        {
            return;
        }

        if (WaitUntilQueueIsSafeMethod?.Invoke(combatManager, null) is Task waitTask)
        {
            await waitTask;
        }
    }

    private static async Task DelayBeforeNextSafetyProbeAsync()
    {
        await Task.Delay(100);
    }

    private async Task DrawExtraCardForChallengeSafelyAsync(int roundNumber, CombatState combatState)
    {
        if (_lastExtraDrawRound == roundNumber)
        {
            return;
        }

        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        try
        {
            _lastExtraDrawRound = roundNumber;
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
        if (!ReferenceEquals(_currentCombatState, combatState)
            || _declinedRewardApplied
            || !IsKaoyanCombatStillActive(combatState))
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

        if (!CanStartKaoyanQuestionNow(combatState, roundNumber))
        {
            return;
        }

        try
        {
            _pendingQuestionRound = -1;
            _questionInProgress = true;
            var question = GenerateAndLogQuestion(roundNumber);
            var popupShown = KaoyanQuestionPopup.ShowQuestion(question, answerIndex =>
            {
                HandleQuestionAnswer(roundNumber, question, answerIndex, combatState);
            });

            if (!popupShown)
            {
                _questionInProgress = false;
                Log.Warn("[KaoyanEnglishMod] Failed to show question popup; question was logged only.");
            }
        }
        catch (Exception exception)
        {
            _questionInProgress = false;
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

        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        _lastAnsweredRound = roundNumber;

        if (answerIndex == null)
        {
            Log.Warn($"[KaoyanEnglishMod] Question skipped. Round {roundNumber}.");
            CompleteKaoyanQuestionFlow();
            return;
        }

        if (answerIndex.Value == question.CorrectIndex)
        {
            Log.Warn($"[KaoyanEnglishMod] Correct answer selected. Round {roundNumber}.");
            ShowRewardChoiceSafely(roundNumber, combatState);
            return;
        }

        Log.Warn(
            $"[KaoyanEnglishMod] Wrong answer selected. Round {roundNumber}. Selected {GetOptionLabel(answerIndex.Value)}, correct {GetOptionLabel(question.CorrectIndex)}.");
        _ = ApplyWrongAnswerPenaltySafelyAsync(roundNumber, combatState);
    }

    private void ShowRewardChoiceSafely(int roundNumber, CombatState combatState)
    {
        if (!IsKaoyanCombatStillActive(combatState) || _lastRewardChoiceRound == roundNumber)
        {
            CancelPendingKaoyanFlow();
            return;
        }

        try
        {
            _rewardChoiceInProgress = true;
            Log.Warn($"[KaoyanEnglishMod] Showing reward choice. Round {roundNumber}.");
            var popupShown = KaoyanRewardChoicePopup.ShowRewardChoice(choice =>
            {
                HandleRewardChoice(roundNumber, choice, combatState);
            });

            if (!popupShown)
            {
                _rewardChoiceInProgress = false;
                CompleteKaoyanQuestionFlow();
                Log.Warn("[KaoyanEnglishMod] Failed to show reward choice popup; reward skipped in 4H.");
            }
        }
        catch (Exception exception)
        {
            _rewardChoiceInProgress = false;
            CompleteKaoyanQuestionFlow();
            Log.Error("[KaoyanEnglishMod] Failed to show reward choice popup.");
            Log.Error(exception.ToString());
        }
    }

    private void HandleRewardChoice(int roundNumber, KaoyanRewardChoice choice, CombatState combatState)
    {
        if (_lastRewardChoiceRound == roundNumber)
        {
            return;
        }

        _lastRewardChoiceRound = roundNumber;
        _rewardChoiceInProgress = false;

        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        switch (choice)
        {
            case KaoyanRewardChoice.Replay:
                Log.Warn($"[KaoyanEnglishMod] Reward selected: Replay. Round {roundNumber}.");
                _ = SelectHandCardForRewardSafelyAsync(roundNumber, choice, combatState);
                break;
            case KaoyanRewardChoice.FreeThisTurn:
                Log.Warn($"[KaoyanEnglishMod] Reward selected: FreeThisTurn. Round {roundNumber}.");
                _ = SelectHandCardForRewardSafelyAsync(roundNumber, choice, combatState);
                break;
            default:
                Log.Warn($"[KaoyanEnglishMod] Unknown reward selected: {choice}. Round {roundNumber}.");
                CompleteKaoyanQuestionFlow();
                break;
        }
    }

    private async Task SelectHandCardForRewardSafelyAsync(int roundNumber, KaoyanRewardChoice choice, CombatState combatState)
    {
        var rewardName = GetRewardLogName(choice);
        _handChoiceInProgress = true;

        try
        {
            if (!IsKaoyanCombatStillActive(combatState))
            {
                CancelPendingKaoyanFlow();
                return;
            }

            Log.Warn($"[KaoyanEnglishMod] Selecting a hand card for {rewardName} reward. Round {roundNumber}.");

            var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
            if (handCards.Count == 0)
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: hand is empty.");
                CompleteKaoyanQuestionFlow();
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
                CompleteKaoyanQuestionFlow();
                return;
            }

            ApplyRewardToCardSafely(roundNumber, choice, selectedCard, combatState);
        }
        catch (Exception exception)
        {
            Log.Warn("[KaoyanEnglishMod] Official hand select UI unavailable; using temporary Kaoyan hand choice popup.");
            Log.Error(exception.ToString());
            SelectHandCardForRewardWithTemporaryPopupSafely(roundNumber, choice, rewardName, combatState);
            return;
        }
        finally
        {
            if (_handChoiceInProgress)
            {
                _handChoiceInProgress = false;
            }
        }

        CompleteKaoyanQuestionFlow();
    }

    private void SelectHandCardForRewardWithTemporaryPopupSafely(
        int roundNumber,
        KaoyanRewardChoice choice,
        string rewardName,
        CombatState combatState)
    {
        try
        {
            if (!IsKaoyanCombatStillActive(combatState))
            {
                CancelPendingKaoyanFlow();
                return;
            }

            var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
            if (handCards.Count == 0)
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: hand is empty.");
                CompleteKaoyanQuestionFlow();
                return;
            }

            var popupShown = KaoyanHandCardChoicePopup.ShowHandCardChoice(
                handCards,
                "\u9009\u62e9\u624b\u724c",
                GetRewardSelectionBody(choice),
                card =>
                {
                    _handChoiceInProgress = false;
                    ApplyRewardToCardSafely(roundNumber, choice, card, combatState);
                    CompleteKaoyanQuestionFlow();
                });

            if (!popupShown)
            {
                _handChoiceInProgress = false;
                CompleteKaoyanQuestionFlow();
                Log.Warn($"[KaoyanEnglishMod] Failed to show hand card choice popup; {rewardName} reward skipped.");
            }
        }
        catch (Exception fallbackException)
        {
            _handChoiceInProgress = false;
            CompleteKaoyanQuestionFlow();
            Log.Error("[KaoyanEnglishMod] Failed to apply reward.");
            Log.Error(fallbackException.ToString());
        }
    }

    private void ApplyRewardToCardSafely(int roundNumber, KaoyanRewardChoice choice, CardModel card, CombatState combatState)
    {
        try
        {
            if (!IsKaoyanCombatStillActive(combatState))
            {
                CancelPendingKaoyanFlow();
                return;
            }

            if (_lastRewardAppliedRound == roundNumber)
            {
                return;
            }

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
                    _lastRewardAppliedRound = roundNumber;
                    card.BaseReplayCount++;
                    Log.Warn("[KaoyanEnglishMod] Replay reward applied.");
                    break;
                case KaoyanRewardChoice.FreeThisTurn:
                    _lastRewardAppliedRound = roundNumber;
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
            if (!ReferenceEquals(_currentCombatState, combatState)
                || _challengeMode != KaoyanChallengeMode.Challenge
                || !IsKaoyanCombatStillActive(combatState))
            {
                CancelPendingKaoyanFlow();
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
        finally
        {
            CompleteKaoyanQuestionFlow();
        }
    }

    private bool CanStartKaoyanQuestionNow(
        CombatState combatState,
        int roundNumber,
        bool allowChallengePrompt = false)
    {
        if (_lastQuestionRound == roundNumber)
        {
            return false;
        }

        if (!IsKaoyanCombatStillActive(combatState))
        {
            return false;
        }

        if (_questionInProgress || _rewardChoiceInProgress || _handChoiceInProgress)
        {
            return false;
        }

        if (_challengePromptInProgress && !allowChallengePrompt)
        {
            return false;
        }

        var modalContainer = NModalContainer.Instance;
        if (modalContainer == null || modalContainer.OpenModal != null)
        {
            return false;
        }

        var combatManager = CombatManager.Instance;
        if (combatManager == null)
        {
            return false;
        }

        return combatManager.IsPlayPhase
            && !combatManager.PlayerActionsDisabled
            && !combatManager.EndingPlayerTurnPhaseOne
            && !combatManager.EndingPlayerTurnPhaseTwo
            && combatManager.IsPartOfPlayerTurn(Owner);
    }

    private bool IsKaoyanCombatStillActive(CombatState combatState)
    {
        if (!ReferenceEquals(_currentCombatState, combatState))
        {
            return false;
        }

        var combatManager = CombatManager.Instance;
        if (combatManager == null
            || !combatManager.IsInProgress
            || combatManager.IsOverOrEnding
            || combatManager.IsEnding
            || combatManager.IsAboutToLose
            || combatManager.IsPaused)
        {
            return false;
        }

        if (combatState.CurrentSide != Owner.Creature.Side)
        {
            return false;
        }

        if (Owner.Creature.CurrentHp <= 0)
        {
            return false;
        }

        return combatState.HittableEnemies.Any(enemy => enemy.CurrentHp > 0);
    }

    private void CompleteKaoyanQuestionFlow()
    {
        _questionInProgress = false;
        _rewardChoiceInProgress = false;
        _handChoiceInProgress = false;
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
