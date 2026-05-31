using LiteNetLib.Utils;
using System.Collections.Generic;
using System;
using Multiplayer.Networking.Data.Items;

namespace Multiplayer.Networking.Packets.Common;

public class CommonItemUpdatePacket
{
    public ItemUpdateData ItemData { get; set; }
}


