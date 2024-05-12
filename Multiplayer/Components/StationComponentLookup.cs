using System.Collections.Generic;
using DV.Logic.Job;
using DV.Utils;
using JetBrains.Annotations;
using Multiplayer.Components.Networking.World;

namespace Multiplayer.Components;

public class StationComponentLookup : SingletonBehaviour<StationComponentLookup>
{
    private readonly Dictionary<Station, NetworkedStation> stationToNetworkedStationController = new();
    private readonly Dictionary<string, NetworkedStation> stationIdToNetworkedStation = new();
    private readonly Dictionary<string, StationController> stationIdToStationController = new();

    public void RegisterStation(StationController stationController)
    {
        var networkedStation = stationController.GetComponent<NetworkedStation>();
        stationToNetworkedStationController[stationController.logicStation] = networkedStation;
        stationIdToNetworkedStation[stationController.logicStation.ID] = networkedStation;
        stationIdToStationController[stationController.logicStation.ID] = stationController;
    }

    public void UnregisterStation(StationController stationController)
    {
        stationToNetworkedStationController.Remove(stationController.logicStation);
        stationIdToNetworkedStation.Remove(stationController.logicStation.ID);
        stationIdToStationController.Remove(stationController.logicStation.ID);
    }

    public bool NetworkedStationFromStation(Station station, out NetworkedStation networkedStation)
    {
        return stationToNetworkedStationController.TryGetValue(station, out networkedStation);
    }

    public bool NetworkedStationFromId(string stationId, out NetworkedStation networkedStation)
    {
        return stationIdToNetworkedStation.TryGetValue(stationId, out networkedStation);
    }

    public bool StationControllerFromId(string stationId, out StationController stationController)
    {
        return stationIdToStationController.TryGetValue(stationId, out stationController);
    }

    [UsedImplicitly]
    public new static string AllowAutoCreate()
    {
        return $"[{nameof(StationComponentLookup)}]";
    }
}
