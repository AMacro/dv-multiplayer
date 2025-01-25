using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Booklets;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.Utils;
using DV.ThingTypes;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedStationController : IdMonoBehaviour<ushort, NetworkedStationController>
{
    #region Lookup Cache
    private static readonly Dictionary<StationController, NetworkedStationController> stationControllerToNetworkedStationController = [];
    private static readonly Dictionary<string, NetworkedStationController> stationIdToNetworkedStationController = [];
    private static readonly Dictionary<string, StationController> stationIdToStationController = [];
    private static readonly Dictionary<Station, NetworkedStationController> stationToNetworkedStationController = [];
    private static readonly Dictionary<JobValidator, NetworkedStationController> jobValidatorToNetworkedStation = [];
    private static readonly List<JobValidator> jobValidators = [];

    public static bool Get(ushort netId, out NetworkedStationController obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedStationController> rawObj);
        obj = (NetworkedStationController)rawObj;
        return b;
    }

    public static Dictionary<ushort, string>GetAll()
    {
        Dictionary<ushort, string> result = [];

        foreach (var kvp in stationIdToNetworkedStationController )
        {
            //Multiplayer.Log($"GetAll() adding {kvp.Value.NetId}, {kvp.Key}");
            result.Add(kvp.Value.NetId, kvp.Key);
        }
        return result;
    }

    public static bool GetStationController(ushort netId, out StationController obj)
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

        stationControllerToNetworkedStationController.Add(stationController,networkedStationController);
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

     const int MAX_FRAMES = 120;

    protected override bool IsIdServerAuthoritative => true;

    public StationController StationController;

    public JobValidator JobValidator;

    public HashSet<NetworkedJob> NetworkedJobs { get; } = [];
    private readonly List<NetworkedJob> NewJobs = [];
    private readonly List<NetworkedJob> DirtyJobs = [];

    private List<Job> availableJobs;
    private List<Job> takenJobs;
    private List<Job> abandonedJobs;
    private List<Job> completedJobs;


    protected override void Awake()
    {
        base.Awake();
        StationController = GetComponent<StationController>();
        StartCoroutine(WaitForLogicStation());
    }

    protected void Start()
    {
        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkLifecycle.Instance.OnTick += Server_OnTick;
        }
    }

    protected void OnDisable()
    {

        if (UnloadWatcher.isQuitting)
            return;

        NetworkLifecycle.Instance.OnTick -= Server_OnTick;

        string stationId = StationController.logicStation.ID;

        stationControllerToNetworkedStationController.Remove(StationController);
        stationIdToNetworkedStationController.Remove(stationId);
        stationIdToStationController.Remove(stationId);
        stationToNetworkedStationController.Remove(StationController.logicStation);
        jobValidatorToNetworkedStation.Remove(JobValidator);
        jobValidators.Remove(this.JobValidator);

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

            if(this.transform.parent.name.Equals(stationName, StringComparison.OrdinalIgnoreCase))
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
    public void AddJobs(JobData[] jobs)
    {

        foreach (JobData jobData in jobs)
        {
            //Cars may still be loading, we shouldn't spawn the job until they are ready
            if (CheckCarsLoaded(jobData))
            {
                Multiplayer.LogDebug(() => $"AddJobs() calling AddJob({jobData.ID})");
                AddJob(jobData);
            }
            else
            {
                Multiplayer.LogDebug(() => $"AddJobs() Delaying({jobData.ID})");
                StartCoroutine(DelayCreateJob(jobData));
            }
        }
    }

    private void AddJob(JobData jobData)
    {
        Job newJob = CreateJobFromJobData(jobData);
        var carNetIds = jobData.GetCars();

        NetworkedJob networkedJob = CreateNetworkedJob(newJob, jobData.NetID, carNetIds);

        NetworkedJobs.Add(networkedJob);

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
            takenJobs.Add(newJob); 
        }
        else
        {
            //we don't need to update anything, so we'll return
            //Maybe item sync will require knowledge of the job for expired/failed/completed reports, but we currently only sync these for connected players
            return;
        }

       
        Multiplayer.LogDebug(() => $"AddJob({jobData.ID}) Starting plate update {newJob.ID} count: {jobData.GetCars().Count}");
        StartCoroutine(UpdateCarPlates(carNetIds, newJob.ID));

        Multiplayer.Log($"Added NetworkedJob {newJob.ID} to NetworkedStationController {StationController.logicStation.ID}");
    }

    private Job CreateJobFromJobData(JobData jobData)
    {

        List<Task> tasks = jobData.Tasks.Select(taskData => taskData.ToTask()).ToList();
        StationsChainData chainData = new(jobData.ChainData.ChainOriginYardId, jobData.ChainData.ChainDestinationYardId);

        Job newJob = new(tasks, jobData.JobType, jobData.TimeLimit, jobData.InitialWage, chainData, jobData.ID, jobData.RequiredLicenses)
        {
            startTime = jobData.StartTime,
            finishTime = jobData.FinishTime,
            State = jobData.State
        };

        return newJob;
    }

    private IEnumerator DelayCreateJob(JobData jobData)
    {
        int frameCounter = 0;

        Multiplayer.LogDebug(()=>$"DelayCreateJob({jobData.NetID}) job type: {jobData.JobType}");

        yield return new WaitForEndOfFrame();

        while (frameCounter < MAX_FRAMES)
        {
            if (CheckCarsLoaded(jobData))
            {
                Multiplayer.LogDebug(() => $"DelayCreateJob({jobData.NetID}) job type: {jobData.JobType}. Successfully created cars!");
                AddJob(jobData);
                yield break;
            }

            frameCounter++;
            yield return new WaitForEndOfFrame();
        }

        Multiplayer.LogWarning($"Timeout waiting for cars to load for job {jobData.NetID}");
    }

    private bool CheckCarsLoaded(JobData jobData)
    {
        //extract all cars from the job and verify they have been initialised
        foreach (var carNetId in jobData.GetCars())
        {
            if (!NetworkedTrainCar.Get(carNetId, out NetworkedTrainCar car) || !car.Client_Initialized)
            {
                //car not spawned or not yet initialised
                return false;
            }
        }

        return true;
    }

    private NetworkedJob CreateNetworkedJob(Job job, ushort netId, List<ushort> carNetIds)
    {
        NetworkedJob networkedJob = new GameObject($"NetworkedJob {job.ID}").AddComponent<NetworkedJob>();
        networkedJob.NetId = netId;
        networkedJob.Initialize(job, this);
        networkedJob.OnJobDirty += OnJobDirty;
        networkedJob.JobCars = carNetIds;
        return networkedJob;
    }

    public void UpdateJobs(JobUpdateStruct[] jobs)
    {
        foreach (JobUpdateStruct job in jobs)
        {
            if (!NetworkedJob.Get(job.JobNetID, out NetworkedJob netJob))
                continue;

            UpdateJobState(netJob, job);
            UpdateJobOverview(netJob, job);

            netJob.Job.startTime = job.StartTime;
            netJob.Job.finishTime = job.FinishTime;
        }
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

                if (canPrint)
                {
                    JobBooklet jobBooklet = BookletCreator.CreateJobBooklet(netJob.Job, validator.bookletPrinter.spawnAnchor.position, validator.bookletPrinter.spawnAnchor.rotation, WorldMover.OriginShiftParent, true);
                    netItem = jobBooklet.GetOrAddComponent<NetworkedItem>();
                    netItem.Initialize(jobBooklet, updateData.ItemNetID, false);
                    netJob.JobBooklet = netItem;
                    printed = true;
                }

                netJob.JobOverview?.GetTrackedItem<JobOverview>()?.DestroyJobOverview();

                break;

            case JobState.Completed:
                takenJobs.Remove(netJob.Job);
                completedJobs.Add(netJob.Job);

                if (canPrint)
                {
                    DisplayableDebt displayableDebt = SingletonBehaviour<JobDebtController>.Instance.LastStagedJobDebt;
                    JobReport jobReport = BookletCreator.CreateJobReport(netJob.Job, displayableDebt, validator.bookletPrinter.spawnAnchor.position, validator.bookletPrinter.spawnAnchor.rotation, WorldMover.OriginShiftParent);
                    netItem = jobReport.GetOrAddComponent<NetworkedItem>();
                    netItem.Initialize(jobReport, updateData.ItemNetID, false);
                    netJob.AddReport(netItem);
                    printed = true;
                }

                StartCoroutine(UpdateCarPlates(netJob.JobCars, string.Empty));
                netJob.JobBooklet?.GetTrackedItem<JobBooklet>()?.DestroyJobBooklet();

                break;

            case JobState.Abandoned:
                takenJobs.Remove(netJob.Job);
                abandonedJobs.Add(netJob.Job);
                StartCoroutine(UpdateCarPlates(netJob.JobCars, string.Empty));
                break;

            case JobState.Expired:
                //if (availableJobs.Contains(netJob.Job))
                //    availableJobs.Remove(netJob.Job);

                netJob.Job.ExpireJob();
                StationController.ClearAvailableJobOverviewGOs();   //todo: better logic when players can hold items
                StartCoroutine(UpdateCarPlates(netJob.JobCars, string.Empty));
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

        job.JobOverview?.GetTrackedItem<JobOverview>()?.DestroyJobOverview();
        job.JobBooklet?.GetTrackedItem<JobBooklet>()?.DestroyJobBooklet();

        job.ClearReports();

        NetworkedJobs.Remove(job);
        GameObject.Destroy(job);
    }

    public static IEnumerator UpdateCarPlates(List<ushort> carNetIds, string jobId)
    {

        Multiplayer.LogDebug(() => $"UpdateCarPlates({jobId}) carNetIds: {carNetIds?.Count}");

        if (carNetIds == null || string.IsNullOrEmpty(jobId))
            yield break;

        foreach (ushort carNetId in carNetIds)
        {
            int frameCounter = 0;
            TrainCar trainCar = null;

            while (frameCounter < MAX_FRAMES)
            {

                if (NetworkedTrainCar.GetTrainCar(carNetId, out trainCar) &&
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
                yield return new WaitForEndOfFrame();
            }

            if (frameCounter >= MAX_FRAMES)
            {
                Multiplayer.LogError($"Failed to update plates for car [{trainCar?.ID}, {carNetId}] (Job: {jobId}) after {frameCounter} frames");
            }
        }
    }

    private void GenerateOverview(NetworkedJob networkedJob, ushort itemNetId, ItemPositionData posData)
    {
        Multiplayer.Log($"GenerateOverview({networkedJob.Job.ID}, {itemNetId}) Position: {posData.Position}, Less currentMove: {posData.Position + WorldMover.currentMove} ");
        JobOverview jobOverview = BookletCreator_JobOverview.Create(networkedJob.Job, posData.Position + WorldMover.currentMove, posData.Rotation,WorldMover.OriginShiftParent);

        NetworkedItem netItem = jobOverview.GetOrAddComponent<NetworkedItem>();
        netItem.Initialize(jobOverview, itemNetId, false);
        networkedJob.JobOverview = netItem;
        StationController.spawnedJobOverviews.Add(jobOverview);
    }
    #endregion
}
