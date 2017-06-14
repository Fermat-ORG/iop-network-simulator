﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkSimulator
{
  /// <summary>
  /// All types of commands that are supported. Note that Unknown represents an invalid command.
  /// </summary>
  public enum CommandType { Unknown, ProfileServer, ProximityServer, StartServer, StopServer, Neighborhood, CancelNeighborhood, Neighbor, CancelNeighbor, Identity, CancelIdentity, Activity, DeleteActivity, TestQuery, TestQueryActivity, Delay, TakeSnapshot, LoadSnapshot, DebugMode }

  /// <summary>
  /// Base class for all types for commands.
  /// </summary>
  public class Command
  {
    /// <summary>Type of the command.</summary>
    public CommandType Type;

    /// <summary>Line number of the command in the scenario file.</summary>
    public int LineNumber;

    /// <summary>Original scenario file line.</summary>
    public string OriginalCommand;

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    /// <param name="Type">Type of the command.</param>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public Command(CommandType Type, int LineNumber, string OriginalCommand)
    {
      this.Type = Type;
      this.LineNumber = LineNumber;
      this.OriginalCommand = OriginalCommand;
    }
  }


  /// <summary>
  /// ProfileServer command creates one or more profile servers with associated LOC server.
  /// </summary>
  public class CommandProfileServer : Command
  {
    /// <summary>Name of the group of the servers.</summary>
    public string GroupName;

    /// <summary>Number of instances to create.</summary>
    public int Count;

    /// <summary>TCP port number from which TCP ports of each profile server and associated LOC servers are to be calculated.</summary>
    public int BasePort;

    /// <summary>GPS latitude in the decimal form of the target area centre.</summary>
    public decimal Latitude;

    /// <summary>GPS longitude in the decimal form of the target area centre.</summary>
    public decimal Longitude;

    /// <summary>Radius in metres that together with Latitude and Longitude specify the target area.</summary>
    public int Radius;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandProfileServer(int LineNumber, string OriginalCommand):
      base(CommandType.ProfileServer, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// ProximityServer command creates one or more proximity servers with associated LOC server.
  /// </summary>
  public class CommandProximityServer : Command
  {
    /// <summary>Name of the group of the servers.</summary>
    public string GroupName;

    /// <summary>Number of instances to create.</summary>
    public int Count;

    /// <summary>TCP port number from which TCP ports of each proximity server and associated LOC servers are to be calculated.</summary>
    public int BasePort;

    /// <summary>GPS latitude in the decimal form of the target area centre.</summary>
    public decimal Latitude;

    /// <summary>GPS longitude in the decimal form of the target area centre.</summary>
    public decimal Longitude;

    /// <summary>Radius in metres that together with Latitude and Longitude specify the target area.</summary>
    public int Radius;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandProximityServer(int LineNumber, string OriginalCommand) :
      base(CommandType.ProximityServer, LineNumber, OriginalCommand)
    {
    }
  }

  /// <summary>
  /// StartServer command starts one or more profile servers.
  /// </summary>
  public class CommandStartServer : Command
  {
    /// <summary>Name of the server group, which servers are going to be started.</summary>
    public string Group;

    /// <summary>Index of the first server from the group.</summary>
    public int Index;

    /// <summary>Number of servers to start.</summary>
    public int Count;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandStartServer(int LineNumber, string OriginalCommand) :
      base(CommandType.StartServer, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// StopServer command stops one or more servers.
  /// </summary>
  public class CommandStopServer : Command
  {
    /// <summary>Name of the server group, which servers are going to be stopped.</summary>
    public string Group;

    /// <summary>Index of the first server from the group.</summary>
    public int Index;

    /// <summary>Number of servers to stop.</summary>
    public int Count;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandStopServer(int LineNumber, string OriginalCommand) :
      base(CommandType.StopServer, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// Neighborhood command forms a bidirectional neighborhood relationship between all servers selected by the command. 
  /// </summary>
  public class CommandNeighborhood : Command
  {
    /// <summary>Names of the groups of servers.</summary>
    public List<string> Groups;

    /// <summary>Instance numbers of the first servers from the groups.</summary>
    public List<int> Indexes;

    /// <summary>Number of servers to take from the groups.</summary>
    public List<int> Counts;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandNeighborhood(int LineNumber, string OriginalCommand):
      base(CommandType.Neighborhood, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// CancelNeighborhood command cancels a bidirectional neighborhood relationship between all servers selected by the command. 
  /// </summary>
  public class CommandCancelNeighborhood : Command
  {
    /// <summary>Names of the groups of servers.</summary>
    public List<string> Groups;

    /// <summary>Instance numbers of the first servers from the groups.</summary>
    public List<int> Indexes;

    /// <summary>Number of servers to take from the groups.</summary>
    public List<int> Counts;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandCancelNeighborhood(int LineNumber, string OriginalCommand) :
      base(CommandType.CancelNeighborhood, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// Neighbor command forms an unidirectional neighborhood relationship between a source server and one or more target servers.
  /// </summary>
  public class CommandNeighbor : Command
  {
    /// <summary>Name of the source server instance.</summary>
    public string Source;

    /// <summary>Names of target servers instances.</summary>
    public List<string> Targets;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandNeighbor(int LineNumber, string OriginalCommand) :
      base(CommandType.Neighbor, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// CancelNeighbor command cancels an unidirectional neighborhood relationship between a source server and one or more target servers.
  /// </summary>
  public class CommandCancelNeighbor : Command
  {
    /// <summary>Name of the source server instance.</summary>
    public string Source;

    /// <summary>Names of target servers instances.</summary>
    public List<string> Targets;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandCancelNeighbor(int LineNumber, string OriginalCommand) :
      base(CommandType.CancelNeighbor, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// Identity command spawns one or more identities.
  /// </summary>
  public class CommandIdentity : Command
  {
    /// <summary>Name of the identity group.</summary>
    public string Name;

    /// <summary>Number of instances to create.</summary>
    public int CreateCount;

    /// <summary>Identity type.</summary>
    public string IdentityType;

    /// <summary>GPS latitude in the decimal form of the target area centre.</summary>
    public decimal Latitude;

    /// <summary>GPS longitude in the decimal form of the target area centre.</summary>
    public decimal Longitude;

    /// <summary>Radius in metres that together with Latitude and Longitude specify the target area.</summary>
    public int Radius;

    /// <summary>File name mask in the image folder that define which images can be randomly selected for identity profiles.</summary>
    public string ProfileImageMask;

    /// <summary>An integer between 0 and 100 that specifies the chance of each instance to have a profile image set.</summary>
    public int ProfileImageChance;

    /// <summary>File name mask in the image folder that define which images can be randomly selected for identity thumbnail images.</summary>
    public string ThumbnailImageMask;

    /// <summary>An integer between 0 and 100 that specifies the chance of each instance to have a thumbnail image set.</summary>
    public int ThumbnailImageChance;

    /// <summary>Name of the server group, which servers are going to host the newly created identities.</summary>
    public string Group;

    /// <summary>Index of the first server from the group.</summary>
    public int Index;

    /// <summary>Number of servers to take from the group.</summary>
    public int Count;


    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandIdentity(int LineNumber, string OriginalCommand) :
      base(CommandType.Identity, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// CancelIdentity command cancels hosting agreements of one or more identities with their profile servers. 
  /// </summary>
  public class CommandCancelIdentity : Command
  {
    /// <summary>Name of the identity group.</summary>
    public string Name;

    /// <summary>Index of the first identity from the group.</summary>
    public int Index;

    /// <summary>Number of identities to take from the group.</summary>
    public int Count;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandCancelIdentity(int LineNumber, string OriginalCommand) :
      base(CommandType.CancelIdentity, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// Activity command creates one or more activities.
  /// </summary>
  public class CommandActivity : Command
  {
    /// <summary>Name of the activity group.</summary>
    public string Name;

    /// <summary>Number of instances to create.</summary>
    public int CreateCount;

    /// <summary>GPS latitude in the decimal form of the target area centre.</summary>
    public decimal Latitude;

    /// <summary>GPS longitude in the decimal form of the target area centre.</summary>
    public decimal Longitude;

    /// <summary>Lower inclusive bound on activity's location precision in metres. This has to be an integer between 0 and 1,000.</summary>
    public int PrecisionMin;

    /// <summary>Upper inclusive bound on activity's location precision in metres. This has to be an integer between 0 and 1,000 and must be greater than or equal to 'PrecisionMin'.</summary>
    public int PrecisionMax;

    /// <summary>Radius in metres that together with Latitude and Longitude specify the target area.</summary>
    public int Radius;

    /// <summary>
    /// Lower inclusive bound on each activity's start time. This is specified in seconds relative to the time when 
    /// the command is processed. It has to be an integer between -86,000 and 86,000.
    /// </summary>
    public int StartTimeFrom;

    /// <summary>
    /// Upper exclusive bound on each activity's start time. This is specified in seconds relative to the time when 
    /// the command is processed. It has to be an integer between -86,000 and 86,000, greater than or equal to 'StartTimeFrom'.
    /// </summary>
    public int StartTimeTo;

    /// <summary>
    /// Lower inclusive bound in seconds on each activity's difference between expiration time and start time. 
    /// Minimal value is 1, maximal value is 86,400. Also 'StartTimeFrom' + 'LifetimeFrom' must be greater than 0.
    /// </summary>
    public int LifetimeFrom;

    /// <summary>
    /// Upper exclusive bound in seconds on each activity's difference between expiration time and start time.
    /// This must be no more than 24 hours - i.e. 86,400 seconds. It has to be greater than or equal to 'LifetimeFrom'
    /// and 'StartTimeTo' + 'LifetimeTo' must be lower than or equal to 86,400.
    /// </summary>
    public int LifetimeTo;

    /// <summary>Name of the identity group, which is used to create the activities.</summary>
    public string Group;

    /// <summary>Index of the first identity from the group. First identity in the group has index 1.</summary>
    public int Index;

    /// <summary>Number of identities to take from the group.</summary>
    public int Count;


    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandActivity(int LineNumber, string OriginalCommand) :
      base(CommandType.Activity, LineNumber, OriginalCommand)
    {
    }
  }

  /// <summary>
  /// DeleteActivity command deletes one or more activities.
  /// </summary>
  public class CommandDeleteActivity : Command
  {
    /// <summary>Name of the activity group.</summary>
    public string Name;

    /// <summary>Index of the first activity from the group.</summary>
    public int Index;

    /// <summary>Number of activities to take from the group.</summary>
    public int Count;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandDeleteActivity(int LineNumber, string OriginalCommand) :
      base(CommandType.DeleteActivity, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// TestQuery command performs one or more profile search queries against specific servers.
  /// </summary>
  public class CommandTestQuery : Command
  {
    /// <summary>Name of the server group, which servers are going to be queried.</summary>
    public string Group;

    /// <summary>Index of the first server from the group.</summary>
    public int Index;

    /// <summary>Number of servers to take from the group.</summary>
    public int Count;

    /// <summary>Wildcard profile name filter for the search query.</summary>
    public string NameFilter;

    /// <summary>Wildcard profile type filter for the search query.</summary>
    public string TypeFilter;

    /// <summary>True if the query should request profile images, false otherwise.</summary>
    public bool IncludeImages;

    /// <summary>GPS latitude in the decimal form of the target area centre.</summary>
    public decimal Latitude;

    /// <summary>GPS longitude in the decimal form of the target area centre.</summary>
    public decimal Longitude;

    /// <summary>If Latitude is not "NO_LOCATION", this is radius in metres that together with Latitude and Longitude specify the target area</summary>
    public int Radius;


    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandTestQuery(int LineNumber, string OriginalCommand) :
      base(CommandType.TestQuery, LineNumber, OriginalCommand)
    {
    }
  }

  /// <summary>
  /// TestQueryActivity command performs one or more activity search queries against specific servers.
  /// </summary>
  public class CommandTestQueryActivity : Command
  {
    /// <summary>Name of the server group, which servers are going to be queried.</summary>
    public string Group;

    /// <summary>Index of the first server from the group.</summary>
    public int Index;

    /// <summary>Number of servers to take from the group.</summary>
    public int Count;

    /// <summary>Wildcard profile type filter for the search query.</summary>
    public string TypeFilter;

    /// <summary>
    /// Upper inclusive bound on activity start time. This is relative time specification in seconds 
    /// to the moment of command execution. It has to be an integer between -86400 and 86400, 
    /// or null if start time filtering is not required.
    /// </summary>
    public int? StartNotAfter;

    /// <summary>
    /// Lower inclusive bound on activity expiration time. This is relative time specification in seconds 
    /// to the moment of command execution. It has to be an integer between -86400 and 86400, 
    /// or null if expiration time filtering is not required. It has to be greater than 'StartNotAfter' 
    /// if both are not null.
    /// </summary>
    public int? ExpirationNotBefore;

    /// <summary>GPS latitude in the decimal form of the target area centre.</summary>
    public decimal Latitude;

    /// <summary>GPS longitude in the decimal form of the target area centre.</summary>
    public decimal Longitude;

    /// <summary>If Latitude is not "NO_LOCATION", this is radius in metres that together with Latitude and Longitude specify the target area</summary>
    public int Radius;


    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandTestQueryActivity(int LineNumber, string OriginalCommand) :
      base(CommandType.TestQueryActivity, LineNumber, OriginalCommand)
    {
    }
  }

  /// <summary>
  /// Delay command waits specified amount of time before executing next command.
  /// </summary>
  public class CommandDelay : Command
  {
    /// <summary>Number of seconds to wait as a positive decimal number.</summary>
    public decimal Seconds;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandDelay(int LineNumber, string OriginalCommand) :
      base(CommandType.Delay, LineNumber, OriginalCommand)
    {
    }
  }

  /// <summary>
  /// TakeSnapshot command saves the current state of the simulation to the directory under the snapshots folder.
  /// </summary>
  public class CommandTakeSnapshot : Command
  {
    /// <summary>Name of the snapshot.</summary>
    public string Name;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandTakeSnapshot(int LineNumber, string OriginalCommand) :
      base(CommandType.TakeSnapshot, LineNumber, OriginalCommand)
    {
    }
  }

  /// <summary>
  /// LoadSnapshot command restores the state of the simulation from the directory under the snapshots folder.
  /// </summary>
  public class CommandLoadSnapshot : Command
  {
    /// <summary>Name of the snapshot.</summary>
    public string Name;

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandLoadSnapshot(int LineNumber, string OriginalCommand) :
      base(CommandType.LoadSnapshot, LineNumber, OriginalCommand)
    {
    }
  }


  /// <summary>
  /// DebugMode command enables or disables debug console outputs.
  /// </summary>
  public class CommandDebugMode : Command
  {
    /// <summary>true to enable, false to disable debug outputs.</summary>
    public bool Enable; 

    /// <summary>
    /// Initializes the base command type.
    /// </summary>
    /// <param name="LineNumber">Line number of the command in the scenario file.</param>
    /// <param name="OriginalCommand">Original scenario file line.</param>
    public CommandDebugMode(int LineNumber, string OriginalCommand) :
      base(CommandType.DebugMode, LineNumber, OriginalCommand)
    {
    }
  }

}
