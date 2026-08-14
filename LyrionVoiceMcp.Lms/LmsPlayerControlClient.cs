using LyrionVoiceMcp.Abstractions;

namespace LyrionVoiceMcp.Lms;

public sealed class LmsPlayerControlClient(LmsJsonRpcClient jsonRpcClient)
    : ILmsPlayerControlClient
{
    public async Task ControlAsync(
        string playerId,
        PlayerControlCommand command,
        CancellationToken cancellationToken)
    {
        switch (command)
        {
            case PlayerControlCommand.Resume:
                await SendAsync(playerId, ["play"], cancellationToken);
                break;
            case PlayerControlCommand.Pause:
                await SendAsync(playerId, ["pause", 1], cancellationToken);
                break;
            case PlayerControlCommand.Stop:
                await SendAsync(playerId, ["stop"], cancellationToken);
                break;
            case PlayerControlCommand.Next:
                await SendAsync(
                    playerId,
                    ["playlist", "index", "+1"],
                    cancellationToken);
                break;
            case PlayerControlCommand.Previous:
                await SendAsync(
                    playerId,
                    ["playlist", "index", "-1"],
                    cancellationToken);
                break;
            case PlayerControlCommand.PowerOn:
                await SetPowerAsync(playerId, poweredOn: true, cancellationToken);
                break;
            case PlayerControlCommand.PowerOff:
                await SetPowerAsync(playerId, poweredOn: false, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(command),
                    command,
                    "Unsupported player control command.");
        }
    }

    private async Task SetPowerAsync(
        string playerId,
        bool poweredOn,
        CancellationToken cancellationToken)
    {
        var command = poweredOn
            ? new object[] { "power", 1, 1 }
            : ["power", 0];
        await SendAsync(playerId, command, cancellationToken);

        var result = await jsonRpcClient.SendAsync(
            playerId,
            ["power", "?"],
            cancellationToken);
        try
        {
            var actualState = LmsJson.ReadRequiredBoolean(
                result,
                "_power",
                "power");
            if (actualState != poweredOn)
            {
                var state = poweredOn ? "on" : "off";
                throw new LmsRequestException(
                    $"LMS did not power {state} the selected player.");
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new LmsRequestException(exception.Message, exception);
        }
    }

    private async Task SendAsync(
        string playerId,
        object[] command,
        CancellationToken cancellationToken)
    {
        await jsonRpcClient.SendAsync(playerId, command, cancellationToken);
    }
}
