using Google.Protobuf;
using Iop.Profileserver;
using Iop.Proximityserver;
using IopCommon;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkSimulator
{
  /// <summary>
  /// Engine that executes the commands.
  /// </summary>
  public class CommandProcessor
  {
    private static Logger log = new Logger("NetworkSimulator.CommandProcessor");

    /// <summary>Directory of the assembly.</summary>
    public static string BaseDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

    /// <summary>Full path to the directory that contains files of running server instances.</summary>
    public static string InstanceDirectory = Path.Combine(BaseDirectory, "instances");

    /// <summary>Full path to the directory that contains original binaries.</summary>
    public static string BinariesDirectory = Path.Combine(BaseDirectory, "bin");

    /// <summary>Full path to the directory that contains images.</summary>
    public static string ImagesDirectory = Path.Combine(BaseDirectory, "images");

    /// <summary>Full path to the directory that contains original profile server files within binary directory.</summary>
    public static string ProfileServerBinariesDirectory = Path.Combine(BinariesDirectory, "ProfileServer");

    /// <summary>Full path to the directory that contains original proximity server files within binary directory.</summary>
    public static string ProximityServerBinariesDirectory = Path.Combine(BinariesDirectory, "ProximityServer");

    /// <summary>Full path to the directory that contains snapshots.</summary>
    public static string SnapshotDirectory = Path.Combine(BaseDirectory, "snapshots");

    /// <summary>List of commands to execute.</summary>
    private List<Command> commands;

    /// <summary>List of profile server instances mapped by their name.</summary>
    private Dictionary<string, ProfileServer> profileServers = new Dictionary<string, ProfileServer>(StringComparer.Ordinal);

    /// <summary>List of proximity server instances mapped by their name.</summary>
    private Dictionary<string, ProximityServer> proximityServers = new Dictionary<string, ProximityServer>(StringComparer.Ordinal);

    /// <summary>List of all server instances mapped by their name.</summary>
    private Dictionary<string, ServerBase> servers = new Dictionary<string, ServerBase>(StringComparer.Ordinal);

    /// <summary>List of identity client instances mapped by their name.</summary>
    private Dictionary<string, IdentityClient> identityClients = new Dictionary<string, IdentityClient>(StringComparer.Ordinal);

    /// <summary>List of activities mapped by their name.</summary>
    private Dictionary<string, Activity> activities = new Dictionary<string, Activity>(StringComparer.Ordinal);

    /// <summary>List of activity groups mapped by group name.</summary>
    private Dictionary<string, List<Activity>> activityGroups = new Dictionary<string, List<Activity>>();

    /// <summary>True if debug mode is currently enabled, false otherwise.</summary>
    private bool DebugModeEnabled;


    /// <summary>
    /// Initializes the object instance.
    /// </summary>
    /// <param name="Commands">List of commands to execute.</param>
    public CommandProcessor(List<Command> Commands)
    {
      log.Trace("()");

      this.commands = Commands;

      log.Trace("(-)");
    }

    /// <summary>
    /// Frees resources used by command processor.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      foreach (IdentityClient identity in identityClients.Values)
        identity.Shutdown();

      foreach (ServerBase server in servers.Values)
        server.Shutdown();

      log.Trace("(-)");
    }

    /// <summary>
    /// Executes all commands.
    /// </summary>
    /// <returns>true if the function succeeds and all the commands are executed successfully, false otherwise.</returns>
    public bool Execute()
    {
      log.Trace("()");

      bool res = false;

      log.Info("Cleaning old history.");

      if (!ClearHistory())
      {
        log.Error("Unable to clear history, please make sure the access to the instance folder is not blocked by other process.");
        log.Trace("(-):{0}", res);
        return res;
      }
      log.Info("");

      int index = 1;
      bool error = false;
      foreach (Command command in commands)
      {
        log.Info("Executing #{0:0000}@l{1}: {2}", index, command.LineNumber, command.OriginalCommand);

        switch (command.Type)
        {
          case CommandType.ProfileServer:
            {
              CommandProfileServer cmd = (CommandProfileServer)command;
              for (int i = 1; i <= cmd.Count; i++)
              {
                string name = GetServerInstanceName(cmd.GroupName, i);
                GpsLocation location = Helpers.GenerateRandomGpsLocation(cmd.Latitude, cmd.Longitude, cmd.Radius);
                int basePort = cmd.BasePort + 20 * (i - 1);
                ProfileServer profileServer = new ProfileServer(name, location, basePort);
                if (profileServer.Initialize())
                {
                  profileServers.Add(name, profileServer);
                  servers.Add(name, profileServer);
                }
                else
                {
                  log.Error("  * Initialization of profile server '{0}' failed.", profileServer.Name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} profile servers created.", cmd.Count);
              break;
            }

          case CommandType.ProximityServer:
            {
              CommandProximityServer cmd = (CommandProximityServer)command;
              for (int i = 1; i <= cmd.Count; i++)
              {
                string name = GetServerInstanceName(cmd.GroupName, i);
                GpsLocation location = Helpers.GenerateRandomGpsLocation(cmd.Latitude, cmd.Longitude, cmd.Radius);
                int basePort = cmd.BasePort + 20 * (i - 1);
                ProximityServer proximityServer = new ProximityServer(name, location, basePort);
                if (proximityServer.Initialize())
                {
                  proximityServers.Add(name, proximityServer);
                  servers.Add(name, proximityServer);
                }
                else
                {
                  log.Error("  * Initialization of proximity server '{0}' failed.", proximityServer.Name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} proximity servers created.", cmd.Count);
              break;
            }

          case CommandType.StartServer:
            {
              CommandStartServer cmd = (CommandStartServer)command;
              for (int i = 0; i < cmd.Count; i++)
              {
                string name = GetServerInstanceName(cmd.Group, cmd.Index + i);
                ServerBase server;
                if (servers.TryGetValue(name, out server))
                {
                  if (!server.Start())
                  {
                    log.Error("  * Unable to start server instance '{0}'.", name);
                    error = true;
                    break;
                  }
                }
                else
                {
                  log.Error("  * Server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} servers started.", cmd.Count);
              break;
            }

          case CommandType.StopServer:
            {
              CommandStopServer cmd = (CommandStopServer)command;
              for (int i = 0; i < cmd.Count; i++)
              {
                string name = GetServerInstanceName(cmd.Group, cmd.Index + i);
                ServerBase server;
                if (servers.TryGetValue(name, out server))
                {
                  if (!server.Stop())
                  {
                    log.Error("  * Unable to stop server instance '{0}'.", name);
                    error = true;
                  }
                }
                else
                {
                  log.Error("  * Server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} servers stopped.", cmd.Count);
              break;
            }

          case CommandType.Identity:
            {
              CommandIdentity cmd = (CommandIdentity)command;

              List<ProfileServer> availableServers = new List<ProfileServer>();
              int availableSlots = 0;
              for (int i = 0; i < cmd.Count; i++)
              {
                string name = GetServerInstanceName(cmd.Group, cmd.Index + i);
                ProfileServer profileServer;
                if (profileServers.TryGetValue(name, out profileServer))
                {
                  availableServers.Add(profileServer);
                  availableSlots += profileServer.AvailableIdentitySlots;
                }
                else
                {
                  log.Error("  * Profile server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (error) break;


              if (availableSlots < cmd.CreateCount)
              {
                log.Error("  * Total number of available identity slots in selected servers is {0}, but {1} slots are required.", availableSlots, cmd.CreateCount);
                error = true;
                break;
              }


              for (int i = 1; i <= cmd.CreateCount; i++)
              {
                string name = GetIdentityName(cmd.Name, i);

                int serverIndex = Helpers.Rng.Next(availableServers.Count);
                ProfileServer profileServer = availableServers[serverIndex];

                GpsLocation location = Helpers.GenerateRandomGpsLocation(cmd.Latitude, cmd.Longitude, cmd.Radius);
                IdentityClient identityClient = null;
                try
                {
                  identityClient = new IdentityClient(name, cmd.IdentityType, location, cmd.ProfileImageMask, cmd.ProfileImageChance, cmd.ThumbnailImageMask, cmd.ThumbnailImageChance);
                  identityClients.Add(name, identityClient);
                }
                catch
                {
                  log.Error("Unable to create identity '{0}'.", name);
                  error = true;
                  break;
                }

                Task<bool> initTask = identityClient.InitializeProfileHostingAsync(profileServer);
                if (initTask.Result) 
                {
                  profileServer.AddIdentityClient(identityClient);
                  if (profileServer.AvailableIdentitySlots == 0)
                    availableServers.RemoveAt(serverIndex);
                }
                else
                {
                  log.Error("Unable to register profile hosting and initialize profile of identity '{0}' on server '{1}'.", name, profileServer.Name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} identities created and initialized on {1} servers.", cmd.CreateCount, cmd.Count);
              break;
            }

          case CommandType.CancelIdentity:
            {
              CommandCancelIdentity cmd = (CommandCancelIdentity)command;

              List<IdentityClient> clients = new List<IdentityClient>();
              for (int i = 0; i < cmd.Count; i++)
              {
                string name = GetIdentityName(cmd.Name, cmd.Index + i);
                IdentityClient identityClient;
                if (identityClients.TryGetValue(name, out identityClient))
                {
                  clients.Add(identityClient);
                }
                else
                {
                  log.Error("  * Identity name '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (error) break;


              foreach (IdentityClient client in clients)
              {
                Task<bool> cancelTask = client.CancelProfileHostingAsync();
                if (!cancelTask.Result)
                {
                  log.Error("Unable to cancel profile hosting agreement of identity '{0}' on server '{1}'.", client.Profile.Name, client.ProfileServer.Name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} identities cancelled their hosting agreement.", clients.Count);

              break;
            }

          case CommandType.Neighborhood:
            {
              CommandNeighborhood cmd = (CommandNeighborhood)command;

              List<ServerBase> neighborhoodList = new List<ServerBase>();
              for (int i = 0; i < cmd.Groups.Count; i++)
              {
                string psGroup = cmd.Groups[i];
                int psCount = cmd.Counts[i];
                int psIndex = cmd.Indexes[i];
                for (int j = 0; j < psCount; j++)
                {
                  string name = GetServerInstanceName(psGroup, psIndex + j);

                  ServerBase server;
                  if (servers.TryGetValue(name, out server))
                  {
                    neighborhoodList.Add(server);
                  }
                  else
                  {
                    log.Error("  * Server instance '{0}' does not exist.", name);
                    error = true;
                    break;
                  }
                }
              }

              if (!error)
              {
                foreach (ServerBase neighbor in neighborhoodList)
                {
                  if (!neighbor.LocServer.AddNeighborhood(neighborhoodList))
                  {
                    log.Error("  * Unable to add neighbors to server '{0}'.", neighbor.Name);
                    error = true;
                    break;
                  }
                }

                if (!error)
                  log.Info("  * Neighborhood of {0} servers has been established.", neighborhoodList.Count);
              }
              break;
            }

          case CommandType.CancelNeighborhood:
            {
              CommandCancelNeighborhood cmd = (CommandCancelNeighborhood)command;

              List<ServerBase> neighborhoodList = new List<ServerBase>();
              for (int i = 0; i < cmd.Groups.Count; i++)
              {
                string psGroup = cmd.Groups[i];
                int psCount = cmd.Counts[i];
                int psIndex = cmd.Indexes[i];
                for (int j = 0; j < psCount; j++)
                {
                  string name = GetServerInstanceName(psGroup, psIndex + j);

                  ServerBase server;
                  if (servers.TryGetValue(name, out server))
                  {
                    neighborhoodList.Add(server);
                  }
                  else
                  {
                    log.Error("  * Server instance '{0}' does not exist.", name);
                    error = true;
                    break;
                  }
                }
              }

              if (!error)
              {
                foreach (ServerBase neighbor in neighborhoodList)
                {
                  if (!neighbor.LocServer.CancelNeighborhood(neighborhoodList))
                  {
                    log.Error("  * Unable to add neighbors to server '{0}'.", neighbor.Name);
                    error = true;
                    break;
                  }
                }

                if (!error)
                  log.Info("  * Neighbor relations among {0} profile servers have been cancelled.", neighborhoodList.Count);
              }
              break;
            }

          case CommandType.Neighbor:
            {
              CommandNeighbor cmd = (CommandNeighbor)command;

              ServerBase server;
              if (servers.TryGetValue(cmd.Source, out server))
              {
                List<ServerBase> neighborhoodList = new List<ServerBase>();
                for (int i = 0; i < cmd.Targets.Count; i++)
                {
                  string name = cmd.Targets[i];
                  ServerBase target;
                  if (servers.TryGetValue(name, out target))
                  {
                    neighborhoodList.Add(target);
                  }
                  else
                  {
                    log.Error("  * Server instance '{0}' does not exist.", name);
                    error = true;
                    break;
                  }
                }


                if (!error)
                {
                  if (server.LocServer.AddNeighborhood(neighborhoodList))
                  {
                    log.Info("  * {0} servers have been added to the neighborhood of server '{1}'.", neighborhoodList.Count, server.Name);
                  }
                  else
                  {
                    log.Error("  * Unable to add neighbors to server '{0}'.", server.Name);
                    error = true;
                    break;
                  }
                }
              }
              else
              {
                log.Error("  * Server instance '{0}' does not exist.", cmd.Source);
                error = true;
                break;
              }

              break;
            }

          case CommandType.CancelNeighbor:
            {
              CommandCancelNeighbor cmd = (CommandCancelNeighbor)command;

              ServerBase server;
              if (servers.TryGetValue(cmd.Source, out server))
              {
                List<ServerBase> neighborhoodList = new List<ServerBase>();
                for (int i = 0; i < cmd.Targets.Count; i++)
                {
                  string name = cmd.Targets[i];
                  ServerBase target;
                  if (servers.TryGetValue(name, out target))
                  {
                    neighborhoodList.Add(target);
                  }
                  else
                  {
                    log.Error("  * Server instance '{0}' does not exist.", name);
                    error = true;
                    break;
                  }
                }


                if (!error)
                {
                  if (server.LocServer.CancelNeighborhood(neighborhoodList))
                  {
                    log.Info("  * {0} servers have been removed from the neighborhood of server '{1}'.", neighborhoodList.Count, server.Name);
                  }
                  else
                  {
                    log.Error("  * Unable to remove neighbors from neighborhood of server '{0}'.", server.Name);
                    error = true;
                    break;
                  }
                }
              }
              else
              {
                log.Error("  * Server instance '{0}' does not exist.", cmd.Source);
                error = true;
                break;
              }

              break;
            }



          case CommandType.Activity:
            {
              CommandActivity cmd = (CommandActivity)command;

              List<IdentityClient> availableIdentityClients = new List<IdentityClient>();
              for (int i = 0; i < cmd.Count; i++)
              {
                string name = GetIdentityName(cmd.Group, cmd.Index + i);
                IdentityClient identityClient;
                if (identityClients.TryGetValue(name, out identityClient))
                {
                  availableIdentityClients.Add(identityClient);
                }
                else
                {
                  log.Error("  * Identity name '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (error) break;

              List<Activity> activityGroup = new List<Activity>();
              activityGroups.Add(cmd.Name, activityGroup);
              Dictionary<IdentityClient, Dictionary<ProximityServer, List<Activity>>> clientServerActivityMapping = new Dictionary<IdentityClient, Dictionary<ProximityServer, List<Activity>>>();
              for (int i = 0; i < cmd.CreateCount; i++)
              {
                uint activityId = (uint)activities.Count + 1;
                string name = GetActivityName(cmd.Name, activityId);

                int identityClientIndex = Helpers.Rng.Next(availableIdentityClients.Count);
                IdentityClient identityClient = availableIdentityClients[identityClientIndex];

                GpsLocation location = Helpers.GenerateRandomGpsLocation(cmd.Latitude, cmd.Longitude, cmd.Radius);


                int startTimeShift = Helpers.Rng.Next(cmd.StartTimeFrom, cmd.StartTimeTo);
                DateTime startTime = DateTime.UtcNow.AddSeconds(startTimeShift);

                int lifetimeShift = Helpers.Rng.Next(cmd.LifetimeFrom, cmd.LifetimeTo);
                DateTime expirationTime = startTime.AddSeconds(lifetimeShift);

                uint precision = (uint)Helpers.Rng.Next(cmd.PrecisionMin, cmd.PrecisionMax + 1);

                ProximityServer proximityServer = FindNearestProximityServer(location);
                if (proximityServer != null)
                {
                  Activity activity = null;
                  try
                  {
                    activity = new Activity(activityId, cmd.Name, location, precision, startTime, expirationTime, null, identityClient);
                    activities.Add(name, activity);
                    activityGroup.Add(activity);

                    Dictionary<ProximityServer, List<Activity>> serverActivityMapping = null;
                    if (!clientServerActivityMapping.TryGetValue(identityClient, out serverActivityMapping))
                    {
                      serverActivityMapping = new Dictionary<ProximityServer, List<Activity>>();
                      clientServerActivityMapping.Add(identityClient, serverActivityMapping);
                    }

                    List<Activity> serverActivities = null;
                    if (!serverActivityMapping.TryGetValue(proximityServer, out serverActivities))
                    {
                      serverActivities = new List<Activity>();
                      serverActivityMapping.Add(proximityServer, serverActivities);
                    }

                    serverActivities.Add(activity);
                  }
                  catch
                  {
                    log.Error("Unable to create activity '{0}'.", name);
                    error = true;
                    break;
                  }
                }
                else
                {
                  log.Error("No proximity server found for activity '{0}'.", name);
                  error = true;
                  break;
                }
              }


              foreach (KeyValuePair<IdentityClient, Dictionary<ProximityServer, List<Activity>>> kvpIdentity in clientServerActivityMapping)
              {
                IdentityClient identityClient = kvpIdentity.Key;

                foreach (KeyValuePair<ProximityServer, List<Activity>> kvpServer in kvpIdentity.Value)
                {
                  ProximityServer proximityServer = kvpServer.Key;
                  List<Activity> activityList = kvpServer.Value;

                  if (proximityServer.AvailableActivitySlots >= activityList.Count)
                  {
                    Task<bool> initTask = identityClient.InitializeActivitiesAsync(proximityServer, activityList);
                    if (initTask.Result)
                    {
                      foreach (Activity activity in activityList)
                        proximityServer.AddActivity(activity);
                    }
                    else
                    {
                      log.Error("Unable to create {0} activities on server '{1}' using client '{2}'.", activityList.Count, proximityServer.Name, identityClient.Profile.Name);
                      error = true;
                      break;
                    }
                  }
                  else
                  {
                    log.Error("Proximity server '{0}' has only {1} activity slots left. Can't create {2} activities using client '{3}'.", proximityServer.Name, proximityServer.AvailableActivitySlots, activityList.Count, identityClient.Profile.Name);
                    error = true;
                    break;
                  }
                }

                if (error) break;
              }

              if (error) break;


              if (!error) log.Info("  * {0} identities created and initialized on {1} servers.", cmd.CreateCount, cmd.Count);
              break;
            }



          case CommandType.DeleteActivity:
            {
              CommandDeleteActivity cmd = (CommandDeleteActivity)command;

              List<Activity> activityList = new List<Activity>();
              for (int i = 0; i < cmd.Count; i++)
              {
                List<Activity> activityGroup;
                if (activityGroups.TryGetValue(cmd.Name, out activityGroup))
                {
                  int activityIndex = cmd.Index + i;
                  if ((0 <= activityIndex) && (activityIndex < activityGroup.Count))
                  {
                    Activity activity = activityGroup[activityIndex];
                    activityList.Add(activity);
                    activityGroup[activityIndex] = null;
                  }
                  else
                  {
                    log.Error("  * Activity group '{0}' does not have member index {1}.", activityIndex);
                    error = true;
                    break;
                  }
                }
                else
                {
                  log.Error("  * Activity group '{0}' does not exist.", cmd.Name);
                  error = true;
                  break;
                }
              }

              if (error) break;


              foreach (Activity activity in activityList)
              {
                IdentityClient identityClient = activity.OwnerIdentityClient;
                Task <bool> deleteTask = identityClient.DeleteActivityAsync(activity.ProximityServer, activity.PrimaryInfo.ActivityId);
                if (deleteTask.Result)
                {
                  activity.HostingActive = false;
                  activity.ProximityServer.RemoveActivity(activity);

                  if (!activities.Remove(activity.GetName()))
                    log.Error("Removing activity {0} from global list failed.", activity.GetName());
                }
                else 
                {
                  log.Error("Unable to delete activity '{0}' from server '{1}' using client '{2}'.", activity.GetName(), activity.ProximityServer.Name, identityClient.Profile.Name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} activities deleted.", activityList.Count);

              break;
            }



          case CommandType.TestQuery:
            {
              CommandTestQuery cmd = (CommandTestQuery)command;

              List<ProfileServer> targetServers = new List<ProfileServer>();
              for (int i = 0; i < cmd.Count; i++)
              {
                string name = GetServerInstanceName(cmd.Group, cmd.Index + i);
                ProfileServer profileServer;
                if (profileServers.TryGetValue(name, out profileServer))
                {
                  targetServers.Add(profileServer);
                }
                else
                {
                  log.Error("  * Profile server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }

              }

              int serversSkipped = 0;
              int serversQueried = 0;
              IdentityClient client = null;
              try
              {
                client = new IdentityClient("Query Client", "Query Client", new GpsLocation(0, 0), null, 0, null, 0);

                int maxResults = cmd.IncludeImages ? 1000 : 10000;
                string nameFilter = cmd.NameFilter != "**" ? cmd.NameFilter : null;
                string typeFilter = cmd.TypeFilter != "**" ? cmd.TypeFilter : null;
                GpsLocation queryLocation = cmd.Latitude != GpsLocation.NoLocation.Latitude ? new GpsLocation(cmd.Latitude, cmd.Longitude) : null;
                foreach (ProfileServer targetServer in targetServers)
                {
                  if (!targetServer.IsInitialized())
                  {
                    log.Trace("Profile server '{0}' not initialized, skipping ...", targetServer.Name);
                    serversSkipped++;
                    continue;
                  }
                  byte[] targetServerId = targetServer.GetNetworkId();
                  client.InitializeTcpClient();
                  Task<IdentityClient.ProfileSearchQueryInfo> searchTask = client.ProfileSearchQueryAsync(targetServer, nameFilter, typeFilter, queryLocation, cmd.Radius, false, cmd.IncludeImages);
                  IdentityClient.ProfileSearchQueryInfo searchResults = searchTask.Result;
                  if (searchResults != null)
                  {
                    List<byte[]> expectedCoveredServers;
                    int localServerResults;
                    List<ProfileQueryInformation> expectedSearchResults = targetServer.GetExpectedSearchResults(nameFilter, typeFilter, queryLocation, cmd.Radius, false, cmd.IncludeImages, out expectedCoveredServers, out localServerResults);
                    List<ProfileQueryInformation> realResults = searchResults.Results;
                    List<byte[]> realCoveredServers = searchResults.CoveredServers;

                    if (DebugModeEnabled)
                    {
                      log.Info("  * '{0}': {1} real results, {2} calculated results, {3} max. real results, {4} local server results, {5} real covered servers, {6} calculated covered servers.", targetServer.Name, realResults.Count, expectedSearchResults.Count, maxResults, localServerResults, realCoveredServers.Count, expectedCoveredServers.Count);
                    }

                    if (!CompareSearchResults(realResults, expectedSearchResults, maxResults))
                    {
                      log.Error("  * Real search results are different from the expected results on server instance '{0}'.", targetServer.Name);
                      error = true;
                      break;
                    }

                    if (!CompareCoveredServers(targetServerId, realCoveredServers, expectedCoveredServers, localServerResults, maxResults))
                    {
                      log.Error("  * Real covered servers are different from the expected covered servers on server instance '{0}'.", targetServer.Name);
                      error = true;
                      break;
                    }

                    serversQueried++;
                  }
                  else
                  {
                    log.Error("  * Unable to perform search on server instance '{0}'.", targetServer.Name);
                    error = true;
                    break;
                  }
                }
              }
              catch (Exception e)
              {
                log.Error("Exception occurred: {0}", e.ToString());
                error = true;
                break;
              }

              if (!error) log.Info("  * Results of search queries on {0} servers match expected results. {1} servers were offline and skipped.", serversQueried, serversSkipped);

              break;
            }




          case CommandType.TestQueryActivity:
            {
              CommandTestQueryActivity cmd = (CommandTestQueryActivity)command;

              List<ProximityServer> targetServers = new List<ProximityServer>();
              for (int i = 0; i < cmd.Count; i++)
              {
                string name = GetServerInstanceName(cmd.Group, cmd.Index + i);
                ProximityServer proximityServer;
                if (proximityServers.TryGetValue(name, out proximityServer))
                {
                  targetServers.Add(proximityServer);
                }
                else
                {
                  log.Error("  * Proximity server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              int serversSkipped = 0;
              int serversQueried = 0;
              IdentityClient client = null;
              try
              {
                client = new IdentityClient("Query Client", "Query Client", new GpsLocation(0, 0), null, 0, null, 0);

                int maxResults = 10000;
                string typeFilter = cmd.TypeFilter != "**" ? cmd.TypeFilter : null;
                GpsLocation queryLocation = cmd.Latitude != GpsLocation.NoLocation.Latitude ? new GpsLocation(cmd.Latitude, cmd.Longitude) : null;
                DateTime? startNotAfter = cmd.StartNotAfter != null ? (DateTime?)DateTime.UtcNow.AddSeconds(cmd.StartNotAfter.Value) : null;
                DateTime? expirationNotBefore = cmd.ExpirationNotBefore != null ? (DateTime?)DateTime.UtcNow.AddSeconds(cmd.ExpirationNotBefore.Value) : null;
                foreach (ProximityServer targetServer in targetServers)
                {
                  if (!targetServer.IsInitialized())
                  {
                    log.Trace("Proximity server '{0}' not initialized, skipping ...", targetServer.Name);
                    serversSkipped++;
                    continue;
                  }

                  byte[] targetServerId = targetServer.GetNetworkId();
                  client.InitializeTcpClient();
                  Task<IdentityClient.ActivitySearchQueryInfo> searchTask = client.ActivitySearchQueryAsync(targetServer, typeFilter, queryLocation, cmd.Radius, startNotAfter, expirationNotBefore, false);
                  IdentityClient.ActivitySearchQueryInfo searchResults = searchTask.Result;
                  if (searchResults != null)
                  {
                    List<byte[]> expectedCoveredServers;
                    int localServerResults;
                    List<ActivityQueryInformation> expectedSearchResults = targetServer.GetExpectedSearchResults(typeFilter, queryLocation, cmd.Radius, startNotAfter, expirationNotBefore, false, out expectedCoveredServers, out localServerResults);
                    List<ActivityQueryInformation> realResults = searchResults.Results;
                    List<byte[]> realCoveredServers = searchResults.CoveredServers;

                    if (DebugModeEnabled)
                    {
                      log.Info("  * '{0}': {1} real results, {2} calculated results, {3} max. real results, {4} local server results, {5} real covered servers, {6} calculated covered servers.", targetServer.Name, realResults.Count, expectedSearchResults.Count, maxResults, localServerResults, realCoveredServers.Count, expectedCoveredServers.Count);
                    }

                    if (!CompareSearchResults(realResults, expectedSearchResults, maxResults))
                    {
                      log.Error("  * Real search results are different from the expected results on server instance '{0}'.", targetServer.Name);

                      log.Debug("Expected results:");
                      foreach (ActivityQueryInformation info in expectedSearchResults)
                        log.Debug("  {0}", GetActivityName(info.SignedActivity.Activity.Type, info.SignedActivity.Activity.Id));

                      log.Debug("Real results:");
                      foreach (ActivityQueryInformation info in realResults)
                        log.Debug("  {0}", GetActivityName(info.SignedActivity.Activity.Type, info.SignedActivity.Activity.Id));

                      error = true;
                      break;
                    }

                    if (!CompareCoveredServers(targetServerId, realCoveredServers, expectedCoveredServers, localServerResults, maxResults))
                    {
                      log.Error("  * Real covered servers are different from the expected covered servers on server instance '{0}'.", targetServer.Name);
                      error = true;
                      break;
                    }

                    serversQueried++;
                  }
                  else
                  {
                    log.Error("  * Unable to perform search on server instance '{0}'.", targetServer.Name);
                    error = true;
                    break;
                  }
                }
              }
              catch (Exception e)
              {
                log.Error("Exception occurred: {0}", e.ToString());
                error = true;
                break;
              }

              if (!error) log.Info("  * Results of search queries on {0} servers match expected results. {1} servers were offline and skipped.", serversQueried, serversSkipped);

              break;
            }



          case CommandType.Delay:
            {
              CommandDelay cmd = (CommandDelay)command;
              log.Info("  * Waiting {0} seconds ...", cmd.Seconds);
              Thread.Sleep(TimeSpan.FromSeconds((double)cmd.Seconds));
              break;
            }


          case CommandType.TakeSnapshot:
            {
              CommandTakeSnapshot cmd = (CommandTakeSnapshot)command;

              HashSet<string> runningProfileServerNames = new HashSet<string>(StringComparer.Ordinal);
              HashSet<string> runningProximityServerNames = new HashSet<string>(StringComparer.Ordinal);
              foreach (ServerBase server in servers.Values)
              {
                if (server.IsRunningProcess())
                {
                  if (!server.Stop())
                  {
                    log.Error("  * Failed to stop server '{0}'.", server.Name);
                    error = true;
                    break;
                  }

                  if (server.Type == ServerType.Profile) runningProfileServerNames.Add(server.Name);
                  else runningProximityServerNames.Add(server.Name);
                }
              }

              if (error) break;

              Snapshot snapshot = new Snapshot(cmd.Name);
              if (snapshot.Take(runningProfileServerNames, profileServers, runningProximityServerNames, proximityServers, identityClients, activities))
              {
                log.Info("  * Snapshot '{0}' has been created.", cmd.Name);
              }
              else
              {
                log.Error("  * Failed to take simulation snapshot.");
                error = true;
              }

              break;
            }

          case CommandType.LoadSnapshot:
            {
              CommandLoadSnapshot cmd = (CommandLoadSnapshot)command;

              if (index != 1)
              {
                log.Error("  * LoadSnapshot must be the very first command in the scenario.");
                error = true;
                break;
              }

              Snapshot snapshot = new Snapshot(cmd.Name);
              if (!snapshot.Load())
              {
                log.Error("  * Unable to load snapshot '{0}'.", cmd.Name);
                error = true;
                break;
              }

              try
              {
                // Initialize profile servers.
                log.Debug("Initializing profile servers.");
                foreach (ProfileServerSnapshot serverSnapshot in snapshot.ProfileServers)
                {
                  ProfileServer profileServer = ProfileServer.CreateFromSnapshot(serverSnapshot);
                  profileServers.Add(profileServer.Name, profileServer);
                  servers.Add(profileServer.Name, profileServer);
                }

                // Initialize proximity servers.
                log.Debug("Initializing proximity servers.");
                foreach (ProximityServerSnapshot serverSnapshot in snapshot.ProximityServers)
                {
                  ProximityServer proximityServer = ProximityServer.CreateFromSnapshot(serverSnapshot);
                  proximityServers.Add(proximityServer.Name, proximityServer);
                  servers.Add(proximityServer.Name, proximityServer);
                }

                // Initialize identities and connect them with their profile servers.
                log.Debug("Initializing identity clients.");
                foreach (IdentitySnapshot identitySnapshot in snapshot.Identities)
                {
                  ProfileServer profileServer = profileServers[identitySnapshot.ProfileServerName];
                  IdentityClient identityClient = IdentityClient.CreateFromSnapshot(identitySnapshot, snapshot.Images, profileServer);
                  profileServer.AddIdentityClientSnapshot(identityClient);
                  identityClients.Add(identityClient.Profile.Name, identityClient);
                }


                // Initialize activities and connect them with their proximity servers.
                log.Debug("Initializing activities.");
                foreach (ActivitySnapshot activitySnapshot in snapshot.Activities)
                {
                  ProximityServer proximityServer = proximityServers[activitySnapshot.ProximityServerName];
                  IdentityClient ownerIdentityClient = identityClients[activitySnapshot.IdentityClientName];
                  Activity activity = Activity.CreateFromSnapshot(activitySnapshot, proximityServer, ownerIdentityClient);
                  proximityServer.AddActivitySnapshot(activity);
                  activities.Add(activity.GetName(), activity);
                }


                // Initialize neighbor relations.
                log.Debug("Initializing neighborhoods.");
                foreach (ProfileServerSnapshot serverSnapshot in snapshot.ProfileServers)
                {
                  ProfileServer profileServer = profileServers[serverSnapshot.Name];

                  List<ServerBase> neighborServers = new List<ServerBase>();
                  foreach (string neighborName in serverSnapshot.LocServer.NeighborsNames)
                  {
                    ServerBase neighborServer = servers[neighborName];
                    neighborServers.Add(neighborServer);
                  }

                  profileServer.LocServer.SetNeighborhood(neighborServers);
                }

                foreach (ProximityServerSnapshot serverSnapshot in snapshot.ProximityServers)
                {
                  ProximityServer proximityServer = proximityServers[serverSnapshot.Name];

                  List<ServerBase> neighborServers = new List<ServerBase>();
                  foreach (string neighborName in serverSnapshot.LocServer.NeighborsNames)
                  {
                    ServerBase neighborServer = servers[neighborName];
                    neighborServers.Add(neighborServer);
                  }

                  proximityServer.LocServer.SetNeighborhood(neighborServers);
                }

                // Start LOC servers and servers.
                log.Debug("Starting profile servers and their LOC servers.");
                foreach (ProfileServerSnapshot serverSnapshot in snapshot.ProfileServers)
                {
                  ProfileServer profileServer = profileServers[serverSnapshot.Name];
                  if (!profileServer.LocServer.Start())
                  {
                    log.Error("  * Unable to start LOC server of profile server instance '{0}'.", profileServer.Name);
                    error = true;
                    break;
                  }

                  if (serverSnapshot.IsRunning)
                  {
                    if (!profileServer.Start())
                    {
                      log.Error("  * Unable to start profile server instance '{0}'.", profileServer.Name);
                      error = true;
                      break;
                    }
                  }
                }

                log.Debug("Starting proximity servers and their LOC servers.");
                foreach (ProximityServerSnapshot serverSnapshot in snapshot.ProximityServers)
                {
                  ProximityServer proximityServer = proximityServers[serverSnapshot.Name];
                  if (!proximityServer.LocServer.Start())
                  {
                    log.Error("  * Unable to start LOC server of proximity server instance '{0}'.", proximityServer.Name);
                    error = true;
                    break;
                  }

                  if (serverSnapshot.IsRunning)
                  {
                    if (!proximityServer.Start())
                    {
                      log.Error("  * Unable to start proximity server instance '{0}'.", proximityServer.Name);
                      error = true;
                      break;
                    }
                  }
                }
              }
              catch (Exception e)
              {
                log.Error("  * Snapshot is corrupted, exception occurred: {0}", e.ToString());
                error = true;
                break;
              }


              if (!error) log.Info("  * Simulation state loaded from snapshot '{0}'.", cmd.Name);

              break;
            }


          case CommandType.DebugMode:
            {
              CommandDebugMode cmd = (CommandDebugMode)command;
              log.Info("  * Debug mode is now {0}.", cmd.Enable ? "ENABLED" : "DISABLED");
              DebugModeEnabled = cmd.Enable;
              break;
            }




          default:
            log.Error("Invalid command type '{0}'.", command.Type);
            error = true;
            break;
        }

        index++;
        if (error) break;
      }

      res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Removes data from previous run.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool ClearHistory()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        if (Directory.Exists(InstanceDirectory))
          Directory.Delete(InstanceDirectory, true);

        Directory.CreateDirectory(InstanceDirectory);
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while trying to delete and recreate '{0}': {1}", InstanceDirectory, e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Generates instance name from a group name and an instance number.
    /// </summary>
    /// <param name="GroupName">Name of the server group.</param>
    /// <param name="InstanceNumber">Instance number.</param>
    /// <returns>Instance name.</returns>
    public static string GetServerInstanceName(string GroupName, int InstanceNumber)
    {
      return string.Format("{0}{1:000}", GroupName, InstanceNumber);
    }


    /// <summary>
    /// Generates identity name from an identity group name and an identity number.
    /// </summary>
    /// <param name="GroupName">Name of the identity group.</param>
    /// <param name="IdentityNumber">Identity number.</param>
    /// <returns>Identity name.</returns>
    public static string GetIdentityName(string GroupName, int IdentityNumber)
    {
      return string.Format("{0}{1:00000}", GroupName, IdentityNumber);
    }


    /// <summary>
    /// Generates internal activity name from an identity group name and an identity number.
    /// </summary>
    /// <param name="GroupName">Name of the activity group.</param>
    /// <param name="ActivityId">Activity identifier.</param>
    /// <returns>Activity name.</returns>
    public static string GetActivityName(string GroupName, uint ActivityId)
    {
      return string.Format("{0}{1:00000}", GroupName, ActivityId);
    }


    /// <summary>
    /// Checks log files of server instances to see if there are any errors.
    /// </summary>
    /// <returns>true if the logs are clear, false if any errors were found.</returns>
    public bool CheckLogs()
    {
      log.Trace("()");

      bool res = false;
      bool error = false;
      foreach (ServerBase server in servers.Values)
      {        
        string logDir;
        List<string> fileNames;
        List<int> errorCount;
        List<int> warningCount;
        if (server.CheckLogs(out logDir, out fileNames, out errorCount, out warningCount))
        {
          if (logDir.ToLowerInvariant().StartsWith(InstanceDirectory.ToLowerInvariant() + Path.DirectorySeparatorChar))
            logDir = Path.Combine("instances", logDir.Substring(InstanceDirectory.Length + 1));

          int instanceErrorCount = 0;
          for (int i = 0; i < errorCount.Count; i++)
            instanceErrorCount += errorCount[i];

          int instanceWarningCount = 0;
          for (int i = 0; i < warningCount.Count; i++)
            instanceWarningCount += warningCount[i];

          if ((instanceErrorCount > 0) || (instanceWarningCount > 0))
          {
            error = true;

            if (fileNames.Count == 1)
            {
              log.Info("  * {0} errors and {1} warnings found in instance {2} log file '{3}'.", errorCount[0], warningCount[0], server.Name, Path.Combine(logDir, fileNames[0]));
            }
            else
            {
              log.Info("  * {0} errors and {1} warnings found in log files of instance {2}, log directory '{3}':", instanceErrorCount, instanceWarningCount, server.Name, logDir);
              for (int i = 0; i < fileNames.Count; i++)
              {
                if (errorCount[i] > 0)
                  log.Info("    * {0}: {1} errors, {2} warnings.", fileNames[i], errorCount[i], warningCount[i]);
              }
            }
          }
        }
        else
        {
          log.Error("  * Failed to analyze logs of instance {0}.", server.Name);
          error = true;
        }
      }

      if (!error)
      {
        log.Info("  * No errors or warnings found in logs.");
        res = true;
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Compares results of a test search query against the expected results.
    /// </summary>
    /// <param name="RealSearchResults">Search results obtained by real client from a server.</param>
    /// <param name="ExpectedSearchResults">Results calculated from the global knowledge.</param>
    /// <param name="MaxTotalResults">Limit to total number of results. If <paramref name="ExpectedSearchResults"/> is lower than this value, 
    /// the result sets must be exactly the same, otherwise <paramref name="ExpectedSearchResults"/> must be a superset of <paramref name="RealSeachResults"/>
    /// and the number of real results must be equal to this value..</param>
    /// <returns>true if real results match expected results, false otherwise.</returns>
    public bool CompareSearchResults<T>(List<T> RealSearchResults, List<T> ExpectedSearchResults, int MaxTotalResults) where T: IMessage
    {
      log.Trace("(RealSearchResults.Count:{0},ExpectedSearchResults.Count:{1},MaxTotalResults:{2})", RealSearchResults.Count, ExpectedSearchResults.Count, MaxTotalResults);

      bool res = false;

      if (ExpectedSearchResults.Count <= MaxTotalResults)
      {
        // If number of all existing results (i.e. expected results) is no more than maximal possible number of results 
        // the client could get, the real set of results and the expected set of results must be exactly the same.
        res = RealSearchResults.Count == ExpectedSearchResults.Count;
      }
      else
      {
        // If the number of expected result is greater than the maximal number of results the client could get (MaxTotalResults)
        // the number of real results must be equal to MaxTotalResults and it must be a subset of expected results set.
        res = RealSearchResults.Count == MaxTotalResults;
      }

      if (res)
      {
        HashSet<byte[]> expectedSearchBins = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);
        foreach (T info in ExpectedSearchResults)
        {
          byte[] infoBinary = info.ToByteArray();
          expectedSearchBins.Add(infoBinary);
        }

        foreach (T info in RealSearchResults)
        {
          byte[] infoBinary = info.ToByteArray();
          if (expectedSearchBins.Contains(infoBinary))
          {
            expectedSearchBins.Remove(infoBinary);
          }
          else
          {
            res = false;
            break;
          }
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Compares list of covered servers returned by a test search query with the list of expected covered servers.
    /// </summary>
    /// <param name="TargetServer">Network ID of profile server that the client queried.</param>
    /// <param name="RealCoveredServers">List of covered servers obtained by real client from a server.</param>
    /// <param name="ExpectedCoveredServers">List of covered servers calculated from the global knowledge.</param>
    /// <param name="LocalServerResults">Number of results from the server that was queried.</param>
    /// <param name="MaxTotalResults">Limit to total number of real results that could be obtained by the client.</param>
    /// <returns>true if real covered servers is same as expected covered servers, false otherwise.</returns>
    public bool CompareCoveredServers(byte[] TargetServerId, List<byte[]> RealCoveredServers, List<byte[]> ExpectedCoveredServers, int LocalServerResults, int MaxTotalResults)
    {
      log.Trace("(RealCoveredServers.Count:{0},ExpectedCoveredServers.Count:{1},LocalServerResults:{2},MaxTotalResults:{3})", RealCoveredServers != null ? RealCoveredServers.Count.ToString() : "n/a", ExpectedCoveredServers != null ? ExpectedCoveredServers.Count.ToString() : "n/a", LocalServerResults, MaxTotalResults);

      bool res = false;

      if ((RealCoveredServers != null) && (ExpectedCoveredServers != null))
      {
        if (MaxTotalResults <= LocalServerResults)
        {
          // In this case all results may come solely from the target server.
          if (RealCoveredServers.Count == 1)
          {
            bool match = ByteArrayComparer.Equals(RealCoveredServers[0], TargetServerId);
            if (match)
            {
              res = true;
              log.Trace("(-):{0}", res);
              return res;
            }
          }
        }
        else
        {
          // In all other cases the lists of covered servers must match exactly.
          res = RealCoveredServers.Count == ExpectedCoveredServers.Count;
        }
      }

      if (res)
      {
        HashSet<int> expectedServersIndexes = new HashSet<int>();
        for (int i = 0; i < RealCoveredServers.Count; i++)
        {
          byte[] realServer = RealCoveredServers[i];

          bool match = false;
          for (int j = 0; j < ExpectedCoveredServers.Count; j++)
          {
            // The record can not be used twice.
            if (expectedServersIndexes.Contains(j)) continue;

            byte[] expectedServer = ExpectedCoveredServers[j];
            bool itemMatch = ByteArrayComparer.Equals(realServer, expectedServer);

            if (itemMatch)
            {
              expectedServersIndexes.Add(j);
              match = true;
              break;
            }
          }

          // If a single record is not found, results are not as expected.
          if (!match)
          {
            res = false;
            break;
          }
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Finds nearest proximity server to a specific location.
    /// </summary>
    /// <param name="Location">GPS location to find nearest server to.</param>
    /// <returns>Nearest proximity server to the specified location or null if no proximity server exists.</returns>
    public ProximityServer FindNearestProximityServer(GpsLocation Location)
    {
      ProximityServer res = null;
      GpsLocation bestLocation = null;
      double distanceToBest = 0;

      foreach (ProximityServer server in proximityServers.Values)
      {
        bool isCloser = false;

        GpsLocation serverLocation = new GpsLocation(server.NodeLocation.Latitude, server.NodeLocation.Longitude);
        double distance = Location.DistanceTo(serverLocation);

        if (bestLocation != null) isCloser = distance < distanceToBest;
        else isCloser = true;

        if (isCloser)
        { 
          res = server;
          bestLocation = serverLocation;
          distanceToBest = distance;
        }
      }

      return res;
    }
  }
}
