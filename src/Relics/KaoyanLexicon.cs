using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using KaoyanEnglishMod.Multiplayer;
using KaoyanEnglishMod.UI;
using KaoyanEnglishMod.Vocab;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace KaoyanEnglishMod.Relics;

public sealed class KaoyanLexicon : RelicModel
{
    private enum KaoyanChallengeMode
    {
        Undecided,
        Challenge,
        Declined,
    }

    private const double DefaultDynamicDifficultyBias = 0.62d;
    private const double MinDynamicDifficultyBias = 0.25d;
    private const double MaxDynamicDifficultyBias = 0.92d;
    private const double CorrectDifficultyBiasStep = 0.04d;
    private const double WrongDifficultyBiasStep = 0.08d;
    private const int CorrectStreakBeforeDifficultyIncrease = 2;

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
    private readonly HashSet<string> _runCorrectWordIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KaoyanWordRunStats> _runWordStats = new(StringComparer.Ordinal);
    private int _consecutiveCorrectAnswers;
    private double _dynamicDifficultyBias = DefaultDynamicDifficultyBias;
    private string? _forcedNextQuestionWordId;
    private bool _runSummaryLogged;
    private bool _combatStateLookupWarningLogged;
    private static bool s_multiplayerSyncWarningLogged;
    private static RunLocationTargetedMessageBuffer? s_registeredMessageBuffer;
    private readonly Dictionary<int, KaoyanQuestion> _multiplayerQuestions = new();
    private KaoyanQuestion? _pendingMultiplayerRewardQuestion;
    private int _pendingMultiplayerRewardAnswerIndex = -1;
    private int _pendingMultiplayerRewardQuestionHash;

    public override RelicRarity Rarity => RelicRarity.Starter;

    public override string PackedIconPath =>
        "res://KaoyanEnglishMod/images/atlases/relic_atlas.sprites/kaoyan_lexicon.tres";

    protected override string PackedIconOutlinePath =>
        "res://KaoyanEnglishMod/images/atlases/relic_outline_atlas.sprites/kaoyan_lexicon.tres";

    protected override string BigIconPath =>
        "res://KaoyanEnglishMod/images/packed/relics/kaoyan_lexicon.png";

    public override async Task BeforeSideTurnStart(
        PlayerChoiceContext choiceContext,
        CombatSide side,
        CombatState combatState)
    {
        if (side == Owner.Creature.Side)
        {
            EnsureCombatState(combatState);
        }

        await Task.CompletedTask;
    }

    public override async Task AfterSideTurnStart(CombatSide side, CombatState combatState)
    {
        if (side != Owner.Creature.Side)
        {
            return;
        }

        EnsureCombatState(combatState);
        if (IsMultiplayerRun())
        {
            ScheduleTurnStartKaoyanFlow(combatState);
            await Task.CompletedTask;
            return;
        }

        ScheduleTurnStartKaoyanFlow(combatState);
        await Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return;
        }

        var combatState = TryGetCurrentCombatState(choiceContext, player);
        if (combatState == null)
        {
            LogMissingCombatStateOnce();
            return;
        }

        EnsureCombatState(combatState);
        if (IsMultiplayerRun())
        {
            ScheduleTurnStartKaoyanFlow(combatState);
            await Task.CompletedTask;
            return;
        }

        ScheduleTurnStartKaoyanFlow(combatState);
        await Task.CompletedTask;
    }

    public override async Task BeforePlayPhaseStart(PlayerChoiceContext choiceContext, Player player)
    {
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
        _combatStateLookupWarningLogged = false;
        _multiplayerQuestions.Clear();
        Log.Warn("[KaoyanEnglishMod] New combat challenge state initialized.");
    }

    private bool IsMultiplayerRun()
    {
        var netService = RunManager.Instance?.NetService;
        return netService != null && netService.Type.IsMultiplayer();
    }

    private bool IsMultiplayerHost()
    {
        return RunManager.Instance?.NetService?.Type == NetGameType.Host;
    }

    private bool IsLocalOwner()
    {
        var netService = RunManager.Instance?.NetService;
        return netService == null || netService.Type == NetGameType.Singleplayer || IsMessageForThisOwner(netService.NetId);
    }

    private ulong GetOwnerNetId()
    {
        var runState = RunManager.Instance?.DebugOnlyGetState();
        var ownerInRunState = runState?.Players.FirstOrDefault(player => ReferenceEquals(player, Owner));
        return ownerInRunState?.NetId ?? Owner.NetId;
    }

    private bool IsMessageForThisOwner(ulong ownerNetId)
    {
        var runState = RunManager.Instance?.DebugOnlyGetState();
        var messageOwner = runState?.GetPlayer(ownerNetId);
        if (messageOwner != null)
        {
            return ReferenceEquals(messageOwner, Owner);
        }

        return Owner.NetId == ownerNetId;
    }

    private void EnsureMultiplayerHandlersRegistered()
    {
        var runManager = RunManager.Instance;
        if (runManager == null || runManager.NetService.Type == NetGameType.Singleplayer)
        {
            return;
        }

        var buffer = runManager.RunLocationTargetedBuffer;
        if (ReferenceEquals(s_registeredMessageBuffer, buffer))
        {
            return;
        }

        if (s_registeredMessageBuffer != null)
        {
            s_registeredMessageBuffer.UnregisterMessageHandler<KaoyanChallengeChoiceMessage>(HandleMultiplayerChallengeChoiceMessageForOwner);
            s_registeredMessageBuffer.UnregisterMessageHandler<KaoyanQuestionOfferedMessage>(HandleMultiplayerQuestionOfferedMessageForOwner);
            s_registeredMessageBuffer.UnregisterMessageHandler<KaoyanAnswerChosenMessage>(HandleMultiplayerAnswerChosenMessageForOwner);
            s_registeredMessageBuffer.UnregisterMessageHandler<KaoyanWrongPenaltyMessage>(HandleMultiplayerWrongPenaltyMessageForOwner);
            s_registeredMessageBuffer.UnregisterMessageHandler<KaoyanRewardChoiceMessage>(HandleMultiplayerRewardChoiceMessageForOwner);
            s_registeredMessageBuffer.UnregisterMessageHandler<KaoyanRewardAppliedMessage>(HandleMultiplayerRewardAppliedMessageForOwner);
        }

        buffer.RegisterMessageHandler<KaoyanChallengeChoiceMessage>(HandleMultiplayerChallengeChoiceMessageForOwner);
        buffer.RegisterMessageHandler<KaoyanQuestionOfferedMessage>(HandleMultiplayerQuestionOfferedMessageForOwner);
        buffer.RegisterMessageHandler<KaoyanAnswerChosenMessage>(HandleMultiplayerAnswerChosenMessageForOwner);
        buffer.RegisterMessageHandler<KaoyanWrongPenaltyMessage>(HandleMultiplayerWrongPenaltyMessageForOwner);
        buffer.RegisterMessageHandler<KaoyanRewardChoiceMessage>(HandleMultiplayerRewardChoiceMessageForOwner);
        buffer.RegisterMessageHandler<KaoyanRewardAppliedMessage>(HandleMultiplayerRewardAppliedMessageForOwner);
        s_registeredMessageBuffer = buffer;

        if (!s_multiplayerSyncWarningLogged)
        {
            s_multiplayerSyncWarningLogged = true;
            Log.Warn("[KaoyanEnglishMod] Multiplayer Kaoyan sync handlers registered.");
        }
    }

    private static KaoyanLexicon? FindMultiplayerLexiconForOwner(ulong ownerNetId)
    {
        var runState = RunManager.Instance?.DebugOnlyGetState();
        var owner = runState?.GetPlayer(ownerNetId);
        return owner?.Relics.OfType<KaoyanLexicon>().FirstOrDefault();
    }

    private static void HandleMultiplayerChallengeChoiceMessageForOwner(
        KaoyanChallengeChoiceMessage message,
        ulong senderId)
    {
        FindMultiplayerLexiconForOwner(message.OwnerNetId)?.HandleMultiplayerChallengeChoiceMessage(message, senderId);
    }

    private static void HandleMultiplayerQuestionOfferedMessageForOwner(
        KaoyanQuestionOfferedMessage message,
        ulong senderId)
    {
        FindMultiplayerLexiconForOwner(message.OwnerNetId)?.HandleMultiplayerQuestionOfferedMessage(message, senderId);
    }

    private static void HandleMultiplayerAnswerChosenMessageForOwner(
        KaoyanAnswerChosenMessage message,
        ulong senderId)
    {
        FindMultiplayerLexiconForOwner(message.OwnerNetId)?.HandleMultiplayerAnswerChosenMessage(message, senderId);
    }

    private static void HandleMultiplayerWrongPenaltyMessageForOwner(
        KaoyanWrongPenaltyMessage message,
        ulong senderId)
    {
        FindMultiplayerLexiconForOwner(message.OwnerNetId)?.HandleMultiplayerWrongPenaltyMessage(message, senderId);
    }

    private static void HandleMultiplayerRewardChoiceMessageForOwner(
        KaoyanRewardChoiceMessage message,
        ulong senderId)
    {
        FindMultiplayerLexiconForOwner(message.OwnerNetId)?.HandleMultiplayerRewardChoiceMessage(message, senderId);
    }

    private static void HandleMultiplayerRewardAppliedMessageForOwner(
        KaoyanRewardAppliedMessage message,
        ulong senderId)
    {
        FindMultiplayerLexiconForOwner(message.OwnerNetId)?.HandleMultiplayerRewardAppliedMessage(message, senderId);
    }

    private void ScheduleTurnStartKaoyanFlow(CombatState combatState)
    {
        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        if (IsMultiplayerRun())
        {
            ScheduleMultiplayerTurnStartKaoyanFlow(combatState);
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
    }

    private void ScheduleMultiplayerTurnStartKaoyanFlow(CombatState combatState)
    {
        if (!IsLocalOwner())
        {
            return;
        }

        if (combatState.RoundNumber == 1 && _challengeMode == KaoyanChallengeMode.Undecided)
        {
            ScheduleMultiplayerChallengePrompt(combatState.RoundNumber, combatState);
            return;
        }

        if (_challengeMode == KaoyanChallengeMode.Challenge)
        {
            ScheduleMultiplayerQuestionOffer(combatState.RoundNumber, combatState);
        }
    }

    private void ScheduleMultiplayerChallengePrompt(int roundNumber, CombatState combatState)
    {
        if (_challengePromptInProgress)
        {
            return;
        }

        _challengePromptInProgress = true;
        _ = ShowMultiplayerChallengePromptWhenSafeAsync(roundNumber, combatState);
    }

    private async Task ShowMultiplayerChallengePromptWhenSafeAsync(int roundNumber, CombatState combatState)
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
                    KaoyanChallengePopup.ShowChallenge(choice => HandleLocalMultiplayerChallengeChoice(roundNumber, choice, combatState));
                    return;
                }

                await DelayBeforeNextSafetyProbeAsync();
            }
        }
        catch (Exception exception)
        {
            _challengePromptInProgress = false;
            Log.Error("[KaoyanEnglishMod] Failed to show multiplayer challenge popup.");
            Log.Error(exception.ToString());
        }
    }

    private void HandleLocalMultiplayerChallengeChoice(int roundNumber, bool isChallengeSelected, CombatState combatState)
    {
        if (!ReferenceEquals(_currentCombatState, combatState) || _challengeMode != KaoyanChallengeMode.Undecided)
        {
            return;
        }

        _challengePromptInProgress = false;
        EnqueueKaoyanAction(new KaoyanChallengeChoiceAction(Owner, roundNumber, isChallengeSelected));

        if (isChallengeSelected)
        {
            _challengeMode = KaoyanChallengeMode.Challenge;
            Log.Warn("[KaoyanEnglishMod] Challenge selected.");
            ScheduleMultiplayerQuestionOffer(roundNumber, combatState);
            return;
        }

        _challengeMode = KaoyanChallengeMode.Declined;
        Log.Warn("[KaoyanEnglishMod] Challenge declined.");
    }

    private void HandleMultiplayerChallengeChoiceMessage(KaoyanChallengeChoiceMessage message, ulong senderId)
    {
        if (!IsMessageForThisOwner(message.OwnerNetId) || _currentCombatState == null)
        {
            return;
        }

        ApplyMultiplayerChallengeChoice(message.RoundNumber, message.IsChallengeSelected, _currentCombatState);
    }

    private void ApplyMultiplayerChallengeChoice(int roundNumber, bool isChallengeSelected, CombatState combatState)
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

        if (IsMultiplayerHost())
        {
            ScheduleMultiplayerQuestionOffer(roundNumber, combatState);
        }
    }

    private void ScheduleMultiplayerQuestionOffer(int roundNumber, CombatState combatState)
    {
        if (_lastQuestionRound == roundNumber
            || _pendingQuestionRound == roundNumber
            || _pendingQuestionTaskRound == roundNumber)
        {
            return;
        }

        _pendingQuestionRound = roundNumber;
        _pendingQuestionTaskRound = roundNumber;
        _ = ProcessMultiplayerQuestionOfferWhenSafeAsync(roundNumber, combatState);
    }

    private async Task ProcessMultiplayerQuestionOfferWhenSafeAsync(int roundNumber, CombatState combatState)
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

                _pendingQuestionRound = -1;
                var question = GenerateAndLogQuestion(roundNumber, registerSeen: false);
                var questionHash = CalculateQuestionHash(question, _dynamicDifficultyBias);
                _multiplayerQuestions[roundNumber] = question;

                EnqueueKaoyanAction(CreateQuestionStartedAction(roundNumber, question, questionHash));
                await ShowMultiplayerQuestionAfterQueuedStartAsync(roundNumber, question, questionHash, combatState);
                return;
            }
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed while preparing multiplayer question.");
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

    private async Task ShowMultiplayerQuestionAfterQueuedStartAsync(
        int roundNumber,
        KaoyanQuestion question,
        int questionHash,
        CombatState combatState)
    {
        while (ReferenceEquals(_currentCombatState, combatState)
            && _challengeMode == KaoyanChallengeMode.Challenge
            && _lastExtraDrawRound != roundNumber)
        {
            if (!IsKaoyanCombatStillActive(combatState))
            {
                CancelPendingKaoyanFlow();
                return;
            }

            await DelayBeforeNextSafetyProbeAsync();
        }

        while (ReferenceEquals(_currentCombatState, combatState)
            && _challengeMode == KaoyanChallengeMode.Challenge)
        {
            await WaitForCombatQueueToSettleAsync();

            if (!IsKaoyanCombatStillActive(combatState))
            {
                CancelPendingKaoyanFlow();
                return;
            }

            if (CanStartKaoyanQuestionNow(combatState, roundNumber, ignoreLastQuestionRound: true))
            {
                _questionInProgress = true;
                var popupShown = KaoyanQuestionPopup.ShowQuestion(question, answerIndex =>
                {
                    HandleLocalMultiplayerQuestionAnswer(roundNumber, question, questionHash, answerIndex, combatState);
                });

                if (!popupShown)
                {
                    _questionInProgress = false;
                    Log.Warn("[KaoyanEnglishMod] Failed to show multiplayer question popup; question was logged only.");
                }

                return;
            }

            await DelayBeforeNextSafetyProbeAsync();
        }
    }

    private async Task RunOfficialMultiplayerTurnStartFlowAsync(
        PlayerChoiceContext choiceContext,
        CombatState combatState)
    {
        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        if (combatState.RoundNumber == 1 && _challengeMode == KaoyanChallengeMode.Undecided)
        {
            var challengeSelected = await SelectMultiplayerIndexChoiceAsync(
                choiceContext,
                localChoiceFactory: ShowLocalChallengeChoiceAsync,
                cancelPlayCardActions: true);

            if (challengeSelected <= 0)
            {
                _challengeMode = KaoyanChallengeMode.Declined;
                _challengePromptInProgress = false;
                Log.Warn("[KaoyanEnglishMod] Challenge declined.");
                await ApplyDeclinedRewardSafelyAsync(combatState);
                return;
            }

            _challengeMode = KaoyanChallengeMode.Challenge;
            _challengePromptInProgress = false;
            Log.Warn("[KaoyanEnglishMod] Challenge selected.");
        }

        if (_challengeMode == KaoyanChallengeMode.Challenge)
        {
            await RunOfficialMultiplayerQuestionFlowAsync(choiceContext, combatState.RoundNumber, combatState);
        }
    }

    private async Task RunOfficialMultiplayerQuestionFlowAsync(
        PlayerChoiceContext choiceContext,
        int roundNumber,
        CombatState combatState)
    {
        if (_lastQuestionRound == roundNumber || _lastAnsweredRound == roundNumber)
        {
            return;
        }

        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        var question = GenerateAndLogDeterministicMultiplayerQuestion(roundNumber);
        _multiplayerQuestions[roundNumber] = question;

        await DrawExtraCardForChallengeSafelyAsync(roundNumber, combatState, choiceContext);

        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        _questionInProgress = true;
        var answerIndex = await SelectMultiplayerIndexChoiceAsync(
            choiceContext,
            () => ShowLocalQuestionChoiceAsync(question),
            cancelPlayCardActions: true);
        _questionInProgress = false;

        _lastAnsweredRound = roundNumber;
        if (answerIndex < 0)
        {
            Log.Warn($"[KaoyanEnglishMod] Question skipped. Round {roundNumber}.");
            CompleteKaoyanQuestionFlow();
            return;
        }

        if (answerIndex == question.CorrectIndex)
        {
            Log.Warn($"[KaoyanEnglishMod] Correct answer selected. Round {roundNumber}.");
            RegisterQuestionAnswered(question.Word, isCorrect: true);
            await SelectAndApplyOfficialMultiplayerRewardAsync(choiceContext, roundNumber, combatState);
            return;
        }

        Log.Warn(
            $"[KaoyanEnglishMod] Wrong answer selected. Round {roundNumber}. Selected {GetOptionLabel(answerIndex)}, correct {GetOptionLabel(question.CorrectIndex)}.");
        RegisterQuestionAnswered(question.Word, isCorrect: false);
        await ApplyWrongAnswerPenaltySafelyAsync(roundNumber, combatState, choiceContext);
    }

    private async Task SelectAndApplyOfficialMultiplayerRewardAsync(
        PlayerChoiceContext choiceContext,
        int roundNumber,
        CombatState combatState)
    {
        if (!IsKaoyanCombatStillActive(combatState) || _lastRewardChoiceRound == roundNumber)
        {
            CancelPendingKaoyanFlow();
            return;
        }

        _rewardChoiceInProgress = true;
        Log.Warn($"[KaoyanEnglishMod] Showing reward choice. Round {roundNumber}.");
        var rewardIndex = await SelectMultiplayerIndexChoiceAsync(
            choiceContext,
            ShowLocalRewardChoiceAsync,
            cancelPlayCardActions: true);
        _rewardChoiceInProgress = false;
        _lastRewardChoiceRound = roundNumber;

        if (!Enum.IsDefined(typeof(KaoyanRewardChoice), rewardIndex))
        {
            Log.Warn($"[KaoyanEnglishMod] Unknown reward selected: {rewardIndex}. Round {roundNumber}.");
            CompleteKaoyanQuestionFlow();
            return;
        }

        var choice = (KaoyanRewardChoice)rewardIndex;
        switch (choice)
        {
            case KaoyanRewardChoice.Replay:
            case KaoyanRewardChoice.FreeThisTurn:
                Log.Warn($"[KaoyanEnglishMod] Reward selected: {GetRewardLogName(choice)}. Round {roundNumber}.");
                await SelectHandCardForOfficialMultiplayerRewardSafelyAsync(
                    choiceContext,
                    roundNumber,
                    choice,
                    combatState);
                break;
            default:
                Log.Warn($"[KaoyanEnglishMod] Unknown reward selected: {choice}. Round {roundNumber}.");
                CompleteKaoyanQuestionFlow();
                break;
        }
    }

    private async Task SelectHandCardForOfficialMultiplayerRewardSafelyAsync(
        PlayerChoiceContext choiceContext,
        int roundNumber,
        KaoyanRewardChoice choice,
        CombatState combatState)
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
                choiceContext,
                Owner,
                new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1),
                null,
                this)).FirstOrDefault();

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
            Log.Error("[KaoyanEnglishMod] Failed to select multiplayer reward card.");
            Log.Error(exception.ToString());
        }
        finally
        {
            _handChoiceInProgress = false;
            CompleteKaoyanQuestionFlow();
        }
    }

    private async Task<int> SelectMultiplayerIndexChoiceAsync(
        PlayerChoiceContext choiceContext,
        Func<Task<int>> localChoiceFactory,
        bool cancelPlayCardActions)
    {
        var choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(Owner);
        await choiceContext.SignalPlayerChoiceBegun(
            cancelPlayCardActions ? PlayerChoiceOptions.CancelPlayCardActions : PlayerChoiceOptions.None);

        try
        {
            if (IsLocalOwner())
            {
                var choiceIndex = await localChoiceFactory();
                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(
                    Owner,
                    choiceId,
                    PlayerChoiceResult.FromIndex(choiceIndex));
                return choiceIndex;
            }

            return (await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(Owner, choiceId)).AsIndex();
        }
        finally
        {
            await choiceContext.SignalPlayerChoiceEnded();
        }
    }

    private Task<int> ShowLocalChallengeChoiceAsync()
    {
        var completionSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var modalContainer = NModalContainer.Instance;
        if (modalContainer == null || modalContainer.OpenModal != null)
        {
            Log.Warn("[KaoyanEnglishMod] Failed to show multiplayer challenge popup; challenge declined.");
            completionSource.TrySetResult(0);
            return completionSource.Task;
        }

        Flash();
        KaoyanChallengePopup.ShowChallenge(
            isChallengeSelected => completionSource.TrySetResult(isChallengeSelected ? 1 : 0));

        return completionSource.Task;
    }

    private Task<int> ShowLocalQuestionChoiceAsync(KaoyanQuestion question)
    {
        var completionSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupShown = KaoyanQuestionPopup.ShowQuestion(
            question,
            answerIndex => completionSource.TrySetResult(answerIndex ?? -1));
        if (!popupShown)
        {
            _questionInProgress = false;
            Log.Warn("[KaoyanEnglishMod] Failed to show multiplayer question popup; question skipped.");
            completionSource.TrySetResult(-1);
        }

        return completionSource.Task;
    }

    private Task<int> ShowLocalRewardChoiceAsync()
    {
        var completionSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupShown = KaoyanRewardChoicePopup.ShowRewardChoice(
            choice => completionSource.TrySetResult((int)choice));
        if (!popupShown)
        {
            _rewardChoiceInProgress = false;
            Log.Warn("[KaoyanEnglishMod] Failed to show multiplayer reward choice popup; reward skipped.");
            completionSource.TrySetResult(-1);
        }

        return completionSource.Task;
    }

    private KaoyanQuestion GenerateAndLogDeterministicMultiplayerQuestion(int roundNumber)
    {
        _lastQuestionRound = roundNumber;

        var vocabService = new KaoyanVocabService(new Random(GetDeterministicMultiplayerQuestionSeed(roundNumber)));
        vocabService.LoadFromGodotResource();
        var question = vocabService.CreateQuestion(
            _runCorrectWordIds,
            GetActiveMistakeCounts(),
            _forcedNextQuestionWordId,
            _dynamicDifficultyBias);
        RegisterQuestionSeen(question.Word);

        Log.Warn($"[KaoyanEnglishMod] Round {roundNumber} question:");
        Log.Warn($"[KaoyanEnglishMod] Word: {question.Word.Word}");
        Log.Warn($"[KaoyanEnglishMod] Rank: {question.Word.Rank}. Dynamic difficulty bias: {_dynamicDifficultyBias:0.00}.");
        Log.Warn($"[KaoyanEnglishMod] A. {question.Options[0]}");
        Log.Warn($"[KaoyanEnglishMod] B. {question.Options[1]}");
        Log.Warn($"[KaoyanEnglishMod] C. {question.Options[2]}");
        Log.Warn($"[KaoyanEnglishMod] D. {question.Options[3]}");
        Log.Warn($"[KaoyanEnglishMod] Correct: {question.Options[question.CorrectIndex]}");

        return question;
    }

    private int GetDeterministicMultiplayerQuestionSeed(int roundNumber)
    {
        var runSeed = RunManager.Instance?.DebugOnlyGetState()?.Rng.StringSeed ?? "KAOYAN";
        var forcedWordId = _forcedNextQuestionWordId ?? string.Empty;
        return StringHelper.GetDeterministicHashCode(
            $"{runSeed}|{GetOwnerNetId()}|{roundNumber}|{_runCorrectWordIds.Count}|{_consecutiveCorrectAnswers}|{_dynamicDifficultyBias:0.000}|{forcedWordId}");
    }

    private CombatState? TryGetCurrentCombatState(PlayerChoiceContext choiceContext, Player player)
    {
        if (_currentCombatState != null && _currentCombatState.ContainsCreature(player.Creature))
        {
            return _currentCombatState;
        }

        var reflectedCombatState = TryGetCombatStateFromChoiceContext(choiceContext);
        if (reflectedCombatState != null && reflectedCombatState.ContainsCreature(player.Creature))
        {
            return reflectedCombatState;
        }

        return null;
    }

    private static CombatState? TryGetCombatStateFromChoiceContext(PlayerChoiceContext choiceContext)
    {
        try
        {
            var contextType = choiceContext.GetType();
            foreach (var property in contextType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (typeof(CombatState).IsAssignableFrom(property.PropertyType)
                    && property.GetValue(choiceContext) is CombatState combatState)
                {
                    return combatState;
                }
            }

            foreach (var field in contextType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (typeof(CombatState).IsAssignableFrom(field.FieldType)
                    && field.GetValue(choiceContext) is CombatState combatState)
                {
                    return combatState;
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warn("[KaoyanEnglishMod] Failed to inspect player choice context for combat state.");
            Log.Error(exception.ToString());
        }

        return null;
    }

    private void LogMissingCombatStateOnce()
    {
        if (_combatStateLookupWarningLogged)
        {
            return;
        }

        _combatStateLookupWarningLogged = true;
        Log.Warn("[KaoyanEnglishMod] Could not resolve current combat state without Creature.CombatState; Kaoyan turn trigger skipped.");
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
        _pendingMultiplayerRewardQuestion = null;
        _pendingMultiplayerRewardAnswerIndex = -1;
        _pendingMultiplayerRewardQuestionHash = 0;
    }

    private void CancelPendingKaoyanFlow()
    {
        _pendingQuestionRound = -1;
        _pendingQuestionTaskRound = -1;
        _challengePromptInProgress = false;
        _questionInProgress = false;
        _rewardChoiceInProgress = false;
        _handChoiceInProgress = false;
        _pendingMultiplayerRewardQuestion = null;
        _pendingMultiplayerRewardAnswerIndex = -1;
        _pendingMultiplayerRewardQuestionHash = 0;
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
        await DrawExtraCardForChallengeSafelyAsync(roundNumber, combatState, new BlockingPlayerChoiceContext());
    }

    private async Task DrawExtraCardForChallengeSafelyAsync(
        int roundNumber,
        CombatState combatState,
        PlayerChoiceContext choiceContext)
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
            await CardPileCmd.Draw(choiceContext, 1m, Owner);
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

    private KaoyanQuestion GenerateAndLogQuestion(int roundNumber, bool registerSeen = true)
    {
        _lastQuestionRound = roundNumber;

        _vocabService ??= new KaoyanVocabService();
        if (!_vocabLoaded)
        {
            _vocabService.LoadFromGodotResource();
            _vocabLoaded = true;
        }

        var question = _vocabService.CreateQuestion(
            _runCorrectWordIds,
            GetActiveMistakeCounts(),
            _forcedNextQuestionWordId,
            _dynamicDifficultyBias);
        if (registerSeen)
        {
            RegisterQuestionSeen(question.Word);
        }

        Log.Warn($"[KaoyanEnglishMod] Round {roundNumber} question:");
        Log.Warn($"[KaoyanEnglishMod] Word: {question.Word.Word}");
        Log.Warn($"[KaoyanEnglishMod] Rank: {question.Word.Rank}. Dynamic difficulty bias: {_dynamicDifficultyBias:0.00}.");
        Log.Warn($"[KaoyanEnglishMod] A. {question.Options[0]}");
        Log.Warn($"[KaoyanEnglishMod] B. {question.Options[1]}");
        Log.Warn($"[KaoyanEnglishMod] C. {question.Options[2]}");
        Log.Warn($"[KaoyanEnglishMod] D. {question.Options[3]}");
        Log.Warn($"[KaoyanEnglishMod] Correct: {question.Options[question.CorrectIndex]}");

        return question;
    }

    private KaoyanQuestionOfferedMessage CreateQuestionOfferedMessage(int roundNumber, KaoyanQuestion question)
    {
        return new KaoyanQuestionOfferedMessage
        {
            OwnerNetId = GetOwnerNetId(),
            RoundNumber = roundNumber,
            WordId = question.Word.Id,
            Rank = question.Word.Rank,
            Word = question.Word.Word,
            ZhMeaning = question.Word.ZhMeaning,
            Difficulty = question.Word.Difficulty,
            SpawnWeight = question.Word.SpawnWeight,
            OptionA = question.Options.Count > 0 ? question.Options[0] : string.Empty,
            OptionB = question.Options.Count > 1 ? question.Options[1] : string.Empty,
            OptionC = question.Options.Count > 2 ? question.Options[2] : string.Empty,
            OptionD = question.Options.Count > 3 ? question.Options[3] : string.Empty,
            CorrectIndex = question.CorrectIndex,
            DynamicDifficultyBias = _dynamicDifficultyBias,
            LocationValue = GetCurrentRunLocation(),
        };
    }

    private static KaoyanQuestion CreateQuestionFromMessage(KaoyanQuestionOfferedMessage message)
    {
        var word = new KaoyanWord
        {
            Id = message.WordId,
            Rank = message.Rank,
            Word = message.Word,
            ZhMeaning = message.ZhMeaning,
            Difficulty = message.Difficulty,
            SpawnWeight = message.SpawnWeight,
        };

        return new KaoyanQuestion(
            word,
            new List<string> { message.OptionA, message.OptionB, message.OptionC, message.OptionD },
            message.CorrectIndex);
    }

    private void HandleMultiplayerQuestionOfferedMessage(KaoyanQuestionOfferedMessage message, ulong senderId)
    {
        if (!IsMessageForThisOwner(message.OwnerNetId) || _currentCombatState == null)
        {
            return;
        }

        _ = ApplyMultiplayerQuestionOfferAsync(message, _currentCombatState, CreateQuestionFromMessage(message), questionAlreadyLogged: false);
    }

    private async Task ApplyMultiplayerQuestionOfferAsync(
        KaoyanQuestionOfferedMessage message,
        CombatState combatState,
        KaoyanQuestion question,
        bool questionAlreadyLogged)
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

            if (!questionAlreadyLogged)
            {
                _lastQuestionRound = message.RoundNumber;
                _dynamicDifficultyBias = message.DynamicDifficultyBias;
                RegisterQuestionSeen(question.Word);
                LogMultiplayerQuestion(message.RoundNumber, question, message.DynamicDifficultyBias);
            }

            _multiplayerQuestions[message.RoundNumber] = question;
            _pendingQuestionRound = -1;

            await DrawExtraCardForChallengeSafelyAsync(message.RoundNumber, combatState);
            await WaitForCombatQueueToSettleAsync();

            if (!IsLocalOwner())
            {
                return;
            }

            while (ReferenceEquals(_currentCombatState, combatState)
                && IsKaoyanCombatStillActive(combatState)
                && !CanStartKaoyanQuestionNow(combatState, message.RoundNumber, ignoreLastQuestionRound: true))
            {
                await DelayBeforeNextSafetyProbeAsync();
            }

            if (!IsKaoyanCombatStillActive(combatState))
            {
                CancelPendingKaoyanFlow();
                return;
            }

            _questionInProgress = true;
            var questionHash = CalculateQuestionHash(question, message.DynamicDifficultyBias);
            var popupShown = KaoyanQuestionPopup.ShowQuestion(question, answerIndex =>
            {
                HandleLocalMultiplayerQuestionAnswer(message.RoundNumber, question, questionHash, answerIndex, combatState);
            });

            if (!popupShown)
            {
                _questionInProgress = false;
                Log.Warn("[KaoyanEnglishMod] Failed to show multiplayer question popup; question was logged only.");
            }
        }
        catch (Exception exception)
        {
            _questionInProgress = false;
            Log.Error("[KaoyanEnglishMod] Failed to apply multiplayer question offer.");
            Log.Error(exception.ToString());
        }
    }

    private static void LogMultiplayerQuestion(int roundNumber, KaoyanQuestion question, double dynamicDifficultyBias)
    {
        Log.Warn($"[KaoyanEnglishMod] Round {roundNumber} question:");
        Log.Warn($"[KaoyanEnglishMod] Word: {question.Word.Word}");
        Log.Warn($"[KaoyanEnglishMod] Rank: {question.Word.Rank}. Dynamic difficulty bias: {dynamicDifficultyBias:0.00}.");
        Log.Warn($"[KaoyanEnglishMod] A. {question.Options[0]}");
        Log.Warn($"[KaoyanEnglishMod] B. {question.Options[1]}");
        Log.Warn($"[KaoyanEnglishMod] C. {question.Options[2]}");
        Log.Warn($"[KaoyanEnglishMod] D. {question.Options[3]}");
        Log.Warn($"[KaoyanEnglishMod] Correct: {question.Options[question.CorrectIndex]}");
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
            RegisterQuestionAnswered(question.Word, isCorrect: true);
            ShowRewardChoiceSafely(roundNumber, combatState);
            return;
        }

        Log.Warn(
            $"[KaoyanEnglishMod] Wrong answer selected. Round {roundNumber}. Selected {GetOptionLabel(answerIndex.Value)}, correct {GetOptionLabel(question.CorrectIndex)}.");
        RegisterQuestionAnswered(question.Word, isCorrect: false);
        _ = ApplyWrongAnswerPenaltySafelyAsync(roundNumber, combatState);
    }

    private void HandleLocalMultiplayerQuestionAnswer(
        int roundNumber,
        KaoyanQuestion question,
        int questionHash,
        int? answerIndex,
        CombatState combatState)
    {
        if (_lastAnsweredRound == roundNumber)
        {
            return;
        }

        var selectedAnswerIndex = answerIndex ?? -1;
        _questionInProgress = false;

        if (selectedAnswerIndex < 0)
        {
            EnqueueKaoyanAction(CreateQuestionOutcomeAction(
                roundNumber,
                question,
                selectedAnswerIndex,
                questionHash,
                hasPenaltyCard: false,
                penaltyCardId: 0,
                rewardChoice: -1,
                hasRewardCard: false,
                rewardCardId: 0));
            return;
        }

        if (selectedAnswerIndex == question.CorrectIndex)
        {
            _pendingMultiplayerRewardQuestion = question;
            _pendingMultiplayerRewardAnswerIndex = selectedAnswerIndex;
            _pendingMultiplayerRewardQuestionHash = questionHash;
            ShowMultiplayerRewardChoiceSafely(roundNumber, combatState);
            return;
        }

        var penaltyCard = SelectMultiplayerPenaltyCard(roundNumber, questionHash);
        var penaltyCardId = 0u;
        var hasPenaltyCard = penaltyCard != null && NetCombatCardDb.Instance.TryGetCardId(penaltyCard, out penaltyCardId);
        EnqueueKaoyanAction(CreateQuestionOutcomeAction(
            roundNumber,
            question,
            selectedAnswerIndex,
            questionHash,
            hasPenaltyCard,
            hasPenaltyCard ? penaltyCardId : 0,
            rewardChoice: -1,
            hasRewardCard: false,
            rewardCardId: 0));
    }

    private void HandleMultiplayerAnswerChosenMessage(KaoyanAnswerChosenMessage message, ulong senderId)
    {
        if (!IsMessageForThisOwner(message.OwnerNetId) || _currentCombatState == null)
        {
            return;
        }

        if (!_multiplayerQuestions.TryGetValue(message.RoundNumber, out var question))
        {
            Log.Warn($"[KaoyanEnglishMod] Multiplayer answer ignored: missing question for round {message.RoundNumber}.");
            return;
        }

        ApplyMultiplayerAnswerChoice(message.RoundNumber, question, message.AnswerIndex, _currentCombatState);
    }

    private void ApplyMultiplayerAnswerChoice(
        int roundNumber,
        KaoyanQuestion question,
        int answerIndex,
        CombatState combatState)
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

        if (answerIndex < 0)
        {
            Log.Warn($"[KaoyanEnglishMod] Question skipped. Round {roundNumber}.");
            CompleteKaoyanQuestionFlow();
            return;
        }

        if (answerIndex == question.CorrectIndex)
        {
            Log.Warn($"[KaoyanEnglishMod] Correct answer selected. Round {roundNumber}.");
            RegisterQuestionAnswered(question.Word, isCorrect: true);

            if (IsLocalOwner())
            {
                ShowMultiplayerRewardChoiceSafely(roundNumber, combatState);
            }

            return;
        }

        Log.Warn(
            $"[KaoyanEnglishMod] Wrong answer selected. Round {roundNumber}. Selected {GetOptionLabel(answerIndex)}, correct {GetOptionLabel(question.CorrectIndex)}.");
        RegisterQuestionAnswered(question.Word, isCorrect: false);

        if (IsMultiplayerHost())
        {
            _ = SendAndApplyMultiplayerWrongPenaltyAsync(roundNumber, combatState);
        }
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

    private void ShowMultiplayerRewardChoiceSafely(int roundNumber, CombatState combatState)
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
                HandleLocalMultiplayerRewardChoice(roundNumber, choice, combatState);
            });

            if (!popupShown)
            {
                _rewardChoiceInProgress = false;
                CompleteKaoyanQuestionFlow();
                EnqueuePendingMultiplayerRewardOutcome(roundNumber, rewardChoice: -1, hasRewardCard: false, rewardCardId: 0);
                Log.Warn("[KaoyanEnglishMod] Failed to show multiplayer reward choice popup; reward skipped.");
            }
        }
        catch (Exception exception)
        {
            _rewardChoiceInProgress = false;
            CompleteKaoyanQuestionFlow();
            EnqueuePendingMultiplayerRewardOutcome(roundNumber, rewardChoice: -1, hasRewardCard: false, rewardCardId: 0);
            Log.Error("[KaoyanEnglishMod] Failed to show multiplayer reward choice popup.");
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
                SendMultiplayerRewardApplied(roundNumber, choice, hasCard: false, cardId: 0);
                break;
        }
    }

    private void HandleLocalMultiplayerRewardChoice(int roundNumber, KaoyanRewardChoice choice, CombatState combatState)
    {
        if (!IsKaoyanCombatStillActive(combatState))
        {
            CancelPendingKaoyanFlow();
            return;
        }

        switch (choice)
        {
            case KaoyanRewardChoice.Replay:
            case KaoyanRewardChoice.FreeThisTurn:
                Log.Warn($"[KaoyanEnglishMod] Reward selected: {GetRewardLogName(choice)}. Round {roundNumber}.");
                _lastRewardChoiceRound = roundNumber;
                _rewardChoiceInProgress = false;
                _ = SelectHandCardForMultiplayerRewardWithOfficialUiSafelyAsync(roundNumber, choice, GetRewardLogName(choice), combatState);
                break;
            default:
                Log.Warn($"[KaoyanEnglishMod] Unknown reward selected: {choice}. Round {roundNumber}.");
                CompleteKaoyanQuestionFlow();
                EnqueuePendingMultiplayerRewardOutcome(roundNumber, rewardChoice: -1, hasRewardCard: false, rewardCardId: 0);
                break;
        }
    }

    private void HandleMultiplayerRewardChoiceMessage(KaoyanRewardChoiceMessage message, ulong senderId)
    {
        Log.Warn($"[KaoyanEnglishMod] Multiplayer reward choice received for owner {message.OwnerNetId}: {(KaoyanRewardChoice)message.RewardChoice}. Waiting for applied reward message.");
    }

    private void BeginMultiplayerRewardCardSelection(
        int roundNumber,
        KaoyanRewardChoice choice,
        CombatState combatState)
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
            case KaoyanRewardChoice.FreeThisTurn:
                _ = SelectHandCardForMultiplayerRewardSafelyAsync(roundNumber, choice, combatState);
                break;
            default:
                Log.Warn($"[KaoyanEnglishMod] Unknown multiplayer reward selected: {choice}. Round {roundNumber}.");
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

    private async Task SelectHandCardForMultiplayerRewardSafelyAsync(
        int roundNumber,
        KaoyanRewardChoice choice,
        CombatState combatState)
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
            Log.Error("[KaoyanEnglishMod] Failed to select multiplayer reward card.");
            Log.Error(exception.ToString());
            CompleteKaoyanQuestionFlow();
        }
        finally
        {
            _handChoiceInProgress = false;
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

    private async Task SelectHandCardForMultiplayerRewardWithOfficialUiSafelyAsync(
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
                EnqueuePendingMultiplayerRewardOutcome(roundNumber, (int)choice, hasRewardCard: false, rewardCardId: 0);
                return;
            }

            var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
            if (handCards.Count == 0)
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: hand is empty.");
                CompleteKaoyanQuestionFlow();
                EnqueuePendingMultiplayerRewardOutcome(roundNumber, (int)choice, hasRewardCard: false, rewardCardId: 0);
                return;
            }

            _handChoiceInProgress = true;
            Log.Warn($"[KaoyanEnglishMod] Selecting a hand card for {rewardName} reward. Round {roundNumber}.");

            if (NCombatRoom.Instance?.Ui?.Hand == null)
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: official hand UI is unavailable.");
                _handChoiceInProgress = false;
                CompleteKaoyanQuestionFlow();
                EnqueuePendingMultiplayerRewardOutcome(roundNumber, (int)choice, hasRewardCard: false, rewardCardId: 0);
                return;
            }

            NPlayerHand.Instance?.CancelAllCardPlay();
            var selectedCard = (await NCombatRoom.Instance.Ui.Hand.SelectCards(
                new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1),
                null,
                null)).FirstOrDefault();
            _handChoiceInProgress = false;

            if (selectedCard == null || !NetCombatCardDb.Instance.TryGetCardId(selectedCard, out var cardId))
            {
                Log.Warn($"[KaoyanEnglishMod] {rewardName} reward skipped: no valid hand card selected.");
                CompleteKaoyanQuestionFlow();
                EnqueuePendingMultiplayerRewardOutcome(roundNumber, (int)choice, hasRewardCard: false, rewardCardId: 0);
                return;
            }

            CompleteKaoyanQuestionFlow();
            await DelayForHandSelectionUiSettleAsync();
            EnqueuePendingMultiplayerRewardOutcome(roundNumber, (int)choice, hasRewardCard: true, rewardCardId: cardId);
        }
        catch (Exception exception)
        {
            _handChoiceInProgress = false;
            CompleteKaoyanQuestionFlow();
            EnqueuePendingMultiplayerRewardOutcome(roundNumber, (int)choice, hasRewardCard: false, rewardCardId: 0);
            Log.Error("[KaoyanEnglishMod] Failed to select multiplayer reward card with official hand UI.");
            Log.Error(exception.ToString());
        }
    }

    private void EnqueuePendingMultiplayerRewardOutcome(
        int roundNumber,
        int rewardChoice,
        bool hasRewardCard,
        uint rewardCardId)
    {
        if (_pendingMultiplayerRewardQuestion == null)
        {
            Log.Warn("[KaoyanEnglishMod] Multiplayer reward outcome skipped: missing pending question.");
            return;
        }

        EnqueueKaoyanAction(CreateQuestionOutcomeAction(
            roundNumber,
            _pendingMultiplayerRewardQuestion,
            _pendingMultiplayerRewardAnswerIndex,
            _pendingMultiplayerRewardQuestionHash,
            hasPenaltyCard: false,
            penaltyCardId: 0,
            rewardChoice,
            hasRewardCard,
            rewardCardId));

        _pendingMultiplayerRewardQuestion = null;
        _pendingMultiplayerRewardAnswerIndex = -1;
        _pendingMultiplayerRewardQuestionHash = 0;
    }

    private CardModel? SelectMultiplayerPenaltyCard(int roundNumber, int questionHash)
    {
        var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
        if (handCards.Count == 0)
        {
            return null;
        }

        var cardsWithIds = handCards
            .Select(card => new
            {
                Card = card,
                HasId = NetCombatCardDb.Instance.TryGetCardId(card, out var id),
                Id = id,
            })
            .Where(item => item.HasId)
            .OrderBy(item => item.Id)
            .ToList();
        if (cardsWithIds.Count == 0)
        {
            return null;
        }

        var index = Math.Abs(questionHash ^ (roundNumber * 397)) % cardsWithIds.Count;
        return cardsWithIds[index].Card;
    }

    private KaoyanQuestionStartedAction CreateQuestionStartedAction(
        int roundNumber,
        KaoyanQuestion question,
        int questionHash)
    {
        return new KaoyanQuestionStartedAction(
            Owner,
            roundNumber,
            question.Word.Id,
            question.Word.Rank,
            question.Word.Word,
            question.Word.ZhMeaning,
            question.Word.Difficulty,
            question.Word.SpawnWeight,
            questionHash,
            _dynamicDifficultyBias);
    }

    private KaoyanQuestionOutcomeAction CreateQuestionOutcomeAction(
        int roundNumber,
        KaoyanQuestion question,
        int answerIndex,
        int questionHash,
        bool hasPenaltyCard,
        uint penaltyCardId,
        int rewardChoice,
        bool hasRewardCard,
        uint rewardCardId)
    {
        return new KaoyanQuestionOutcomeAction(
            Owner,
            roundNumber,
            question.Word.Id,
            question.Word.Rank,
            question.Word.Word,
            question.Word.ZhMeaning,
            question.Word.Difficulty,
            question.Word.SpawnWeight,
            answerIndex,
            question.CorrectIndex,
            questionHash,
            hasPenaltyCard,
            penaltyCardId,
            rewardChoice,
            hasRewardCard,
            rewardCardId);
    }

    private void EnqueueKaoyanAction(GameAction action)
    {
        try
        {
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to enqueue Kaoyan multiplayer action.");
            Log.Error(exception.ToString());
        }
    }

    private static int CalculateQuestionHash(KaoyanQuestion question, double dynamicDifficultyBias)
    {
        var text = string.Join(
            "|",
            question.Word.Id,
            question.Word.Rank.ToString(),
            question.Word.Word,
            question.Word.ZhMeaning,
            question.CorrectIndex.ToString(),
            dynamicDifficultyBias.ToString("0.000"),
            string.Join("/", question.Options));
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var character in text)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash;
        }
    }

    private static async Task DelayForHandSelectionUiSettleAsync()
    {
        await Task.Delay(50);
    }

    private void SendMultiplayerRewardApplied(int roundNumber, KaoyanRewardChoice choice, bool hasCard, uint cardId)
    {
        SendMultiplayerMessage(new KaoyanRewardAppliedMessage
        {
            OwnerNetId = GetOwnerNetId(),
            RoundNumber = roundNumber,
            RewardChoice = (int)choice,
            HasCard = hasCard,
            CardId = cardId,
            LocationValue = GetCurrentRunLocation(),
        });
    }

    private void HandleMultiplayerRewardAppliedMessage(KaoyanRewardAppliedMessage message, ulong senderId)
    {
        if (!IsMessageForThisOwner(message.OwnerNetId) || _currentCombatState == null)
        {
            return;
        }

        var choice = (KaoyanRewardChoice)message.RewardChoice;
        _lastRewardChoiceRound = message.RoundNumber;
        _rewardChoiceInProgress = false;

        if (!message.HasCard)
        {
            Log.Warn($"[KaoyanEnglishMod] Multiplayer {GetRewardLogName(choice)} reward skipped: no card selected.");
            CompleteKaoyanQuestionFlow();
            return;
        }

        if (!NetCombatCardDb.Instance.TryGetCard(message.CardId, out var card) || card == null)
        {
            Log.Warn($"[KaoyanEnglishMod] Multiplayer {GetRewardLogName(choice)} reward skipped: card id {message.CardId} was not found.");
            CompleteKaoyanQuestionFlow();
            return;
        }

        ApplyRewardToCardSafely(message.RoundNumber, choice, card, _currentCombatState);
        CompleteKaoyanQuestionFlow();
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
        await ApplyWrongAnswerPenaltySafelyAsync(roundNumber, combatState, new BlockingPlayerChoiceContext());
    }

    private async Task ApplyWrongAnswerPenaltySafelyAsync(
        int roundNumber,
        CombatState combatState,
        PlayerChoiceContext choiceContext)
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
            await CardCmd.Exhaust(choiceContext, cardToExhaust);
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

    private async Task SendAndApplyMultiplayerWrongPenaltyAsync(int roundNumber, CombatState combatState)
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

            var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
            if (handCards.Count == 0)
            {
                var emptyMessage = new KaoyanWrongPenaltyMessage
                {
                    OwnerNetId = GetOwnerNetId(),
                    RoundNumber = roundNumber,
                    HasCard = false,
                    CardId = 0,
                    LocationValue = GetCurrentRunLocation(),
                };
                SendMultiplayerMessage(emptyMessage);
                await ApplyMultiplayerWrongPenaltyAsync(emptyMessage, combatState, advanceRngBeforeApplying: false);
                return;
            }

            var cardToExhaust = Owner.RunState.Rng.CombatCardSelection.NextItem(handCards);
            uint cardId = 0;
            var hasCardId = cardToExhaust != null && NetCombatCardDb.Instance.TryGetCardId(cardToExhaust, out cardId);
            var message = new KaoyanWrongPenaltyMessage
            {
                OwnerNetId = GetOwnerNetId(),
                RoundNumber = roundNumber,
                HasCard = hasCardId,
                CardId = hasCardId ? cardId : 0,
                LocationValue = GetCurrentRunLocation(),
            };

            SendMultiplayerMessage(message);
            await ApplyMultiplayerWrongPenaltyAsync(message, combatState, advanceRngBeforeApplying: false);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to send multiplayer wrong answer penalty.");
            Log.Error(exception.ToString());
            CompleteKaoyanQuestionFlow();
        }
    }

    private void HandleMultiplayerWrongPenaltyMessage(KaoyanWrongPenaltyMessage message, ulong senderId)
    {
        if (!IsMessageForThisOwner(message.OwnerNetId) || _currentCombatState == null)
        {
            return;
        }

        _ = ApplyMultiplayerWrongPenaltyAsync(message, _currentCombatState, advanceRngBeforeApplying: true);
    }

    private async Task ApplyMultiplayerWrongPenaltyAsync(
        KaoyanWrongPenaltyMessage message,
        CombatState combatState,
        bool advanceRngBeforeApplying)
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

            Log.Warn($"[KaoyanEnglishMod] Applying wrong answer penalty: exhaust 1 random hand card. Round {message.RoundNumber}.");

            var handCards = PileType.Hand.GetPile(Owner).Cards.ToList();
            if (handCards.Count == 0 || !message.HasCard)
            {
                Log.Warn("[KaoyanEnglishMod] Wrong answer penalty skipped: hand is empty.");
                return;
            }

            if (advanceRngBeforeApplying)
            {
                Owner.RunState.Rng.CombatCardSelection.NextItem(handCards);
            }

            if (!NetCombatCardDb.Instance.TryGetCard(message.CardId, out var cardToExhaust) || cardToExhaust == null)
            {
                Log.Warn($"[KaoyanEnglishMod] Wrong answer penalty skipped: card id {message.CardId} was not found.");
                return;
            }

            var cardLabel = GetCardLogLabel(cardToExhaust);
            await CardCmd.Exhaust(new BlockingPlayerChoiceContext(), cardToExhaust);
            Log.Warn($"[KaoyanEnglishMod] Wrong answer penalty exhausted card: {cardLabel}.");
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to apply multiplayer wrong answer penalty.");
            Log.Error(exception.ToString());
        }
        finally
        {
            CompleteKaoyanQuestionFlow();
        }
    }

    private Dictionary<string, int> GetActiveMistakeCounts()
    {
        return _runWordStats
            .Where(pair => pair.Value.WrongAnswers > 0 && !_runCorrectWordIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value.WrongAnswers, StringComparer.Ordinal);
    }

    private void SendMultiplayerMessage<T>(T message)
        where T : struct, MegaCrit.Sts2.Core.Multiplayer.Serialization.INetMessage
    {
        try
        {
            var netService = RunManager.Instance?.NetService;
            if (netService == null || netService.Type == NetGameType.Singleplayer)
            {
                return;
            }

            netService.SendMessage(message);
        }
        catch (Exception exception)
        {
            Log.Error("[KaoyanEnglishMod] Failed to send multiplayer sync message.");
            Log.Error(exception.ToString());
        }
    }

    private static MegaCrit.Sts2.Core.Runs.RunLocation GetCurrentRunLocation()
    {
        return RunManager.Instance.RunLocationTargetedBuffer.CurrentLocation;
    }

    private void RegisterQuestionSeen(KaoyanWord word)
    {
        var stats = GetOrCreateRunWordStats(word);
        stats.TimesShown++;
    }

    private void RegisterQuestionAnswered(KaoyanWord word, bool isCorrect)
    {
        var stats = GetOrCreateRunWordStats(word);
        if (isCorrect)
        {
            stats.CorrectAnswers++;
            stats.MasteredAfterMistake |= stats.WrongAnswers > 0;
            _runCorrectWordIds.Add(word.Id);
            _consecutiveCorrectAnswers++;

            if (_forcedNextQuestionWordId == word.Id)
            {
                _forcedNextQuestionWordId = null;
            }

            if (_consecutiveCorrectAnswers >= CorrectStreakBeforeDifficultyIncrease)
            {
                _dynamicDifficultyBias = Math.Min(
                    MaxDynamicDifficultyBias,
                    _dynamicDifficultyBias + CorrectDifficultyBiasStep);
            }
        }
        else
        {
            stats.WrongAnswers++;
            _consecutiveCorrectAnswers = 0;
            _dynamicDifficultyBias = Math.Max(
                MinDynamicDifficultyBias,
                _dynamicDifficultyBias - WrongDifficultyBiasStep);

            if (stats.WrongAnswers >= 2)
            {
                _forcedNextQuestionWordId = word.Id;
                Log.Warn($"[KaoyanEnglishMod] Word will be forced again after repeated mistakes: {word.Word}.");
            }
        }

        Log.Warn(
            $"[KaoyanEnglishMod] Run vocab state: mastered={_runCorrectWordIds.Count}, activeMistakes={GetActiveMistakeCounts().Count}, correctStreak={_consecutiveCorrectAnswers}, difficultyBias={_dynamicDifficultyBias:0.00}.");
    }

    internal static async Task ApplyQueuedChallengeChoiceAsync(
        Player player,
        int roundNumber,
        bool isChallengeSelected)
    {
        var lexicon = player.Relics.OfType<KaoyanLexicon>().FirstOrDefault();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (lexicon == null || combatState == null)
        {
            return;
        }

        lexicon.EnsureCombatState(combatState);
        if (!lexicon.IsKaoyanCombatStillActive(combatState))
        {
            lexicon.CancelPendingKaoyanFlow();
            return;
        }

        lexicon._challengePromptInProgress = false;
        if (isChallengeSelected)
        {
            if (lexicon._challengeMode != KaoyanChallengeMode.Challenge)
            {
                Log.Warn("[KaoyanEnglishMod] Challenge selected.");
            }

            lexicon._challengeMode = KaoyanChallengeMode.Challenge;
            return;
        }

        lexicon._challengeMode = KaoyanChallengeMode.Declined;
        Log.Warn("[KaoyanEnglishMod] Challenge declined.");
        await lexicon.ApplyDeclinedRewardSafelyAsync(combatState);
    }

    internal static async Task ApplyQueuedQuestionStartedAsync(
        Player player,
        int roundNumber,
        string wordId,
        int rank,
        string word,
        string zhMeaning,
        string difficulty,
        int spawnWeight,
        int questionHash,
        double dynamicDifficultyBias)
    {
        var lexicon = player.Relics.OfType<KaoyanLexicon>().FirstOrDefault();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (lexicon == null || combatState == null)
        {
            return;
        }

        lexicon.EnsureCombatState(combatState);
        if (lexicon._challengeMode == KaoyanChallengeMode.Undecided)
        {
            lexicon._challengeMode = KaoyanChallengeMode.Challenge;
        }

        if (!lexicon.IsKaoyanCombatStillActive(combatState))
        {
            lexicon.CancelPendingKaoyanFlow();
            return;
        }

        lexicon._pendingQuestionRound = -1;
        lexicon._lastQuestionRound = roundNumber;
        lexicon._dynamicDifficultyBias = dynamicDifficultyBias;
        lexicon.RegisterQuestionSeen(new KaoyanWord
        {
            Id = wordId,
            Rank = rank,
            Word = word,
            ZhMeaning = zhMeaning,
            Difficulty = difficulty,
            SpawnWeight = spawnWeight,
        });

        Log.Warn($"[KaoyanEnglishMod] Multiplayer question start action applied. Round {roundNumber}, hash {questionHash}.");
        await lexicon.DrawExtraCardForChallengeSafelyAsync(roundNumber, combatState, new BlockingPlayerChoiceContext());
    }

    internal static async Task ApplyQueuedQuestionOutcomeAsync(
        Player player,
        int roundNumber,
        string wordId,
        int rank,
        string word,
        string zhMeaning,
        string difficulty,
        int spawnWeight,
        int answerIndex,
        int correctIndex,
        int questionHash,
        bool hasPenaltyCard,
        uint penaltyCardId,
        int rewardChoice,
        bool hasRewardCard,
        uint rewardCardId)
    {
        var lexicon = player.Relics.OfType<KaoyanLexicon>().FirstOrDefault();
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (lexicon == null || combatState == null)
        {
            return;
        }

        try
        {
            lexicon.EnsureCombatState(combatState);
            if (!lexicon.IsKaoyanCombatStillActive(combatState))
            {
                lexicon.CancelPendingKaoyanFlow();
                return;
            }

            if (lexicon._lastAnsweredRound == roundNumber)
            {
                return;
            }

            lexicon._lastAnsweredRound = roundNumber;
            var answeredWord = new KaoyanWord
            {
                Id = wordId,
                Rank = rank,
                Word = word,
                ZhMeaning = zhMeaning,
                Difficulty = difficulty,
                SpawnWeight = spawnWeight,
            };

            if (answerIndex < 0)
            {
                Log.Warn($"[KaoyanEnglishMod] Question skipped. Round {roundNumber}. Hash {questionHash}.");
                return;
            }

            if (answerIndex == correctIndex)
            {
                Log.Warn($"[KaoyanEnglishMod] Correct answer selected. Round {roundNumber}. Hash {questionHash}.");
                lexicon.RegisterQuestionAnswered(answeredWord, isCorrect: true);
                await lexicon.ApplyQueuedRewardOutcomeAsync(
                    roundNumber,
                    rewardChoice,
                    hasRewardCard,
                    rewardCardId,
                    combatState);
                return;
            }

            Log.Warn(
                $"[KaoyanEnglishMod] Wrong answer selected. Round {roundNumber}. Selected {GetOptionLabel(answerIndex)}, correct {GetOptionLabel(correctIndex)}. Hash {questionHash}.");
            lexicon.RegisterQuestionAnswered(answeredWord, isCorrect: false);
            await lexicon.ApplyQueuedWrongPenaltyAsync(roundNumber, hasPenaltyCard, penaltyCardId, combatState);
        }
        finally
        {
            lexicon.CompleteKaoyanQuestionFlow();
        }
    }

    private async Task ApplyQueuedWrongPenaltyAsync(
        int roundNumber,
        bool hasPenaltyCard,
        uint penaltyCardId,
        CombatState combatState)
    {
        Log.Warn($"[KaoyanEnglishMod] Applying wrong answer penalty: exhaust 1 random hand card. Round {roundNumber}.");
        if (!hasPenaltyCard)
        {
            Log.Warn("[KaoyanEnglishMod] Wrong answer penalty skipped: hand is empty.");
            return;
        }

        if (!NetCombatCardDb.Instance.TryGetCard(penaltyCardId, out var cardToExhaust) || cardToExhaust == null)
        {
            Log.Warn($"[KaoyanEnglishMod] Wrong answer penalty skipped: card id {penaltyCardId} was not found.");
            return;
        }

        if (!PileType.Hand.GetPile(Owner).Cards.Contains(cardToExhaust))
        {
            Log.Warn("[KaoyanEnglishMod] Wrong answer penalty skipped: selected card is no longer in hand.");
            return;
        }

        var cardLabel = GetCardLogLabel(cardToExhaust);
        await CardCmd.Exhaust(new BlockingPlayerChoiceContext(), cardToExhaust);
        Log.Warn($"[KaoyanEnglishMod] Wrong answer penalty exhausted card: {cardLabel}.");
    }

    private async Task ApplyQueuedRewardOutcomeAsync(
        int roundNumber,
        int rewardChoice,
        bool hasRewardCard,
        uint rewardCardId,
        CombatState combatState)
    {
        if (!Enum.IsDefined(typeof(KaoyanRewardChoice), rewardChoice))
        {
            Log.Warn($"[KaoyanEnglishMod] Multiplayer reward skipped: no reward selected. Round {roundNumber}.");
            return;
        }

        var choice = (KaoyanRewardChoice)rewardChoice;
        _lastRewardChoiceRound = roundNumber;
        if (!hasRewardCard)
        {
            Log.Warn($"[KaoyanEnglishMod] {GetRewardLogName(choice)} reward skipped: hand is empty or no card selected.");
            return;
        }

        if (!NetCombatCardDb.Instance.TryGetCard(rewardCardId, out var rewardCard) || rewardCard == null)
        {
            Log.Warn($"[KaoyanEnglishMod] {GetRewardLogName(choice)} reward skipped: card id {rewardCardId} was not found.");
            return;
        }

        ApplyRewardToCardSafely(roundNumber, choice, rewardCard, combatState);
        await Task.CompletedTask;
    }

    public void LogRunVocabSummary(bool isVictory)
    {
        if (_runSummaryLogged)
        {
            return;
        }

        _runSummaryLogged = true;

        if (_runWordStats.Count == 0)
        {
            Log.Warn(
                $"[KaoyanEnglishMod] Run vocab summary ({GetRunResultLabel(isVictory)}): no Kaoyan questions answered this run.");
            return;
        }

        var totalQuestions = _runWordStats.Values.Sum(stats => stats.TimesShown);
        var wrongWords = _runWordStats.Values
            .Where(stats => stats.WrongAnswers > 0)
            .OrderByDescending(stats => stats.WrongAnswers)
            .ThenBy(stats => stats.Word.Rank)
            .ToList();

        Log.Warn(
            $"[KaoyanEnglishMod] Run vocab summary ({GetRunResultLabel(isVictory)}): questions={totalQuestions}, wrongWords={wrongWords.Count}, mastered={_runCorrectWordIds.Count}, activeMistakes={GetActiveMistakeCounts().Count}, difficultyBias={_dynamicDifficultyBias:0.00}.");

        if (wrongWords.Count == 0)
        {
            Log.Warn("[KaoyanEnglishMod] Run vocab summary: no wrong words this run.");
            return;
        }

        foreach (var stats in wrongWords)
        {
            Log.Warn(
                $"[KaoyanEnglishMod] Run wrong word: {stats.Word.Word} | rank={stats.Word.Rank} | shown={stats.TimesShown} | wrong={stats.WrongAnswers} | correct={stats.CorrectAnswers} | masteredAfterMistake={stats.MasteredAfterMistake} | meaning={stats.Word.ZhMeaning}.");
        }

        KaoyanRunSummaryPopup.ShowRunSummary(
            wrongWords.Select(stats => new KaoyanRunSummaryWord(
                stats.Word.Word,
                stats.Word.ZhMeaning,
                stats.Word.Rank,
                stats.TimesShown,
                stats.WrongAnswers,
                stats.CorrectAnswers,
                stats.MasteredAfterMistake)).ToList(),
            isVictory);
    }

    private KaoyanWordRunStats GetOrCreateRunWordStats(KaoyanWord word)
    {
        if (_runWordStats.TryGetValue(word.Id, out var stats))
        {
            stats.Word = word;
            return stats;
        }

        stats = new KaoyanWordRunStats(word);
        _runWordStats[word.Id] = stats;
        return stats;
    }

    private bool CanStartKaoyanQuestionNow(
        CombatState combatState,
        int roundNumber,
        bool allowChallengePrompt = false,
        bool ignoreLastQuestionRound = false)
    {
        if (!ignoreLastQuestionRound && _lastQuestionRound == roundNumber)
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

    private static string GetRunResultLabel(bool isVictory)
    {
        return isVictory ? "victory" : "defeat";
    }

    private sealed class KaoyanWordRunStats(KaoyanWord word)
    {
        public KaoyanWord Word { get; set; } = word;

        public int TimesShown { get; set; }

        public int CorrectAnswers { get; set; }

        public int WrongAnswers { get; set; }

        public bool MasteredAfterMistake { get; set; }
    }

}
