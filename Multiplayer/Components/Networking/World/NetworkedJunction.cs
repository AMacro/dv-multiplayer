using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Components.Networking.World;

public class NetworkedJunction : IdMonoBehaviour<ushort, NetworkedJunction>
{
    #region Lookup Cache
    private static NetworkedJunction[] _indexedJunctions;
    private static readonly Dictionary<Junction, NetworkedJunction> junctionToNetworkedJunction = [];
    public static NetworkedJunction[] IndexedJunctions => _indexedJunctions ??= RailTrackRegistry.Instance.TrackRootParent.GetComponentsInChildren<NetworkedJunction>().OrderBy(nj => nj.NetId).ToArray();

    public static bool Get(ushort netId, out NetworkedJunction obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedJunction> rawObj);
        obj = (NetworkedJunction)rawObj;
        return b;
    }

    public static bool TryGet(ushort netId, out Junction junction)
    {
        if(Get(netId, out var networkedJunction))
        {
            junction = networkedJunction.Junction;
            return true;
        }

        junction = null;
        return false;
    }

    public static bool TryGetNetId(Junction junction, out ushort netId)
    {
        if (junctionToNetworkedJunction.TryGetValue(junction, out var networkedJunction))
        {
            netId = networkedJunction.NetId;
            return true;
        }

        netId = 0;
        return false;
    }

    #endregion

    protected override bool IsIdServerAuthoritative => false;

    public Junction Junction;
    private bool initialised = false;

    protected override void Awake()
    {
        base.Awake();
        Junction = GetComponent<Junction>();
        Junction.Switched += Junction_Switched;
        junctionToNetworkedJunction[Junction] = this;

        initialised = NetworkLifecycle.Instance.IsHost();
    }
    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (UnloadWatcher.isQuitting)
            return;

        junctionToNetworkedJunction.Remove(Junction);
    }

    private void Junction_Switched(Junction.SwitchMode switchMode, int branch)
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket || !initialised)
            return;

        NetworkLifecycle.Instance.Client.SendJunctionSwitched(NetId, (byte)branch, switchMode);
    }

    public void Switch(byte mode, byte selectedBranch, bool initialising = false)
    {
        //B99
        Junction.Switch((Junction.SwitchMode)mode, selectedBranch);

        if (!initialised && initialising)
            initialised = true;
    }
}
