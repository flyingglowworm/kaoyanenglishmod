using System.Threading.Tasks;
using KaoyanEnglishMod.Relics;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace KaoyanEnglishMod.Multiplayer;

public sealed class KaoyanChallengeChoiceAction(Player player, int roundNumber, bool isChallengeSelected) : GameAction
{
    public override ulong OwnerId => player.NetId;

    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    protected override async Task ExecuteAction()
    {
        await KaoyanLexicon.ApplyQueuedChallengeChoiceAsync(player, roundNumber, isChallengeSelected);
    }

    public override INetAction ToNetAction()
    {
        return new NetKaoyanChallengeChoiceAction
        {
            RoundNumber = roundNumber,
            IsChallengeSelected = isChallengeSelected,
        };
    }

    public override string ToString()
    {
        return $"KaoyanChallengeChoiceAction round {roundNumber} selected {isChallengeSelected}";
    }
}

public struct NetKaoyanChallengeChoiceAction : INetAction, IPacketSerializable
{
    public int RoundNumber;
    public bool IsChallengeSelected;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteInt(RoundNumber);
        writer.WriteBool(IsChallengeSelected);
    }

    public void Deserialize(PacketReader reader)
    {
        RoundNumber = reader.ReadInt();
        IsChallengeSelected = reader.ReadBool();
    }

    public readonly GameAction ToGameAction(Player player)
    {
        return new KaoyanChallengeChoiceAction(player, RoundNumber, IsChallengeSelected);
    }
}

public sealed class KaoyanQuestionStartedAction(
    Player player,
    int roundNumber,
    string wordId,
    int rank,
    string word,
    string zhMeaning,
    string difficulty,
    int spawnWeight,
    int questionHash,
    double dynamicDifficultyBias) : GameAction
{
    public override ulong OwnerId => player.NetId;

    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    protected override async Task ExecuteAction()
    {
        await KaoyanLexicon.ApplyQueuedQuestionStartedAsync(
            player,
            roundNumber,
            wordId,
            rank,
            word,
            zhMeaning,
            difficulty,
            spawnWeight,
            questionHash,
            dynamicDifficultyBias);
    }

    public override INetAction ToNetAction()
    {
        return new NetKaoyanQuestionStartedAction
        {
            RoundNumber = roundNumber,
            WordId = wordId,
            Rank = rank,
            Word = word,
            ZhMeaning = zhMeaning,
            Difficulty = difficulty,
            SpawnWeight = spawnWeight,
            QuestionHash = questionHash,
            DynamicDifficultyBias = dynamicDifficultyBias,
        };
    }

    public override string ToString()
    {
        return $"KaoyanQuestionStartedAction round {roundNumber} word {wordId} hash {questionHash}";
    }
}

public struct NetKaoyanQuestionStartedAction : INetAction, IPacketSerializable
{
    public int RoundNumber;
    public string WordId;
    public int Rank;
    public string Word;
    public string ZhMeaning;
    public string Difficulty;
    public int SpawnWeight;
    public int QuestionHash;
    public double DynamicDifficultyBias;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteInt(RoundNumber);
        writer.WriteString(WordId ?? string.Empty);
        writer.WriteInt(Rank);
        writer.WriteString(Word ?? string.Empty);
        writer.WriteString(ZhMeaning ?? string.Empty);
        writer.WriteString(Difficulty ?? string.Empty);
        writer.WriteInt(SpawnWeight);
        writer.WriteInt(QuestionHash);
        writer.WriteDouble(DynamicDifficultyBias);
    }

    public void Deserialize(PacketReader reader)
    {
        RoundNumber = reader.ReadInt();
        WordId = reader.ReadString();
        Rank = reader.ReadInt();
        Word = reader.ReadString();
        ZhMeaning = reader.ReadString();
        Difficulty = reader.ReadString();
        SpawnWeight = reader.ReadInt();
        QuestionHash = reader.ReadInt();
        DynamicDifficultyBias = reader.ReadDouble();
    }

    public readonly GameAction ToGameAction(Player player)
    {
        return new KaoyanQuestionStartedAction(
            player,
            RoundNumber,
            WordId,
            Rank,
            Word,
            ZhMeaning,
            Difficulty,
            SpawnWeight,
            QuestionHash,
            DynamicDifficultyBias);
    }
}

public sealed class KaoyanQuestionOutcomeAction(
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
    uint rewardCardId) : GameAction
{
    public override ulong OwnerId => player.NetId;

    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    protected override async Task ExecuteAction()
    {
        await KaoyanLexicon.ApplyQueuedQuestionOutcomeAsync(
            player,
            roundNumber,
            wordId,
            rank,
            word,
            zhMeaning,
            difficulty,
            spawnWeight,
            answerIndex,
            correctIndex,
            questionHash,
            hasPenaltyCard,
            penaltyCardId,
            rewardChoice,
            hasRewardCard,
            rewardCardId);
    }

    public override INetAction ToNetAction()
    {
        return new NetKaoyanQuestionOutcomeAction
        {
            RoundNumber = roundNumber,
            WordId = wordId,
            Rank = rank,
            Word = word,
            ZhMeaning = zhMeaning,
            Difficulty = difficulty,
            SpawnWeight = spawnWeight,
            AnswerIndex = answerIndex,
            CorrectIndex = correctIndex,
            QuestionHash = questionHash,
            HasPenaltyCard = hasPenaltyCard,
            PenaltyCardId = penaltyCardId,
            RewardChoice = rewardChoice,
            HasRewardCard = hasRewardCard,
            RewardCardId = rewardCardId,
        };
    }

    public override string ToString()
    {
        return $"KaoyanQuestionOutcomeAction round {roundNumber} answer {answerIndex} hash {questionHash}";
    }
}

public struct NetKaoyanQuestionOutcomeAction : INetAction, IPacketSerializable
{
    public int RoundNumber;
    public string WordId;
    public int Rank;
    public string Word;
    public string ZhMeaning;
    public string Difficulty;
    public int SpawnWeight;
    public int AnswerIndex;
    public int CorrectIndex;
    public int QuestionHash;
    public bool HasPenaltyCard;
    public uint PenaltyCardId;
    public int RewardChoice;
    public bool HasRewardCard;
    public uint RewardCardId;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteInt(RoundNumber);
        writer.WriteString(WordId ?? string.Empty);
        writer.WriteInt(Rank);
        writer.WriteString(Word ?? string.Empty);
        writer.WriteString(ZhMeaning ?? string.Empty);
        writer.WriteString(Difficulty ?? string.Empty);
        writer.WriteInt(SpawnWeight);
        writer.WriteInt(AnswerIndex);
        writer.WriteInt(CorrectIndex);
        writer.WriteInt(QuestionHash);
        writer.WriteBool(HasPenaltyCard);
        writer.WriteUInt(PenaltyCardId);
        writer.WriteInt(RewardChoice);
        writer.WriteBool(HasRewardCard);
        writer.WriteUInt(RewardCardId);
    }

    public void Deserialize(PacketReader reader)
    {
        RoundNumber = reader.ReadInt();
        WordId = reader.ReadString();
        Rank = reader.ReadInt();
        Word = reader.ReadString();
        ZhMeaning = reader.ReadString();
        Difficulty = reader.ReadString();
        SpawnWeight = reader.ReadInt();
        AnswerIndex = reader.ReadInt();
        CorrectIndex = reader.ReadInt();
        QuestionHash = reader.ReadInt();
        HasPenaltyCard = reader.ReadBool();
        PenaltyCardId = reader.ReadUInt();
        RewardChoice = reader.ReadInt();
        HasRewardCard = reader.ReadBool();
        RewardCardId = reader.ReadUInt();
    }

    public readonly GameAction ToGameAction(Player player)
    {
        return new KaoyanQuestionOutcomeAction(
            player,
            RoundNumber,
            WordId,
            Rank,
            Word,
            ZhMeaning,
            Difficulty,
            SpawnWeight,
            AnswerIndex,
            CorrectIndex,
            QuestionHash,
            HasPenaltyCard,
            PenaltyCardId,
            RewardChoice,
            HasRewardCard,
            RewardCardId);
    }
}
