using System;
using Multiplayer.Components.Networking.Train;
using UnityEngine;

namespace Multiplayer.Networking.Data;

public class ServerPlayer
{
    public byte Id { get; set; }
    public bool IsLoaded { get; set; }
    public string Username { get; set; }
    public Guid Guid { get; set; }
    public Vector3 RawPosition { get; set; }
    public float RawRotationY { get; set; }
    public ushort CarId { get; set; }

    public Vector3 AbsoluteWorldPosition => CarId == 0 || !NetworkedTrainCar.Get(CarId, out NetworkedTrainCar car)
        ? RawPosition
        : car.transform.TransformPoint(RawPosition) - WorldMover.currentMove;

    public Vector3 WorldPosition => CarId == 0 || !NetworkedTrainCar.Get(CarId, out NetworkedTrainCar car)
        ? RawPosition + WorldMover.currentMove
        : car.transform.TransformPoint(RawPosition);

    public float WorldRotationY => CarId == 0 || !NetworkedTrainCar.Get(CarId, out NetworkedTrainCar car)
        ? RawRotationY
        : (Quaternion.Euler(0, RawRotationY, 0) * car.transform.rotation).eulerAngles.y;

    public override string ToString()
    {
        return $"{Id} ({Username}, {Guid.ToString()})";
    }
}
