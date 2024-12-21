using Multiplayer.Networking.Data.Train;
using System;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.Train;

public class NetworkedRigidbody : TickedQueue<RigidbodySnapshot>
{
    private const int MAX_FRAMES = 60;
    private Rigidbody rigidbody;

    protected override void OnEnable()
    {
        StartCoroutine(WaitForRB());
    }

    protected IEnumerator WaitForRB()
    {
        int counter = 0;

        while (rigidbody == null && counter < MAX_FRAMES)
        {
            rigidbody = GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                counter++;
                yield return new WaitForEndOfFrame();
            }
        }

        base.OnEnable();

        if (rigidbody == null)
        {
            gameObject.TryGetComponent(out TrainCar car);

            Multiplayer.LogError($"{gameObject.name} ({car?.ID}): {nameof(NetworkedBogie)} requires a {nameof(Bogie)} component on the same GameObject! Waited {counter} iterations");
        }
    }

    protected override void Process(RigidbodySnapshot snapshot, uint snapshotTick)
    {
        if (snapshot == null)
        {
            Multiplayer.LogError($"NetworkedRigidBody.Process() Snapshot NULL!");
            return;
        }

        try
        {
            Multiplayer.LogDebug(() => $"NetworkedRigidBody.Process() {snapshot.IncludedDataFlags}, {snapshot.Position.ToString() ?? "null"}, {snapshot.Rotation.ToString() ?? "null"}, {snapshot.Velocity.ToString() ?? "null"}, {snapshot.AngularVelocity.ToString() ?? "null"}");
            snapshot.Apply(rigidbody);
        }
        catch (Exception ex)
        {
            Multiplayer.LogError($"NetworkedRigidBody.Process() {ex.Message}\r\n {ex.StackTrace}");
        }
    }
}
