namespace LyrionVoiceMcp.Abstractions;

public enum PlayerControlCommand
{
    Resume,
    Pause,
    Stop,
    Next,
    Previous,
    PowerOn,
    PowerOff
}

public enum PlayerControlRejectionReason
{
    InvalidPlayer,
    InvalidAction,
    PlayerNotFound,
    AmbiguousPlayer
}

public abstract record PlayerControlOutcome;

public sealed record PlayerControlSucceeded(LmsPlayerStatus Player)
    : PlayerControlOutcome;

public sealed record PlayerControlRejected(
    PlayerControlRejectionReason Reason,
    string Message) : PlayerControlOutcome;

public interface ILmsPlayerControlClient
{
    Task ControlAsync(
        string playerId,
        PlayerControlCommand command,
        CancellationToken cancellationToken);
}

public interface IPlayerControlService
{
    Task<PlayerControlOutcome> ControlAsync(
        string playerSelector,
        PlayerControlCommand command,
        CancellationToken cancellationToken);
}
