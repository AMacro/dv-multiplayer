using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data.Train;
using System.Collections;
using UnityEngine;

namespace Multiplayer.Components.Networking.Train;

public class NetworkedBogie : TickedQueue<BogieData>
{
    private const int MAX_FRAMES = 60;
    private Bogie bogie;

    protected override void OnEnable()
    {
        StartCoroutine(WaitForBogie());
    }

    protected IEnumerator WaitForBogie()
    {
        int counter = 0;

        while (bogie == null && counter < MAX_FRAMES)
        {
            bogie = GetComponent<Bogie>();
            if (bogie == null)
            {
                counter++;
                yield return new WaitForEndOfFrame();
            }
        }

        base.OnEnable();

        if (bogie == null)
        {
            Multiplayer.LogError($"{gameObject.name} ({bogie?.Car?.ID}): {nameof(NetworkedBogie)} requires a {nameof(Bogie)} component on the same GameObject! Waited {counter} iterations");
        }
    }

    protected override void Process(BogieData snapshot, uint snapshotTick)
    {
        if (bogie.HasDerailed)
            return;

        if (snapshot.HasDerailed)
        {
            bogie.Derail();
            return;
        }

        if (snapshot.IncludesTrackData)
        {
            if (!NetworkedRailTrack.Get(snapshot.TrackNetId, out NetworkedRailTrack track))
            {
                Multiplayer.LogWarning($"NetworkedBogie.Process() Failed to find track {snapshot.TrackNetId} for bogie: {bogie.Car.ID}");
                return;
            }

            bogie.SetTrack(track.RailTrack, snapshot.PositionAlongTrack, snapshot.TrackDirection);

        }
        else
        {
            if(bogie.track)
                bogie.traveller.MoveToSpan(snapshot.PositionAlongTrack);
            else
                Multiplayer.LogWarning($"NetworkedBogie.Process() No track for current bogie for bogie: {bogie?.Car?.ID}, unable to move position!");
        }

        int physicsSteps = Mathf.FloorToInt((NetworkLifecycle.Instance.Tick - (float)snapshotTick) / NetworkLifecycle.TICK_RATE / Time.fixedDeltaTime) + 1;
        for (int i = 0; i < physicsSteps; i++)
            bogie.UpdatePointSetTraveller();
    }
}
