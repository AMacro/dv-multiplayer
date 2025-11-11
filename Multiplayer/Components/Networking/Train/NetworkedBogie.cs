using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Train;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.Train;

public class NetworkedBogie : TickedQueue<BogieData>
{
    private const int MAX_FRAMES = 60;
    public Bogie Bogie { get; private set; }
    private Rigidbody rb;

    protected override void OnEnable()
    {
        StartCoroutine(WaitForBogie());
    }

    protected IEnumerator WaitForBogie()
    {
        int counter = 0;

        while (Bogie == null || rb == null && counter < MAX_FRAMES)
        {
            if (Bogie == null)
                Bogie = GetComponent<Bogie>();

            if (rb == null)
                rb = GetComponent<Rigidbody>();

            if (rb == null || Bogie == null)
            {
                counter++;
                yield return new WaitForEndOfFrame();
            }
        }

        base.OnEnable();

        if (Bogie == null)
        {
            Multiplayer.LogError($"{gameObject.name} ({Bogie?.Car?.ID}): {nameof(NetworkedBogie)} requires a {nameof(Bogie)} component on the same GameObject! Waited {counter} iterations");
        }

        if (rb == null)
        {
            Multiplayer.LogError($"{gameObject.name} ({Bogie?.Car?.ID}): {nameof(NetworkedBogie)} requires a {nameof(rb)} component on the same GameObject! Waited {counter} iterations");
        }
    }

    protected override void Process(BogieData snapshot, uint snapshotTick)
    {

        //Multiplayer.LogDebug(()=>$"NetworkedBogie.Process({identifier}) DataFlags: {snapshot.DataFlags}, {snapshotTick}, {snapshot.TrackNetId}, {snapshot.PositionAlongTrack} {snapshot.TrackDirection}");

        if (Bogie == null || rb == null || Bogie.HasDerailed)
            return;

        if (snapshot.HasDerailed)
        {
            Bogie.Derail();
            return;
        }

        if (snapshot.IncludesTrackData)
        {
            if (!NetworkedRailTrack.TryGet(snapshot.TrackNetId, out NetworkedRailTrack track))
            {
                Multiplayer.LogWarning($"NetworkedBogie.Process({identifier}) Failed to find track {snapshot.TrackNetId} for bogie: {Bogie.Car.ID}");
                return;
            }

            if (Bogie.track != track.RailTrack)
                Bogie.SetTrack(track.RailTrack, snapshot.PositionAlongTrack, snapshot.TrackDirection);
            else
                Bogie.traveller.MoveToSpan(snapshot.PositionAlongTrack);
        }
        else
        {
            if (Bogie.track)
                Bogie.traveller.MoveToSpan(snapshot.PositionAlongTrack);
            else
                Multiplayer.LogWarning($"NetworkedBogie.Process({identifier}) No track for current bogie for bogie: {Bogie?.Car?.ID}, unable to move position!");
        }

        int physicsSteps = Mathf.FloorToInt((NetworkLifecycle.Instance.Tick - (float)snapshotTick) / NetworkLifecycle.TICK_RATE / Time.fixedDeltaTime) + 1;
        for (int i = 0; i < physicsSteps; i++)
        {
            var z = transform.InverseTransformDirection(rb.velocity).z;
            Bogie.UpdatePointSetTraveller(z);
        }
    }
}
