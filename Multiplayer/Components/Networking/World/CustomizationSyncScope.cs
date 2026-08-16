using System;

namespace Multiplayer.Components.Networking.World;

public static class CustomizationSyncScope
{
    [ThreadStatic]
    private static int remoteDepth;

    public static bool IsApplyingRemote => remoteDepth > 0;

    public static IDisposable Remote()
    {
        remoteDepth++;
        return new RemoteScope();
    }

    private sealed class RemoteScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            remoteDepth = Math.Max(0, remoteDepth - 1);
        }
    }
}
