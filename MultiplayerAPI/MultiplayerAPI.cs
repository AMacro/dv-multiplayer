using MPAPI.Interfaces;
using System;
using System.Linq;
using System.Reflection;

namespace MPAPI;

/// <summary>
/// Provides an API interface for accessing Multiplayer Mod functionality and managing server/client instances.
/// </summary>
/// <remarks>
/// This class serves as the main entry point for the Multiplayer API, providing events for server and client lifecycle management,
/// and access to the current server, client, and API instances.
/// </remarks>
public static class MultiplayerAPI
{
    /// <summary>
    /// Gets the version of the Multiplayer API DLL that is currently loaded.
    /// </summary>
    /// <value>The version string of the API DLL.</value>
    public static string LoadedApiVersion
    {
        get
        {
            AssemblyInformationalVersionAttribute info = (AssemblyInformationalVersionAttribute)typeof(MultiplayerAPI).Assembly.
                                                            GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                                                            .FirstOrDefault();

            if (info == null)
                return "";

            return info.InformationalVersion.Split('+')[0];
        }
    }

    /// <summary>
    /// Gets the version of the Multiplayer API that the Multiplayer mod supports.
    /// </summary>
    /// <value>The supported API version string, or <c>null</c> if multiplayer is not loaded.</value>
    /// <remarks>
    /// This indicates the API version that the Multiplayer mod was built against and is compatible with.
    /// If this differs from <see cref="LoadedApiVersion"/>, there may be compatibility issues.
    /// </remarks>
    public static string SupportedApiVersion => _instance?.SupportedApiVersion;

    /// <summary>
    /// Gets the version of the Multiplayer mod itself.
    /// </summary>
    /// <value>The Multiplayer mod version string, or <c>null</c> if multiplayer is not loaded.</value>
    public static string MultiplayerVersion => _instance?.MultiplayerVersion;

    /// <summary>
    /// Event fired when a server instance has been created.
    /// </summary>
    /// <remarks>
    /// This event provides access to the <see cref="IServer"/> instance that was started.
    /// </remarks>
    public static event Action<IServer> ServerStarted;

    /// <summary>
    /// Event fired when a client instance has been created.
    /// </summary>
    /// <remarks>
    /// This event provides access to the <see cref="IClient"/> instance that was started.
    /// </remarks>
    public static event Action<IClient> ClientStarted;

    /// <summary>
    /// Event fired when a server instance is stopped.
    /// </summary>
    public static event Action ServerStopped;

    /// <summary>
    /// Event fired when a client instance is stopped.
    /// </summary>
    public static event Action ClientStopped;

    private static IMultiplayerAPI _instance;
    private static IServer _server;
    private static IClient _client;

    /// <summary>
    /// Gets whether the Multiplayer mod is available.
    /// </summary>
    public static bool IsMultiplayerLoaded => _instance != null;

    /// <summary>
    /// Gets the current API instance (<c>null</c> if Multiplayer mod is not loaded).
    /// </summary>
    public static IMultiplayerAPI Instance => _instance;

    /// <summary>
    /// Gets the current Server API instance (<c>null</c> if Multiplayer mod is not loaded or server not running).
    /// </summary>
    public static IServer Server => _server;

    /// <summary>
    /// Gets the current Client API instance (<c>null</c> if Multiplayer mod is not loaded or client not running).
    /// </summary>
    public static IClient Client => _client;

    /// <summary>
    /// Internal method for the Multiplayer mod to register itself.
    /// </summary>
    /// <param name="apiInstance">The API implementation.</param>
    internal static void RegisterAPI(IMultiplayerAPI apiInstance)
    {
        _instance = apiInstance;
    }

    /// <summary>
    /// Internal method for the Multiplayer mod to register a client instance.
    /// </summary>
    /// <param name="client">The Client implementation</param>
    internal static void RegisterClient(IClient client)
    {
        _client = client;
        ClientStarted?.Invoke(client);
    }

    /// <summary>
    /// Internal method for the Multiplayer mod to deregister a client instance.
    /// </summary>
    internal static void ClearClient()
    {
        _client = null;
        ClientStopped?.Invoke();
    }

    /// <summary>
    /// Internal method for the Multiplayer mod to register a server instance.
    /// </summary>
    /// <param name="server">The API implementation.</param>
    internal static void RegisterServer(IServer server)
    {
        _server = server;
        ServerStarted?.Invoke(server);
    }

    /// <summary>
    /// Internal method for the Multiplayer mod to deregister a server instance.
    /// </summary>
    internal static void ClearServer()
    {
        _server = null;
        ServerStopped?.Invoke();
    }
}
