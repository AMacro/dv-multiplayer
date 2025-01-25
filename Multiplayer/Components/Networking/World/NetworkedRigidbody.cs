using Multiplayer.Networking.Data;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedRigidbody : TickedQueue<RigidbodySnapshot>
{
    private Rigidbody rigidbody;

    protected override void OnEnable()
    {
        rigidbody = GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            Multiplayer.LogError($"{gameObject.name}: {nameof(NetworkedRigidbody)} requires a {nameof(Rigidbody)} component on the same GameObject!");
            return;
        }

        base.OnEnable();
    }

    protected override void Process(RigidbodySnapshot snapshot, uint snapshotTick)
    {
        snapshot.Apply(rigidbody);
    }
}
