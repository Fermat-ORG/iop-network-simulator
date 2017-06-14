using IopCrypto;
using IopProtocol;
using Iop.Profileserver;
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
using Iop.Proximityserver;

namespace NetworkSimulator
{
  /// <summary>
  /// Represents a single identity in the network.
  /// </summary>
  public class IdentityClient
  {
    /// <summary>Instance logger.</summary>
    private Logger log;

    /// <summary>Information about client's profile.</summary>
    public ClientProfile Profile;

    /// <summary>Information about client's profile propagated to the neighborhood.</summary>
    public ClientProfile PropagatedProfile;

    /// <summary>Profile server hosting the identity profile.</summary>
    private ProfileServer profileServer;
    /// <summary>Profile server hosting the identity profile.</summary>
    public ProfileServer ProfileServer { get { return profileServer; } }

    /// <summary>TCP client for communication with the server.</summary>
    private TcpClient client;

    /// <summary>
    /// Normal or TLS stream for sending and receiving data over TCP client. 
    /// In case of the TLS stream, the underlaying stream is going to be closed automatically.
    /// </summary>
    private Stream stream;

    /// <summary>Message builder for easy creation of profile server protocol message.</summary>
    private PsMessageBuilder psMessageBuilder;

    /// <summary>Message builder for easy creation of proximity server protocol message.</summary>
    private ProxMessageBuilder proxMessageBuilder;

    /// <summary>Cryptographic Keys that represent the client's identity.</summary>
    private KeysEd25519 keys;

    /// <summary>Network identifier of the client's identity.</summary>
    private byte[] identityId;

    /// <summary>Profile server's public key received when starting conversation.</summary>
    private byte[] profileServerKey;

    /// <summary>Challenge that the profile server sent to the client when starting conversation.</summary>
    private byte[] challenge;

    /// <summary>Challenge that the client sent to the profile server when starting conversation.</summary>
    private byte[] clientChallenge;

    /// <summary>true if the client initialized its profile on the profile server, false otherwise.</summary>
    private bool profileInitialized;

    /// <summary>true if the client has an active hosting agreement with the profile server, false otherwise.</summary>
    private bool hostingActive;

    /// <summary>
    /// Empty constructor for manual construction of the instance when loading simulation for snapshot.
    /// </summary>
    public IdentityClient()
    {
    }


    /// <summary>
    /// Creates a new identity client.
    /// </summary>
    /// <param name="Name">Identity name.</param>
    /// <param name="Type">Identity type.</param>
    /// <param name="Location">Initial GPS location.</param>
    /// <param name="ProfileImageMask">File name mask in the images folder that define which images can be randomly selected for profile image.</param>
    /// <param name="ProfileImageChance">An integer between 0 and 100 that specifies the chance of each instance to have a profile image set.</param>
    /// <param name="ThumbnailImageMask">File name mask in the images folder that define which images can be randomly selected for thumbnail image.</param>
    /// <param name="ThumbnailImageChance">An integer between 0 and 100 that specifies the chance of each instance to have a profile image set.</param>
    public IdentityClient(string Name, string Type, GpsLocation Location, string ProfileImageMask, int ProfileImageChance, string ThumbnailImageMask, int ThumbnailImageChance)
    {
      log = new Logger("NetworkSimulator.IdentityClient", "[" + Name + "] ");
      log.Trace("(Name:'{0}',Type:'{1}',Location:{2},ProfileImageMask:'{3}',ProfileImageChance:{4},ThumbnailImageMask:'{5}',ProfileImageChance:{6})", Name, Type, Location, ProfileImageMask, ProfileImageChance, ThumbnailImageMask, ThumbnailImageChance);

      keys = Ed25519.GenerateKeys();
      Profile = new ClientProfile();
      Profile.Version = SemVer.V100;
      Profile.PublicKey = keys.PublicKey;
      Profile.Name = Name;
      Profile.Type = Type;
      Profile.Location = Location;
      Profile.ExtraData = null;

      bool hasProfileImage = Helpers.Rng.NextDouble() < (double)ProfileImageChance / 100;
      if (hasProfileImage)
      {
        Profile.ProfileImageFileName = GetImageFileByMask(ProfileImageMask);
        Profile.SetProfileImage(Profile.ProfileImageFileName != null ? File.ReadAllBytes(Profile.ProfileImageFileName) : null);
      }

      bool hasThumbnailImage = Helpers.Rng.NextDouble() < (double)ThumbnailImageChance / 100;
      if (hasThumbnailImage)
      {
        Profile.ThumbnailImageFileName = GetImageFileByMask(ThumbnailImageMask);
        Profile.SetThumbnailImage(Profile.ThumbnailImageFileName != null ? File.ReadAllBytes(Profile.ThumbnailImageFileName) : null);
      }

      PropagatedProfile = new ClientProfile();
      PropagatedProfile.CopyFrom(Profile);

      identityId = Crypto.Sha256(keys.PublicKey);
      psMessageBuilder = new PsMessageBuilder(0, new List<SemVer>() { SemVer.V100 }, keys);
      proxMessageBuilder = new ProxMessageBuilder(0, new List<SemVer>() { SemVer.V100 }, keys);

      profileInitialized = false;
      hostingActive = false;

      log.Trace("(-)");
    }


    /// <summary>
    /// Frees resources used by identity client.
    /// </summary>
    public void Shutdown()
    {
      CloseTcpClient();
    }


    /// <summary>
    /// Obtains a file name of a profile image from a group of image file names.
    /// </summary>
    /// <param name="Mask">File name mask in images folder.</param>
    /// <returns>Profile image file name.</returns>
    public string GetImageFileByMask(string Mask)
    {
      log.Trace("(Mask:'{0}')", Mask);

      string res = null;
      string path = CommandProcessor.ImagesDirectory;
      string[] files = Directory.GetFiles(path, Mask, SearchOption.TopDirectoryOnly);
      if (files.Length > 0)
      {
        int fileIndex = Helpers.Rng.Next(files.Length);
        res = files[fileIndex];
      }

      log.Trace("(-):{0}", res != null ? "Image" : "null");
      return res;
    }


    /// <summary>
    /// Establishes a hosting agreement with a profile server and initializes a profile.
    /// </summary>
    /// <param name="Server">Profile server to host the identity.</param>
    /// <returns>true if the function succeeded, false otherwise.</returns>
    public async Task<bool> InitializeProfileHostingAsync(ProfileServer Server)
    {
      log.Trace("(Server.Name:'{0}')", Server.Name);
      bool res = false;

      profileServer = Server;
      InitializeTcpClient();

      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientNonCustomerInterfacePort, true);

        if (await EstablishProfileHostingAsync(Profile.Type))
        {
          hostingActive = true;
          CloseTcpClient();

          InitializeTcpClient();
          await ConnectAsync(Server.IpAddress, Server.ClientCustomerInterfacePort, true);
          if (await PsCheckInAsync())
          {
            if (await PsInitializeProfileAsync(Profile.Name, Profile.ProfileImage, Profile.ThumbnailImage, Profile.Location, Profile.ExtraData))
            {
              profileInitialized = true;
              res = true;
            }
            else log.Error("Unable to initialize profile on profile server '{0}'.", Server.Name);
          }
          else log.Error("Unable to check-in to profile server '{0}'.", Server.Name);
        }
        else log.Error("Unable to establish profile hosting with server '{0}'.", Server.Name);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      CloseTcpClient();

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Cancels a hosting agreement with hosting profile server.
    /// </summary>
    /// <returns>true if the function succeeded, false otherwise.</returns>
    public async Task<bool> CancelProfileHostingAsync()
    {
      log.Trace("()");
      bool res = false;

      InitializeTcpClient();

      try
      {
        await ConnectAsync(profileServer.IpAddress, profileServer.ClientCustomerInterfacePort, true);
        if (await PsCheckInAsync())
        {
          if (await PsCancelHostingAgreementAsync())
          {
            hostingActive = false;
            res = true;
          }
          else log.Error("Unable to cancel hosting agreement on profile server '{0}'.", profileServer.Name);
        }
        else log.Error("Unable to check-in to profile server '{0}'.", profileServer.Name);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      CloseTcpClient();

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Initializes TCP client to be ready to connect to the server.
    /// </summary>
    public void InitializeTcpClient()
    {
      log.Trace("()");

      CloseTcpClient();

      client = new TcpClient();
      client.NoDelay = true;
      client.LingerState = new LingerOption(true, 0);
      psMessageBuilder.ResetId();
      proxMessageBuilder.ResetId();

      log.Trace("(-)");
    }

    /// <summary>
    /// Closes an open connection and reinitialize the TCP client so that it can be used again.
    /// </summary>
    public void CloseTcpClient()
    {
      log.Trace("()");

      if (stream != null) stream.Dispose();
      if (client != null) client.Dispose();

      log.Trace("(-)");
    }

    /// <summary>
    /// Establishes a hosting agreement for the client's identity with specific identity type using the already opened connection to the profile server.
    /// </summary>
    /// <param name="IdentityType">Identity type of the new identity.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> EstablishProfileHostingAsync(string IdentityType)
    {
      log.Trace("()");

      bool startConversationOk = await PsStartConversationAsync();

      HostingPlanContract contract = new HostingPlanContract()
      {
        PlanId = ProtocolHelper.ByteArrayToByteString(new byte[0]),
        IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(Profile.PublicKey),
        StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(DateTime.Now),
        IdentityType = IdentityType
      };

      PsProtocolMessage requestMessage = psMessageBuilder.CreateRegisterHostingRequest(contract);
      await PsSendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await PsReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;
      byte[] receivedContract = responseMessage.Response.ConversationResponse.RegisterHosting.Contract.ToByteArray();
      bool contractOk = ByteArrayComparer.Equals(contract.ToByteArray(), receivedContract);
      byte[] signature = responseMessage.Response.ConversationResponse.Signature.ToByteArray();
      bool signatureOk = VerifyServerSignature(receivedContract, signature);

      bool registerHostingOk = idOk && statusOk && contractOk && signatureOk;

      bool res = startConversationOk && registerHostingOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Verifies server's signature of binary data.
    /// </summary>
    /// <param name="Data">Binary data that server signed.</param>
    /// <param name="Signature">Signature of <paramref name="Data"/> to verify.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool VerifyServerSignature(byte[] Data, byte[] Signature)
    {
      return VerifySignature(Data, Signature, profileServerKey);
    }


    /// <summary>
    /// Verifies server's signature of binary data.
    /// </summary>
    /// <param name="Data">Binary data that server signed.</param>
    /// <param name="Signature">Signature of <paramref name="Data"/> to verify.</param>
    /// <param name="PublicKey">Public key of the entity that signed the data.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool VerifySignature(byte[] Data, byte[] Signature, byte[] PublicKey)
    {
      log.Trace("()");

      bool res = Ed25519.Verify(Signature, Data, PublicKey);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Generates client's challenge and creates start conversation request with it.
    /// </summary>
    /// <returns>StartConversationRequest message that is ready to be sent to the profile server.</returns>
    public PsProtocolMessage PsCreateStartConversationRequest()
    {
      clientChallenge = new byte[PsMessageBuilder.ChallengeDataSize];
      Crypto.Rng.GetBytes(clientChallenge);
      PsProtocolMessage res = psMessageBuilder.CreateStartConversationRequest(clientChallenge);
      return res;
    }

    /// <summary>
    /// Starts conversation with the profile server the client is connected to and checks whether the server response contains expected values.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> PsStartConversationAsync()
    {
      log.Trace("()");

      PsProtocolMessage requestMessage = PsCreateStartConversationRequest();
      await PsSendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await PsReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;
      bool challengeVerifyOk = PsVerifyProfileServerChallengeSignature(responseMessage);

      SemVer receivedVersion = new SemVer(responseMessage.Response.ConversationResponse.Start.Version);
      bool versionOk = receivedVersion.Equals(new SemVer(psMessageBuilder.Version));

      bool pubKeyLenOk = responseMessage.Response.ConversationResponse.Start.PublicKey.Length == 32;
      bool challengeOk = responseMessage.Response.ConversationResponse.Start.Challenge.Length == 32;

      profileServerKey = responseMessage.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      challenge = responseMessage.Response.ConversationResponse.Start.Challenge.ToByteArray();

      bool res = idOk && statusOk && challengeVerifyOk && versionOk && pubKeyLenOk && challengeOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends profile server message over the network stream.
    /// </summary>
    /// <param name="Data">Message to send.</param>
    public async Task PsSendMessageAsync(PsProtocolMessage Data)
    {
      string dataStr = Data.ToString();
      log.Trace("()\n{0}", dataStr.Substring(0, Math.Min(dataStr.Length, 512)));

      byte[] rawData = PsMessageBuilder.MessageToByteArray(Data);
      await stream.WriteAsync(rawData, 0, rawData.Length);

      log.Trace("(-)");
    }


    /// <summary>
    /// Reads and parses profile server protocol message from the network stream.
    /// </summary>
    /// <returns>Parsed protocol message or null if the function fails.</returns>
    public async Task<PsProtocolMessage> PsReceiveMessageAsync()
    {
      log.Trace("()");

      PsProtocolMessage res = null;

      byte[] header = new byte[ProtocolHelper.HeaderSize];
      int headerBytesRead = 0;
      int remain = header.Length;

      bool done = false;
      log.Trace("Reading message header.");
      while (!done && (headerBytesRead < header.Length))
      {
        int readAmount = await stream.ReadAsync(header, headerBytesRead, remain);
        if (readAmount == 0)
        {
          log.Trace("Connection to server closed while reading the header.");
          done = true;
          break;
        }

        headerBytesRead += readAmount;
        remain -= readAmount;
      }

      uint messageSize = BitConverter.ToUInt32(header, 1);
      log.Trace("Message body size is {0} bytes.", messageSize);

      byte[] messageBytes = new byte[ProtocolHelper.HeaderSize + messageSize];
      Array.Copy(header, messageBytes, header.Length);

      remain = (int)messageSize;
      int messageBytesRead = 0;
      while (!done && (messageBytesRead < messageSize))
      {
        int readAmount = await stream.ReadAsync(messageBytes, ProtocolHelper.HeaderSize + messageBytesRead, remain);
        if (readAmount == 0)
        {
          log.Trace("Connection to server closed while reading the body.");
          done = true;
          break;
        }

        messageBytesRead += readAmount;
        remain -= readAmount;
      }

      res = new PsProtocolMessage(Iop.Profileserver.MessageWithHeader.Parser.ParseFrom(messageBytes).Body);

      string resStr = res.ToString();
      log.Trace("(-):\n{0}", resStr.Substring(0, Math.Min(resStr.Length, 512)));
      return res;
    }


    /// <summary>
    /// Verifies whether the profile server successfully signed the correct start conversation challenge.
    /// </summary>
    /// <param name="StartConversationResponse">StartConversationResponse received from the profile server.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool PsVerifyProfileServerChallengeSignature(PsProtocolMessage StartConversationResponse)
    {
      log.Trace("()");

      byte[] receivedChallenge = StartConversationResponse.Response.ConversationResponse.Start.ClientChallenge.ToByteArray();
      byte[] profileServerPublicKey = StartConversationResponse.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      bool res = ByteArrayComparer.Equals(receivedChallenge, clientChallenge)
        && psMessageBuilder.VerifySignedConversationResponseBodyPart(StartConversationResponse, receivedChallenge, profileServerPublicKey);

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Initializes a new identity profile on the profile server.
    /// </summary>
    /// <param name="Name">Name of the profile.</param>
    /// <param name="ProfileImage">Optionally, profile image data.</param>
    /// <param name="ThumbnailImage">Optionally, thumbnail image data.</param>
    /// <param name="Location">GPS location of the identity.</param>
    /// <param name="ExtraData">Optionally, identity's extra data.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> PsInitializeProfileAsync(string Name, byte[] ProfileImage, byte[] ThumbnailImage, GpsLocation Location, string ExtraData)
    {
      log.Trace("()");

      this.Profile.Name = Name;
      this.Profile.SetImages(ProfileImage, ThumbnailImage);
      this.Profile.Location = Location;
      this.Profile.ExtraData = ExtraData;
      this.PropagatedProfile.CopyFrom(Profile);
      ProfileInformation profile = this.Profile.ToProfileInformation();
      PsProtocolMessage requestMessage = psMessageBuilder.CreateUpdateProfileRequest(profile, this.Profile.ProfileImage, this.Profile.ThumbnailImage);
      await PsSendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await PsReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool res = idOk && statusOk;

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Performs a check-in process for the client's identity using the already opened connection to the profile server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> PsCheckInAsync()
    {
      log.Trace("()");

      bool startConversationOk = await PsStartConversationAsync();

      PsProtocolMessage requestMessage = psMessageBuilder.CreateCheckInRequest(challenge);
      await PsSendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await PsReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool checkInOk = idOk && statusOk;

      bool res = startConversationOk && checkInOk;

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Connects to the target server address on the specific port and optionally performs TLS handshake.
    /// </summary>
    /// <param name="Address">IP address of the target server.</param>
    /// <param name="Port">TCP port to connect to.</param>
    /// <param name="UseTls">If true, the TLS handshake is performed after the connection is established.</param>
    public async Task ConnectAsync(IPAddress Address, int Port, bool UseTls)
    {
      log.Trace("(Address:'{0}',Port:{1},UseTls:{2})", Address, Port, UseTls);

      await client.ConnectAsync(Address, Port);

      stream = client.GetStream();
      if (UseTls)
      {
        SslStream sslStream = new SslStream(stream, false, PeerCertificateValidationCallback);
        await sslStream.AuthenticateAsClientAsync("", null, SslProtocols.Tls12, false);
        stream = sslStream;
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Cancels a agreement with the profile server, to which there already is an opened connection.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> PsCancelHostingAgreementAsync()
    {
      log.Trace("()");

      PsProtocolMessage requestMessage = psMessageBuilder.CreateCancelHostingAgreementRequest(null);
      await PsSendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await PsReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool res = idOk && statusOk;

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Profile search query results together with list of covered servers.
    /// </summary>
    public class ProfileSearchQueryInfo
    {
      /// <summary>Search results - list of found profiles.</summary>
      public List<ProfileQueryInformation> Results;

      /// <summary>List of covered servers returned by the queried profile server.</summary>
      public List<byte[]> CoveredServers;
    }


    /// <summary>
    /// Connects to a profile server and performs a search query on it and downloads all possible results.
    /// </summary>
    /// <param name="Server">Profile server to query.</param>
    /// <param name="NameFilter">Name filter of the search query, or null if name filtering is not required.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="IncludeHostedOnly">If set to true, the search results should only include profiles hosted on the queried profile server.</param>
    /// <param name="IncludeImages">If set to true, the search results should include images.</param>
    /// <returns>List of results or null if the function fails.</returns>
    public async Task<ProfileSearchQueryInfo> ProfileSearchQueryAsync(ProfileServer Server, string NameFilter, string TypeFilter, GpsLocation LocationFilter, int Radius, bool IncludeHostedOnly, bool IncludeImages)
    {
      log.Trace("()");

      ProfileSearchQueryInfo res = null;
      bool connected = false;
      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientNonCustomerInterfacePort, true);
        connected = true;
        if (await PsStartConversationAsync())
        {
          uint maxResults = (uint)(IncludeImages ? 1000 : 10000);
          uint maxResponseResults = (uint)(IncludeImages ? 100 : 1000);
          PsProtocolMessage requestMessage = psMessageBuilder.CreateProfileSearchRequest(TypeFilter, NameFilter, null, LocationFilter, (uint)Radius, maxResponseResults, maxResults, IncludeHostedOnly, IncludeImages);
          await PsSendMessageAsync(requestMessage);
          PsProtocolMessage responseMessage = await PsReceiveMessageAsync();

          bool idOk = responseMessage.Id == requestMessage.Id;
          bool statusOk = responseMessage.Response.Status == Status.Ok;

          bool searchRequestOk = idOk && statusOk;
          if (searchRequestOk)
          {
            int totalResultCount = (int)responseMessage.Response.SingleResponse.ProfileSearch.TotalRecordCount;
            List<byte[]> coveredServers = new List<byte[]>();
            foreach (ByteString coveredServerId in responseMessage.Response.SingleResponse.ProfileSearch.CoveredServers)
              coveredServers.Add(coveredServerId.ToByteArray());

            List<ProfileQueryInformation> results = responseMessage.Response.SingleResponse.ProfileSearch.Profiles.ToList();
            while (results.Count < totalResultCount)
            {
              int remaining = Math.Min((int)maxResponseResults, totalResultCount - results.Count);
              requestMessage = psMessageBuilder.CreateProfileSearchPartRequest((uint)results.Count, (uint)remaining);
              await PsSendMessageAsync(requestMessage);
              responseMessage = await PsReceiveMessageAsync();

              idOk = responseMessage.Id == requestMessage.Id;
              statusOk = responseMessage.Response.Status == Status.Ok;

              searchRequestOk = idOk && statusOk;
              if (!searchRequestOk) break;

              results.AddRange(responseMessage.Response.SingleResponse.ProfileSearchPart.Profiles.ToList());
            }

            res = new ProfileSearchQueryInfo();
            res.CoveredServers = coveredServers;
            res.Results = results;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (connected) CloseTcpClient();

      if (res != null) log.Trace("(-):*.Results.Count={0},*.CoveredServers.Count={1}", res.Results.Count, res.CoveredServers.Count);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Checks whether the client's identity matches specific search query.
    /// </summary>
    /// <param name="NameFilter">Name filter of the search query, or null if name filtering is not required.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius in metres of the target area.</param>
    /// <param name="Propagated">If true, client's profile that is propagated to neighborhood is being used, otherwise client's main profile is being used.</param>
    /// <returns>true if the identity matches the query, false otherwise.</returns>
    public bool MatchesSearchQuery(string NameFilter, string TypeFilter, GpsLocation LocationFilter, int Radius, bool Propagated)
    {
      log.Trace("(NameFilter:'{0}',TypeFilter:'{1}',LocationFilter:'{2}',Radius:{3},Propagated:{4})", NameFilter, TypeFilter, LocationFilter, Radius, Propagated);

      ClientProfile clientProfile = Propagated ? PropagatedProfile : Profile;

      bool res = false;
      // Do not include if the profile is uninitialized or hosting cancelled.
      if (profileInitialized && hostingActive)
      {
        bool matchType = false;
        bool useTypeFilter = !string.IsNullOrEmpty(TypeFilter) && (TypeFilter != "*") && (TypeFilter != "**");
        if (useTypeFilter)
        {
          string value = clientProfile.Type.ToLowerInvariant();
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

        bool matchName = false;
        bool useNameFilter = !string.IsNullOrEmpty(NameFilter) && (NameFilter != "*") && (NameFilter != "**");
        if (useNameFilter)
        {
          string value = clientProfile.Name.ToLowerInvariant();
          string filterValue = NameFilter.ToLowerInvariant();
          matchName = value == filterValue;

          bool valueStartsWith = NameFilter.EndsWith("*");
          bool valueEndsWith = NameFilter.StartsWith("*");
          bool valueContains = valueStartsWith && valueEndsWith;

          if (valueContains)
          {
            filterValue = filterValue.Substring(1, filterValue.Length - 2);
            matchName = value.Contains(filterValue);
          }
          else if (valueStartsWith)
          {
            filterValue = filterValue.Substring(0, filterValue.Length - 1);
            matchName = value.StartsWith(filterValue);
          }
          else if (valueEndsWith)
          {
            filterValue = filterValue.Substring(1);
            matchName = value.EndsWith(filterValue);
          }
        }
        else matchName = true;

        if (matchType && matchName)
        {
          bool matchLocation = false;
          if (LocationFilter != null)
          {
            double distance = GpsLocation.DistanceBetween(LocationFilter, clientProfile.Location);
            matchLocation = distance <= (double)Radius;
          }
          else matchLocation = true;

          res = matchLocation;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Converts client's profile to ProfileQueryInformation structure.
    /// </summary>
    /// <param name="IsHosted">Value for ProfileQueryInformation.IsHosted field.</param>
    /// <param name="IsOnline">Value for ProfileQueryInformation.IsOnline field.</param>
    /// <param name="HostingProfileServerId">Value for ProfileQueryInformation.HostingServerNetworkId field.</param>
    /// <param name="IncludeImages">If set to true, images are included in the query information.</param>
    /// <param name="Propagated">If true, client's profile that is propagated to neighborhood is being used, otherwise client's main profile is being used.</param>
    /// <returns>ProfileQueryInformation representing the client's profile.</returns>
    public ProfileQueryInformation GetProfileQueryInformation(bool IsHosted, bool IsOnline, byte[] HostingProfileServerId, bool IncludeImages, bool Propagated)
    {
      ClientProfile clientProfile = Propagated ? PropagatedProfile : Profile;
      ProfileQueryInformation res = new ProfileQueryInformation()
      {
        IsHosted = IsHosted,
        IsOnline = IsOnline,
        SignedProfile = clientProfile.ToSignedProfileInformation(keys.ExpandedPrivateKey),
        ThumbnailImage = ProtocolHelper.ByteArrayToByteString(IncludeImages && (clientProfile.ThumbnailImage != null) ? clientProfile.ThumbnailImage : new byte[0]),
        HostingServerNetworkId = ProtocolHelper.ByteArrayToByteString(HostingProfileServerId != null ? HostingProfileServerId : new byte[0])
      };

      return res;
    }



    /// <summary>
    /// Callback routine that validates server TLS certificate.
    /// As we do not perform certificate validation, we just return true.
    /// </summary>
    /// <param name="sender"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="certificate"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="chain"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="sslPolicyErrors"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <returns><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</returns>
    public static bool PeerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
      return true;
    }


    /// <summary>
    /// Activity search query results together with list of covered servers.
    /// </summary>
    public class ActivitySearchQueryInfo
    {
      /// <summary>Search results - list of found activities.</summary>
      public List<ActivityQueryInformation> Results;

      /// <summary>List of covered servers returned by the queried proximity server.</summary>
      public List<byte[]> CoveredServers;
    }


    /// <summary>
    /// Connects to a proximity server and performs a search query on it and downloads all possible results.
    /// </summary>
    /// <param name="Server">Proximity server to query.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="StartNotAfter">Filter on activity start time, or null if start time filtering is not required.</param>
    /// <param name="ExpirationNotBefore">Filter on activity expiration time, or null if expiration time filtering is not required.</param>
    /// <param name="IncludePrimaryOnly">If set to true, the search results should only include primary activities of the queried proximity server.</param>
    /// <returns>List of results or null if the function fails.</returns>
    public async Task<ActivitySearchQueryInfo> ActivitySearchQueryAsync(ProximityServer Server, string TypeFilter, GpsLocation LocationFilter, int Radius, DateTime? StartNotAfter, DateTime? ExpirationNotBefore, bool IncludePrimaryOnly)
    {
      log.Trace("()");

      ActivitySearchQueryInfo res = null;
      bool connected = false;
      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientInterfacePort, true);
        connected = true;

        byte[] sessionChallenge = new byte[ProxMessageBuilder.ChallengeDataSize];
        Crypto.Rng.GetBytes(sessionChallenge);

        if (await ProxStartConversationAsync(sessionChallenge))
        {
          uint maxResults = 10000;
          uint maxResponseResults = 1000;
          ProxProtocolMessage requestMessage = proxMessageBuilder.CreateActivitySearchRequest(TypeFilter, null, StartNotAfter, ExpirationNotBefore, null, LocationFilter, (uint)Radius, maxResponseResults, maxResults, IncludePrimaryOnly);
          await ProxSendMessageAsync(requestMessage);
          ProxProtocolMessage responseMessage = await ProxReceiveMessageAsync();

          bool idOk = responseMessage.Id == requestMessage.Id;
          bool statusOk = responseMessage.Response.Status == Status.Ok;

          bool searchRequestOk = idOk && statusOk;
          if (searchRequestOk)
          {
            int totalResultCount = (int)responseMessage.Response.SingleResponse.ActivitySearch.TotalRecordCount;
            List<byte[]> coveredServers = new List<byte[]>();
            foreach (ByteString coveredServerId in responseMessage.Response.SingleResponse.ActivitySearch.CoveredServers)
              coveredServers.Add(coveredServerId.ToByteArray());

            List<ActivityQueryInformation> results = responseMessage.Response.SingleResponse.ActivitySearch.Activities.ToList();
            while (results.Count < totalResultCount)
            {
              int remaining = Math.Min((int)maxResponseResults, totalResultCount - results.Count);
              requestMessage = proxMessageBuilder.CreateActivitySearchPartRequest((uint)results.Count, (uint)remaining);
              await ProxSendMessageAsync(requestMessage);
              responseMessage = await ProxReceiveMessageAsync();

              idOk = responseMessage.Id == requestMessage.Id;
              statusOk = responseMessage.Response.Status == Status.Ok;

              searchRequestOk = idOk && statusOk;
              if (!searchRequestOk) break;

              results.AddRange(responseMessage.Response.SingleResponse.ActivitySearchPart.Activities.ToList());
            }

            res = new ActivitySearchQueryInfo();
            res.CoveredServers = coveredServers;
            res.Results = results;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (connected) CloseTcpClient();

      if (res != null) log.Trace("(-):*.Results.Count={0},*.CoveredServers.Count={1}", res.Results.Count, res.CoveredServers.Count);
      else log.Trace("(-):null");
      return res;
    }






    /// <summary>
    /// Creates identity client snapshot.
    /// </summary>
    /// <returns>Identity client snapshot.</returns>
    public IdentitySnapshot CreateSnapshot()
    {
      IdentitySnapshot res = new IdentitySnapshot()
      {
        Profile = new IdentityProfileSnapshot()
        {
          Type = this.Profile.Type,
          Version = this.Profile.Version,
          ProfileImageHash = this.Profile.ProfileImageHash.ToHex(),
          ThumbnailImageHash = this.Profile.ThumbnailImageHash.ToHex(),
          ProfileImageFileName = Path.GetFileName(this.Profile.ProfileImageFileName),
          ThumbnailImageFileName = Path.GetFileName(this.Profile.ThumbnailImageFileName),
          LocationLatitude = this.Profile.Location.Latitude,
          LocationLongitude = this.Profile.Location.Longitude,
          Name = this.Profile.Name,
          ExtraData = this.Profile.ExtraData
        },
        PropagatedProfile = new IdentityProfileSnapshot()
        {
          Type = this.PropagatedProfile.Type,
          Version = this.PropagatedProfile.Version,
          ProfileImageHash = this.PropagatedProfile.ProfileImageHash.ToHex(),
          ThumbnailImageHash = this.PropagatedProfile.ThumbnailImageHash.ToHex(),
          ProfileImageFileName = Path.GetFileName(this.PropagatedProfile.ProfileImageFileName),
          ThumbnailImageFileName = Path.GetFileName(this.PropagatedProfile.ThumbnailImageFileName),
          LocationLatitude = this.PropagatedProfile.Location.Latitude,
          LocationLongitude = this.PropagatedProfile.Location.Longitude,
          Name = this.PropagatedProfile.Name,
          ExtraData = this.PropagatedProfile.ExtraData
        },
        Challenge = this.challenge.ToHex(),
        ClientChallenge = this.clientChallenge.ToHex(),
        ExpandedPrivateKeyHex = this.keys.ExpandedPrivateKeyHex,
        HostingActive = this.hostingActive,
        IdentityId = this.identityId.ToHex(),
        PrivateKeyHex = this.keys.PrivateKeyHex,
        ProfileInitialized = this.profileInitialized,
        ProfileServerKey = this.profileServerKey.ToHex(),
        ProfileServerName = this.profileServer.Name,
        PublicKeyHex = this.keys.PublicKeyHex,
      };

      return res;
    }


    /// <summary>
    /// Creates instance of identity client from snapshot.
    /// </summary>
    /// <param name="Snapshot">Identity client snapshot.</param>
    /// <param name="Images">Hexadecimal image data mapping to SHA256 hash.</param>
    /// <param name="ProfileServer">Profile server that hosts identity's profile.</param>
    /// <returns>New identity client instance.</returns>
    public static IdentityClient CreateFromSnapshot(IdentitySnapshot Snapshot, Dictionary<string, string> Images, ProfileServer ProfileServer)
    {
      IdentityClient res = new IdentityClient();
      res.challenge = Snapshot.Challenge.FromHex();
      res.clientChallenge = Snapshot.ClientChallenge.FromHex();

      res.keys = new KeysEd25519();
      res.keys.ExpandedPrivateKeyHex = Snapshot.ExpandedPrivateKeyHex;
      res.keys.PublicKeyHex = Snapshot.PublicKeyHex;
      res.keys.PrivateKeyHex = Snapshot.PrivateKeyHex;
      res.keys.ExpandedPrivateKey = res.keys.ExpandedPrivateKeyHex.FromHex();
      res.keys.PublicKey = res.keys.PublicKeyHex.FromHex();
      res.keys.PrivateKey = res.keys.PrivateKeyHex.FromHex();

      res.Profile = new ClientProfile()
      {
        Version = Snapshot.Profile.Version,
        Type = Snapshot.Profile.Type,
        Name = Snapshot.Profile.Name,
        ProfileImageFileName = Snapshot.Profile.ProfileImageFileName != null ? Path.Combine(CommandProcessor.ImagesDirectory, Snapshot.Profile.ProfileImageFileName) : null,
        ThumbnailImageFileName = Snapshot.Profile.ThumbnailImageFileName != null ? Path.Combine(CommandProcessor.ImagesDirectory, Snapshot.Profile.ThumbnailImageFileName) : null,
        Location = new GpsLocation(Snapshot.Profile.LocationLatitude, Snapshot.Profile.LocationLongitude),
        ExtraData = Snapshot.Profile.ExtraData,
        ProfileImageHash = Snapshot.Profile.ProfileImageHash.FromHex(),
        ThumbnailImageHash = Snapshot.Profile.ThumbnailImageHash.FromHex(),
        ProfileImage = Snapshot.Profile.ProfileImageHash != null ? Images[Snapshot.Profile.ProfileImageHash].FromHex() : null,
        ThumbnailImage = Snapshot.Profile.ThumbnailImageHash != null ? Images[Snapshot.Profile.ThumbnailImageHash].FromHex() : null,
        PublicKey = res.keys.PublicKey,
      };

      res.PropagatedProfile = new ClientProfile()
      {
        Version = Snapshot.PropagatedProfile.Version,
        Type = Snapshot.PropagatedProfile.Type,
        Name = Snapshot.PropagatedProfile.Name,
        ProfileImageFileName = Snapshot.PropagatedProfile.ProfileImageFileName != null ? Path.Combine(CommandProcessor.ImagesDirectory, Snapshot.PropagatedProfile.ProfileImageFileName) : null,
        ThumbnailImageFileName = Snapshot.PropagatedProfile.ThumbnailImageFileName != null ? Path.Combine(CommandProcessor.ImagesDirectory, Snapshot.PropagatedProfile.ThumbnailImageFileName) : null,
        Location = new GpsLocation(Snapshot.PropagatedProfile.LocationLatitude, Snapshot.PropagatedProfile.LocationLongitude),
        ExtraData = Snapshot.PropagatedProfile.ExtraData,
        ProfileImageHash = Snapshot.PropagatedProfile.ProfileImageHash.FromHex(),
        ThumbnailImageHash = Snapshot.PropagatedProfile.ThumbnailImageHash.FromHex(),
        ProfileImage = Snapshot.PropagatedProfile.ProfileImageHash != null ? Images[Snapshot.PropagatedProfile.ProfileImageHash].FromHex() : null,
        ThumbnailImage = Snapshot.PropagatedProfile.ThumbnailImageHash != null ? Images[Snapshot.PropagatedProfile.ThumbnailImageHash].FromHex() : null,
        PublicKey = res.keys.PublicKey,
      };

      res.hostingActive = Snapshot.HostingActive;
      res.identityId = Snapshot.IdentityId.FromHex();
      res.profileInitialized = Snapshot.ProfileInitialized;

      res.profileServerKey = Snapshot.ProfileServerKey.FromHex();

      res.profileServer = ProfileServer;
      res.log = new Logger("NetworkSimulator.IdentityClient", "[" + res.Profile.Name + "] ");
      res.psMessageBuilder = new PsMessageBuilder(0, new List<SemVer>() { SemVer.V100 }, res.keys);
      res.proxMessageBuilder = new ProxMessageBuilder(0, new List<SemVer>() { SemVer.V100 }, res.keys);
      res.InitializeTcpClient();

      return res;
    }


    /// <summary>
    /// Signs data with client's private key.
    /// </summary>
    /// <param name="Data">Data to sign.</param>
    /// <returns>Signature of the data.</returns>
    public byte[] SignData(byte[] Data)
    {
      return Ed25519.Sign(Data, keys.ExpandedPrivateKey);
    }



    /// <summary>
    /// Sends proximity server protocol message over the network stream.
    /// </summary>
    /// <param name="Data">Message to send.</param>
    public async Task ProxSendMessageAsync(ProxProtocolMessage Data)
    {
      string dataStr = Data.ToString();
      log.Trace("()\n{0}", dataStr.Substring(0, Math.Min(dataStr.Length, 512)));

      byte[] rawData = ProxMessageBuilder.MessageToByteArray(Data);
      await stream.WriteAsync(rawData, 0, rawData.Length);

      log.Trace("(-)");
    }


    /// <summary>
    /// Reads and parses proximity server protocol message from the network stream.
    /// </summary>
    /// <returns>Parsed protocol message or null if the function fails.</returns>
    public async Task<ProxProtocolMessage> ProxReceiveMessageAsync()
    {
      log.Trace("()");

      ProxProtocolMessage res = null;

      byte[] header = new byte[ProtocolHelper.HeaderSize];
      int headerBytesRead = 0;
      int remain = header.Length;

      bool done = false;
      log.Trace("Reading message header.");
      while (!done && (headerBytesRead < header.Length))
      {
        int readAmount = await stream.ReadAsync(header, headerBytesRead, remain);
        if (readAmount == 0)
        {
          log.Trace("Connection to server closed while reading the header.");
          done = true;
          break;
        }

        headerBytesRead += readAmount;
        remain -= readAmount;
      }

      uint messageSize = BitConverter.ToUInt32(header, 1);
      log.Trace("Message body size is {0} bytes.", messageSize);

      byte[] messageBytes = new byte[ProtocolHelper.HeaderSize + messageSize];
      Array.Copy(header, messageBytes, header.Length);

      remain = (int)messageSize;
      int messageBytesRead = 0;
      while (!done && (messageBytesRead < messageSize))
      {
        int readAmount = await stream.ReadAsync(messageBytes, ProtocolHelper.HeaderSize + messageBytesRead, remain);
        if (readAmount == 0)
        {
          log.Trace("Connection to server closed while reading the body.");
          done = true;
          break;
        }

        messageBytesRead += readAmount;
        remain -= readAmount;
      }

      res = new ProxProtocolMessage(Iop.Proximityserver.MessageWithHeader.Parser.ParseFrom(messageBytes).Body);

      string resStr = res.ToString();
      log.Trace("(-):\n{0}", resStr.Substring(0, Math.Min(resStr.Length, 512)));
      return res;
    }


    /// <summary>
    /// Starts conversation with the proximity server the client is connected to and checks whether the server response contains expected values.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ProxStartConversationAsync(byte[] Challenge)
    {
      log.Trace("()");

      ProxProtocolMessage requestMessage = proxMessageBuilder.CreateStartConversationRequest(Challenge);
      await ProxSendMessageAsync(requestMessage);
      ProxProtocolMessage responseMessage = await ProxReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;
      bool challengeVerifyOk = ProxVerifyProfileServerChallengeSignature(responseMessage, Challenge);

      SemVer receivedVersion = new SemVer(responseMessage.Response.ConversationResponse.Start.Version);
      bool versionOk = receivedVersion.Equals(new SemVer(psMessageBuilder.Version));

      bool pubKeyLenOk = responseMessage.Response.ConversationResponse.Start.PublicKey.Length == 32;
      bool challengeOk = responseMessage.Response.ConversationResponse.Start.Challenge.Length == 32;

      challenge = responseMessage.Response.ConversationResponse.Start.Challenge.ToByteArray();

      bool res = idOk && statusOk && challengeVerifyOk && versionOk && pubKeyLenOk && challengeOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Verifies identity with proximity server to which the client is already connected.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ProxVerifyIdentity()
    {
      log.Trace("()");

      byte[] sessionChallenge = new byte[ProxMessageBuilder.ChallengeDataSize];
      Crypto.Rng.GetBytes(sessionChallenge);
      bool startConversationOk = await ProxStartConversationAsync(sessionChallenge);

      ProxProtocolMessage requestMessage = proxMessageBuilder.CreateVerifyIdentityRequest(challenge);
      await ProxSendMessageAsync(requestMessage);
      ProxProtocolMessage responseMessage = await ProxReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool verifyIdentityOk = idOk && statusOk;

      bool res = startConversationOk && verifyIdentityOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Verifies whether the profile server successfully signed the correct start conversation challenge.
    /// </summary>
    /// <param name="StartConversationResponse">StartConversationResponse received from the profile server.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool ProxVerifyProfileServerChallengeSignature(ProxProtocolMessage StartConversationResponse, byte[] Challenge)
    {
      log.Trace("()");

      byte[] receivedChallenge = StartConversationResponse.Response.ConversationResponse.Start.ClientChallenge.ToByteArray();
      byte[] proximityServerPublicKey = StartConversationResponse.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      bool res = ByteArrayComparer.Equals(receivedChallenge, Challenge)
        && proxMessageBuilder.VerifySignedConversationResponseBodyPart(StartConversationResponse, receivedChallenge, proximityServerPublicKey);

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Creates activity on a proximity server.
    /// </summary>
    /// <param name="Server">Primary proximity server to create activity on.</param>
    /// <param name="ActivityInfo">Activity information.</param>
    /// <param name="IgnoreServerIds">List of server network IDs to ignore by target proximity server.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ProxCreateActivityAsync(ProximityServer Server, ActivityInfo ActivityInfo, List<byte[]> IgnoreServerIds = null)
    {
      log.Trace("()");

      bool res = false;

      InitializeTcpClient();

      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientInterfacePort, true);
        bool verifyIdentityOk = await ProxVerifyIdentity();

        SignedActivityInformation signedActivityInformation = ActivityInfo.ToSignedActivityInformation();
        ProxProtocolMessage request = proxMessageBuilder.CreateCreateActivityRequest(signedActivityInformation.Activity, IgnoreServerIds);
        await ProxSendMessageAsync(request);
        ProxProtocolMessage response = await ProxReceiveMessageAsync();

        bool idOk = request.Id == response.Id;
        bool statusOk = response.Response.Status == Status.Ok;
        res = verifyIdentityOk && idOk && statusOk;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      CloseTcpClient();

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Creates activity on a proximity server.
    /// </summary>
    /// <param name="Server">Primary proximity server to create activity on.</param>
    /// <param name="ActivityId">Activity identifier.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> DeleteActivityAsync(ProximityServer Server, uint ActivityId)
    {
      log.Trace("()");

      bool res = false;

      InitializeTcpClient();

      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientInterfacePort, true);
        bool verifyIdentityOk = await ProxVerifyIdentity();

        ProxProtocolMessage request = proxMessageBuilder.CreateDeleteActivityRequest(ActivityId);
        await ProxSendMessageAsync(request);
        ProxProtocolMessage response = await ProxReceiveMessageAsync();

        bool idOk = request.Id == response.Id;
        bool statusOk = response.Response.Status == Status.Ok;
        res = verifyIdentityOk && idOk && statusOk;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      CloseTcpClient();

      log.Trace("(-):{0}", res);
      return res;
    }




    /// <summary>
    /// Creates activities on proximity server.
    /// </summary>
    /// <param name="Server">Proximity server to create activities on.</param>
    /// <returns>true if the function succeeded, false otherwise.</returns>
    public async Task<bool> InitializeActivitiesAsync(ProximityServer Server, List<Activity> Activities)
    {
      log.Trace("(Server.Name:'{0}')", Server.Name);
      bool error = false;

      InitializeTcpClient();

      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientInterfacePort, true);
        if (await ProxVerifyIdentity())
        {
          foreach (Activity activity in Activities)
          {
            SignedActivityInformation signedActivityInformation = activity.PrimaryInfo.ToSignedActivityInformation();
            ProxProtocolMessage request = proxMessageBuilder.CreateCreateActivityRequest(signedActivityInformation.Activity, null);
            await ProxSendMessageAsync(request);
            ProxProtocolMessage response = await ProxReceiveMessageAsync();

            bool idOk = request.Id == response.Id;
            bool statusOk = response.Response.Status == Status.Ok;
            if (idOk && statusOk)
            {
              activity.InitializeActivityOnServer(Server);
            }
            else
            {
              log.Error("Unable to create activity '{0}' on proximity server '{1}', error code {2}.", activity.GetName(), Server.Name, response.Response.Status);
              error = true;
              break;
            }
          }
        }
        else log.Error("Unable to verify identity on proximity server '{0}'.", Server.Name);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      CloseTcpClient();

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
