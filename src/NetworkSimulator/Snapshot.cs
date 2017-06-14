using Newtonsoft.Json;
using IopCrypto;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IopCommon;

namespace NetworkSimulator
{
  /// <summary>
  /// Base class for profile and proximity server snapshots.
  /// </summary>
  public abstract class ServerSnapshotBase
  {
    /// <summary>Name of the instance.</summary>
    public string Name;

    /// <summary>true if the server was running before the snapshot was taken.</summary>
    public bool IsRunning;

    /// <summary>GPS location latitude of the server.</summary>
    public decimal LocationLatitude;

    /// <summary>GPS location longitude of the server.</summary>
    public decimal LocationLongitude;

    /// <summary>IP address of the interface on which the server is listening.</summary>
    public string IpAddress;

    /// <summary>Base TCP port of the instance, which can use ports between Port and Port + 19.</summary>
    public int BasePort;

    /// <summary>Port of LOC server.</summary>
    public int LocPort;

    /// <summary>Port of server primary interface.</summary>
    public int PrimaryInterfacePort;

    /// <summary>Port of server neighbors interface.</summary>
    public int NeighborInterfacePort;

    /// <summary>Network ID of the server.</summary>
    public string NetworkId;

    /// <summary>Related LOC server instance.</summary>
    public LocServerSnapshot LocServer;
  }


  /// <summary>
  /// Description of profile server instance snapshot.
  /// </summary>
  public class ProfileServerSnapshot : ServerSnapshotBase
  {
    /// <summary>Port of profile server non-customer interface.</summary>
    public int ClientNonCustomerInterfacePort;

    /// <summary>Port of profile server customer interface.</summary>
    public int ClientCustomerInterfacePort;

    /// <summary>Port of profile server application service interface.</summary>
    public int ClientAppServiceInterfacePort;

    /// <summary>Number of free slots for identities.</summary>
    public int AvailableIdentitySlots;

    /// <summary>List of hosted customer identities.</summary>
    public List<string> HostedIdentities;
  }

  /// <summary>
  /// Description of proximity server instance snapshot.
  /// </summary>
  public class ProximityServerSnapshot : ServerSnapshotBase
  {
    /// <summary>Port of proximity server client interface.</summary>
    public int ClientInterfacePort;

    /// <summary>Number of free slots for activities.</summary>
    public int AvailableActivitySlots;

    /// <summary>List of primary activities.</summary>
    public List<string> Activities;
  }


  /// <summary>
  /// Description of LOC server instance.
  /// </summary>
  public class LocServerSnapshot
  {
    /// <summary>Interface IP address the server listens on.</summary>
    public string IpAddress;

    /// <summary>TCP port the server listens on.</summary>
    public int Port;

    /// <summary>Name of profile servers that are neighbors of the parent profile server.</summary>
    public List<string> NeighborsNames;
  }


  /// <summary>
  /// Profile information of the client's identity.
  /// </summary>
  public class IdentityProfileSnapshot
  {
    /// <summary>Identity name.</summary>
    public string Name;

    /// <summary>Identity Type.</summary>
    public string Type;

    /// <summary>Initial GPS location latitude.</summary>
    public decimal LocationLatitude;

    /// <summary>Initial GPS location longitude.</summary>
    public decimal LocationLongitude;

    /// <summary>Profile image file name or null if the identity has no profile image.</summary>
    public string ProfileImageFileName;

    /// <summary>SHA256 hash of profile image data or null if the identity has no profile image.</summary>
    public string ProfileImageHash;

    /// <summary>Thumbnail image file name or null if the identity has no thumbnail image.</summary>
    public string ThumbnailImageFileName;

    /// <summary>SHA256 hash of thumbnail image data or null if the identity has no thumbnail image.</summary>
    public string ThumbnailImageHash;

    /// <summary>Profile extra data information.</summary>
    public string ExtraData;

    /// <summary>Profile version.</summary>
    public SemVer Version;
  }


  /// <summary>
  /// Description of identity client instance.
  /// </summary>
  public class IdentitySnapshot
  {
    /// <summary>Identity's profile on its hosting server.</summary>
    public IdentityProfileSnapshot Profile;

    /// <summary>Identity's profile on the neighborhood servers.</summary>
    public IdentityProfileSnapshot PropagatedProfile;

    /// <summary>Hosting profile server name.</summary>
    public string ProfileServerName;

    /// <summary>Network identifier of the client's identity.</summary>
    public string IdentityId;

    /// <summary>Public key in uppercase hex format.</summary>
    public string PublicKeyHex;
    
    /// <summary>Private key in uppercase hex format.</summary>
    public string PrivateKeyHex;
    
    /// <summary>Expanded private key in uppercase hex format.</summary>
    public string ExpandedPrivateKeyHex;

    /// <summary>Challenge that the profile server sent to the client when starting conversation.</summary>
    public string Challenge;

    /// <summary>Challenge that the client sent to the profile server when starting conversation.</summary>
    public string ClientChallenge;

    /// <summary>Profile server's public key received when starting conversation.</summary>
    public string ProfileServerKey;
    
    /// <summary>true if the client initialized its profile on the profile server, false otherwise.</summary>
    public bool ProfileInitialized;

    /// <summary>true if the client has an active hosting agreement with the profile server, false otherwise.</summary>
    public bool HostingActive;
  }




  /// <summary>
  /// Activity information snapshot.
  /// </summary>
  public class ActivityInfoSnapshot
  {
    /// <summary>Activity version.</summary>
    public SemVer Version;

    /// <summary>Activity ID.</summary>
    public uint Id;

    /// <summary>Network identifier of the identity that created the activity.</summary>
    public string OwnerIdentityId;

    /// <summary>Public key the identity that created the activity.</summary>
    public string OwnerPublicKey;

    /// <summary>Network identifier of the profile server where the owner of the activity has its profile.</summary>
    public string OwnerProfileServerId;

    /// <summary>IP address of the profile server where the owner of the activity has its profile.</summary>
    public string OwnerProfileServerIpAddress;

    /// <summary>TCP port of primary interface of the profile server where the owner of the activity has its profile.</summary>
    public ushort OwnerProfileServerPrimaryPort;

    /// <summary>Activity type.</summary>
    public string Type;

    /// <summary>GPS location latitude.</summary>
    public decimal LocationLatitude;

    /// <summary>GPS location longitude.</summary>
    public decimal LocationLongitude;

    /// <summary>Precision of the activity's location in metres.</summary>
    public uint PrecisionRadius;

    /// <summary>Time when the activity starts.</summary>
    public long StartTime;

    /// <summary>Time when the activity expires and can be deleted.</summary>
    public long ExpirationTime;

    /// <summary>Cryptographic signature of the activity information when represented with a ActivityInformation structure.</summary>
    public string Signature;

    /// <summary>User defined extra data that serve for satisfying search queries in proximity server network.</summary>
    public string ExtraData;
  }



  /// <summary>
  /// Description of activity instance.
  /// </summary>
  public class ActivitySnapshot
  {
    /// <summary>Activity information on its primary server.</summary>
    public ActivityInfoSnapshot PrimaryInfo;

    /// <summary>Activity information on neighborhood servers.</summary>
    public ActivityInfoSnapshot PropagatedInfo;

    /// <summary>Primary proximity server name.</summary>
    public string ProximityServerName;

    /// <summary>Activity owner identity client.</summary>
    public string IdentityClientName;

    /// <summary>true if the activity is on the server, false otherwise.</summary>
    public bool HostingActive;
  }



  /// <summary>
  /// Describes whole state of a simulation instance in a form that can be saved to a file.
  /// </summary>
  public class Snapshot
  {
    private static Logger log = new Logger("NetworkSimulator.Snapshot");

    /// <summary>Name of the file with serialized profile server information.</summary>
    public const string ProfileServersFileName = "ProfileServers.json";

    /// <summary>Name of the file with serialized proximity server information.</summary>
    public const string ProximityServersFileName = "ProximityServers.json";

    /// <summary>Name of the file with serialized identities information.</summary>
    public const string IdentitiesFileName = "Identities.json";

    /// <summary>Name of the file with serialized activities information.</summary>
    public const string ActivitiesFileName = "Activities.json";

    /// <summary>Name of the file with serialized images information.</summary>
    public const string ImagesFileName = "Images.json";

    /// <summary>Name of the snapshot.</summary>
    public string Name;

    /// <summary>List of profile servers.</summary>
    public List<ProfileServerSnapshot> ProfileServers;

    /// <summary>List of proximity servers.</summary>
    public List<ProximityServerSnapshot> ProximityServers;

    /// <summary>List of identities.</summary>
    public List<IdentitySnapshot> Identities;

    /// <summary>List of activities.</summary>
    public List<ActivitySnapshot> Activities;

    /// <summary>Date of images used by identities mapped by their SHA256 hash.</summary>
    public Dictionary<string, string> Images;

    /// <summary>Directory with snapshot files.</summary>
    private string snapshotDirectory;

    /// <summary>Name of profile servers JSON file within the snapshot directory.</summary>
    private string profileServersFile;
    
    /// <summary>Name of proximity servers JSON file within the snapshot directory.</summary>
    private string proximityServersFile;

    /// <summary>Name of identities JSON file within the snapshot directory.</summary>
    private string identitiesFile;

    /// <summary>Name of activities JSON file within the snapshot directory.</summary>
    private string activitiesFile;

    /// <summary>Name of images JSON file within the snapshot directory.</summary>
    private string imagesFile;


    /// <summary>
    /// Initializes snapshot instance.
    /// </summary>
    /// <param name="Name">Name of the snapshot.</param>
    public Snapshot(string Name)
    {
      this.Name = Name;
      ProfileServers = new List<ProfileServerSnapshot>();
      ProximityServers = new List<ProximityServerSnapshot>();
      Identities = new List<IdentitySnapshot>();
      Activities = new List<ActivitySnapshot>();
      Images = new Dictionary<string, string>(StringComparer.Ordinal);

      snapshotDirectory = Path.Combine(CommandProcessor.SnapshotDirectory, this.Name);
      profileServersFile = Path.Combine(snapshotDirectory, ProfileServersFileName);
      proximityServersFile = Path.Combine(snapshotDirectory, ProximityServersFileName);
      identitiesFile = Path.Combine(snapshotDirectory, IdentitiesFileName);
      activitiesFile = Path.Combine(snapshotDirectory, ActivitiesFileName);
      imagesFile = Path.Combine(snapshotDirectory, ImagesFileName);
    }


    /// <summary>
    /// Stops all running servers and takes snapshot of the simulation.
    /// <para>All servers are expected to be stopped when this method is called.</para>
    /// </summary>
    /// <param name="RunningProfileServerNames">List of profile servers that were running before the snapshot was taken.</param>
    /// <param name="ProfileServers">List of simulation profile servers.</param>
    /// <param name="RunningProfileServerNames">List of proximity servers that were running before the snapshot was taken.</param>
    /// <param name="ProximityServers">List of simulation proximity servers.</param>
    /// <param name="IdentityClients">List of simulation identities.</param>
    /// <param name="Activities">List of simulation activities.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Take(HashSet<string> RunningProfileServerNames, Dictionary<string, ProfileServer> ProfileServers, HashSet<string> RunningProximityServerNames, Dictionary<string, ProximityServer> ProximityServers, Dictionary<string, IdentityClient> IdentityClients, Dictionary<string, Activity> Activities)
    {
      log.Trace("()");

      foreach (ProfileServer server in ProfileServers.Values)
      {
        ProfileServerSnapshot serverSnapshot = server.CreateSnapshot();
        serverSnapshot.IsRunning = RunningProfileServerNames.Contains(server.Name);
        this.ProfileServers.Add(serverSnapshot);
      }

      foreach (ProximityServer server in ProximityServers.Values)
      {
        ProximityServerSnapshot serverSnapshot = server.CreateSnapshot();
        serverSnapshot.IsRunning = RunningProximityServerNames.Contains(server.Name);
        this.ProximityServers.Add(serverSnapshot);
      }

      foreach (IdentityClient identity in IdentityClients.Values)
      {
        IdentitySnapshot identitySnapshot = identity.CreateSnapshot();

        if (identitySnapshot.Profile.ProfileImageHash != null)
        {
          if (!this.Images.ContainsKey(identitySnapshot.Profile.ProfileImageHash))
          {
            string imageDataHex = identity.Profile.ProfileImage.ToHex();
            this.Images.Add(identitySnapshot.Profile.ProfileImageHash, imageDataHex);
          }
        }

        if (identitySnapshot.Profile.ThumbnailImageHash != null)
        {
          if (!this.Images.ContainsKey(identitySnapshot.Profile.ThumbnailImageHash))
          {
            string imageDataHex = identity.Profile.ThumbnailImage.ToHex();
            this.Images.Add(identitySnapshot.Profile.ThumbnailImageHash, imageDataHex);
          }
        }

        if (identitySnapshot.PropagatedProfile.ProfileImageHash != null)
        {
          if (!this.Images.ContainsKey(identitySnapshot.PropagatedProfile.ProfileImageHash))
          {
            string imageDataHex = identity.PropagatedProfile.ProfileImage.ToHex();
            this.Images.Add(identitySnapshot.PropagatedProfile.ProfileImageHash, imageDataHex);
          }
        }

        if (identitySnapshot.PropagatedProfile.ThumbnailImageHash != null)
        {
          if (!this.Images.ContainsKey(identitySnapshot.PropagatedProfile.ThumbnailImageHash))
          {
            string imageDataHex = identity.PropagatedProfile.ThumbnailImage.ToHex();
            this.Images.Add(identitySnapshot.PropagatedProfile.ThumbnailImageHash, imageDataHex);
          }
        }

        this.Identities.Add(identitySnapshot);
      }

      foreach (Activity activity in Activities.Values)
      {
        ActivitySnapshot activitySnapshot = activity.CreateSnapshot();
        this.Activities.Add(activitySnapshot);
      }


      bool error = false;
      try
      {
        if (Directory.Exists(snapshotDirectory))
          Directory.Delete(snapshotDirectory, true);

        Directory.CreateDirectory(snapshotDirectory);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while trying to delete and recreate '{0}': {1}", snapshotDirectory, e.ToString());
        error = true;
      }

      if (!error)
      {
        try
        {
          string serializedProfileServers = JsonConvert.SerializeObject(this.ProfileServers, Formatting.Indented);
          string serializedProximityServers = JsonConvert.SerializeObject(this.ProximityServers, Formatting.Indented);
          string serializedIdentities = JsonConvert.SerializeObject(this.Identities, Formatting.Indented);
          string serializedActivities = JsonConvert.SerializeObject(this.Activities, Formatting.Indented);
          string serializedImages = JsonConvert.SerializeObject(this.Images, Formatting.Indented);

          File.WriteAllText(profileServersFile, serializedProfileServers);
          File.WriteAllText(proximityServersFile, serializedProximityServers);
          File.WriteAllText(identitiesFile, serializedIdentities);
          File.WriteAllText(activitiesFile, serializedActivities);
          File.WriteAllText(imagesFile, serializedImages);
        }
        catch (Exception e)
        {
          log.Error("Exception occurred while trying to save serialized simulation information: {0}", e.ToString());
          error = true;
        }
      }

      if (!error)
      {
        List<ServerBase> allServers = new List<ServerBase>();
        allServers.AddRange(ProfileServers.Values);
        allServers.AddRange(ProximityServers.Values);
        foreach (ServerBase server in allServers)
        {
          string serverInstanceDirectory = server.GetInstanceDirectoryName();
          string snapshotInstanceDirectory = Path.Combine(new string[] { snapshotDirectory, "bin", server.Name });
          if (!Helpers.DirectoryCopy(serverInstanceDirectory, snapshotInstanceDirectory, true, new string[] { "logs", "tmp" }))
          {
            log.Error("Unable to copy files from directory '{0}' to '{1}'.", serverInstanceDirectory, snapshotInstanceDirectory);
            error = true;
            break;
          }

          string logsDirectory = Path.Combine(snapshotInstanceDirectory, "Logs");
          try
          {
            if (Directory.Exists(logsDirectory))
              Directory.Delete(logsDirectory, true);
          }
          catch (Exception e)
          {
            log.Error("Exception occurred while trying to delete directory '{0}': {1}", logsDirectory, e.ToString());
            error = true;
            break;
          }
        }
      }

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Loads snapshot from snapshot folder.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Load()
    {
      log.Trace("()");

      bool error = false;

      try
      {
        log.Debug("Loading profile servers information.");
        string serializedProfileServers = File.ReadAllText(profileServersFile);

        log.Debug("Deserializing profile servers information.");
        ProfileServers = JsonConvert.DeserializeObject<List<ProfileServerSnapshot>>(serializedProfileServers);


        log.Debug("Loading proximity servers information.");
        string serializedProximityServers = File.ReadAllText(proximityServersFile);

        log.Debug("Deserializing proximity servers information.");
        ProximityServers = JsonConvert.DeserializeObject<List<ProximityServerSnapshot>>(serializedProximityServers);


        log.Debug("Loading identities information.");
        string serializedIdentities = File.ReadAllText(identitiesFile);

        log.Debug("Deserializing identities information.");
        Identities = JsonConvert.DeserializeObject<List<IdentitySnapshot>>(serializedIdentities);


        log.Debug("Loading activities information.");
        string serializedActivities = File.ReadAllText(activitiesFile);

        log.Debug("Deserializing activities information.");
        Activities = JsonConvert.DeserializeObject<List<ActivitySnapshot>>(serializedActivities);


        log.Debug("Loading images information.");
        string serializedImages = File.ReadAllText(imagesFile);

        log.Debug("Deserializing images information.");
        Images = JsonConvert.DeserializeObject<Dictionary<string, string>>(serializedImages);


        log.Debug("Loading profile servers instance folders.");
        foreach (ProfileServerSnapshot server in ProfileServers)
        {
          string serverInstanceDirectory = ServerBase.GetInstanceDirectoryName(server.Name, ServerType.Profile);
          string snapshotInstanceDirectory = Path.Combine(new string[] { snapshotDirectory, "bin", server.Name });
          log.Debug("Copying '{0}' to '{1}'.", snapshotInstanceDirectory, serverInstanceDirectory);
          if (!Helpers.DirectoryCopy(snapshotInstanceDirectory, serverInstanceDirectory))
          {
            log.Error("Unable to copy files from directory '{0}' to '{1}'.", snapshotInstanceDirectory, serverInstanceDirectory);
            error = true;
            break;
          }
        }


        log.Debug("Loading proximity servers instance folders.");
        foreach (ProximityServerSnapshot server in ProximityServers)
        {
          string serverInstanceDirectory = ServerBase.GetInstanceDirectoryName(server.Name, ServerType.Proximity);
          string snapshotInstanceDirectory = Path.Combine(new string[] { snapshotDirectory, "bin", server.Name });
          log.Debug("Copying '{0}' to '{1}'.", snapshotInstanceDirectory, serverInstanceDirectory);
          if (!Helpers.DirectoryCopy(snapshotInstanceDirectory, serverInstanceDirectory))
          {
            log.Error("Unable to copy files from directory '{0}' to '{1}'.", snapshotInstanceDirectory, serverInstanceDirectory);
            error = true;
            break;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while trying to load serialized simulation files: {0}", e.ToString());
        error = true;
      }

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
