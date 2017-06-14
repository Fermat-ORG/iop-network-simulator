using Google.Protobuf;
using Iop.Profileserver;
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
  /// Represents a single profile server. Provides abilities to start and stop a server process
  /// and holds information about the expected state of the server's database.
  /// </summary>
  public class ProfileServer : ServerBase
  {
    /// <summary>Maximal number of identities that a single profile server can host.</summary>
    public const int MaxHostedIdentities = 20000;

    /// <summary>Name of the final configuration file.</summary>
    public const string ConfigFileName = "ProfileServer.conf";

    /// <summary>Name of the main executable file.</summary>
    public const string ExecutableFileName = "ProfileServer";

    /// <summary>Port of profile server non-customer interface.</summary>
    private int clientNonCustomerInterfacePort;
    /// <summary>Port of profile server non-customer interface.</summary>
    public int ClientNonCustomerInterfacePort { get { return clientNonCustomerInterfacePort; } }

    /// <summary>Port of profile server customer interface.</summary>
    private int clientCustomerInterfacePort;
    /// <summary>Port of profile server customer interface.</summary>
    public int ClientCustomerInterfacePort { get { return clientCustomerInterfacePort; } }

    /// <summary>Port of profile server application service interface.</summary>
    private int clientAppServiceInterfacePort;


    /// <summary>Number of free slots for identities.</summary>
    private int availableIdentitySlots;
    /// <summary>Number of free slots for identities.</summary>
    public int AvailableIdentitySlots { get { return availableIdentitySlots; } }

    /// <summary>List of hosted customer identities.</summary>
    private List<IdentityClient> hostedIdentities;


    /// <summary>
    /// Creates a new instance of a profile server.
    /// </summary>
    /// <param name="Name">Unique profile server instance name.</param>
    /// <param name="Location">GPS location of this profile server instance.</param>
    /// <param name="Port">Base TCP port that defines the range of ports that are going to be used by this profile server instance and its related servers.</param>
    public ProfileServer(string Name, GpsLocation Location, int Port):
      base(Name, Location, Port)
    {
      log = new Logger("NetworkSimulator.ProfileServer", "[" + Name + "] ");
      log.Trace("(Name:'{0}',Location:{1},Port:{2})", Name, Location, Port);


      clientNonCustomerInterfacePort = basePort + 3;
      clientCustomerInterfacePort = basePort + 4;
      clientAppServiceInterfacePort = basePort + 5;

      availableIdentitySlots = MaxHostedIdentities;
      hostedIdentities = new List<IdentityClient>();

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
          + "server_neighbor_interface_port = " + neighborInterfacePort.ToString() + "\n"
          + "client_non_customer_interface_port = " + clientNonCustomerInterfacePort.ToString() + "\n"
          + "client_customer_interface_port = " + clientCustomerInterfacePort.ToString() + "\n"
          + "client_app_service_interface_port = " + clientAppServiceInterfacePort.ToString() + "\n"
          + "tls_server_certificate = ProfileServer.pfx\n"
          + "image_data_folder = images\n"
          + "tmp_data_folder = tmp\n"
          + "db_file_name = ProfileServer.db\n"
          + "max_hosted_identities = 20000\n"
          + "max_identity_relations = 100\n"
          + "neighborhood_initialization_parallelism = 10\n"
          + "loc_port = " + locPort.ToString() + "\n"
          + "neighbor_profiles_expiration_time = 86400\n"
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
    /// Adds a new identity client to the profile servers identity client list.
    /// </summary>
    /// <param name="Client">Identity client that the profile server is going to host.</param>
    public void AddIdentityClient(IdentityClient Client)
    {
      log.Trace("(Client.Profile.Name:'{0}')", Client.Profile.Name);

      hostedIdentities.Add(Client);
      availableIdentitySlots--;

      log.Trace("(-)");
    }

    /// <summary>
    /// Adds a new identity client to the profile servers identity client list when initializing from snapshot.
    /// </summary>
    /// <param name="Client">Identity client that the profile server is going to host.</param>
    public void AddIdentityClientSnapshot(IdentityClient Client)
    {
      hostedIdentities.Add(Client);
    }



    /// <summary>
    /// Calculates expected search query results from the given profile server and its neighbors.
    /// </summary>
    /// <param name="NameFilter">Name filter of the search query, or null if name filtering is not required.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="IncludeHostedOnly">If set to true, the search results should only include profiles hosted on the queried profile server.</param>
    /// <param name="IncludeImages">If set to true, the search results should include images.</param>
    /// <param name="ExpectedCoveredServers">If the function succeeds, this is filled with list of covered servers that the search query should return.</param>
    /// <param name="LocalServerResultsCount">If the function succeeds, this is filled with the number of search results obtained from the local server.</param>
    /// <returns>List of profiles that match the given criteria or null if the function fails.</returns>
    public List<ProfileQueryInformation> GetExpectedSearchResults(string NameFilter, string TypeFilter, GpsLocation LocationFilter, int Radius, bool IncludeHostedOnly, bool IncludeImages, out List<byte[]> ExpectedCoveredServers, out int LocalServerResultsCount)
    {
      log.Trace("(NameFilter:'{0}',TypeFilter:'{1}',LocationFilter:'{2}',Radius:{3},IncludeHostedOnly:{4},IncludeImages:{5})", NameFilter, TypeFilter, LocationFilter, Radius, IncludeHostedOnly, IncludeImages);

      List<ProfileQueryInformation> res = new List<ProfileQueryInformation>();
      ExpectedCoveredServers = new List<byte[]>();
      ExpectedCoveredServers.Add(networkId);

      List<ProfileQueryInformation> localResults = SearchQuery(NameFilter, TypeFilter, LocationFilter, Radius, IncludeImages, false);
      LocalServerResultsCount = localResults.Count;

      foreach (ProfileQueryInformation localResult in localResults)
      {
        localResult.IsHosted = true;
        localResult.IsOnline = false;
      }

      res.AddRange(localResults);

      if (!IncludeHostedOnly)
      {
        List<ServerBase> neighbors = LocServer.GetNeighbors();
        foreach (ServerBase neighborServer in neighbors)
        {
          if (!(neighborServer is ProfileServer)) continue;
          ProfileServer neighbor = (ProfileServer)neighborServer;
          ByteString neighborId = ProtocolHelper.ByteArrayToByteString(neighbor.GetNetworkId());
          ExpectedCoveredServers.Add(neighborId.ToByteArray());
          List<ProfileQueryInformation> neighborResults = neighbor.SearchQuery(NameFilter, TypeFilter, LocationFilter, Radius, IncludeImages, true);
          foreach (ProfileQueryInformation neighborResult in neighborResults)
          {
            neighborResult.IsHosted = false;
            neighborResult.HostingServerNetworkId = neighborId;
          }

          res.AddRange(neighborResults);
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Performs a search query on the profile server's hosted identities.
    /// </summary>
    /// <param name="NameFilter">Name filter of the search query, or null if name filtering is not required.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="IncludeImages">If set to true, the search results should include images.</param>
    /// <param name="Propagated">If set to true, the search results only contains versions of profiles that were propagated to the neighborhood.</param>
    /// <returns>List of hosted profiles that match the given criteria.</returns>
    public List<ProfileQueryInformation> SearchQuery(string NameFilter, string TypeFilter, GpsLocation LocationFilter, int Radius, bool IncludeImages, bool Propagated)
    {
      log.Trace("(NameFilter:'{0}',TypeFilter:'{1}',LocationFilter:'{2}',Radius:{3},IncludeImages:{4},Propagated:{5})", NameFilter, TypeFilter, LocationFilter, Radius, IncludeImages, Propagated);

      List<ProfileQueryInformation> res = new List<ProfileQueryInformation>();

      foreach (IdentityClient client in hostedIdentities)
      {
        if (client.MatchesSearchQuery(NameFilter, TypeFilter, LocationFilter, Radius, Propagated))
        {
          ProfileQueryInformation info = client.GetProfileQueryInformation(false, false, null, IncludeImages, Propagated);
          res.Add(info);
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Creates profile server's snapshot.
    /// </summary>
    /// <returns>Profile server's snapshot.</returns>
    public ProfileServerSnapshot CreateSnapshot()
    {
      ProfileServerSnapshot res = new ProfileServerSnapshot()
      {
        AvailableIdentitySlots = this.availableIdentitySlots,
        BasePort = this.basePort,
        ClientAppServiceInterfacePort = this.clientAppServiceInterfacePort,
        ClientCustomerInterfacePort = this.clientCustomerInterfacePort,
        ClientNonCustomerInterfacePort = this.clientNonCustomerInterfacePort,
        HostedIdentities = this.hostedIdentities.Select(i => i.Profile.Name).ToList(),
        IpAddress = this.ipAddress.ToString(),
        IsRunning = false,
        LocPort = this.locPort,
        LocServer = this.locServer.CreateSnapshot(),
        LocationLatitude = this.location.Latitude,
        LocationLongitude = this.location.Longitude,
        Name = this.name,
        NetworkId = this.networkId.ToHex(),
        PrimaryInterfacePort = this.primaryInterfacePort,
        NeighborInterfacePort = this.neighborInterfacePort
      };
      return res;
    }


    /// <summary>
    /// Creates instance of profile server from snapshot.
    /// </summary>
    /// <param name="Snapshot">Profile server snapshot.</param>
    /// <returns>New profile server instance.</returns>
    public static ProfileServer CreateFromSnapshot(ProfileServerSnapshot Snapshot)
    {
      ProfileServer res = new ProfileServer(Snapshot.Name, new GpsLocation(Snapshot.LocationLatitude, Snapshot.LocationLongitude), Snapshot.BasePort);

      res.availableIdentitySlots = Snapshot.AvailableIdentitySlots;
      res.clientAppServiceInterfacePort = Snapshot.ClientAppServiceInterfacePort;
      res.clientCustomerInterfacePort = Snapshot.ClientCustomerInterfacePort;
      res.clientNonCustomerInterfacePort = Snapshot.ClientNonCustomerInterfacePort;
      res.ipAddress = IPAddress.Parse(Snapshot.IpAddress);
      res.locPort = Snapshot.LocPort;
      res.networkId = Snapshot.NetworkId.FromHex();
      res.basePort = Snapshot.BasePort;
      res.primaryInterfacePort = Snapshot.PrimaryInterfacePort;
      res.neighborInterfacePort = Snapshot.NeighborInterfacePort;
      res.instanceDirectory = res.GetInstanceDirectoryName();
      res.locServer = new LocServer(res.ipAddress, res.locPort, res.location, res);

      return res;
    }
  }
}
