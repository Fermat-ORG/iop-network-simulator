using Google.Protobuf;
using Iop.Profileserver;
using IopCrypto;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkSimulator
{
  /// <summary>
  /// Identity profile information.
  /// </summary>
  public class ClientProfile
  {
    /// <summary>Profile version.</summary>
    public SemVer Version;

    /// <summary>Identity public key.</summary>
    public byte[] PublicKey;

    /// <summary>Profile name.</summary>
    public string Name;

    /// <summary>Profile type.</summary>
    public string Type;

    /// <summary>Profile image.</summary>
    public byte[] ProfileImage;

    /// <summary>Profile image file name or null if the identity has no profile image.</summary>
    public string ProfileImageFileName;

    /// <summary>Thumbnail image.</summary>
    public byte[] ThumbnailImage;

    /// <summary>Thumbnail image file name or null if the identity has no thumbnail image.</summary>
    public string ThumbnailImageFileName;

    /// <summary>Initial profile location.</summary>
    public GpsLocation Location;

    /// <summary>Extra data information.</summary>
    public string ExtraData;

    /// <summary>SHA256 hash of ProfileImage data.</summary>
    public byte[] ProfileImageHash;

    /// <summary>SHA256 hash of ThumbnailImage data.</summary>
    public byte[] ThumbnailImageHash;


    /// <summary>
    /// Copies values from the another profile instance.
    /// </summary>
    /// <param name="Profile">Profile to copy values from.</param>
    public void CopyFrom(ClientProfile Profile)
    {
      this.Version = Profile.Version;
      this.PublicKey = Profile.PublicKey;
      this.Name = Profile.Name;
      this.Type = Profile.Type;

      if (Profile.ProfileImage != null)
      {
        this.ProfileImage = new byte[Profile.ProfileImage.Length];
        Array.Copy(Profile.ProfileImage, this.ProfileImage, this.ProfileImage.Length);
      }

      this.ProfileImageFileName = Profile.ProfileImageFileName;

      if (Profile.ThumbnailImage != null)
      {
        this.ThumbnailImage = new byte[Profile.ThumbnailImage.Length];
        Array.Copy(Profile.ThumbnailImage, this.ThumbnailImage, this.ThumbnailImage.Length);
      }

      this.ThumbnailImageFileName = Profile.ThumbnailImageFileName;
      this.Location = new GpsLocation(Profile.Location.Latitude, Profile.Location.Longitude);
      this.ExtraData = Profile.ExtraData;

      if (Profile.ProfileImageHash != null)
      {
        this.ProfileImageHash = new byte[Profile.ProfileImageHash.Length];
        Array.Copy(Profile.ProfileImageHash, this.ProfileImageHash, this.ProfileImageHash.Length);
      }

      if (Profile.ThumbnailImageHash != null)
      {
        this.ThumbnailImageHash = new byte[Profile.ThumbnailImageHash.Length];
        Array.Copy(Profile.ThumbnailImageHash, this.ThumbnailImageHash, this.ThumbnailImageHash.Length);
      }
    }

    /// <summary>
    /// Copies values from the profile information description to properties of this instance.
    /// </summary>
    /// <param name="Profile">Profile information description.</param>
    /// <param name="ProfileImage">Profile image data.</param>
    /// <param name="ThumbnailImage">Thumbnail image data.</param>
    public void CopyFromProfileInformation(ProfileInformation Profile, byte[] ProfileImage = null, byte[] ThumbnailImage = null)
    {
      this.Version = new SemVer(Profile.Version);
      this.PublicKey = Profile.PublicKey.ToByteArray();
      this.Name = Profile.Name;
      this.Type = Profile.Type;

      this.ProfileImage = new byte[ProfileImage.Length];
      Array.Copy(ProfileImage, this.ProfileImage, this.ProfileImage.Length);

      this.ThumbnailImage = new byte[ThumbnailImage.Length];
      Array.Copy(ThumbnailImage, this.ThumbnailImage, this.ThumbnailImage.Length);

      this.Location = new GpsLocation(Profile.Latitude, Profile.Longitude);
      this.ExtraData = Profile.ExtraData;
      this.ProfileImageHash = Profile.ProfileImageHash.ToByteArray();
      this.ThumbnailImageHash = Profile.ThumbnailImageHash.ToByteArray();
    }

    /// <summary>
    /// Creates ProfileInformation structure from values of this instance.
    /// </summary>
    /// <returns>ProfileInformation structure.</returns>
    public ProfileInformation ToProfileInformation()
    {
      ProfileInformation res = new ProfileInformation()
      {
        Version = this.Version.ToByteString(),
        Type = this.Type != null ? this.Type : "",
        Name = this.Name != null ? this.Name : "",
        Latitude = this.Location.GetLocationTypeLatitude(),
        Longitude = this.Location.GetLocationTypeLongitude(),
        ExtraData = this.ExtraData != null ? this.ExtraData : "",
        PublicKey = ProtocolHelper.ByteArrayToByteString(this.PublicKey),
        ProfileImageHash = ProtocolHelper.ByteArrayToByteString(this.ProfileImageHash != null ? this.ProfileImageHash : new byte[0]),
        ThumbnailImageHash = ProtocolHelper.ByteArrayToByteString(this.ThumbnailImageHash != null ? this.ThumbnailImageHash : new byte[0])
      };
      return res;
    }

    /// <summary>
    /// Creates SignedProfileInformation structure from values of this instance.
    /// </summary>
    /// <param name="PrivateKey">Private key to use to create signature.</param>
    /// <returns>SignedProfileInformation structure.</returns>
    public SignedProfileInformation ToSignedProfileInformation(byte[] PrivateKey)
    {
      ProfileInformation profileInformation = this.ToProfileInformation();
      byte[] signature = Ed25519.Sign(profileInformation.ToByteArray(), PrivateKey);
      SignedProfileInformation res = new SignedProfileInformation()
      {
        Profile = profileInformation,
        Signature = ProtocolHelper.ByteArrayToByteString(signature)
      };
      return res;
    }



    /// <summary>
    /// Changes profile image and calculates its hash.
    /// </summary>
    /// <param name="Data">Raw image data.</param>
    public void SetProfileImage(byte[] Data)
    {
      ProfileImage = Data;
      ProfileImageHash = Data != null ? Crypto.Sha256(Data) : null;
    }


    /// <summary>
    /// Changes thumbnail image and calculates its hash.
    /// </summary>
    /// <param name="Data">Raw image data.</param>
    public void SetThumbnailImage(byte[] Data)
    {
      ThumbnailImage = Data;
      ThumbnailImageHash = Data != null ? Crypto.Sha256(Data) : null;
    }


    /// <summary>
    /// Changes profile and thumbnail images and hashes.
    /// </summary>
    /// <param name="ProfileImage"></param>
    /// <param name="ThumbnailImage"></param>
    public void SetImages(byte[] ProfileImage, byte[] ThumbnailImage)
    {
      SetProfileImage(ProfileImage);
      SetThumbnailImage(ThumbnailImage);
    }

  }
}

