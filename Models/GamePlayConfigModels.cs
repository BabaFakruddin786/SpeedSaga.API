namespace SpeedSaga.API.Models;

public record GamePlayRewardModeDto(
    string Code,
    string DisplayName,
    string? HintText,
    decimal RewardMultiplier,
    decimal TimeLimitFactor,
    int SortOrder);

public record GamePlayTimeModeDto(
    string Code,
    string DisplayLabel,
    int BaseSeconds,
    int SortOrder);

public record GamePlayEntryFeeDto(
    long EntryFeePaise,
    int SortOrder);

public record GamePlayConfigDto(
    string GameType,
    string PlayMode,
    IReadOnlyList<GamePlayRewardModeDto> RewardModes,
    IReadOnlyList<GamePlayTimeModeDto> TimeModes,
    IReadOnlyList<GamePlayEntryFeeDto> EntryFees,
    int TwoPlayerPoolPercent);
