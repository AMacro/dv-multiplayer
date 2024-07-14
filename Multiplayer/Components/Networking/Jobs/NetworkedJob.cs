using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using DV.ThingTypes;
using DV.Utils;
using Multiplayer.Components.Networking.Player;
using Multiplayer.Components.Networking.World;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using UnityEngine;
using static System.Collections.Specialized.BitVector32;

namespace Multiplayer.Components.Networking.Jobs;

public class NetworkedJob : IdMonoBehaviour<ushort, NetworkedJob>
{
    #region Lookup Cache

    private static readonly Dictionary<Job, NetworkedJob> jobToNetworkedJob = new();
    private static readonly Dictionary<string, NetworkedJob> jobIdToNetworkedJob = new();
    private static readonly Dictionary<string, Job> jobIdToJob = new();

    public static bool Get(ushort netId, out NetworkedJob obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedJob> rawObj);
        obj = (NetworkedJob)rawObj;
        return b;
    }

    public static bool GetJob(ushort netId, out Job obj)
    {
        bool b = Get(netId, out NetworkedJob networkedJob);
        obj = b ? networkedJob.job : null;
        return b;
    }


    public static NetworkedJob GetFromJob(Job job)
    {
        return jobToNetworkedJob[job];
    }

    public static bool TryGetFromJob(Job job, out NetworkedJob networkedJob)
    {
        return jobToNetworkedJob.TryGetValue(job, out networkedJob);
    }

    /*public static NetworkedJob AddJob(string stationID, Job job)
    {
        NetworkedJob netJob = new NetworkedJob(stationID, job);

        jobToNetworkedJob[job] = netJob;
        jobIdToNetworkedJob[job.ID] = netJob;
        jobIdToJob[job.ID] = job;

        Multiplayer.Log($"NetworkedJob Added with netId: {jobToNetworkedJob[job].NetId}, jobId: {job.ID}");
        return jobToNetworkedJob[job];
    }*/
    #endregion

    public Job job;
    public JobOverview jobOverview;
    public JobBooklet jobBooklet;
    public string stationID;
    public bool isJobNew = true;
    public bool isJobDirty = false;
    public bool isTaskDirty = false;

    public bool? allowTake = null;
    public Guid takenBy; //GUID of player who took the job
    public JobValidator jobValidator;
 
    //might be useful when a job is taken?
    //public bool HasPlayers => PlayerManager.Car == Job || GetComponentInChildren<NetworkedPlayer>() != null;

    #region Client

    private bool client_Initialized;

    #endregion

    protected override bool IsIdServerAuthoritative => true;

    protected override void Awake()
    {
        Multiplayer.Log("NetworkJob.Awake()");
        base.Awake();


        /*
        job = GetComponent<Job>();
        jobToNetworkedJob[job] = this;



        if (NetworkLifecycle.Instance.IsHost())
        {
            //do we need a job watcher - probably not, but maybe or maybe we need a task watcher
            //NetworkTrainsetWatcher.Instance.CheckInstance(); // Ensure the NetworkTrainsetWatcher is initialized
        }
        else
        {
            //Networked task??

            //Client_trainSpeedQueue = TrainCar.GetOrAddComponent<TrainSpeedQueue>();
            //Client_trainRigidbodyQueue = TrainCar.GetOrAddComponent<NetworkedRigidbody>();
            //StartCoroutine(Client_InitLater());
        }
        */
    }

    private void Start()
    {
        //startup stuff
        Multiplayer.Log("NetworkedJob.Start()");

        jobToNetworkedJob[job] = this;
        jobIdToNetworkedJob[job.ID] = this;
        jobIdToJob[job.ID] = job;

        isJobNew = true;  //Send new jobs on tick

        StationController station;
        if (!StationComponentLookup.Instance.StationControllerFromId(stationID, out station))
        {
            Multiplayer.LogWarning($"NetworkJob.Start() Could not get staion for stationId: {stationID}");
            return;
        }

        if (!NetworkLifecycle.Instance.IsHost())
        {
            //station.logicStation.AddJobToStation(job);
            if (station.logicStation.availableJobs.Contains(job))
            {
                Multiplayer.LogError("Trying to add the same job[" + job.ID + "] multiple times to station! Skipping, trying to recover.");
                return;
            }

            station.logicStation.availableJobs.Add(job);
            job.JobTaken += this.OnJobTaken;
            job.JobExpired += this.OnJobExpired;
            //job.JobAddedToStation?.Invoke();
            SingletonBehaviour<CoroutineManager>.Instance.StartCoroutine(NetworkedStation.UpdateCarPlates(job.tasks, job.ID));
        }
        else
        {
            //setup even handlers
            job.JobTaken += this.OnJobTaken;
            job.JobExpired += this.OnJobExpired;
            NetworkLifecycle.Instance.OnTick += Server_OnTick;
        }
            
        Multiplayer.Log("NetworkedJob.Start() Started");
        //possibly capture tasks at this point for tracking??
    }

    private void OnDisable()
    {
        if (UnloadWatcher.isQuitting)
            return;

        NetworkLifecycle.Instance.OnTick -= Common_OnTick;
        NetworkLifecycle.Instance.OnTick -= Server_OnTick;

        if (UnloadWatcher.isUnloading)
            return;

        job.JobTaken -= this.OnJobTaken;

        jobToNetworkedJob.Remove(job);
        jobIdToNetworkedJob.Remove(job.ID);
        jobIdToNetworkedJob.Remove(job.ID);

        //Clean up any actions we added
        
        if (NetworkLifecycle.Instance.IsHost())
        {
           //actions relating only to host
        }

        Destroy(this);
    }

    /*public NetworkedJob(string stationID, Job job)
    {
        this.job = job;
        this.stationID = stationID;

        //setup even handlers
        //job.JobTaken +=

        isJobNew = true; //Send new jobs on tick

    }*/

    #region Server

    //wait for tasks?

    /*
    public bool Server_ValidateClientTakeJob(ServerPlayer player, CommonTrainPortsPacket packet)
    {
       
        return false;
    }
    */

    /*
    public bool Server_ValidateClientAbandonedJob(ServerPlayer player, CommonTrainPortsPacket packet)
    {
       
        return false;
    }
    */

    /*
    public bool Server_ValidateClientCompleteJob(ServerPlayer player, CommonTrainPortsPacket packet)
    {
       
        return false;
    }
    */

  
    private void Server_OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading)
            return;

        Server_SendNewJob();
        //Server_SendJobStatus();
        //Server_SendTaskStatus();
        //Server_SendJobDestroy();

    }

    private void Server_SendNewJob()
    {
        if (!isJobNew)
            return;

        isJobNew = false;
        NetworkLifecycle.Instance.Server.SendJobCreatePacket(this);
    }
    /*
    private void Server_SendJobStatus()
    {
        if (!sendCouplers)
            return;
        sendCouplers = false;

        if (Job.frontCoupler.hoseAndCock.IsHoseConnected)
            NetworkLifecycle.Instance.Client.SendHoseConnected(Job.frontCoupler, Job.frontCoupler.coupledTo, false);

        if (Job.rearCoupler.hoseAndCock.IsHoseConnected)
            NetworkLifecycle.Instance.Client.SendHoseConnected(Job.rearCoupler, Job.rearCoupler.coupledTo, false);

        NetworkLifecycle.Instance.Client.SendCockState(NetId, Job.frontCoupler, Job.frontCoupler.IsCockOpen);
        NetworkLifecycle.Instance.Client.SendCockState(NetId, Job.rearCoupler, Job.rearCoupler.IsCockOpen);
    }
    */


    #endregion

    #region Common

    private void Common_OnTick(uint tick)
    {
        if (UnloadWatcher.isUnloading)
            return;
        /*
        Common_SendHandbrakePosition();
        Common_SendFuses();
        Common_SendPorts();
        */
    }

    public void OnJobTaken(Job jobTaken,bool _)
    {
        Multiplayer.Log($"JobTaken: {jobTaken.ID}");
        jobTaken.JobTaken -= this.OnJobTaken;
        jobTaken.JobExpired -= this.OnJobExpired;

        /*
        takenJob.JobCompleted += OnJobCompleted;
        takenJob.JobAbandoned += OnJobAbandoned;
        availableJobs.Remove(takenJob);
        takenJobs.Add(takenJob);
        */

        isJobDirty = true;
        /*
        jobTaken.JobExpired -= this.OnJobExpired;
        jobTaken.JobCompleted += this.OnJobCompleted;
        jobTaken.JobAbandoned += this.OnJobAbandoned;
        */
    }

    public void OnJobExpired(Job jobExpired)
    {
        Multiplayer.Log($"Job Expired: {job.ID}");
        jobExpired.JobTaken -= this.OnJobTaken;
        jobExpired.JobExpired -= this.OnJobExpired;
        //jobExpired.JobCompleted += this.OnJobCompleted;
        //jobExpired.JobAbandoned += this.OnJobAbandoned;

        isJobDirty = true;

    }

    #endregion

    #region Client

    /*
    public void Client_ReceiveJopStatus(in TrainsetMovementPart movementPart, uint tick)
    {
        if (!client_Initialized)
            return;
        if (Job.isEligibleForSleep)
            Job.ForceOptimizationState(false);

        if (movementPart.IsRigidbodySnapshot)
        {
            Job.Derail();
            Job.stress.ResetTrainStress();
            Client_trainRigidbodyQueue.ReceiveSnapshot(movementPart.RigidbodySnapshot, tick);
        }
        else
        {
            Client_trainSpeedQueue.ReceiveSnapshot(movementPart.Speed, tick);
            Job.stress.slowBuildUpStress = movementPart.SlowBuildUpStress;
            client_bogie1Queue.ReceiveSnapshot(movementPart.Bogie1, tick);
            client_bogie2Queue.ReceiveSnapshot(movementPart.Bogie2, tick);
        }
    }
    */
    #endregion
}
