using System.Linq;
using DV;

namespace Multiplayer.Components.Networking.World;

public class NetworkedJunction : IdMonoBehaviour<ushort, NetworkedJunction>
{
    private static NetworkedJunction[] _indexedJunctions;
    public static NetworkedJunction[] IndexedJunctions => _indexedJunctions ??= WorldData.Instance.TrackRootParent.GetComponentsInChildren<NetworkedJunction>().OrderBy(nj => nj.NetId).ToArray();

    protected override bool IsIdServerAuthoritative => false;

    public Junction Junction;
    private bool initialised = false;

    protected override void Awake()
    {
        base.Awake();
        Junction = GetComponent<Junction>();
        Junction.Switched += Junction_Switched;
    }

    private void Junction_Switched(Junction.SwitchMode switchMode, int branch)
    {
        if (NetworkLifecycle.Instance.IsProcessingPacket)
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

    public static bool Get(ushort netId, out NetworkedJunction obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedJunction> rawObj);
        obj = (NetworkedJunction)rawObj;
        return b;
    }
}
