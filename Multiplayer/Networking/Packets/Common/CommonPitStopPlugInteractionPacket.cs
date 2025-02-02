using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Multiplayer.Networking.Packets.Common;

public class CommonPitStopPlugInteractionPacket
{
    public ushort NetId { get; set; }
    public byte InteractionType { get; set; }
    public byte PlayerId { get; set; }
    public ushort TrainCarNetId { get; set; }
    public bool IsLeftSide { get; set; }
}
