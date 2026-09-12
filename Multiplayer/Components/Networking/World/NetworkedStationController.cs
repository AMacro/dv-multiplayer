using DV.Booklets;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.ThingTypes;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data.Items;
using Multiplayer.Networking.Data.Jobs;
using Multiplayer.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedStationController : IdMonoBehaviour<uint, NetworkedStationController>
{
    #region Lookup Cache
    private static readonly Dictionary<StationController, NetworkedStationController> stationControllerToNetworkedStationController = [];
    private static readonly Dictionary<string, NetworkedStationController> stationIdToNetworkedStationController = [];
    private static readonly Dictionary<string, StationController> stationIdToStationController = [];
    private static readonly Dictionary<Station, NetworkedStationController> stationToNetworkedStationController = [];
    private static readonly Dictionary<JobValidator, NetworkedStationController> jobValidatorToNetworkedStation = [];
    private static readonly List<JobValidator> jobValidators = [];

    public static bool Get(uint netId, out NetworkedStationController obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<uint, NetworkedStationController> rawObj);
        obj = (NetworkedStationController)rawObj;
        return b;
    }

    public static bool TryGet(uint netId, out StationController stationController)
    {
        if (Get(netId, out var networkedStationController))
        {
            stationController = networkedStationController.StationController;
            return true;
        }

        stationController = null;
        return false;
    }

    public static bool TryGet(uint netId, out Station station)
    {
        if (Get(netId, out var networkedStationController))
        {
            station = networkedStationController.StationController.logicStation;
            return true;
        }

        station = null;
        return false;
    }

    public static bool TryGet(uint netId, out JobValidator jobValidator)
    {
        if (Get(netId, out var networkedStationController))
        {
            jobValidator = networkedStationController.JobValidator;
            return true;
        }

        jobValidator = null;
        return false;
    }

    public static bool TryGetNetId(StationController stationController, out uint netId)
    {
        if (GetFromStationController(stationController, out var networkedStationController))
        {
            netId = networkedStationController.NetId;
            return true;
        }

        netId = 0;
        return false;
    }

    public static bool TryGetNetId(Station station, out uint netId)
    {
        if (GetFromStation(station, out var networkedStationController))
        {
            netId = networkedStationController.NetId;
            return true;
        }

        netId = 0;
        return false;
    }

    public static bool TryGetNetId(JobValidator jobValidator, out uint netId)
    {
        if (GetFromJobValidator(jobValidator, out var networkedStationController))
        {
            netId = networkedStationController.NetId;
            return true;
        }

        netId = 0;
        return false;
    }

    public static bool GetStationController(uint netId, out StationController obj)
    {
        bool b = Get(netId, out NetworkedStationController networkedStationController);
        obj = b ? networkedStationController.StationController : null;
        return b;
    }

    public static bool GetFromStationId(string stationId, out NetworkedStationController networkedStationController)
    {
        return stationIdToNetworkedStationController.TryGetValue(stationId, out networkedStationController);
    }

    public static bool GetFromStation(Station station, out NetworkedStationController networkedStationController)
    {
        return stationToNetworkedStationController.TryGetValue(station, out networkedStationController);
    }
    public static bool GetStationControllerFromStationId(string stationId, out StationController stationController)
    {
        return stationIdToStationController.TryGetValue(stationId, out stationController);
    }

    public static bool GetFromStationController(StationController stationController, out NetworkedStationController networkedStationController)
    {
        return stationControllerToNetworkedStationController.TryGetValue(stationController, out networkedStationController);
    }

    public static bool GetFromJobValidator(JobValidator jobValidator, out NetworkedStationController networkedStationController)
    {
        if (jobValidator == null)
        {
            networkedStationController = null;
            return false;
        }

        return jobValidatorToNetworkedStation.TryGetValue(jobValidator, out networkedStationController);
    }

    public static void RegisterStationController(NetworkedStationController networkedStationController, StationController stationController)
    {
        string stationID = stationController.logicStation.ID;
        networkedStationController.NetId = StringHashing.Fnv1aHash(stationID);

        stationControllerToNetworkedStationController.Add(stationController, networkedStationController);
        stationIdToNetworkedStationController.Add(stationID, networkedStationController);
        stationIdToStationController.Add(stationID, stationController);
        stationToNetworkedStationController.Add(stationController.logicStation, networkedStationController);
    }

    public static void QueueJobValidator(JobValidator jobValidator)
    {
        //Multiplayer.Log($"QueueJobValidator() {jobValidator.transform.parent.name}");

        jobValidators.Add(jobValidator);
    }

    private static void RegisterJobValidator(JobValidator jobValidator, NetworkedStationController stationController)
    {
        //Multiplayer.Log($"RegisterJobValidator() {jobValidator.transform.parent.name}, {stationController.name}");
        stationController.JobValidator = jobValidator;
        jobValidatorToNetworkedStation[jobValidator] = stationController;
    }
    #endregion

    public static readonly HashSet<uint> WaitingNSCs = [];
    public static readonly HashSet<ushort> DelayedJobs = [];

    private readonly HashSetQueue<CarPlateUpdate> CarPlateUpdates = new();

    const int MAX_FRAMES = 120;

    protected override bool IsIdServerAuthoritative => false;

    public StationController StationController;

    public JobValidator JobValidator;

    private Coroutine UpdateCarPlatesRoutine;

    public HashSet<NetworkedJob> NetworkedJobs { get; } = [];
    private readonly List<NetworkedJob> NewJobs = [];
    private readonly List<NetworkedJob> DirtyJobs = [];
    private readonly HashSet<ushort> TimeoutJobs = [];

    private List<Job> availableJobs;
    private List<Job> takenJobs;
    private List<Job> abandonedJobs;
    private List<Job> completedJobs;


    protected override void Awake()
    {
        base.Awake();
        StationController = GetComponent<StationController>();
        StartCoroutine(WaitForLogicStation());
        if (!NetworkLifecycle.Instance.IsHost())
            UpdateCarPlatesRoutine = StartCoroutine(UpdateCarPlatesCoro());
    }

    protected void Start()
    {
        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkLifecycle.Instance.OnTick += Server_OnTick;
        }
        else
        {
            NetworkLifecycle.Instance.OnTick += Client_OnTick;
        }
    }

    protected void OnDisable()
    {
        if (UpdateCarPlatesRoutine is not null) StopCoroutine(UpdateCarPlatesRoutine);

        if (UnloadWatcher.isQuitting)
            return;

        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkLifecycle.Instance.OnTick -= Server_OnTick;
        }
        else
        {
            NetworkLifecycle.Instance.OnTick -= Client_OnTick;
        }

        if (StationController != null)
        {
            string stationId = StationController.logicStation?.ID;

            stationControllerToNetworkedStationController.Remove(StationController);

            if (stationId != null)
            {
                stationIdToNetworkedStationController.Remove(stationId);
                stationIdToStationController.Remove(stationId);
            }

            if (StationController.logicStation != null)
                stationToNetworkedStationController.Remove(StationController.logicStation);

            if (JobValidator != null)
            {
                jobValidatorToNetworkedStation.Remove(JobValidator);
                jobValidators.Remove(this.JobValidator);
            }
        }

        Destroy(this);
    }

    private IEnumerator WaitForLogicStation()
    {
        while (StationController.logicStation == null)
            yield return null;

        RegisterStationController(this, StationController);

        availableJobs = StationController.logicStation.availableJobs;
        takenJobs = StationController.logicStation.takenJobs;
        abandonedJobs = StationController.logicStation.abandonedJobs;
        completedJobs = StationController.logicStation.completedJobs;

        //Multiplayer.Log($"NetworkedStation.Awake({StationController.logicStation.ID})");

        foreach (JobValidator validator in jobValidators)
        {
            string stationName = validator.transform.parent.name ?? "";
            stationName += "_office_anchor";

            if (this.transform.parent.name.Equals(stationName, StringComparison.OrdinalIgnoreCase))
            {
                JobValidator = validator;
                RegisterJobValidator(validator, this);
                jobValidators.Remove(validator);
                break;
            }
        }
    }

    #region Server
    //Adding job
    public void AddJob(Job job)
    {
        NetworkedJob networkedJob = new GameObject($"NetworkedJob {job.ID}").AddComponent<NetworkedJob>();
        networkedJob.Initialize(job, this);
        NetworkedJobs.Add(networkedJob);

        NewJobs.Add(networkedJob);

        //Setup handlers
        networkedJob.OnJobDirty += OnJobDirty;
    }

    private void OnJobDirty(NetworkedJob job)
    {
        if (!DirtyJobs.Contains(job))
            DirtyJobs.Add(job);
    }

    private void Server_OnTick(uint tick)
    {
        //Send new jobs
        if (NewJobs.Count > 0)
        {
            NetworkLifecycle.Instance.Server.SendJobsCreatePacket(this, NewJobs.ToArray());
            NewJobs.Clear();
        }

        //Send jobs with a changed status
        if (DirtyJobs.Count > 0)
        {
            //todo send packet with updates
            NetworkLifecycle.Instance.Server.SendJobsUpdatePacket(NetId, DirtyJobs.ToArray());
            DirtyJobs.Clear();
        }
    }
    #endregion Server

    #region Client

    private void Client_OnTick(uint tick)
    {
        //Try get previously timed out jobs if player is in station (not on every tick)
        if ((TimeoutJobs.Count > 0) && tick % 50 == 0 && StationController.playerEnteredJobGenerationZone)
        {
            NetworkLifecycle.Instance.Client.SendJobsRequest(this, [], false, TimeoutJobs.ToArray());
        }
    }

    public void AddJobs(JobData[] jobs, bool noTimeout = false)
    {

        foreach (JobData jobData in jobs)
        {
            //check for duplicates and not let them pass  --> !!!TODO: this shouldn´t be neccesarry - need to find reason for second send of the jobs
            if (NetworkedJobs.Any(nj => nj.Job.ID == jobData.ID))
            {
                Multiplayer.LogWarning($"Client already has job {jobData.ID}, not adding second copy");
                DelayedJobs.Remove(jobData.NetID);
                TimeoutJobs.Remove(jobData.NetID);
                continue;
            }

            //Cars may still be loading, we shouldn't spawn the job until they are ready
            if (CheckCarsLoaded(jobData))
            {
                Multiplayer.LogDebug(() => $"AddJobs() calling AddJob({jobData.ID})");
                AddJob(jobData);
            }
            else
            {
                Multiplayer.LogDebug(() => $"AddJobs() Delaying({jobData.ID})");
                DelayedJobs.Add(jobData.NetID);
                StartCoroutine(DelayCreateJob(jobData, noTimeout));
            }
        }
    }

    private void AddJob(JobData jobData)
    {
        var newJobData = JobData.ToJob(jobData);

        Job newJob = newJobData.newJob;
        var netIdToTask = newJobData.netIdToTask;

        var carNetIds = jobData.GetCars();

        NetworkedJob networkedJob = CreateNetworkedJob(newJob, jobData.NetID, carNetIds, netIdToTask);
        if (!NetworkedJobs.Add(networkedJob))
        {
            Multiplayer.LogWarning($"Net Job {jobData.ID} already present, not adding!");
            return;
        }

        if (networkedJob.Job.State == JobState.Available)
        {
            StationController.logicStation.AddJobToStation(newJob);
            StationController.processedNewJobs.Add(newJob);

            if (jobData.ItemNetID != 0)
            {
                GenerateOverview(networkedJob, jobData.ItemNetID, jobData.ItemPosition);
            }
        }
        else if (networkedJob.Job.State == JobState.InProgress)
        {
            StationController.logicStation.AddJobToStation(newJob);
            StationController.processedNewJobs.Add(newJob);

            takenJobs.Add(newJob);
            newJob.TakeJob(true); //take job as if loaded from save to prevent debt controller kicking in
        }
        else
        {
            //we don't need to update anything, so we'll return
            //Maybe item sync will require knowledge of the job for expired/failed/completed reports, but we currently only sync these for connected players
            return;
        }


        Multiplayer.LogDebug(() => $"AddJob({jobData.ID}) Starting plate update {newJob.ID} count: {jobData.GetCars().Count}");
        UpdateCarPlates(carNetIds, newJob.ID);

        Multiplayer.Log($"Added NetworkedJob {newJob.ID} to NetworkedStationController {StationController.logicStation.ID}");
        DelayedJobs.Remove(jobData.NetID);
        TimeoutJobs.Remove(jobData.NetID);
    }

    private IEnumerator DelayCreateJob(JobData jobData, bool noTimeout = false)
    {
        int frameCounter = 0;
        int maxWait = noTimeout ? 300 : MAX_FRAMES;

        Multiplayer.LogDebug(() => $"DelayCreateJob([{jobData.NetID}, {jobData.ID}]) job type: {jobData.JobType}");

        yield return null;

        while (noTimeout || frameCounter < maxWait)
        {
            if (CheckCarsLoaded(jobData))
            {
                Multiplayer.LogDebug(() => $"DelayCreateJob([{jobData.NetID}, {jobData.ID}]) job type: {jobData.JobType}. Successfully created cars!");
                AddJob(jobData);
                yield break;
            }

            frameCounter++;
            yield return null;
        }

        Multiplayer.LogWarning($"Timeout waiting for cars to load for job [{jobData.NetID}, {jobData.ID}]");
        TimeoutJobs.Add(jobData.NetID);
        DelayedJobs.Remove(jobData.NetID);
    }

    private bool CheckCarsLoaded(JobData jobData)
    {
        //extract all cars from the job and verify they have been initialised
        foreach (var carNetId in jobData.GetCars())
        {
            if (!NetworkedTrainCar.TryGet(carNetId, out NetworkedTrainCar car) || !car.Client_Initialized)
            {
                //car not spawned or not yet initialised
                return false;
            }
        }

        return true;
    }

    private NetworkedJob CreateNetworkedJob(Job job, ushort netId, List<ushort> carNetIds, Dictionary<ushort, Task> netIdToTask)
    {
        NetworkedJob networkedJob = new GameObject($"NetworkedJob {job.ID}").AddComponent<NetworkedJob>();
        networkedJob.NetId = netId;
        networkedJob.Initialize(job, this);
        networkedJob.SetTasksFromServer(netIdToTask);
        //networkedJob.OnJobDirty += OnJobDirty;
        networkedJob.JobCars = carNetIds;
        return networkedJob;
    }

    public void RequestAdditionalJobs(bool generateJobs)
    {
        var present = NetworkedJobs.Select(nj => nj.NetId).ToArray();
        NetworkLifecycle.Instance.Client.SendJobsRequest(this, present, generateJobs, []);
    }

    public IEnumerator UpdateJobs(uint stationNetId, JobUpdateStruct[] jobs)
    {
        Coroutine[] coroutines = jobs.Select(j => StartCoroutine(UpdateJob(stationNetId, j))).ToArray();
        foreach (var coroutine in coroutines)
            yield return coroutine;
    }

    public IEnumerator UpdateJob(uint stationNetId, JobUpdateStruct job)
    {
        int frameCounter = 0;

        if (DelayedJobs.Contains(job.JobNetID)) Multiplayer.LogDebug(() => $"Job with netId {job.JobNetID} is delayed in its creation, will wait with updating");
        while (frameCounter < 600 && DelayedJobs.Contains(job.JobNetID))
        {
            frameCounter++;
            yield return WaitFor.EndOfFrame;
        }

        if (frameCounter >= 600) Multiplayer.LogWarning($"NetworkedStationController.UpdateJob timed out waiting for delayed job with netId {job.JobNetID}");

        if (!NetworkedJob.Get(job.JobNetID, out NetworkedJob netJob))
        {
            NetworkLifecycle.Instance.Client.LogWarning($"Unknown or invalid jobNetId {job.JobNetID}, aksing server to re-send jobs and skipping update!");
            RequestAdditionalJobs(false);
            yield break;
        }

        netJob.Job.startTime = job.StartTime;
        netJob.Job.finishTime = job.FinishTime;

        UpdateJobState(netJob, job);
        UpdateJobOverview(netJob, job);
    }

    private void UpdateJobState(NetworkedJob netJob, JobUpdateStruct job)
    {
        if (netJob.Job.State != job.JobState)
        {
            netJob.Job.State = job.JobState;
            HandleJobStateChange(netJob, job);
        }
    }

    private void UpdateJobOverview(NetworkedJob netJob, JobUpdateStruct job)
    {
        Multiplayer.Log($"UpdateJobOverview({netJob.Job.ID}) State: {job.JobState}, ItemNetId: {job.ItemNetID}");
        if (job.JobState == DV.ThingTypes.JobState.Available && job.ItemNetID != 0)
        {
            if (netJob.JobOverview == null)
                GenerateOverview(netJob, job.ItemNetID, job.ItemPositionData);
            /*
            else
                netJob.JobOverview.NetId = job.ItemNetID;
            */
        }
    }

    private void HandleJobStateChange(NetworkedJob netJob, JobUpdateStruct updateData)
    {
        JobValidator validator = null;
        NetworkedItem netItem;
        string jobIdStr = $"[{netJob?.Job?.ID}, {netJob.NetId}]";

        NetworkLifecycle.Instance.Client.LogDebug(() => $"HandleJobStateChange({jobIdStr}) Current state: {netJob?.Job?.State}, New state: {updateData.JobState}, ValidationStationNetId: {updateData.ValidationStationId}, ItemNetId: {updateData.ItemNetID}");

        bool shouldPrint = updateData.JobState == JobState.InProgress || updateData.JobState == JobState.Completed;
        bool canPrint = true;

        if (shouldPrint)
        {
            if (updateData.ValidationStationId != 0 && Get(updateData.ValidationStationId, out var netStation))
            {
                validator = netStation.JobValidator;
            }
            else
            {
                NetworkLifecycle.Instance.Client.LogError($"HandleJobStateChange({jobIdStr}) Validator not found or data missing! Validator ID: {updateData.ValidationStationId}");
                canPrint = false;
            }

            if (updateData.ItemNetID == 0)
            {
                NetworkLifecycle.Instance.Client.LogError($"HandleJobStateChange({jobIdStr}) Missing item data!");
                canPrint = false;
            }
        }


        bool printed = false;
        switch (netJob.Job.State)
        {
            case JobState.InProgress:
                availableJobs.Remove(netJob.Job);
                takenJobs.Add(netJob.Job);

                netJob.Job.TakeJob(true); //take job as if loaded from save to prevent debt controller kicking in
                JobsManager.Instance.currentJobs.Add(netJob.Job);

                if (canPrint)
                {
                    JobBooklet jobBooklet = BookletCreator.CreateJobBooklet(netJob.Job, validator.bookletPrinter.spawnAnchor.position, validator.bookletPrinter.spawnAnchor.rotation, WorldMover.OriginShiftParent, true);
                    netItem = jobBooklet.GetOrAddComponent<NetworkedItem>();
                    netItem.Initialize(jobBooklet, updateData.ItemNetID, false);
                    netJob.JobBooklet = netItem;
                    printed = true;
                }

                netJob.DestroyJobOverview();

                break;

            case JobState.Completed:
                takenJobs.Remove(netJob.Job);
                completedJobs.Add(netJob.Job);
                JobsManager.Instance.CompleteTheJob(netJob.Job);

                if (canPrint)
                {
                    DisplayableDebt displayableDebt = JobDebtController.Instance.LastStagedJobDebt;
                    JobReport jobReport = BookletCreator.CreateJobReport(netJob.Job, displayableDebt, validator.bookletPrinter.spawnAnchor.position, validator.bookletPrinter.spawnAnchor.rotation, WorldMover.OriginShiftParent);
                    netItem = jobReport.GetOrAddComponent<NetworkedItem>();
                    netItem.Initialize(jobReport, updateData.ItemNetID, false);
                    netJob.AddReport(netItem);
                    printed = true;
                }

                UpdateCarPlates(netJob.JobCars, string.Empty);

                netJob.DestroyJobBooklet();

                break;

            case JobState.Abandoned:
                takenJobs.Remove(netJob.Job);
                abandonedJobs.Add(netJob.Job);
                JobsManager.Instance.AbandonJob(netJob.Job);
                UpdateCarPlates(netJob.JobCars, string.Empty);
                break;

            case JobState.Expired:
                netJob.Job.ExpireJob();
                JobsManager.Instance.currentJobs.Remove(netJob.Job);
                netJob.DestroyJobOverview();

                UpdateCarPlates(netJob.JobCars, string.Empty);
                break;

            default:
                NetworkLifecycle.Instance.Client.LogError($"HandleJobStateChange({jobIdStr}) Unrecognised Job State: {updateData.JobState}");
                break;
        }

        if (printed)
        {
            netJob.ValidatorResponseReceived = true;
            netJob.ValidationAccepted = true;
            validator.jobValidatedSound.Play(validator.bookletPrinter.spawnAnchor.position, 1f, 1f, 0f, 1f, 500f, default, null, validator.transform, false, 0f, null);
            validator.bookletPrinter.Print(false);
        }
    }

    public void RemoveJob(NetworkedJob job)
    {
        if (availableJobs.Contains(job.Job))
            availableJobs.Remove(job.Job);

        if (takenJobs.Contains(job.Job))
            takenJobs.Remove(job.Job);

        if (completedJobs.Contains(job.Job))
            completedJobs.Remove(job.Job);

        if (abandonedJobs.Contains(job.Job))
            abandonedJobs.Remove(job.Job);

        job.DestroyJobOverview();
        job.DestroyJobBooklet();

        job.ClearReports();

        NetworkedJobs.Remove(job);
        GameObject.Destroy(job);
    }

    public void UpdateCarPlates(List<ushort> carNetIds, string jobId)
    {
        if (!CarPlateUpdates.Enqueue(new(carNetIds, jobId)))
            Multiplayer.LogDebug(() => $"Car plate update to {jobId} for cars with netIds {string.Join(", ", carNetIds)} alredy scheduled!");
    }

    public IEnumerator UpdateCarPlatesCoro()
    {
        while (this.isActiveAndEnabled == true)
        {
            yield return null;

            if (!StationController.playerEnteredJobGenerationZone)
            {
                yield return WaitFor.Seconds(30f);
                continue;
            }

            if (!CarPlateUpdates.TryDequeue(out var cpu))
            {
                yield return null;
                continue;
            }
            else
            {
                var (carNetIds, jobId) = cpu;
                Multiplayer.LogDebug(() => $"UpdateCarPlates({jobId}) carNetIds: {carNetIds?.Count()}");

                if (carNetIds == null)
                {
                    yield return null;
                    continue;
                }

                foreach (ushort carNetId in carNetIds)
                {
                    int frameCounter = 0;
                    TrainCar trainCar = null;

                    while (frameCounter < MAX_FRAMES)
                    {

                        if (NetworkedTrainCar.TryGet(carNetId, out trainCar) &&
                            trainCar != null &&
                            trainCar.trainPlatesCtrl?.trainCarPlates != null &&
                            trainCar.trainPlatesCtrl.trainCarPlates.Count > 0)
                        {
                            //Multiplayer.LogDebug(() => $"UpdateCarPlates({jobId}) car: {carNetId}, frameCount: {frameCounter}. Calling Update");
                            trainCar.UpdateJobIdOnCarPlates(jobId);
                            break;
                        }

                        Multiplayer.LogDebug(() => $"UpdateCarPlates({jobId}) car: {carNetId}, frameCount: {frameCounter}. Incrementing frames");
                        frameCounter++;
                        yield return WaitFor.EndOfFrame;
                    }

                    if (frameCounter >= MAX_FRAMES)
                    {
                        Multiplayer.LogError($"Failed to update plates for car [{trainCar?.ID}, {carNetId}] (Job: {jobId}) after {frameCounter} frames");
                        yield return null;
                    }
                }
            }

            yield return null;
        }
    }

    private void GenerateOverview(NetworkedJob networkedJob, ushort itemNetId, ItemPositionData posData)
    {
        Multiplayer.Log($"GenerateOverview([{networkedJob.Job.ID},{networkedJob.Job.jobType}], {itemNetId}) Position: {posData.Position}, Less currentMove: {posData.Position + WorldMover.currentMove}");
        JobOverview jobOverview = BookletCreator_JobOverview.Create(networkedJob.Job, posData.Position + WorldMover.currentMove, posData.Rotation, WorldMover.OriginShiftParent);

        NetworkedItem netItem = jobOverview.GetOrAddComponent<NetworkedItem>();
        netItem.Initialize(jobOverview, itemNetId, false);
        networkedJob.JobOverview = netItem;
        StationController.spawnedJobOverviews.Add(jobOverview);
    }
    #endregion
}
