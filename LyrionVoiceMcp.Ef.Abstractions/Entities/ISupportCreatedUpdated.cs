namespace LyrionVoiceMcp.Ef.Abstractions.Entities;

public interface ISupportCreatedUpdated
{
    DateTime CreatedAtUtc { get; set; }

    DateTime UpdatedAtUtc { get; set; }
}
