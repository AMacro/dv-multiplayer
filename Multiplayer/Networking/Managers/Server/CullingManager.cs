using Multiplayer.Components.Networking;
using Multiplayer.Networking.Data;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Networking.Managers.Server;

public class CullingManager : IDisposable
{
    private const float DEFAULT_CULL_SQR_DISTANCE = 250000f;

    public event Action<ServerPlayer> PlayerEnteredActivationRegion;
    public event Action<ServerPlayer> PlayerEnteredCullingRegion;

    public List<ServerPlayer> ActivePlayers => playerToLastNearbyTime.Keys.ToList();

    private readonly Dictionary<ServerPlayer, float> playerToLastNearbyTime = [];
    private readonly float _checkInterval = 2f;
    private readonly float _cullSqrDistance = DEFAULT_CULL_SQR_DISTANCE;
    private readonly float _activationSqrDistance = DEFAULT_CULL_SQR_DISTANCE / 2;
    private readonly float _cullDelay = 3f;
    private GameObject _referenceObject = null;

    private Coroutine checkCoro;

    public CullingManager(float checkInterval, float cullSqrDistance, float activationSqrDistance, float cullDelay, GameObject referenceObject)
    {
        if (checkInterval > 0)
            _checkInterval = checkInterval;

        if (cullSqrDistance > 0)
            _cullSqrDistance = cullSqrDistance;

        if (activationSqrDistance > 0)
            _activationSqrDistance = activationSqrDistance;

        if (cullDelay >= 0)
            _cullDelay = cullDelay;

        if (referenceObject != null)
            _referenceObject = referenceObject;
        else
            throw new Exception("Reference object is null!");

        checkCoro = CoroutineManager.Instance.StartCoroutine(PlayerDistanceChecker());

        NetworkLifecycle.Instance.Server.PlayerDisconnected += OnPlayerDisconnected;
    }

    public void Dispose()
    {
        if (checkCoro != null)
            CoroutineManager.Instance.Stop(checkCoro);

        NetworkLifecycle.Instance.Server.PlayerDisconnected -= OnPlayerDisconnected;
    }

    //todo: fix when merged with ModAPI branch
    private void OnPlayerDisconnected(ServerPlayer serverPlayer)
    {
        var player = playerToLastNearbyTime.Keys.Where(p => p == serverPlayer).FirstOrDefault();

        if (player == null)
            return;

        playerToLastNearbyTime.Remove(player);
    }

    private IEnumerator PlayerDistanceChecker()
    {
        //wait for game to finish loading
        yield return new WaitForSeconds(2f);

        while (true)
        {
            yield return new WaitForSeconds(_checkInterval);

            //if not active then there is no one close by
            if (_referenceObject != null && _referenceObject.activeInHierarchy)
            {
                foreach (var player in NetworkLifecycle.Instance.Server.ServerPlayers)
                {
                    if (player.PlayerId == NetworkLifecycle.Instance.Server.SelfId || player.LoadingState != PlayerLoadingState.Complete)
                        continue;

                    float sqrDistance = (player.WorldPosition - _referenceObject.transform.position).sqrMagnitude;

                    bool initialised = playerToLastNearbyTime.TryGetValue(player, out float lastVisit);

                    if (initialised && sqrDistance > _cullSqrDistance)
                    {
                        // Too far away for too long, stop tracking
                        if ((Time.time - lastVisit) > _cullDelay)
                        {
                            playerToLastNearbyTime.Remove(player);
                            PlayerEnteredCullingRegion?.Invoke(player);
                        }

                        continue;
                    }

                    if (!initialised)
                    {
                        //make sure they are close by before we add them to the nearby list
                        if (sqrDistance > _activationSqrDistance)
                            continue;

                        PlayerEnteredActivationRegion?.Invoke(player);
                    }

                    //player nearby recently, update time
                    playerToLastNearbyTime[player] = Time.time;
                }
            }
        }
    }
}
