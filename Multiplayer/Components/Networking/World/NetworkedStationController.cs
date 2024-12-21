using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Booklets;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.Utils;
using Multiplayer.Components.Networking.Jobs;
using Multiplayer.Components.Networking.Train;
using Multiplayer.Networking.Data;
using Multiplayer.Utils;
using UnityEngine;

namespace Multiplayer.Components.Networking.World;

public class NetworkedStationController : IdMonoBehaviour<ushort, NetworkedStationController>
{
    #region Lookup Cache
    private static readonly Dictionary<StationController, NetworkedStationController> stationControllerToNetworkedStationController = new();
    private static readonly Dictionary<string, NetworkedStationController> stationIdToNetworkedStationController = new();
    private static readonly Dictionary<string, StationController> stationIdToStationController = new();
    private static readonly Dictionary<Station, NetworkedStationController> stationToNetworkedStationController = new();
    private static readonly Dictionary<JobValidator, NetworkedStationController> jobValidatorToNetworkedStation = new();
    private static readonly List<JobValidator> jobValidators = new List<JobValidator>();

    public static bool Get(ushort netId, out NetworkedStationController obj)
    {
        bool b = Get(netId, out IdMonoBehaviour<ushort, NetworkedStationController> rawObj);
        obj = (NetworkedStationController)rawObj;
        return b;
    }

    public static Dictionary<ushort, string>GetAll()
    {
        Dictionary<ushort, string> result = new Dictionary<ushort, string>();

        foreach (var kvp in stationIdToNetworkedStationController )
        {
            Multiplayer.Log($"GetAll() adding {kvp.Value.NetId}, {kvp.Key}");
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


    protected override bool IsIdServerAuthoritative => true;

    public StationController StationController;

    public JobValidator JobValidator;

    public HashSet<NetworkedJob> NetworkedJobs { get; } = new HashSet<NetworkedJob>();
    private List<NetworkedJob> NewJobs = new List<NetworkedJob>();
    private List<NetworkedJob> DirtyJobs = new List<NetworkedJob>();

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

    private void Start()
    {
        if (NetworkLifecycle.Instance.IsHost())
        {
            NetworkLifecycle.Instance.OnTick += Server_OnTick;
        }
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
            Job newJob = CreateJobFromJobData(jobData);
            NetworkedJob networkedJob = CreateNetworkedJob(newJob, jobData.NetID);

            NetworkedJobs.Add(networkedJob);

            if (networkedJob.Job.State == DV.ThingTypes.JobState.Available)
            {
                StationController.logicStation.AddJobToStation(newJob);
                StationController.processedNewJobs.Add(newJob);

                if (jobData.ItemNetID != 0)
                {
                    GenerateOverview(networkedJob, jobData.ItemNetID, jobData.ItemPosition);
                }
            }

            StartCoroutine(UpdateCarPlates(newJob.tasks, newJob.ID));

            Multiplayer.Log($"Added NetworkedJob {newJob.ID} to NetworkedStationController {StationController.logicStation.ID}");
        }
    }

    private Job CreateJobFromJobData(JobData jobData)
    {
        List<Task> tasks = jobData.Tasks.Select(taskData => taskData.ToTask()).ToList();
        StationsChainData chainData = new StationsChainData(jobData.ChainData.ChainOriginYardId, jobData.ChainData.ChainDestinationYardId);

        Job newJob = new Job(tasks, jobData.JobType, jobData.TimeLimit, jobData.InitialWage, chainData, jobData.ID, jobData.RequiredLicenses);
        newJob.startTime = jobData.StartTime;
        newJob.finishTime = jobData.FinishTime;
        newJob.State = jobData.State;

        return newJob;
    }

    private NetworkedJob CreateNetworkedJob(Job job, ushort netId)
    {
        NetworkedJob networkedJob = new GameObject($"NetworkedJob {job.ID}").AddComponent<NetworkedJob>();
        networkedJob.NetId = netId;
        networkedJob.Initialize(job, this);
        networkedJob.OnJobDirty += OnJobDirty;
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

  private void HandleJobStateChange(NetworkedJob netJob, JobUpdateStruct job)
    {
        JobValidator validator = null;
        NetworkedItem netItem;

        if (job.ItemNetID != 0 && job.ValidationStationId != 0)
            if (Get(job.ValidationStationId, out var netStation))
                validator = netStation.JobValidator;

        if ((netJob.Job.State == DV.ThingTypes.JobState.InProgress ||
            netJob.Job.State == DV.ThingTypes.JobState.Completed) &&
            validator == null)
        {
            NetworkLifecycle.Instance.Client.LogError($"NetworkedStation.UpdateJobs() jobNetId: {job.JobNetID}, Validator required and not found!");
            return;
        }

        bool printed = false;
        switch (netJob.Job.State)
        {
            case DV.ThingTypes.JobState.InProgress:
                availableJobs.Remove(netJob.Job);
                takenJobs.Add(netJob.Job);

                JobBooklet jobBooklet = BookletCreator.CreateJobBooklet(netJob.Job, validator.bookletPrinter.spawnAnchor.position, validator.bookletPrinter.spawnAnchor.rotation, WorldMover.OriginShiftParent, true);

                netItem = jobBooklet.GetOrAddComponent<NetworkedItem>();
                netItem.Initialize(jobBooklet, job.ItemNetID, false);
                netJob.JobBooklet = netItem;
                printed = true;

                netJob.JobOverview?.GetTrackedItem<JobOverview>()?.DestroyJobOverview();

                break;

            case DV.ThingTypes.JobState.Completed:
                takenJobs.Remove(netJob.Job);
                completedJobs.Add(netJob.Job);

                DisplayableDebt displayableDebt = SingletonBehaviour<JobDebtController>.Instance.LastStagedJobDebt;
                JobReport jobReport = BookletCreator.CreateJobReport(netJob.Job, displayableDebt, validator.bookletPrinter.spawnAnchor.position, validator.bookletPrinter.spawnAnchor.rotation, WorldMover.OriginShiftParent);

                netItem = jobReport.GetOrAddComponent<NetworkedItem>();
                netItem.Initialize(jobReport, job.ItemNetID, false);
                netJob.AddReport(netItem);
                printed = true;

                netJob.JobBooklet?.GetTrackedItem<JobBooklet>()?.DestroyJobBooklet();

                break;

            case DV.ThingTypes.JobState.Abandoned:
                takenJobs.Remove(netJob.Job);
                abandonedJobs.Add(netJob.Job);
                break;

            case DV.ThingTypes.JobState.Expired:
                //if (availableJobs.Contains(netJob.Job))
                //    availableJobs.Remove(netJob.Job);

                netJob.Job.ExpireJob();
                StationController.ClearAvailableJobOverviewGOs();   //todo: better logic when players can hold items
                break;

            default:
                NetworkLifecycle.Instance.Client.LogError($"NetworkedStation.UpdateJobs() Unrecognised Job State for JobId: {job.JobNetID}, {netJob.Job.ID}");
                break;
        }

        if (printed && validator != null)
        {
            Multiplayer.Log($"NetworkedStation.UpdateJobs() jobNetId: {job.JobNetID}, Playing sounds");
            netJob.ValidatorResponseReceived = true;
            netJob.ValidationAccepted = true;
            validator.jobValidatedSound.Play(validator.bookletPrinter.spawnAnchor.position, 1f, 1f, 0f, 1f, 500f, default(AudioSourceCurves), null, validator.transform, false, 0f, null);
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

    public static IEnumerator UpdateCarPlates(List<DV.Logic.Job.Task> tasks, string jobId)
    {

       List<Car> cars = new List<Car>();
       UpdateCarPlatesRecursive(tasks, jobId, ref cars);


        if (cars == null)
            yield break;

        foreach (Car car in cars)
        {

            TrainCar trainCar = null;
            int loopCtr = 0;
            while (!NetworkedTrainCar.GetTrainCarFromTrainId(car.ID, out trainCar))
            {
                loopCtr++;
                if (loopCtr > 5000)
                {
                    break;
                }        

                yield return null;
            }

            trainCar?.UpdateJobIdOnCarPlates(jobId);
        }
    }
    private static void UpdateCarPlatesRecursive(List<DV.Logic.Job.Task> tasks, string jobId, ref List<Car> cars)
    {

        foreach (Task task in tasks)
        {
            if (task is WarehouseTask)
                cars = cars.Union(((WarehouseTask)task).cars).ToList();
            else if (task is TransportTask)
                cars = cars.Union(((TransportTask)task).cars).ToList();
            else if (task is SequentialTasks)
            {
                List<Task> seqTask = new();

                for (LinkedListNode<Task> node = ((SequentialTasks)task).tasks.First; node != null; node = node.Next)
                {
                    seqTask.Add(node.Value);
                }
                //drill down
                UpdateCarPlatesRecursive(seqTask, jobId, ref cars);
            }
            else if (task is ParallelTasks)
                UpdateCarPlatesRecursive(((ParallelTasks)task).tasks, jobId, ref cars);
            else
                throw new ArgumentException("NetworkedStation.UpdateCarPlatesRecursive() Unknown task type: " + task.GetType());
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

    private void OnDisable()
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
    #endregion
}
