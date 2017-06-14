using IopCrypto;
using IopProtocol;
using Iop.Proximityserver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Google.Protobuf;
using IopCommon;
using Iop.Shared;

namespace NetworkSimulator
{
  /// <summary>
  /// Represents a single proximity user activity in the network.
  /// </summary>
  public class Activity
  {
    /// <summary>Instance logger.</summary>
    private Logger log;

    /// <summary>Information about activity on its primary server.</summary>
    public ActivityInfo PrimaryInfo;

    /// <summary>Information about activity in the neighborhood.</summary>
    public ActivityInfo PropagatedInfo;

    /// <summary>Primary proximity server of this activity.</summary>
    private ProximityServer proximityServer;
    /// <summary>Profile server hosting the identity profile.</summary>
    public ProximityServer ProximityServer { get { return proximityServer; } }

    /// <summary>Identity client that owns the activity.</summary>
    private IdentityClient ownerIdentityClient;
    /// <summary>Identity client that owns the activity.</summary>
    public IdentityClient OwnerIdentityClient { get { return ownerIdentityClient; } }

    /// <summary>true if the activity has been created on the proximity server, false otherwise.</summary>
    public bool HostingActive;

    /// <summary>
    /// Empty constructor for manual construction of the instance when loading simulation for snapshot.
    /// </summary>
    public Activity()
    {
    }


    /// <summary>
    /// Creates a new activity.
    /// </summary>
    /// <param name="Id">Activity ID.</param>
    /// <param name="Type">Activity type.</param>
    /// <param name="Location">Activity GPS location.</param>
    /// <param name="Precision">Activity GPS location precision radius.</param>
    /// <param name="StartTime">Activity start time.</param>
    /// <param name="ExpirationTime">Activity expiration time.</param>
    /// <param name="ExtraData">Activity extra data.</param>
    /// <param name="OwnerIdentity">Owner of the activity.</param>
    public Activity(uint Id, string Type, GpsLocation Location, uint Precision, DateTime StartTime, DateTime ExpirationTime, string ExtraData, IdentityClient OwnerIdentity)
    {
      ownerIdentityClient = OwnerIdentity;

      ActivityInformation activityInformation = new ActivityInformation()
      {
        Version = SemVer.V100.ToByteString(),
        Id = Id,
        Type = Type,
        ProfileServerContact = new ServerContactInfo()
        {
          IpAddress = ProtocolHelper.ByteArrayToByteString(OwnerIdentity.ProfileServer.IpAddress.GetAddressBytes()),
          NetworkId = ProtocolHelper.ByteArrayToByteString(OwnerIdentity.ProfileServer.GetNetworkId()),
          PrimaryPort = (uint)OwnerIdentity.ProfileServer.PrimaryInterfacePort
        },
        OwnerPublicKey = ProtocolHelper.ByteArrayToByteString(OwnerIdentity.Profile.PublicKey),
        Latitude = Location.GetLocationTypeLatitude(),
        Longitude = Location.GetLocationTypeLongitude(),
        Precision = Precision,
        StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(StartTime),
        ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(ExpirationTime),
        ExtraData = ExtraData != null ? ExtraData : ""
      };
      SignedActivityInformation signedActivityInformation = new SignedActivityInformation()
      {
        Activity = activityInformation,
        Signature = ProtocolHelper.ByteArrayToByteString(OwnerIdentity.SignData(activityInformation.ToByteArray()))
      };
      PrimaryInfo = new ActivityInfo();
      PrimaryInfo.CopyFromSignedActivityInformation(signedActivityInformation);

      PropagatedInfo = new ActivityInfo();
      PropagatedInfo.CopyFrom(PrimaryInfo);


      log = new Logger("NetworkSimulator.Activity", "[" + GetName() + "] ");
      log.Trace("(Type:{0},Location:{1},Precision:{2},StartTime:{3},ExpirationTime:{4},ExtraData:'{5}',OwnerIdentity.Profile.Name:{6})", Type, Location, Precision, StartTime.ToString("yyyy-MM-dd HH:mm:ss"), ExpirationTime.ToString("yyyy-MM-dd HH:mm:ss"), ExtraData, OwnerIdentity.Profile.Name);


      log.Trace("(-)");
    }


    /// <summary>
    /// Returns internal activity name that consits of its ID and type.
    /// </summary>
    /// <returns>Internal activity name.</returns>
    public string GetName()
    {
      return CommandProcessor.GetActivityName(PrimaryInfo.Type, PrimaryInfo.ActivityId);
    }



    /// <summary>
    /// Connects proximity server with activity.
    /// </summary>
    /// <param name="Server">Primary proximity server for the activity.</param>
    public void InitializeActivityOnServer(ProximityServer Server)
    {
      proximityServer = Server;
      HostingActive = true;
    }


    /// <summary>
    /// Deletes activity from its primary proximity server.
    /// </summary>
    /// <returns>true if the function succeeded, false otherwise.</returns>
    public async Task<bool> DeleteActivityFromServerAsync()
    {
      log.Trace("()");
      bool res = await ownerIdentityClient.DeleteActivityAsync(proximityServer, this.PrimaryInfo.ActivityId);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether the activity matches specific search query.
    /// </summary>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="StartNotAfterFilter">Filter for upper bound on start time, or null if start time filtering is not required.</param>
    /// <param name="ExpirationNotBeforeFilter">Filter for lower bound on expiration time, or null if expiration time filtering is not required.</param>
    /// <param name="Propagated">If true, activity information propagated to neighborhood is used, otherwise primary activity information is used.</param>
    /// <returns>true if the identity matches the query, false otherwise.</returns>
    public bool MatchesSearchQuery(string TypeFilter, GpsLocation LocationFilter, int Radius, DateTime? StartNotAfterFilter, DateTime? ExpirationNotBeforeFilter, bool Propagated)
    {
      log.Trace("(TypeFilter:'{0}',LocationFilter:[{1}],Radius:{2},StartNotAfterFilter:{3},ExpirationNotBeforeFilter:{4},Propagated:{5})", TypeFilter, LocationFilter, Radius,
        StartNotAfterFilter != null ? StartNotAfterFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null", ExpirationNotBeforeFilter != null ? ExpirationNotBeforeFilter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null", Propagated);

      ActivityInfo activityInfo = Propagated ? PropagatedInfo : PrimaryInfo;

      bool res = false;
      // Do not include if the activity is not on the server.
      if (HostingActive)
      {
        log.Debug("Activity {0}: Type {1}, Location {2}, Precision {3}, StartTime {4}, ExpirationTime {5}", GetName(), PrimaryInfo.Type, PrimaryInfo.Location, PrimaryInfo.PrecisionRadius, PrimaryInfo.StartTime.ToString("yyyy-MM-dd HH:mm:ss"), PrimaryInfo.ExpirationTime.ToString("yyyy-MM-dd HH:mm:ss"));
        bool matchType = false;
        bool useTypeFilter = !string.IsNullOrEmpty(TypeFilter) && (TypeFilter != "*") && (TypeFilter != "**");
        if (useTypeFilter)
        {
          string value = activityInfo.Type.ToLowerInvariant();
          string filterValue = TypeFilter.ToLowerInvariant();
          matchType = value == filterValue;

          bool valueStartsWith = TypeFilter.EndsWith("*");
          bool valueEndsWith = TypeFilter.StartsWith("*");
          bool valueContains = valueStartsWith && valueEndsWith;

          if (valueContains)
          {
            filterValue = filterValue.Substring(1, filterValue.Length - 2);
            matchType = value.Contains(filterValue);
          }
          else if (valueStartsWith)
          {
            filterValue = filterValue.Substring(0, filterValue.Length - 1);
            matchType = value.StartsWith(filterValue);
          }
          else if (valueEndsWith)
          {
            filterValue = filterValue.Substring(1);
            matchType = value.EndsWith(filterValue);
          }
        }
        else matchType = true;

        if (matchType)
        {
          bool matchLocation = false;
          if (LocationFilter != null)
          {
            double distance = GpsLocation.DistanceBetween(LocationFilter, activityInfo.Location);
            matchLocation = distance - activityInfo.PrecisionRadius <= (double)Radius;
          }
          else matchLocation = true;

          if (matchLocation)
          {
            bool matchStartNotAfter = (StartNotAfterFilter == null) || (activityInfo.StartTime <= StartNotAfterFilter.Value);
            bool matchExpirationNotBeforeAfter = (ExpirationNotBeforeFilter == null) || (activityInfo.ExpirationTime >= ExpirationNotBeforeFilter.Value);

            res = matchStartNotAfter && matchExpirationNotBeforeAfter;
          }
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Converts activity information to ActivityQueryInformation structure.
    /// </summary>
    /// <param name="IsPrimary">Value for ActivityQueryInformation.IsPrimary field.</param>
    /// <param name="PrimaryServer">Value for ActivityQueryInformation.PrimaryServer field.</param>
    /// <param name="Propagated">If true, activity information propagated to neighborhood is used, otherwise primary activity information is used.</param>
    /// <returns>ActivityQueryInformation representing the activity.</returns>
    public ActivityQueryInformation GetActivityQueryInformation(bool IsPrimary, ServerContactInfo PrimaryServer, bool Propagated)
    {
      ActivityInfo activityInfo = Propagated ? PropagatedInfo : PrimaryInfo;
      ActivityQueryInformation res = new ActivityQueryInformation()
      {
        IsPrimary = IsPrimary,
        SignedActivity = activityInfo.ToSignedActivityInformation()
      };

      if (PrimaryServer != null) res.PrimaryServer = PrimaryServer;

      return res;
    }


    /// <summary>
    /// Creates activity snapshot.
    /// </summary>
    /// <returns>Activity snapshot.</returns>
    public ActivitySnapshot CreateSnapshot()
    {
      ActivitySnapshot res = new ActivitySnapshot()
      {
        PrimaryInfo = new ActivityInfoSnapshot()
        {
          Version = this.PrimaryInfo.Version,
          Id = this.PrimaryInfo.ActivityId,

          OwnerIdentityId = this.PrimaryInfo.OwnerIdentityId.ToHex(),
          OwnerPublicKey = this.PrimaryInfo.OwnerPublicKey.ToHex(),
          OwnerProfileServerId = this.PrimaryInfo.OwnerProfileServerId.ToHex(),
          OwnerProfileServerIpAddress = this.PrimaryInfo.OwnerProfileServerIpAddress.ToString(),
          OwnerProfileServerPrimaryPort = this.PrimaryInfo.OwnerProfileServerPrimaryPort,

          Type = this.PrimaryInfo.Type,
          LocationLatitude = this.PrimaryInfo.Location.Latitude,
          LocationLongitude = this.PrimaryInfo.Location.Longitude,
          PrecisionRadius = this.PrimaryInfo.PrecisionRadius,

          StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(this.PrimaryInfo.StartTime),
          ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(this.PrimaryInfo.ExpirationTime),

          Signature = this.PrimaryInfo.Signature.ToHex(),
          ExtraData = this.PrimaryInfo.ExtraData
        },
        PropagatedInfo = new ActivityInfoSnapshot()
        {
          Version = this.PropagatedInfo.Version,
          Id = this.PropagatedInfo.ActivityId,

          OwnerIdentityId = this.PropagatedInfo.OwnerIdentityId.ToHex(),
          OwnerPublicKey = this.PropagatedInfo.OwnerPublicKey.ToHex(),
          OwnerProfileServerId = this.PropagatedInfo.OwnerProfileServerId.ToHex(),
          OwnerProfileServerIpAddress = this.PropagatedInfo.OwnerProfileServerIpAddress.ToString(),
          OwnerProfileServerPrimaryPort = this.PropagatedInfo.OwnerProfileServerPrimaryPort,

          Type = this.PropagatedInfo.Type,
          LocationLatitude = this.PropagatedInfo.Location.Latitude,
          LocationLongitude = this.PropagatedInfo.Location.Longitude,
          PrecisionRadius = this.PropagatedInfo.PrecisionRadius,

          StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(this.PropagatedInfo.StartTime),
          ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(this.PropagatedInfo.ExpirationTime),

          Signature = this.PropagatedInfo.Signature.ToHex(),
          ExtraData = this.PropagatedInfo.ExtraData
        },
        ProximityServerName = this.proximityServer.Name,
        IdentityClientName = this.ownerIdentityClient.Profile.Name,
        HostingActive = this.HostingActive        
      };

      return res;
    }


    /// <summary>
    /// Creates instance of activity client from snapshot.
    /// </summary>
    /// <param name="Snapshot">Activity snapshot.</param>
    /// <param name="ProximityServer">Primary proximity server of the activity.</param>
    /// <returns>New activity instance.</returns>
    public static Activity CreateFromSnapshot(ActivitySnapshot Snapshot, ProximityServer ProximityServer, IdentityClient OwnerIdentityClient)
    {
      Activity res = new Activity()
      {
        proximityServer = ProximityServer,
        HostingActive = Snapshot.HostingActive,
        ownerIdentityClient = OwnerIdentityClient,

        PrimaryInfo = new ActivityInfo()
        {
          Version = Snapshot.PrimaryInfo.Version,
          ActivityId = Snapshot.PrimaryInfo.Id,

          OwnerIdentityId = Snapshot.PrimaryInfo.OwnerIdentityId.FromHex(),
          OwnerPublicKey = Snapshot.PrimaryInfo.OwnerPublicKey.FromHex(),
          OwnerProfileServerId = Snapshot.PrimaryInfo.OwnerProfileServerId.FromHex(),
          OwnerProfileServerIpAddress = IPAddress.Parse(Snapshot.PrimaryInfo.OwnerProfileServerIpAddress),
          OwnerProfileServerPrimaryPort = Snapshot.PrimaryInfo.OwnerProfileServerPrimaryPort,

          Type = Snapshot.PrimaryInfo.Type,
          Location = new GpsLocation(Snapshot.PrimaryInfo.LocationLatitude, Snapshot.PrimaryInfo.LocationLongitude),
          PrecisionRadius = Snapshot.PrimaryInfo.PrecisionRadius,

          StartTime = ProtocolHelper.UnixTimestampMsToDateTime(Snapshot.PrimaryInfo.StartTime).Value,
          ExpirationTime = ProtocolHelper.UnixTimestampMsToDateTime(Snapshot.PrimaryInfo.ExpirationTime).Value,

          Signature = Snapshot.PrimaryInfo.Signature.FromHex(),
          ExtraData = Snapshot.PrimaryInfo.ExtraData
        },

        PropagatedInfo = new ActivityInfo()
        {
          Version = Snapshot.PropagatedInfo.Version,
          ActivityId = Snapshot.PropagatedInfo.Id,

          OwnerIdentityId = Snapshot.PropagatedInfo.OwnerIdentityId.FromHex(),
          OwnerPublicKey = Snapshot.PropagatedInfo.OwnerPublicKey.FromHex(),
          OwnerProfileServerId = Snapshot.PropagatedInfo.OwnerProfileServerId.FromHex(),
          OwnerProfileServerIpAddress = IPAddress.Parse(Snapshot.PropagatedInfo.OwnerProfileServerIpAddress),
          OwnerProfileServerPrimaryPort = Snapshot.PropagatedInfo.OwnerProfileServerPrimaryPort,

          Type = Snapshot.PropagatedInfo.Type,
          Location = new GpsLocation(Snapshot.PropagatedInfo.LocationLatitude, Snapshot.PropagatedInfo.LocationLongitude),
          PrecisionRadius = Snapshot.PropagatedInfo.PrecisionRadius,

          StartTime = ProtocolHelper.UnixTimestampMsToDateTime(Snapshot.PropagatedInfo.StartTime).Value,
          ExpirationTime = ProtocolHelper.UnixTimestampMsToDateTime(Snapshot.PropagatedInfo.ExpirationTime).Value,

          Signature = Snapshot.PropagatedInfo.Signature.FromHex(),
          ExtraData = Snapshot.PropagatedInfo.ExtraData
        },

      };
      res.log = new Logger("NetworkSimulator.Activity", "[" + res.GetName() + "] ");

      return res;
    }
  }
}
