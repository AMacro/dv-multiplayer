using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Logic.Job;
using Multiplayer.Components.Networking.Train;
using UnityEngine;
using static DV.Common.GameFeatureFlags;
using static DV.UI.ATutorialsMenuProvider;

namespace Multiplayer.Components.Networking.World;

public class NetworkedStation : MonoBehaviour
{
    private StationController stationController;

    private void Awake()
    {
        Multiplayer.Log("NetworkedStation.Awake()");

        stationController = GetComponent<StationController>();
        StartCoroutine(WaitForLogicStation());
    }

    private IEnumerator WaitForLogicStation()
    {
        while (stationController.logicStation == null)
            yield return null;

        StationComponentLookup.Instance.RegisterStation(stationController);

        Multiplayer.Log("NetworkedStation.Awake() done");
    }

    public static IEnumerator UpdateCarPlates(List<DV.Logic.Job.Task> tasks, string jobId)
    {

       List<Car> cars = new List<Car>();
       UpdateCarPlatesRecursive(tasks, jobId, ref cars);


        if (cars != null)
        {
            Multiplayer.Log("NetworkedStation.UpdateCarPlates() Cars count: " + cars.Count);

            foreach (Car car in cars)
            {
                Multiplayer.Log("NetworkedStation.UpdateCarPlates() Car: " + car.ID);

                TrainCar trainCar = null;
                int loopCtr = 0;
                while (!NetworkedTrainCar.GetTrainCarFromTrainId(car.ID, out trainCar))
                {
                    loopCtr++;
                    if (loopCtr > 5000)
                    {
                        Multiplayer.Log("NetworkedStation.UpdateCarPlates() TimeOut");
                        break;
                    }
                        

                    yield return null;
                }

                trainCar?.UpdateJobIdOnCarPlates(jobId);
            }
        }
    }
    private static void UpdateCarPlatesRecursive(List<DV.Logic.Job.Task> tasks, string jobId, ref List<Car> cars)
    {
        Multiplayer.Log("NetworkedStation.UpdateCarPlatesRecursive() Starting");

        foreach (Task task in tasks)
        {
            if (task is WarehouseTask)
            {
                Multiplayer.Log("NetworkedStation.UpdateCarPlatesRecursive() WarehouseTask");
                cars = cars.Union(((WarehouseTask)task).cars).ToList();
            }
            else if (task is TransportTask)
            {
                Multiplayer.Log("NetworkedStation.UpdateCarPlatesRecursive() TransportTask");
                cars = cars.Union(((TransportTask)task).cars).ToList();
            }
            else if (task is SequentialTasks)
            {
                Multiplayer.Log("NetworkedStation.UpdateCarPlatesRecursive() SequentialTasks");
                List<Task> seqTask = new();

                for (LinkedListNode<Task> node = ((SequentialTasks)task).tasks.First; node != null; node = node.Next)
                {
                    Multiplayer.Log($"NetworkedStation.UpdateCarPlatesRecursive() SequentialTask Adding node");
                    seqTask.Add(node.Value);
                }

                Multiplayer.Log($"NetworkedStation.UpdateCarPlatesRecursive() SequentialTask Node Count:{seqTask.Count}");

                Multiplayer.Log("NetworkedStation.UpdateCarPlatesRecursive() Calling UpdateCarPlates()");
                //drill down
                UpdateCarPlatesRecursive(seqTask, jobId, ref cars);
                Multiplayer.Log($"NetworkedStation.UpdateCarPlatesRecursive() SequentialTask RETURNED");
            }
            else if (task is ParallelTasks)
            {
                //not implemented
                Multiplayer.Log("NetworkedStation.UpdateCarPlatesRecursive() ParallelTasks");

                Multiplayer.Log("NetworkedStation.UpdateCarPlatesRecursive() Calling UpdateCarPlates()");
                //drill down
                UpdateCarPlatesRecursive(((ParallelTasks)task).tasks, jobId, ref cars);
            }
            else
            {
                throw new ArgumentException("NetworkedStation.UpdateCarPlatesRecursive() Unknown task type: " + task.GetType());
            }
        }

        Multiplayer.Log("NetworkedStation.UpdateCarPlatesRecursive() Returning");
    }
}
