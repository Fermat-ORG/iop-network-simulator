using Google.Protobuf;
using Iop.Proximityserver;
using IopCrypto;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NetworkSimulator
{
  /// <summary>
  /// Activity information.
  /// </summary>
  public class ActivityInfo
  {
    /// <summary>Profile version.</summary>
    public SemVer Version;

    /// <summary>Activity identifier.</summary>
    public uint ActivityId;

    /// <summary>Network identifier of the identity that created the activity.</summary>
    public byte[] OwnerIdentityId;

    /// <summary>Public key the identity that created the activity.</summary>
    public byte[] OwnerPublicKey;

    /// <summary>Network identifier of the profile server where the owner of the activity has its profile.</summary>
    public byte[] OwnerProfileServerId;

    /// <summary>IP address of the profile server where the owner of the activity has its profile.</summary>
    public IPAddress OwnerProfileServerIpAddress;

    /// <summary>TCP port of primary interface of the profile server where the owner of the activity has its profile.</summary>
    public ushort OwnerProfileServerPrimaryPort;

    /// <summary>Activity type.</summary>
    public string Type;

    /// <summary>Activity's GPS location.</summary>
    public GpsLocation Location;

    /// <summary>Precision of the activity's location in metres.</summary>
    public uint PrecisionRadius;

    /// <summary>Time when the activity starts.</summary>
    public DateTime StartTime;

    /// <summary>Time when the activity expires and can be deleted.</summary>
    public DateTime ExpirationTime;

    /// <summary>Cryptographic signature of the activity information when represented with a ActivityInformation structure.</summary>
    public byte[] Signature;

    /// <summary>User defined extra data that serve for satisfying search queries in proximity server network.</summary>
    public string ExtraData;



    /// <summary>
    /// Copies values from the another activity instance.
    /// </summary>
    /// <param name="Activity">Activity to copy values from.</param>
    public void CopyFrom(ActivityInfo Activity)
    {
      this.Version = Activity.Version;
      this.ActivityId = Activity.ActivityId;

      this.OwnerIdentityId = new byte[Activity.OwnerIdentityId.Length];
      Array.Copy(Activity.OwnerIdentityId, this.OwnerIdentityId, this.OwnerIdentityId.Length);

      this.OwnerPublicKey = new byte[Activity.OwnerPublicKey.Length];
      Array.Copy(Activity.OwnerPublicKey, this.OwnerPublicKey, this.OwnerPublicKey.Length);

      this.OwnerProfileServerId = new byte[Activity.OwnerProfileServerId.Length];
      Array.Copy(Activity.OwnerProfileServerId, this.OwnerProfileServerId, this.OwnerProfileServerId.Length);

      this.OwnerProfileServerIpAddress = Activity.OwnerProfileServerIpAddress;
      this.OwnerProfileServerPrimaryPort = Activity.OwnerProfileServerPrimaryPort;
      this.Type = Activity.Type;
      this.Location = new GpsLocation(Activity.Location.Latitude, Activity.Location.Longitude);
      this.PrecisionRadius = Activity.PrecisionRadius;
      this.StartTime = Activity.StartTime;
      this.ExpirationTime = Activity.ExpirationTime;

      this.Signature = new byte[Activity.Signature.Length];
      Array.Copy(Activity.Signature, this.Signature, this.Signature.Length);

      this.ExtraData = Activity.ExtraData;
    }

    /// <summary>
    /// Copies values from the activity information description to properties of this instance.
    /// </summary>
    /// <param name="SignedActivity">Signed activity information description.</param>
    public void CopyFromSignedActivityInformation(SignedActivityInformation SignedActivity)
    {
      this.Version = new SemVer(SignedActivity.Activity.Version);
      this.ActivityId = SignedActivity.Activity.Id;

      this.OwnerPublicKey = SignedActivity.Activity.OwnerPublicKey.ToByteArray();
      this.OwnerIdentityId = Crypto.Sha256(this.OwnerPublicKey);

      this.OwnerProfileServerId = SignedActivity.Activity.ProfileServerContact.NetworkId.ToByteArray();
      this.OwnerProfileServerIpAddress = new IPAddress(SignedActivity.Activity.ProfileServerContact.IpAddress.ToByteArray());
      this.OwnerProfileServerPrimaryPort = (ushort)SignedActivity.Activity.ProfileServerContact.PrimaryPort;
      this.Type = SignedActivity.Activity.Type;
      this.Location = new GpsLocation(SignedActivity.Activity.Latitude, SignedActivity.Activity.Longitude);
      this.PrecisionRadius = SignedActivity.Activity.Precision;
      this.StartTime = ProtocolHelper.UnixTimestampMsToDateTime(SignedActivity.Activity.StartTime).Value;
      this.ExpirationTime = ProtocolHelper.UnixTimestampMsToDateTime(SignedActivity.Activity.ExpirationTime).Value;
      this.Signature = SignedActivity.Signature.ToByteArray();
      this.ExtraData = SignedActivity.Activity.ExtraData;
    }

    /// <summary>
    /// Creates SignedActivityInformation structure from values of this instance.
    /// </summary>
    /// <returns>SignedActivityInformation structure.</returns>
    public SignedActivityInformation ToSignedActivityInformation()
    {
      SignedActivityInformation res = new SignedActivityInformation()
      {
        Activity = new ActivityInformation()
        {
          Version = this.Version.ToByteString(),
          Id = this.ActivityId,
          OwnerPublicKey = ProtocolHelper.ByteArrayToByteString(this.OwnerPublicKey),
          ProfileServerContact = new ServerContactInfo()
          {
            NetworkId = ProtocolHelper.ByteArrayToByteString(this.OwnerProfileServerId),
            IpAddress = ProtocolHelper.ByteArrayToByteString(this.OwnerProfileServerIpAddress.GetAddressBytes()),
            PrimaryPort = this.OwnerProfileServerPrimaryPort
          },
          Type = this.Type,
          Latitude = this.Location.GetLocationTypeLatitude(),
          Longitude = this.Location.GetLocationTypeLongitude(),
          Precision = this.PrecisionRadius,
          StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(this.StartTime),
          ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(this.ExpirationTime),
          ExtraData = this.ExtraData
        },
        Signature = ProtocolHelper.ByteArrayToByteString(this.Signature)
      };
      return res;
    }   
  }
}

