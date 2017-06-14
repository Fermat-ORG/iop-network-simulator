using Iop.Locnet;
using IopCommon;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkSimulator
{
  /// <summary>
  /// Simulator of LOC server. With each server we spawn a LOC server 
  /// which will provide information about the neighborhood to the server.
  /// </summary>
  public class LocServer
  {
    /// <summary>Instance logger.</summary>
    private Logger log;

    /// <summary>Interface IP address to listen on.</summary>
    private IPAddress ipAddress;

    /// <summary>TCP port to listen on.</summary>
    private int port;

    /// <summary>Associated server.</summary>
    private ServerBase associatedServer;

    /// <summary>Lock object to protect access to Neighbors.</summary>
    private object neighborsLock = new object();

    /// <summary>List of servers that are neighbors of the associated server.</summary>
    private Dictionary<string, ServerBase> neighbors = new Dictionary<string, ServerBase>(StringComparer.Ordinal);

    /// <summary>TCP server that provides information about the neighborhood via LocNet protocol.</summary>
    private TcpListener listener;

    /// <summary>If associated server is connected, this is its connection.</summary>
    private TcpClient connectedAssociatedServer;

    /// <summary>true if associated server sent us GetNeighbourNodesByDistanceLocalRequest request and wants to receive updates.</summary>
    private bool connectedAssociatedServerWantsUpdates;

    /// <summary>If associated server is connected, this is its message builder.</summary>
    private LocMessageBuilder connectedAssociatedServerMessageBuilder;

    /// <summary>Event that is set when acceptThread is not running.</summary>
    private ManualResetEvent acceptThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is waiting for the new clients to connect to the TCP server port.</summary>
    private Thread acceptThread;

    /// <summary>True if the shutdown was initiated, false otherwise.</summary>
    private bool isShutdown = false;

    /// <summary>Shutdown event is set once the shutdown was initiated.</summary>
    private ManualResetEvent shutdownEvent = new ManualResetEvent(false);

    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the shutdown is initiated.</summary>
    private CancellationTokenSource shutdownCancellationTokenSource = new CancellationTokenSource();

    /// <summary>Lock object for writing to client streams. This is simulation only, we do not expect more than one client.</summary>
    private SemaphoreSlim StreamWriteLock = new SemaphoreSlim(1);

    /// <summary>Node location.</summary>
    private Iop.Locnet.GpsLocation nodeLocation;


    /// <summary>
    /// Initializes the LOC server instance.
    /// </summary>
    /// <param name="IpAddress">IP address on which LOC server is going to listen.</param>
    /// <param name="Port">TCP port on which LOC server is going to listen.</param>
    /// <param name="Location">GPS location of the LOC server.</param>
    /// <param name="AssociatedServer">Associated server.</param>
    public LocServer(IPAddress IpAddress, int Port, IopProtocol.GpsLocation Location, ServerBase AssociatedServer)
    {
      log = new Logger("NetworkSimulator.LocServer", string.Format("[{0}#{1}] ", AssociatedServer is ProfileServer ? "ps" : "px", AssociatedServer.Name));
      log.Trace("(IpAddress:{0},Port:{1},Location:[{2}])", IpAddress, Port, Location);

      this.associatedServer = AssociatedServer;
      ipAddress = IpAddress;
      port = Port;

      nodeLocation = new Iop.Locnet.GpsLocation()
      {
        Latitude = Location.GetLocationTypeLatitude(),
        Longitude = Location.GetLocationTypeLongitude()
      };

      listener = new TcpListener(ipAddress, port);
      listener.Server.LingerState = new LingerOption(true, 0);
      listener.Server.NoDelay = true;

      log.Trace("(-)");
    }


    /// <summary>
    /// Starts the TCP server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Start()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        log.Trace("Listening on '{0}:{1}'.", ipAddress, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (res)
      {
        acceptThread = new Thread(new ThreadStart(AcceptThread));
        acceptThread.Start();
      }


      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Stops the TCP server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Stop()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        if (listener != null)
        {
          listener.Stop();
          res = true;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources used by the LOC server.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      isShutdown = true;
      shutdownEvent.Set();
      shutdownCancellationTokenSource.Cancel();

      Stop();

      if ((acceptThread != null) && !acceptThreadFinished.WaitOne(10000))
        log.Error("Accept thread did not terminated in 10 seconds.");

      log.Trace("(-)");
    }


    /// <summary>
    /// Thread procedure that is responsible for accepting new clients on the TCP server port.
    /// </summary>
    public void AcceptThread()
    {
      log.Trace("()");

      acceptThreadFinished.Reset();

      AutoResetEvent acceptTaskEvent = new AutoResetEvent(false);

      while (!isShutdown)
      {
        log.Debug("Waiting for new client.");
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        acceptTask.ContinueWith(t => acceptTaskEvent.Set());

        WaitHandle[] handles = new WaitHandle[] { acceptTaskEvent, shutdownEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == shutdownEvent)
        {
          log.Debug("Shutdown detected.");
          break;
        }

        try
        {
          // acceptTask is finished here, asking for Result won't block.
          TcpClient client = acceptTask.Result;
          log.Debug("New client '{0}' accepted.", client.Client.RemoteEndPoint);
          ClientHandlerAsync(client);
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
      }

      acceptThreadFinished.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for each client that connects to the TCP server.
    /// </summary>
    /// <param name="Client">Client that is connected to TCP server.</param>
    private async void ClientHandlerAsync(TcpClient Client)
    {
      LogDiagnosticContext.Start();

      log.Debug("(Client.Client.RemoteEndPoint:{0})", Client.Client.RemoteEndPoint);

      connectedAssociatedServer = Client;
      connectedAssociatedServerMessageBuilder = new LocMessageBuilder(0, new List<SemVer>() { SemVer.V100 });

      await ReceiveMessageLoop(Client, connectedAssociatedServerMessageBuilder);

      connectedAssociatedServerWantsUpdates = false;
      connectedAssociatedServer = null;
      Client.Dispose();

      log.Debug("(-)");

      LogDiagnosticContext.Stop();
    }


    /// <summary>
    /// Reads messages from the client stream and processes them in a loop until the client disconnects 
    /// or until an action (such as a protocol violation) that leads to disconnecting of the client occurs.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    public async Task ReceiveMessageLoop(TcpClient Client, LocMessageBuilder MessageBuilder)
    {
      log.Trace("()");

      try
      {
        NetworkStream stream = Client.GetStream();
        RawMessageReader messageReader = new RawMessageReader(stream);
        while (!isShutdown)
        {
          RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(shutdownCancellationTokenSource.Token);
          bool disconnect = rawMessage.Data == null;
          bool protocolViolation = rawMessage.ProtocolViolation;
          if (rawMessage.Data != null)
          {
            LocProtocolMessage message = (LocProtocolMessage)LocMessageBuilder.CreateMessageFromRawData(rawMessage.Data);
            if (message != null) disconnect = !await ProcessMessageAsync(Client, MessageBuilder, message);
            else protocolViolation = true;
          }

          if (protocolViolation)
          {
            await SendProtocolViolation(Client);
            break;
          }

          if (disconnect)
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-)");
    }



    /// <summary>
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation(TcpClient Client)
    {
      LocMessageBuilder mb = new LocMessageBuilder(0, new List<SemVer>() { SemVer.V100 });
      LocProtocolMessage response = mb.CreateErrorProtocolViolationResponse(new LocProtocolMessage(new Message() { Id = 0x0BADC0DE }));

      await SendMessageAsync(Client, response);
    }


    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(TcpClient Client, LocMessageBuilder MessageBuilder, LocProtocolMessage IncomingMessage)
    {
      bool res = false;
      log.Debug("()");
      try
      {
        log.Trace("Received message type is {0}, message ID is {1}.", IncomingMessage.MessageTypeCase, IncomingMessage.Id);
        switch (IncomingMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              LocProtocolMessage responseMessage = MessageBuilder.CreateErrorProtocolViolationResponse(IncomingMessage);
              Request request = IncomingMessage.Request;

              bool setKeepAlive = false;

              SemVer version = new SemVer(request.Version);
              log.Trace("Request type is {0}, version is {1}.", request.RequestTypeCase, version);
              switch (request.RequestTypeCase)
              {
                case Request.RequestTypeOneofCase.LocalService:
                  {
                    log.Trace("Local service request type is {0}.", request.LocalService.LocalServiceRequestTypeCase);
                    switch (request.LocalService.LocalServiceRequestTypeCase)
                    {
                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.RegisterService:
                        responseMessage = ProcessMessageRegisterServiceRequest(Client, MessageBuilder, IncomingMessage);
                        break;

                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.DeregisterService:
                        responseMessage = ProcessMessageDeregisterServiceRequest(Client, MessageBuilder, IncomingMessage);
                        break;

                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.GetNeighbourNodes:
                        responseMessage = ProcessMessageGetNeighbourNodesByDistanceLocalRequest(Client, MessageBuilder, IncomingMessage, out setKeepAlive);
                        break;

                      default:
                        log.Error("Invalid local service request type '{0}'.", request.LocalService.LocalServiceRequestTypeCase);
                        break;
                    }
                    break;
                  }

                default:
                  log.Error("Invalid request type '{0}'.", request.RequestTypeCase);
                  break;
              }


              if (responseMessage != null)
              {
                // Send response to client.
                res = await SendMessageAsync(Client, responseMessage);

                if (res)
                {
                  // If the message was sent successfully to the target, we close the connection only in case of protocol violation error.
                  if (responseMessage.MessageTypeCase == Message.MessageTypeOneofCase.Response)
                    res = responseMessage.Response.Status != Status.ErrorProtocolViolation;
                }

                if (res && setKeepAlive)
                {
                  connectedAssociatedServerWantsUpdates = true;
                  log.Debug("{0} server '{1}' is now connected to its LOC server and waiting for updates.", associatedServer.Type, associatedServer.Name);
                }
              }
              else
              {
                // If there is no response to send immediately to the client,
                // we want to keep the connection open.
                res = true;
              }
              break;
            }

          case Message.MessageTypeOneofCase.Response:
            {
              Response response = IncomingMessage.Response;
              log.Trace("Response status is {0}, details are '{1}', response type is {2}.", response.Status, response.Details, response.ResponseTypeCase);

              switch (response.ResponseTypeCase)
              {
                case Response.ResponseTypeOneofCase.LocalService:
                  {
                    log.Trace("Local service response type is {0}.", response.LocalService.LocalServiceResponseTypeCase);
                    switch (response.LocalService.LocalServiceResponseTypeCase)
                    {
                      case LocalServiceResponse.LocalServiceResponseTypeOneofCase.NeighbourhoodUpdated:
                        // Nothing to be done here.
                        res = true;
                        break;

                      default:
                        log.Error("Invalid local service response type '{0}'.", response.LocalService.LocalServiceResponseTypeCase);
                        break;
                    }

                    break;
                  }

                default:
                  log.Error("Unknown response type '{0}'.", response.ResponseTypeCase);
                  // Connection will be closed in ReceiveMessageLoop.
                  break;
              }

              break;
            }

          default:
            log.Error("Unknown message type '{0}', connection to the client will be closed.", IncomingMessage.MessageTypeCase);
            await SendProtocolViolation(Client);
            // Connection will be closed in ReceiveMessageLoop.
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        await SendProtocolViolation(Client);
        // Connection will be closed in ReceiveMessageLoop.
      }

      log.Debug("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public async Task<bool> SendMessageAsync(TcpClient Client, LocProtocolMessage Message)
    {
      log.Trace("()");

      bool res = await SendMessageInternalAsync(Client, Message);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the message was sent successfully to the target recipient.</returns>
    private async Task<bool> SendMessageInternalAsync(TcpClient Client, LocProtocolMessage Message)
    {
      log.Trace("()");

      bool res = false;

      string msgStr = Message.ToString();
      log.Trace("Sending message:\n{0}", msgStr);
      byte[] responseBytes = LocMessageBuilder.MessageToByteArray(Message);

      await StreamWriteLock.WaitAsync();
      try
      {
        NetworkStream stream = Client.GetStream();
        if (stream != null)
        {
          await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
          res = true;
        }
        else log.Info("Connection to the client has been terminated.");
      }
      catch (IOException)
      {
        log.Info("Connection to the client has been terminated.");
      }
      finally
      {
        StreamWriteLock.Release();
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Processes GetNeighbourNodesByDistanceLocalRequest message from client.
    /// <para>Obtains information about the server's neighborhood and initiates sending updates to it.</para>
    /// </summary>
    /// <param name="Client">TCP client that sent the request.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <param name="KeepAlive">This is set to true if KeepAliveAndSendUpdates in the request was set.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public LocProtocolMessage ProcessMessageGetNeighbourNodesByDistanceLocalRequest(TcpClient Client, LocMessageBuilder MessageBuilder, LocProtocolMessage RequestMessage, out bool KeepAlive)
    {
      log.Trace("()");

      LocProtocolMessage res = null;

      GetNeighbourNodesByDistanceLocalRequest getNeighbourNodesByDistanceLocalRequest = RequestMessage.Request.LocalService.GetNeighbourNodes;
      KeepAlive = getNeighbourNodesByDistanceLocalRequest.KeepAliveAndSendUpdates;

      List<NodeInfo> neighborList = new List<NodeInfo>();
      lock (neighborsLock)
      {
        foreach (ServerBase neighborServer in neighbors.Values)
        {
          NodeInfo ni = neighborServer.GetNodeInfo();
          neighborList.Add(ni);
        }
      }

      res = MessageBuilder.CreateGetNeighbourNodesByDistanceLocalResponse(RequestMessage, neighborList);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Adds neighbors to the associated server.
    /// </summary>
    /// <param name="NeighborhoodList">List of all servers in the new neighborhood.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool AddNeighborhood(List<ServerBase> NeighborhoodList)
    {
      log.Trace("(NeighborhoodList.Count:{0})", NeighborhoodList.Count);

      List<NeighbourhoodChange> changes = new List<NeighbourhoodChange>();
      bool res = false;
      lock (neighborsLock)
      {
        foreach (ServerBase neighborServer in NeighborhoodList)
        {
          // Do not add your own associated server.
          if (neighborServer.Name == associatedServer.Name) continue;

          // Ignore neighbors that we already have in the list.
          if (neighbors.ContainsKey(neighborServer.Name))
          {
            log.Debug("Server '{0}' already has '{1}' as its neighbor.", associatedServer.Name, neighborServer.Name);
            continue;
          }

          neighborServer.Lock();
          if (neighborServer.IsInitialized())
          {
            neighbors.Add(neighborServer.Name, neighborServer);
            log.Debug("Server '{0}' added to the neighborhood of server '{1}'.", neighborServer.Name, associatedServer.Name);

            // This neighbor server already runs, so we know its profile 
            // we can inform our associated server about it.
            NeighbourhoodChange change = new NeighbourhoodChange();
            change.AddedNodeInfo = neighborServer.GetNodeInfo();
            changes.Add(change);
          }
          else
          {
            // This neighbor server does not run yet, so we do not have its profile.
            // We will install an event to be triggered when this server starts.
            log.Debug("Server '{0}' is not initialized yet, installing notification for server '{1}'.", neighborServer.Name, associatedServer.Name);
            neighborServer.InstallInitializationNeighborhoodNotification(associatedServer);
          }
          neighborServer.Unlock();
        }
      }

      if ((connectedAssociatedServer != null) && connectedAssociatedServerWantsUpdates)
      {
        // If our associated server is running already, adding servers to its neighborhood 
        // ends with sending update notification to the associated server.
        if (changes.Count > 0)
        {
          log.Debug("Sending {0} neighborhood changes to server '{1}'.", changes.Count, associatedServer.Name);
          LocProtocolMessage message = connectedAssociatedServerMessageBuilder.CreateNeighbourhoodChangedNotificationRequest(changes);
          res = SendMessageAsync(connectedAssociatedServer, message).Result;
        }
        else
        {
          log.Debug("No neighborhood changes to send to server '{0}'.", associatedServer.Name);
          res = true;
        }
      }
      else
      {
        // Our associated server is not started/connected yet, which means we just modified its neighborhood,
        // and the information about its neighborhood will be send to it once it sends us GetNeighbourNodesByDistanceLocalRequest.
        log.Debug("Associated server '{0}' is not connected or not fully initialized yet, can't send changes.", associatedServer.Name);
        res = true;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sets neighborhood of the associated server during the load from the snapshot.
    /// </summary>
    /// <param name="NeighborhoodList">List of all servers in the server's neighborhood.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool SetNeighborhood(List<ServerBase> NeighborhoodList)
    {
      log.Trace("(NeighborhoodList.Count:{0})", NeighborhoodList.Count);

      bool res = false;
      lock (neighborsLock)
      {
        neighbors.Clear();

        foreach (ServerBase neighbor in NeighborhoodList)
          neighbors.Add(neighbor.Name, neighbor);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Removes servers from the associated server's neighborhood.
    /// </summary>
    /// <param name="NeighborhoodList">List of servers to cancel neighbor connection with..</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool CancelNeighborhood(List<ServerBase> NeighborhoodList)
    {
      log.Trace("(NeighborhoodList.Count:{0})", NeighborhoodList.Count);

      List<NeighbourhoodChange> changes = new List<NeighbourhoodChange>();
      bool res = false;
      lock (neighborsLock)
      {
        foreach (ServerBase neighborServer in NeighborhoodList)
        {
          // Do not process your own associated server.
          if (neighborServer.Name == associatedServer.Name) continue;

          neighborServer.Lock();
          if (neighborServer.IsInitialized())
          {
            // Ignore servers that are not in the neighborhood.
            if (neighbors.ContainsKey(neighborServer.Name))
            {
              neighbors.Remove(neighborServer.Name);
              log.Trace("Server '{0}' removed from the neighborhood of server '{1}'.", neighborServer.Name, associatedServer.Name);

              // This neighbor server already runs, so we know its profile 
              // we can inform our associated server about it.
              NeighbourhoodChange change = new NeighbourhoodChange();
              change.RemovedNodeId = ProtocolHelper.ByteArrayToByteString(neighborServer.GetNetworkId());
              changes.Add(change);
            }
          }
          else
          {
            // This neighbor server does not run yet, so we do not have its profile.
            // We will uninstall a possibly installed event.
            log.Debug("Server '{0}' is not initialized yet, uninstalling notification for server '{1}'.", neighborServer.Name, associatedServer.Name);
            neighborServer.UninstallInitializationNeighborhoodNotification(associatedServer);
          }
          neighborServer.Unlock();
        }
      }

      if ((connectedAssociatedServer != null) && connectedAssociatedServerWantsUpdates)
      {
        // If our associated server is running already, removing servers to its neighborhood 
        // ends with sending update notification to the associated server.
        if (changes.Count > 0)
        {
          log.Debug("Sending {0} neighborhood changes to server '{1}'.", changes.Count, associatedServer.Name);
          LocProtocolMessage message = connectedAssociatedServerMessageBuilder.CreateNeighbourhoodChangedNotificationRequest(changes);
          res = SendMessageAsync(connectedAssociatedServer, message).Result;
        }
        else
        {
          log.Debug("No neighborhood changes to send to server '{0}'.", associatedServer.Name);
          res = true;
        }
      }
      else
      {
        // Our associated server is not started/connected yet, which means we just modified its neighborhood,
        // and the information about its neighborhood will be send to it once it sends us GetNeighbourNodesByDistanceLocalRequest.
        log.Debug("Server '{0}' is not connected or not fully initialized yet, can't send changes.", associatedServer.Name);
        res = true;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes RegisterServiceRequest message from client.
    /// <para>Obtains information about the associated server's NodeProfile.</para>
    /// </summary>
    /// <param name="Client">TCP client that sent the request.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public LocProtocolMessage ProcessMessageRegisterServiceRequest(TcpClient Client, LocMessageBuilder MessageBuilder, LocProtocolMessage RequestMessage)
    {
      log.Trace("()");

      LocProtocolMessage res = MessageBuilder.CreateRegisterServiceResponse(RequestMessage, new IopProtocol.GpsLocation(nodeLocation.Latitude, nodeLocation.Longitude));

      RegisterServiceRequest registerServiceRequest = RequestMessage.Request.LocalService.RegisterService;

      byte[] serverId = registerServiceRequest.Service.ServiceData.ToByteArray();
      switch (associatedServer.Type)
      {
        case ServerType.Profile:
          if ((registerServiceRequest.Service.Type == ServiceType.Profile) && (serverId.Length == 32)) associatedServer.SetNetworkId(serverId);
          else log.Error("Received register service request is invalid.");
          break;

        case ServerType.Proximity:
          if ((registerServiceRequest.Service.Type == ServiceType.Proximity) && (serverId.Length == 32))
          {
            // SetNetworkId sends LOC neighborhood change notifications to neighbors of the server.
            // In case of Proximity server, however, we need to wait for serverProcessInitializationCompleteEvent as well.
            // We will create a worker thread that waits for serverProcessInitializationCompleteEvent and sets the network ID.
            ThreadPool.QueueUserWorkItem(new WaitCallback(associatedServer.SetNetworkIdCallback), serverId);
          } else log.Error("Received register service request is invalid.");
          break;

        default:
          log.Error("Invalid associated server type {0}.", associatedServer.Type);
          break;
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }

    /// <summary>
    /// Processes DeregisterServiceRequest message from client.
    /// <para>Removes information about the server's NodeProfile.</para>
    /// </summary>
    /// <param name="Client">TCP client that sent the request.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public LocProtocolMessage ProcessMessageDeregisterServiceRequest(TcpClient Client, LocMessageBuilder MessageBuilder, LocProtocolMessage RequestMessage)
    {
      log.Trace("()");

      LocProtocolMessage res = MessageBuilder.CreateDeregisterServiceResponse(RequestMessage);

      DeregisterServiceRequest deregisterServiceRequest = RequestMessage.Request.LocalService.DeregisterService;
      associatedServer.Uninitialize();

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }

    /// <summary>
    /// Returns list of related server's neighbors.
    /// </summary>
    /// <returns>List of related server's neigbhors.</returns>
    public List<ServerBase> GetNeighbors()
    {
      List<ServerBase> res = null;

      lock (neighborsLock)
      {
        res = neighbors.Values.ToList();
      }

      return res;
    }

    /// <summary>
    /// Creates LOC server's snapshot.
    /// </summary>
    /// <returns>LOC server's snapshot.</returns>
    public LocServerSnapshot CreateSnapshot()
    {
      LocServerSnapshot res = new LocServerSnapshot()
      {
        IpAddress = this.ipAddress.ToString(),
        NeighborsNames = this.neighbors.Keys.ToList(),
        Port = this.port,
      };
      return res;
    }

    /// <summary>
    /// Adds a neighbor server to the list of neighbors when loading simulation from snapshot.
    /// </summary>
    public void AddNeighborSnapshot(ServerBase Neighbor)
    {
      lock (neighborsLock)
      {
        neighbors.Add(Neighbor.Name, Neighbor);
      }
    }
  }
}
