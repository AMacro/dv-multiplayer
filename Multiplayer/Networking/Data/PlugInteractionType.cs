using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Multiplayer.Networking.Data
{
    public enum PlugInteractionType : byte
    {
        Rejected,
        PickedUp,
        Dropped,
        DockHome,
        DockSocket
    }
}
