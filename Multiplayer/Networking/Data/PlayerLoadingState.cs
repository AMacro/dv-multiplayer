namespace Multiplayer.Networking.Data;

/// <summary>
/// Represents the loading state of a client during the connection process.
/// States progress sequentially from BaseData to Complete.
/// </summary>
public enum PlayerLoadingState : byte
{
    None,
    ReadyForGameData,
    ReadyForWorldState,
    ReadyForTrainSets,
    ReadyForCustomizers,
    ReadyForItems,
    ReadyForJobs,
    ReadyForTiles,
    Complete
}
