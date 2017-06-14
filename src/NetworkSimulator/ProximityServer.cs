using Google.Protobuf;
using Iop.Proximityserver;
using IopCommon;
using IopCrypto;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkSimulator
{
  /// <summary>
  /// Represents a single proximity server. Provides abilities to start and stop a server process
  /// and holds information about the expected state of the server's database.
  /// </summary>
  public class ProximityServer : ServerBase
  {
    /// <summary>Maximal number of activities that a single proximity server can host.</summary>
    public const int MaxHostedActivities = 50000;

    /// <summary>Name of the final configuration file.</summary>
    public const string ConfigFileName = "ProximityServer.conf";

    /// <summary>Name of the main executable file.</summary>
    public const string ExecutableFileName = "ProximityServer";

    /// <summary>Port of proximity server client interface.</summary>
    private int clientInterfacePort;
    /// <summary>Port of proximity server client interface.</summary>
    public int ClientInterfacePort { get { return clientInterfacePort; } }

    /// <summary>Number of free slots for activities.</summary>
    private int availableActivitySlots;
    /// <summary>Number of free slots for activities.</summary>
    public int AvailableActivitySlots { get { return availableActivitySlots; } }

    /// <summary>List of managed activities.</summary>
    private Dictionary<string, Activity> activities;

    
    /// <summary>
    /// Creates a new instance of a proximity server.
    /// </summary>
    /// <param name="Name">Unique proximity server instance name.</param>
    /// <param name="Location">GPS location of this proximity server instance.</param>
    /// <param name="Port">Base TCP port that defines the range of ports that are going to be used by this proximity server instance and its related servers.</param>
    public ProximityServer(string Name, GpsLocation Location, int Port):
      base(Name, Location, Port)
    {
      log = new Logger("NetworkSimulator.ProximityServer", "[" + Name + "] ");
      log.Trace("(Name:'{0}',Location:{1},Port:{2})", Name, Location, Port);

      clientInterfacePort = basePort + 3;

      availableActivitySlots = MaxHostedActivities;
      activities = new Dictionary<string, Activity>();

      log.Trace("(-)");
    }


    /// <summary>
    /// Returns name of the configuration file.
    /// </summary>
    /// <returns>Name of the configuration file.</returns>
    public override string GetConfigFileName()
    {
      return ConfigFileName;
    }


    /// <summary>
    /// Returns name of the main executable file.
    /// </summary>
    /// <returns>Name of the main executable file.</returns>
    public override string GetExecutableFileName()
    {
      return ExecutableFileName;
    }

    /// <summary>
    /// Creates a final configuration file for the instance.
    /// </summary>
    /// <param name="FinalConfigFile">Name of the final configuration file.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public override bool InitializeConfig(string FinalConfigFile)
    {
      log.Trace("(FinalConfigFile:'{0}')", FinalConfigFile);

      bool res = false;

      try
      {
        string config = "test_mode = on\n"
          + "external_server_address = 127.0.0.1\n"
          + "bind_to_interface = 0.0.0.0\n"
          + "primary_interface_port = " + primaryInterfacePort.ToString() + "\n"
          + "neighbor_interface_port = " + neighborInterfacePort.ToString() + "\n"
          + "client_interface_port = " + clientInterfacePort.ToString() + "\n"
          + "tls_server_certificate = ProximityServer.pfx\n"
          + "db_file_name = ProximityServer.db\n"
          + "max_activities = 50000\n"
          + "neighborhood_initialization_parallelism = 10\n"
          + "loc_port = " + locPort.ToString() + "\n"
          + "neighbor_expiration_time = 86400\n"
          + "max_neighborhood_size = 110\n"
          + "max_follower_servers_count = 200\n"
          + "follower_refresh_time = 43200\n"
          + "can_api_port = 15001\n";

        File.WriteAllText(FinalConfigFile, config);
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    

    /// <summary>
    /// Adds a new activity to the proximity server activity list.
    /// </summary>
    /// <param name="Activity">Activity to add.</param>
    public void AddActivity(Activity Activity)
    {
      log.Trace("(Activity.GetName():'{0}')", Activity.GetName());

      activities.Add(Activity.GetName(), Activity);
      availableActivitySlots--;

      log.Trace("(-)");
    }

    /// <summary>
    /// Removes existing activity from the proximity server activity list.
    /// </summary>
    /// <param name="Activity">Activity to remove.</param>
    public void RemoveActivity(Activity Activity)
    {
      log.Trace("(Activity.GetName():'{0}')", Activity.GetName());

      activities.Remove(Activity.GetName());
      availableActivitySlots++;

      log.Trace("(-)");
    }

    /// <summary>
    /// Adds a new activity to the proximity server activity list when initializing from snapshot.
    /// </summary>
    /// <param name="Activity">Activity to add.</param>
    public void AddActivitySnapshot(Activity Activity)
    {
      activities.Add(Activity.GetName(), Activity);
    }

    

    /// <summary>
    /// Calculates expected search query results from the given proximity server and its neighbors.
    /// </summary>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="StartNotAfterFilter">Filter for upper bound on start time, or null if start time filtering is not required.</param>
    /// <param name="ExpirationNotBeforeFilter">Filter for lower bound on expiration time, or null if expiration time filtering is not required.</param>
    /// <param name="IncludePrimaryOnly">If set to true, the search results should only include primary activities on the queried proximity server.</param>
    /// <param name="ExpectedCoveredServers">If the function succeeds, this is filled with list of covered servers that the search query should return.</param>
    /// <param name="LocalServerResultsCount">If the function succeeds, this is filled with the number of search results obtained from the local server.</param>
    /// <returns>List of activities that match the given criteria or null if the function fails.</returns>
    public List<ActivityQueryInformation> GetExpectedSearchResults(string TypeFilter, GpsLocation LocationFilter, int Radius, DateTime? StartNotAfterFilter, DateTime? ExpirationNotBeforeFilter, bool IncludePrimaryOnly, out List<byte[]> ExpectedCoveredServers, out int LocalServerResultsCount)
    {
      log.Trace("(TypeFilter:'{0}',LocationFilter:[{1}],Radius:{2},StartNotAfterFilter:{3},ExpirationNotBeforeFilter:{4},IncludePrimaryOnly:{5})", TypeFilter, LocationFilter, Radius, 
        StartNotAfterFilter != null ? StartNotAfterFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null", ExpirationNotBeforeFilter != null ? ExpirationNotBeforeFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null", IncludePrimaryOnly);

      List<ActivityQueryInformation> res = new List<ActivityQueryInformation>();
      ExpectedCoveredServers = new List<byte[]>();
      ExpectedCoveredServers.Add(networkId);

      List<ActivityQueryInformation> localResults = SearchQuery(TypeFilter, LocationFilter, Radius, StartNotAfterFilter, ExpirationNotBeforeFilter, false);
      LocalServerResultsCount = localResults.Count;

      foreach (ActivityQueryInformation localResult in localResults)
      {
        localResult.IsPrimary = true;
      }

      res.AddRange(localResults);

      if (!IncludePrimaryOnly)
      {
        List<ServerBase> neighbors = LocServer.GetNeighbors();
        foreach (ServerBase neighborServer in neighbors)
        {
          if (!(neighborServer is ProximityServer)) continue;
          ProximityServer neighbor = (ProximityServer)neighborServer;
          ByteString neighborId = ProtocolHelper.ByteArrayToByteString(neighbor.GetNetworkId());
          ExpectedCoveredServers.Add(neighborId.ToByteArray());

          ServerContactInfo neighborContactInfo = neighbor.GetServerContactInfo();
          List<ActivityQueryInformation> neighborResults = neighbor.SearchQuery(TypeFilter, LocationFilter, Radius, StartNotAfterFilter, ExpirationNotBeforeFilter, true);
          foreach (ActivityQueryInformation neighborResult in neighborResults)
          {
            neighborResult.IsPrimary = false;
            neighborResult.PrimaryServer = neighborContactInfo;
          }

          res.AddRange(neighborResults);
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Performs a search query on the proximity server's primary identities.
    /// </summary>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="StartNotAfterFilter">Filter for upper bound on start time, or null if start time filtering is not required.</param>
    /// <param name="ExpirationNotBeforeFilter">Filter for lower bound on expiration time, or null if expiration time filtering is not required.</param>
    /// <param name="Propagated">If set to true, the search results only contains versions of activities that were propagated to the neighborhood.</param>
    /// <returns>List of primary activities that match the given criteria.</returns>
    public List<ActivityQueryInformation> SearchQuery(string TypeFilter, GpsLocation LocationFilter, int Radius, DateTime? StartNotAfterFilter, DateTime? ExpirationNotBeforeFilter, bool Propagated)
    {
      log.Trace("(TypeFilter:'{0}',LocationFilter:[{1}],Radius:{2},StartNotAfterFilter:{3},ExpirationNotBeforeFilter:{4},Propagated:{5})", TypeFilter, LocationFilter, Radius,
        StartNotAfterFilter != null ? StartNotAfterFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null", ExpirationNotBeforeFilter != null ? ExpirationNotBeforeFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null", Propagated);

      List<ActivityQueryInformation> res = new List<ActivityQueryInformation>();

      foreach (Activity activity in activities.Values)
      {
        if (activity.MatchesSearchQuery(TypeFilter, LocationFilter, Radius, StartNotAfterFilter, ExpirationNotBeforeFilter, Propagated))
        {
          ActivityQueryInformation info = activity.GetActivityQueryInformation(false, null, Propagated);
          res.Add(info);
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Creates proximity server's snapshot.
    /// </summary>
    /// <returns>Proximity server's snapshot.</returns>
    public ProximityServerSnapshot CreateSnapshot()
    {
      ProximityServerSnapshot res = new ProximityServerSnapshot()
      {
        AvailableActivitySlots = this.availableActivitySlots,
        BasePort = this.basePort,
        Activities = this.activities.Values.Select(a => a.GetName()).ToList(),
        IpAddress = this.ipAddress.ToString(),
        IsRunning = false,
        LocPort = this.locPort,
        LocServer = this.locServer.CreateSnapshot(),
        LocationLatitude = this.location.Latitude,
        LocationLongitude = this.location.Longitude,
        Name = this.name,
        NetworkId = this.networkId.ToHex(),
        PrimaryInterfacePort = this.primaryInterfacePort,
        NeighborInterfacePort = this.neighborInterfacePort,
        ClientInterfacePort = this.clientInterfacePort
      };
      return res;
    }


    /// <summary>
    /// Creates instance of proximity server from snapshot.
    /// </summary>
    /// <param name="Snapshot">Proximity server snapshot.</param>
    /// <returns>New proximity server instance.</returns>
    public static ProximityServer CreateFromSnapshot(ProximityServerSnapshot Snapshot)
    {
      ProximityServer res = new ProximityServer(Snapshot.Name, new GpsLocation(Snapshot.LocationLatitude, Snapshot.LocationLongitude), Snapshot.BasePort);

      res.availableActivitySlots = Snapshot.AvailableActivitySlots;
      res.ipAddress = IPAddress.Parse(Snapshot.IpAddress);
      res.locPort = Snapshot.LocPort;
      res.networkId = Snapshot.NetworkId.FromHex();
      res.basePort = Snapshot.BasePort;
      res.primaryInterfacePort = Snapshot.PrimaryInterfacePort;
      res.neighborInterfacePort = Snapshot.NeighborInterfacePort;
      res.clientInterfacePort = Snapshot.ClientInterfacePort;
      res.instanceDirectory = res.GetInstanceDirectoryName();
      res.locServer = new LocServer(res.ipAddress, res.locPort, res.location, res);

      return res;
    }


    /// <summary>
    /// Obtains server's contact information.
    /// </summary>
    /// <returns>Server's contact information.</returns>
    public ServerContactInfo GetServerContactInfo()
    {
      ServerContactInfo res = new ServerContactInfo()
      {
        IpAddress = ProtocolHelper.ByteArrayToByteString(IpAddress.GetAddressBytes()),
        NetworkId = ProtocolHelper.ByteArrayToByteString(networkId),
        PrimaryPort = (uint)PrimaryInterfacePort
      };
      return res;
    }
  }
}
