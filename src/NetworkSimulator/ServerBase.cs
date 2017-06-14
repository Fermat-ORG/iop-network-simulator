using Google.Protobuf;
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
  /// <summary>Types of servers.</summary>
  public enum ServerType { Profile, Proximity };

  /// <summary>
  /// Base class for profile and proximity servers.
  /// </summary>
  public abstract class ServerBase
  {
    /// <summary>Instance logger.</summary>
    protected Logger log;

    /// <summary>Name of the directory containing files of this server instance.</summary>
    protected string instanceDirectory;

    /// <summary>Name of the server instance.</summary>
    protected string name;
    /// <summary>Name of the server instance.</summary>
    public string Name { get { return name; } }

    /// <summary>GPS location of the server.</summary>
    protected GpsLocation location;

    /// <summary>IP address of the interface on which the server is listening.</summary>
    protected IPAddress ipAddress;
    /// <summary>IP address of the interface on which the server is listening.</summary>
    public IPAddress IpAddress { get { return ipAddress; } }

    /// <summary>Base TCP port of the instance, which can use ports between Port and Port + 19.</summary>
    protected int basePort;

    /// <summary>Port of LOC server.</summary>
    protected int locPort;
    /// <summary>Port of LOC server.</summary>
    public int LocPort { get { return locPort; } }

    /// <summary>Port of server primary interface.</summary>
    protected int primaryInterfacePort;
    /// <summary>Port of server primary interface.</summary>
    public int PrimaryInterfacePort { get { return primaryInterfacePort; } }

    /// <summary>Port of server neighbors interface.</summary>
    protected int neighborInterfacePort;

    /// <summary>System process of the running instance.</summary>
    protected Process runningProcess;

    /// <summary>Event that is set when the server instance process is fully initialized.</summary>
    protected ManualResetEvent serverProcessInitializationCompleteEvent = new ManualResetEvent(false);

    /// <summary>Associated LOC server.</summary>
    protected LocServer locServer;
    /// <summary>Associated LOC server.</summary>
    public LocServer LocServer { get { return locServer; } }


    /// <summary>Lock object to protect access to some internal fields.</summary>
    protected object internalLock = new object();

    /// <summary>Network ID of the server, or null if it has not been initialized yet.</summary>
    protected byte[] networkId = null;

    /// <summary>
    /// Server is initialized if it registered with its associated LOC server and filled in its network ID.
    /// If it deregisters with its LOC server, it is set to false again, but its network ID remains.
    /// </summary>
    protected volatile bool initialized = false;

    /// <summary>Node location in LOC.</summary>
    protected Iop.Locnet.GpsLocation nodeLocation;
    /// <summary>Node location in LOC.</summary>
    public Iop.Locnet.GpsLocation NodeLocation { get { return nodeLocation; } }

    /// <summary>List of servers for which this server acts as a neighbor, that are to be informed once this server is initialized.</summary>
    protected HashSet<ServerBase> initializationNeighborhoodNotificationList;

    /// <summary>Type of the server.</summary>
    protected ServerType type;
    /// <summary>Type of the server.</summary>
    public ServerType Type { get { return type; } }


    /// <summary>
    /// Returns name of the configuration file.
    /// </summary>
    /// <returns>Name of the configuration file.</returns>
    public abstract string GetConfigFileName();

    /// <summary>
    /// Returns name of the main executable file.
    /// </summary>
    /// <returns>Name of the main executable file.</returns>
    public abstract string GetExecutableFileName();
   
    /// <summary>
    /// Creates a final configuration file for the instance.
    /// </summary>
    /// <param name="FinalConfigFile">Name of the final configuration file.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public abstract bool InitializeConfig(string FinalConfigFile);


    /// <summary>
    /// Creates a new instance of a server.
    /// </summary>
    /// <param name="Name">Unique server instance name.</param>
    /// <param name="Location">GPS location of this server instance.</param>
    /// <param name="Port">Base TCP port that defines the range of ports that are going to be used by this server instance and its related servers.</param>
    public ServerBase(string Name, GpsLocation Location, int Port)
    {
      type = this is ProfileServer ? ServerType.Profile : ServerType.Proximity;
      log = new Logger("NetworkSimulator.ServerBase", string.Format("[{0}#{1}]", type == ServerType.Profile ? "ps" : "px", Name));
      log.Trace("(Name:'{0}',Location:{1},Port:{2})", Name, Location, Port);

      this.name = Name;
      this.location = Location;
      basePort = Port;
      ipAddress = IPAddress.Parse("127.0.0.1");

      locPort = basePort;
      primaryInterfacePort = basePort + 1;
      neighborInterfacePort = basePort + 2;

      nodeLocation = new Iop.Locnet.GpsLocation()
      {
        Latitude = Location.GetLocationTypeLatitude(),
        Longitude = Location.GetLocationTypeLongitude()
      };

      initializationNeighborhoodNotificationList = new HashSet<ServerBase>();

      log.Trace("(-)");
    }


    /// <summary>
    /// Returns instance directory for the server instance.
    /// </summary>
    /// <returns>Instance directory for the server instance.</returns>
    public string GetInstanceDirectoryName()
    {
      return GetInstanceDirectoryName(name);
    }

    /// <summary>
    /// Returns instance directory for the server instance.
    /// </summary>
    /// <param name="InstanceName">Name of the server instance.</param>
    /// <returns>Instance directory for the server instance.</returns>
    public string GetInstanceDirectoryName(string InstanceName)
    {
      return GetInstanceDirectoryName(InstanceName, type);
    }


    /// <summary>
    /// Returns instance directory for the server instance.
    /// </summary>
    /// <param name="InstanceName">Name of the server instance.</param>
    /// <param name="Type">Type of the server.</param>
    /// <returns>Instance directory for the server instance.</returns>
    public static string GetInstanceDirectoryName(string InstanceName, ServerType Type)
    {
      return Path.Combine(CommandProcessor.InstanceDirectory, (Type == ServerType.Profile ? "Ps-" : "Px-") + InstanceName);
    }

    /// <summary>
    /// Initialize a new instance of a server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Initialize()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        instanceDirectory = GetInstanceDirectoryName();
        Directory.CreateDirectory(instanceDirectory);

        string orgBinDir = type == ServerType.Profile ? CommandProcessor.ProfileServerBinariesDirectory : CommandProcessor.ProximityServerBinariesDirectory;
        if (Helpers.DirectoryCopy(orgBinDir, instanceDirectory))
        {
          string configFinal = Path.Combine(instanceDirectory, GetConfigFileName());
          if (InitializeConfig(configFinal))
          {
            locServer = new LocServer(this.IpAddress, this.LocPort, this.location, this);
            res = locServer.Start();
          }
          else log.Error("Unable to initialize configuration file '{0}' for server '{1}'.", configFinal, name);
        }
        else log.Error("Unable to copy files from directory '{0}' to '{1}'.", CommandProcessor.ProfileServerBinariesDirectory, instanceDirectory);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources used by the server.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      locServer.Shutdown();
      Stop();

      log.Trace("(-)");
    }

    /// <summary>
    /// Starts server instance.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Start()
    {
      log.Trace("()");

      bool res = false;

      runningProcess = RunProcess();
      if (runningProcess != null)
      {
        log.Trace("Waiting for {0} server to start ...", type);
        if (serverProcessInitializationCompleteEvent.WaitOne(60 * 1000))
        {
          log.Trace("Waiting for {0} server to initialize with its LOC server ...", type);
          int counter = 45;
          while (!IsInitialized() && (counter > 0))
          {
            Thread.Sleep(1000);
            counter--;
          }
          res = counter > 0;
        }
        else log.Error("Instance process failed to start on time.");

        if (!res) StopProcess();
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Stops server instance if it is running.
    /// </summary>
    /// <returns>true if the server was running and it was stopped, false otherwise.</returns>
    public bool Stop()
    {
      log.Trace("()");
      bool res = false;

      if (runningProcess != null)
      {
        log.Trace("Instance process is running, stopping it now.");
        if (StopProcess())
        {
          Uninitialize();
          res = true;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether the server process is running.
    /// </summary>
    /// <returns>true if the server process is running.</returns>
    public bool IsRunningProcess()
    {
      return runningProcess != null;
    }

    /// <summary>
    /// Stops running instance process.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool StopProcess()
    {
      log.Trace("()");

      bool res = false;

      try
      {
        log.Trace("Sending ENTER to instance process.");
        string inputData = Environment.NewLine;
        using (StreamWriter sw = new StreamWriter(runningProcess.StandardInput.BaseStream, Encoding.UTF8))
        {
          sw.Write(inputData);
        }

        if (runningProcess.WaitForExit(20 * 1000))
        {
          res = true;
        }
        else
        {
          log.Error("Instance did not finish on time, killing it now.");
          res = Helpers.KillProcess(runningProcess);
        }

        if (res)
        {
          serverProcessInitializationCompleteEvent.Reset();
          runningProcess = null;
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
    /// Runs the server instance.
    /// </summary>
    /// <returns>Running server process.</returns>
    public Process RunProcess()
    {
      log.Trace("()");

      bool error = false;
      Process process = null;
      bool processIsRunning = false;
      try
      {
        process = new Process();
        process.StartInfo.FileName = Path.Combine(instanceDirectory, GetExecutableFileName());
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.WorkingDirectory = instanceDirectory;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;

        log.Trace("Starting command line: '{0}'", process.StartInfo.FileName);

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += new DataReceivedEventHandler(ProcessOutputHandler);
        process.ErrorDataReceived += new DataReceivedEventHandler(ProcessOutputHandler);

        if (process.Start())
        {
          processIsRunning = true;
        }
        else
        {
          log.Error("New process was not started.");
          error = true;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred during starting: {0}", e.ToString());
        error = true;
      }

      if (!error)
      {
        try
        {
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();
        }
        catch (Exception e)
        {
          log.Error("Exception occurred after start: {0}", e.ToString());
          error = true;
        }
      }

      if (error)
      {
        if (processIsRunning && (process != null))
          Helpers.KillProcess(process);
      }

      Process res = !error ? process : null;
      log.Trace("(-):{0}", res != null ? "Process" : "null");
      return res;
    }


    /// <summary>
    /// Standard output handler for server process.
    /// </summary>
    /// <param name="SendingProcess">Not used.</param>
    /// <param name="OutLine">Line of output without new line character.</param>
    public void ProcessOutputHandler(object SendingProcess, DataReceivedEventArgs OutLine)
    {
      if (OutLine.Data != null)
        ProcessNewOutput(OutLine.Data + Environment.NewLine);
    }

    /// <summary>
    /// Simple analyzer of the server process standard output, 
    /// that can recognize when the server is fully initialized and ready for the test.
    /// </summary>
    /// <param name="Data">Line of output.</param>
    public void ProcessNewOutput(string Data)
    {
      log.Trace("(Data.Length:{0})", Data.Length);
      log.Trace("Data: {0}", Data);

      switch (Type)
      {
        case ServerType.Profile:
          if (Data.Contains("ENTER"))
            serverProcessInitializationCompleteEvent.Set();
          break;

        case ServerType.Proximity:
          if (Data.Contains("Location initialization completed"))
            serverProcessInitializationCompleteEvent.Set();
          break;

        default:
          log.Error("Invalid server type {0}.", Type);
          break;
      }      

      log.Trace("(-)");
    }


    /// <summary>
    /// Sets server's network identifier.
    /// </summary>
    /// <param name="NetworkId">Server's network identifier.</param>
    public void SetNetworkId(byte[] NetworkId)
    {
      log.Trace("(NetworkId:'{0}')", NetworkId.ToHex());

      List<ServerBase> serversToNotify = null;
      lock (internalLock)
      {
        networkId = NetworkId;
        initialized = true;

        if (initializationNeighborhoodNotificationList.Count != 0)
        {
          serversToNotify = initializationNeighborhoodNotificationList.ToList();
          initializationNeighborhoodNotificationList.Clear();
        }
      }

      if (serversToNotify != null)
      {
        log.Debug("Sending neighborhood notification to {0} {1} servers.", serversToNotify.Count, type);
        foreach (ServerBase serverToNotify in serversToNotify)
          serverToNotify.LocServer.AddNeighborhood(new List<ServerBase>() { this });
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Callback routine that calls SetNetworkId once serverProcessInitializationCompleteEvent is finished.
    /// </summary>
    /// <param name="NetworkId">Server's network identifier.</param>
    public void SetNetworkIdCallback(object NetworkId)
    {
      log.Trace("()");

      while (IsRunningProcess())
      {
        if (serverProcessInitializationCompleteEvent.WaitOne(500))
        {
          byte[] networkId = (byte[])NetworkId;
          SetNetworkId(networkId);
          break;
        }
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Sets the server's to uninitialized state.
    /// </summary>
    public void Uninitialize()
    {
      log.Trace("()");

      lock (internalLock)
      {
        initialized = false;
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Obtains server's network identifier.
    /// </summary>
    /// <returns>Server's network ID.</returns>
    public byte[] GetNetworkId()
    {
      log.Trace("()");

      byte[] res = null;

      lock (internalLock)
      {
        res = networkId;
      }

      log.Trace("(-):{0}", res != null ? res.ToHex() : "null");
      return res;
    }

    /// <summary>
    /// Obtains information whether the server has been initialized already.
    /// Initialization means the server has been started and announced its profile to its LOC server.
    /// </summary>
    /// <returns>true if the server is initialized and its profile is known.</returns>
    public bool IsInitialized()
    {
      log.Trace("()");

      bool res = false;

      lock (internalLock)
      {
        res = initialized;
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Acquires internal lock of the server.
    /// </summary>
    public void Lock()
    {
      log.Trace("()");

      Monitor.Enter(internalLock);

      log.Trace("(-)");
    }

    /// <summary>
    /// Releases internal lock of the server.
    /// </summary>
    public void Unlock()
    {
      log.Trace("()");

      Monitor.Exit(internalLock);

      log.Trace("(-)");
    }

    /// <summary>
    /// Returns NodeInfo structure of the server.
    /// </summary>
    /// <returns>NodeInfo structure of the server.</returns>
    public Iop.Locnet.NodeInfo GetNodeInfo()
    {
      log.Trace("()");

      Iop.Locnet.NodeInfo res = null;
      lock (internalLock)
      {
        res = new Iop.Locnet.NodeInfo()
        {
          NodeId = ProtocolHelper.ByteArrayToByteString(new byte[0]),
          Contact = new Iop.Locnet.NodeContact()
          {
            IpAddress = ProtocolHelper.ByteArrayToByteString(ipAddress.GetAddressBytes()),
            ClientPort = (uint)locPort,
            NodePort = (uint)locPort
          },
          Location = nodeLocation,
        };

        Iop.Locnet.ServiceInfo serviceInfo = new Iop.Locnet.ServiceInfo()
        {
          Type = type == ServerType.Profile ? Iop.Locnet.ServiceType.Profile : Iop.Locnet.ServiceType.Proximity,
          Port = (uint)primaryInterfacePort,
          ServiceData = ProtocolHelper.ByteArrayToByteString(networkId)
        };
        res.Services.Add(serviceInfo);
      }

      log.Trace("(-)");
      return res;
    }

    /// <summary>
    /// Installs a notification to sent to the server, for which this server acts as a neighbor.
    /// The notification will be sent as soon as this server starts and performs its profile initialization.
    /// </summary>
    /// <param name="ServerToInform">Server to inform.</param>
    public void InstallInitializationNeighborhoodNotification(ServerBase ServerToInform)
    {
      log.Trace("(ServerToInform.Name:'{0}')", ServerToInform.Name);

      lock (internalLock)
      {
        if (initializationNeighborhoodNotificationList.Add(ServerToInform)) log.Debug("Server '{0}' added to neighborhood notification list of server '{1}'.", ServerToInform.Name, Name);
        else log.Debug("Server '{0}' is already on neighborhood notification list of server '{1}'.", ServerToInform.Name, Name);
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Uninstalls a neighborhood notification.
    /// </summary>
    /// <param name="ServerToInform">Server that was about to be informed, but will not be anymore.</param>
    public void UninstallInitializationNeighborhoodNotification(ServerBase ServerToInform)
    {
      log.Trace("(ServerToInform.Name:'{0}')", ServerToInform.Name);

      lock (internalLock)
      {
        if (initializationNeighborhoodNotificationList.Remove(ServerToInform)) log.Debug("Server '{0}' removed from neighborhood notification list of server '{1}'.", ServerToInform.Name, Name);
        else log.Debug("Server '{0}' not found on the neighborhood notification list of server '{1}' and can't be removed.", ServerToInform.Name, Name);
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Checks log files of server instance to see if there are any errors.
    /// </summary>
    /// <param name="LogDirectory">If the function succeeds, this is filled with the name of the log directory of the server instance.</param>
    /// <param name="FileNames">If the function succeeds, this is filled with log file names.</param>
    /// <param name="ErrorCount">If the function succeeds, this is filled with number of errors found in log files.</param>
    /// <param name="WarningCount">If the function succeeds, this is filled with number of warnings found in log files.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool CheckLogs(out string LogDirectory, out List<string> FileNames, out List<int> ErrorCount, out List<int> WarningCount)
    {
      log.Trace("()");

      FileNames = null;
      ErrorCount = null;
      WarningCount = null;
      LogDirectory = null;

      List<string> fileNames = new List<string>();
      List<int> errorCount = new List<int>();
      List<int> warningCount = new List<int>();
      string logDirectory = Path.Combine(instanceDirectory, "Logs");
      bool error = false;
      try
      {        
        string[] files = Directory.GetFiles(logDirectory, "*.txt", SearchOption.TopDirectoryOnly);
        foreach (string file in files)
        {
          int errCount = 0;
          int warnCount = 0;
          if (CheckLogFile(file, out errCount, out warnCount))
          {
            fileNames.Add(Path.GetFileName(file));
            errorCount.Add(errCount);
            warningCount.Add(warnCount);
          }
          else
          {
            error = true;
            break;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Unable to analyze logs, exception occurred: {0}", e.ToString());
      }

      bool res = !error;
      if (res)
      {
        FileNames = fileNames;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        LogDirectory = instanceDirectory;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks a signle log file of server instance to see if there are any errors.
    /// </summary>
    /// <param name="FileName">Name of the log file to check.</param>
    /// <param name="ErrorCount">If the function succeeds, this is filled with number of errors found in the log file.</param>
    /// <param name="WarningCount">If the function succeeds, this is filled with number of warnings found in the log file.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool CheckLogFile(string FileName, out int ErrorCount, out int WarningCount)
    {
      log.Trace("(FileName:'{0}')", FileName);

      bool res = false;
      ErrorCount = 0;
      WarningCount = 0;
      try
      {
        int errors = 0;
        int warnings = 0;
        string[] lines = File.ReadAllLines(FileName);
        for (int i = 0; i < lines.Length; i++)
        {
          string line = lines[i];
          if (line.Contains("] ERROR:") && (!line.Contains(string.Format("Failed to refresh {0} server's IPNS record", type.ToString().ToLowerInvariant()))))
            errors++;

          if (line.Contains("] WARN:") && (!line.Contains(string.Format("WARN: IopCommon.DbLogger.Log Sensitive data logging is enabled", type))))
            warnings++;
        }

        ErrorCount = errors;
        WarningCount = warnings;
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Unable to analyze logs, exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0},ErrorCount={1},WarningCount={1}", res, ErrorCount, WarningCount);
      return res;
    }
  }
}
