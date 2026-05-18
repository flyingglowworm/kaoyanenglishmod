using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace KaoyanEnglishMod.Multiplayer;

public struct KaoyanChallengeChoiceMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
{
    public ulong OwnerNetId;
    public int RoundNumber;
    public bool IsChallengeSelected;
    public RunLocation LocationValue;

    public readonly bool ShouldBroadcast => true;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.VeryDebug;
    public readonly RunLocation Location => LocationValue;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteULong(OwnerNetId);
        writer.WriteInt(RoundNumber);
        writer.WriteBool(IsChallengeSelected);
        writer.Write(LocationValue);
    }

    public void Deserialize(PacketReader reader)
    {
        OwnerNetId = reader.ReadULong();
        RoundNumber = reader.ReadInt();
        IsChallengeSelected = reader.ReadBool();
        LocationValue = reader.Read<RunLocation>();
    }
}

public struct KaoyanQuestionOfferedMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
{
    public ulong OwnerNetId;
    public int RoundNumber;
    public string WordId;
    public int Rank;
    public string Word;
    public string ZhMeaning;
    public string Difficulty;
    public int SpawnWeight;
    public string OptionA;
    public string OptionB;
    public string OptionC;
    public string OptionD;
    public int CorrectIndex;
    public double DynamicDifficultyBias;
    public RunLocation LocationValue;

    public readonly bool ShouldBroadcast => true;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.VeryDebug;
    public readonly RunLocation Location => LocationValue;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteULong(OwnerNetId);
        writer.WriteInt(RoundNumber);
        writer.WriteString(WordId ?? string.Empty);
        writer.WriteInt(Rank);
        writer.WriteString(Word ?? string.Empty);
        writer.WriteString(ZhMeaning ?? string.Empty);
        writer.WriteString(Difficulty ?? string.Empty);
        writer.WriteInt(SpawnWeight);
        writer.WriteString(OptionA ?? string.Empty);
        writer.WriteString(OptionB ?? string.Empty);
        writer.WriteString(OptionC ?? string.Empty);
        writer.WriteString(OptionD ?? string.Empty);
        writer.WriteInt(CorrectIndex);
        writer.WriteDouble(DynamicDifficultyBias);
        writer.Write(LocationValue);
    }

    public void Deserialize(PacketReader reader)
    {
        OwnerNetId = reader.ReadULong();
        RoundNumber = reader.ReadInt();
        WordId = reader.ReadString();
        Rank = reader.ReadInt();
        Word = reader.ReadString();
        ZhMeaning = reader.ReadString();
        Difficulty = reader.ReadString();
        SpawnWeight = reader.ReadInt();
        OptionA = reader.ReadString();
        OptionB = reader.ReadString();
        OptionC = reader.ReadString();
        OptionD = reader.ReadString();
        CorrectIndex = reader.ReadInt();
        DynamicDifficultyBias = reader.ReadDouble();
        LocationValue = reader.Read<RunLocation>();
    }
}

public struct KaoyanAnswerChosenMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
{
    public ulong OwnerNetId;
    public int RoundNumber;
    public int AnswerIndex;
    public RunLocation LocationValue;

    public readonly bool ShouldBroadcast => true;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.VeryDebug;
    public readonly RunLocation Location => LocationValue;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteULong(OwnerNetId);
        writer.WriteInt(RoundNumber);
        writer.WriteInt(AnswerIndex);
        writer.Write(LocationValue);
    }

    public void Deserialize(PacketReader reader)
    {
        OwnerNetId = reader.ReadULong();
        RoundNumber = reader.ReadInt();
        AnswerIndex = reader.ReadInt();
        LocationValue = reader.Read<RunLocation>();
    }
}

public struct KaoyanWrongPenaltyMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
{
    public ulong OwnerNetId;
    public int RoundNumber;
    public bool HasCard;
    public uint CardId;
    public RunLocation LocationValue;

    public readonly bool ShouldBroadcast => true;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.VeryDebug;
    public readonly RunLocation Location => LocationValue;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteULong(OwnerNetId);
        writer.WriteInt(RoundNumber);
        writer.WriteBool(HasCard);
        writer.WriteUInt(CardId);
        writer.Write(LocationValue);
    }

    public void Deserialize(PacketReader reader)
    {
        OwnerNetId = reader.ReadULong();
        RoundNumber = reader.ReadInt();
        HasCard = reader.ReadBool();
        CardId = reader.ReadUInt();
        LocationValue = reader.Read<RunLocation>();
    }
}

public struct KaoyanRewardChoiceMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
{
    public ulong OwnerNetId;
    public int RoundNumber;
    public int RewardChoice;
    public RunLocation LocationValue;

    public readonly bool ShouldBroadcast => true;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.VeryDebug;
    public readonly RunLocation Location => LocationValue;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteULong(OwnerNetId);
        writer.WriteInt(RoundNumber);
        writer.WriteInt(RewardChoice);
        writer.Write(LocationValue);
    }

    public void Deserialize(PacketReader reader)
    {
        OwnerNetId = reader.ReadULong();
        RoundNumber = reader.ReadInt();
        RewardChoice = reader.ReadInt();
        LocationValue = reader.Read<RunLocation>();
    }
}

public struct KaoyanRewardAppliedMessage : INetMessage, IPacketSerializable, IRunLocationTargetedMessage
{
    public ulong OwnerNetId;
    public int RoundNumber;
    public int RewardChoice;
    public bool HasCard;
    public uint CardId;
    public RunLocation LocationValue;

    public readonly bool ShouldBroadcast => true;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.VeryDebug;
    public readonly RunLocation Location => LocationValue;

    public readonly void Serialize(PacketWriter writer)
    {
        writer.WriteULong(OwnerNetId);
        writer.WriteInt(RoundNumber);
        writer.WriteInt(RewardChoice);
        writer.WriteBool(HasCard);
        writer.WriteUInt(CardId);
        writer.Write(LocationValue);
    }

    public void Deserialize(PacketReader reader)
    {
        OwnerNetId = reader.ReadULong();
        RoundNumber = reader.ReadInt();
        RewardChoice = reader.ReadInt();
        HasCard = reader.ReadBool();
        CardId = reader.ReadUInt();
        LocationValue = reader.Read<RunLocation>();
    }
}
