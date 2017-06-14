using IopCommon;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NetworkSimulator
{
  /// <summary>
  /// Parser of commands given in a scenario. 
  /// </summary>
  public static class CommandParser
  {
    private static Logger log = new Logger("NetworkSimulator.CommandParser");

    /// <summary>Maximal radius in metres.</summary>
    public const int MaxRadius = 20000000;

    /// <summary>
    /// Reads contents of a scenario file and parses its commands.
    /// </summary>
    /// <param name="ScenarioFile">Name of the scenario file to parse.</param>
    /// <returns>List of commands.</returns>
    public static List<Command> ParseScenarioFile(string ScenarioFile)
    {
      log.Trace("(ScenarioFile:'{0}')", ScenarioFile);

      List<Command> res = null;
      try
      {
        if (File.Exists(ScenarioFile))
        {
          string[] lines = File.ReadAllLines(ScenarioFile);
          res = ParseScenario(lines);
        }
        else log.Error("Unable to find scenario file '{0}'.", ScenarioFile);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (res != null) log.Trace("(-):*.Count={0}", res.Count);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Parses lines of a scenario file.
    /// </summary>
    /// <param name="Lines">List of scenario lines.</param>
    /// <returns>List of commands.</returns>
    public static List<Command> ParseScenario(string[] Lines)
    {
      log.Trace("()");

      bool error = false;
      List<Command> commands = new List<Command>();
      if ((Lines != null) && (Lines.Length > 0))
      {
        int lineNumber = 0;
        try
        {
          foreach (string aline in Lines)
          {
            lineNumber++;
            string line = aline.Trim();
            if ((line.Length == 0) || (line[0] == '#')) continue;

            string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Command command = ParseCommand(parts, lineNumber);
            if (command == null)
            {
              error = true;
              break;
            }

            commands.Add(command);
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred while parsing line number {0}: {1}", lineNumber, e.ToString());
          error = true;
        }
      }
      else
      {
        log.Error("Scenario file is empty.");
        error = true;
      }

      List<Command> res = null;
      if (!error)
        res = commands;

      if (res != null) log.Trace("(-):*.Count={0}", res.Count);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Parses a single line of a scenario file.
    /// </summary>
    /// <param name="Parts">List of tokens of the line being parsed.</param>
    /// <param name="LineNumber">Line number of the line being parsed.</param>
    /// <returns>Initialized command object.</returns>
    public static Command ParseCommand(string[] Parts, int LineNumber)
    {
      log.Trace("(Parse:'{0}',LineNumber:{1})", string.Join(" ", Parts), LineNumber);

      Command res = null;
      CommandType commandType;
      if (!Enum.TryParse(Parts[0], out commandType))
        commandType = CommandType.Unknown;

      int paramCount = Parts.Length - 1;
      int p = 1;
      string line = string.Join(" ", Parts);

      switch (commandType)
      {
        case CommandType.ProfileServer:
          {
            if (paramCount != 6)
            {
              log.Error("ProfileServer requires 6 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandProfileServer command = new CommandProfileServer(LineNumber, line)
            {
              GroupName = Parts[p++],
              Count = int.Parse(Parts[p++]),
              BasePort = int.Parse(Parts[p++]),
              Latitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Longitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Radius = int.Parse(Parts[p++])
            };

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Count, LineNumber);
              break;
            }

            int basePortUpperLimit = 65535 - 20 * command.Count;
            bool basePortValid = (1 <= command.BasePort) && (command.BasePort <= basePortUpperLimit);
            if (!basePortValid)
            {
              log.Error("Having Count {0}, BasePort '{1}' on line {2} is invalid. It must be an integer between 1 and {3}.", command.Count, command.BasePort, LineNumber, basePortUpperLimit);
              break;
            }

            bool latitudeValid = new GpsLocation(command.Latitude, 0).IsValid();
            if (!latitudeValid)
            {
              log.Error("Latitude '{0}' on line {1} is invalid. It must be a decimal number between -90 and 90.", command.Latitude, LineNumber);
              break;
            }

            bool longitudeValid = new GpsLocation(0, command.Longitude).IsValid();
            if (!longitudeValid)
            {
              log.Error("Longitude '{0}' on line {1}. It must be a decimal number between -179.999999 and 180.", command.Longitude, LineNumber);
              break;
            }

            bool radiusValid = (0 <= command.Radius) && (command.Radius <= MaxRadius);
            if (!radiusValid)
            {
              log.Error("Radius '{0}' given on line {1}. It must be an integer between 0 and {2}.", command.Radius, LineNumber, MaxRadius);
              break;
            }

            res = command;
            break;
          }


        case CommandType.ProximityServer:
          {
            if (paramCount != 6)
            {
              log.Error("ProximityServer requires 6 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandProximityServer command = new CommandProximityServer(LineNumber, line)
            {
              GroupName = Parts[p++],
              Count = int.Parse(Parts[p++]),
              BasePort = int.Parse(Parts[p++]),
              Latitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Longitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Radius = int.Parse(Parts[p++])
            };

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Count, LineNumber);
              break;
            }

            int basePortUpperLimit = 65535 - 20 * command.Count;
            bool basePortValid = (1 <= command.BasePort) && (command.BasePort <= basePortUpperLimit);
            if (!basePortValid)
            {
              log.Error("Having Count {0}, BasePort '{1}' on line {2} is invalid. It must be an integer between 1 and {3}.", command.Count, command.BasePort, LineNumber, basePortUpperLimit);
              break;
            }

            bool latitudeValid = new GpsLocation(command.Latitude, 0).IsValid();
            if (!latitudeValid)
            {
              log.Error("Latitude '{0}' on line {1} is invalid. It must be a decimal number between -90 and 90.", command.Latitude, LineNumber);
              break;
            }

            bool longitudeValid = new GpsLocation(0, command.Longitude).IsValid();
            if (!longitudeValid)
            {
              log.Error("Longitude '{0}' on line {1}. It must be a decimal number between -179.999999 and 180.", command.Longitude, LineNumber);
              break;
            }

            bool radiusValid = (0 <= command.Radius) && (command.Radius <= MaxRadius);
            if (!radiusValid)
            {
              log.Error("Radius '{0}' given on line {1}. It must be an integer between 0 and {2}.", command.Radius, LineNumber, MaxRadius);
              break;
            }

            res = command;
            break;
          }


        case CommandType.StartServer:
          {
            if (paramCount != 3)
            {
              log.Error("StartServer requires 3 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandStartServer command = new CommandStartServer(LineNumber, line)
            {
              Group = Parts[p++],
              Index = int.Parse(Parts[p++]),
              Count = int.Parse(Parts[p++])
            };

            bool indexValid = (1 <= command.Index) && (command.Index <= 999);
            if (!indexValid)
            {
              log.Error("Index '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Index, LineNumber);
              break;
            }

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Count, LineNumber);
              break;
            }

            countValid = command.Index + command.Count <= 999;
            if (!countValid)
            {
              log.Error("Having Index '{0}', Count '{1}' on line {2} is invalid. Index + Count must not be greater than 999.", command.Index, command.Count, LineNumber);
              break;
            }

            res = command;
            break;
          }

        case CommandType.StopServer:
          {
            if (paramCount != 3)
            {
              log.Error("StopServer requires 3 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandStopServer command = new CommandStopServer(LineNumber, line)
            {
              Group = Parts[p++],
              Index = int.Parse(Parts[p++]),
              Count = int.Parse(Parts[p++])
            };

            bool indexValid = (1 <= command.Index) && (command.Index <= 999);
            if (!indexValid)
            {
              log.Error("Index '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Index, LineNumber);
              break;
            }

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Count, LineNumber);
              break;
            }

            countValid = command.Index + command.Count <= 999;
            if (!countValid)
            {
              log.Error("Having Index '{0}', Count '{1}' on line {2} is invalid. Index + Count must not be greater than 999.", command.Index, command.Count, LineNumber);
              break;
            }

            res = command;
            break;
          }

        case CommandType.Neighborhood:
          {
            if ((paramCount % 3) != 0)
            {
              log.Error("Neighborhood requires 3*N parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandNeighborhood command = new CommandNeighborhood(LineNumber, line)
            {
              Groups = new List<string>(),
              Indexes = new List<int>(),
              Counts = new List<int>()
            };

            for (int i = 0; i < paramCount; i += 3)
            {
              command.Groups.Add(Parts[p++]);
              command.Indexes.Add(int.Parse(Parts[p++]));
              command.Counts.Add(int.Parse(Parts[p++]));

              int groupNo = (i / 3) + 1;
              int groupIndex = groupNo - 1;

              bool indexValid = (1 <= command.Indexes[groupIndex]) && (command.Indexes[groupIndex] <= 999);
              if (!indexValid)
              {
                log.Error("Index${0} '{1}' on line {2} is invalid. It must be an integer between 1 and 999.", groupNo, command.Indexes[groupIndex], LineNumber);
                break;
              }

              bool countValid = (1 <= command.Counts[groupIndex]) && (command.Counts[groupIndex] <= 999);
              if (!countValid)
              {
                log.Error("Count${0} '{1}' on line {2} is invalid. It must be an integer between 1 and 999.", groupNo, command.Counts[groupIndex], LineNumber);
                break;
              }

              countValid = command.Indexes[groupIndex] + command.Counts[groupIndex] <= 999;
              if (!countValid)
              {
                log.Error("Having Index${0} '{1}', Count${0} '{2}' on line {3} is invalid. Index$i + Count$i must not be greater than 999.", groupNo, command.Indexes[groupIndex], command.Counts[groupIndex], LineNumber);
                break;
              }
            }

            res = command;
            break;
          }

        case CommandType.CancelNeighborhood:
          {
            if ((paramCount % 3) != 0)
            {
              log.Error("CancelNeighborhood requires 3*N parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandCancelNeighborhood command = new CommandCancelNeighborhood(LineNumber, line)
            {
              Groups = new List<string>(),
              Indexes = new List<int>(),
              Counts = new List<int>()
            };

            for (int i = 0; i < paramCount; i += 3)
            {
              command.Groups.Add(Parts[p++]);
              command.Indexes.Add(int.Parse(Parts[p++]));
              command.Counts.Add(int.Parse(Parts[p++]));

              int groupNo = (i / 3) + 1;

              bool indexValid = (1 <= command.Indexes[i]) && (command.Indexes[i] <= 999);
              if (!indexValid)
              {
                log.Error("Index${0} '{1}' on line {2} is invalid. It must be an integer between 1 and 999.", groupNo, command.Indexes[i], LineNumber);
                break;
              }

              bool countValid = (1 <= command.Counts[i]) && (command.Counts[i] <= 999);
              if (!countValid)
              {
                log.Error("Count${0} '{1}' on line {2} is invalid. It must be an integer between 1 and 999.", groupNo, command.Counts[i], LineNumber);
                break;
              }

              countValid = command.Indexes[i] + command.Counts[i] <= 999;
              if (!countValid)
              {
                log.Error("Having Index${0} '{1}', Count{0} '{2}' on line {3} is invalid. Index$i + Count$i must not be greater than 999.", groupNo, command.Indexes[i], command.Counts[i], LineNumber);
                break;
              }
            }

            res = command;
            break;
          }

        case CommandType.Neighbor:
          {
            if (paramCount < 2)
            {
              log.Error("Neighbor requires 2 or more parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandNeighbor command = new CommandNeighbor(LineNumber, line)
            {
              Source = Parts[p++],
              Targets = new List<string>()
            };

            for (int i = 0; i < paramCount - 1; i++)
              command.Targets.Add(Parts[p++]);

            res = command;
            break;
          }

        case CommandType.CancelNeighbor:
          {
            if (paramCount < 2)
            {
              log.Error("CancelNeighbor requires 2 or more parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandCancelNeighbor command = new CommandCancelNeighbor(LineNumber, line)
            {
              Source = Parts[p++],
              Targets = new List<string>()
            };

            for (int i = 0; i < paramCount - 1; i++)
              command.Targets.Add(Parts[p++]);

            res = command;
            break;
          }

        case CommandType.Identity:
          {
            if (paramCount != 13)
            {
              log.Error("Identity requires 13 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandIdentity command = new CommandIdentity(LineNumber, line)
            {
              Name = Parts[p++],
              CreateCount = int.Parse(Parts[p++]),
              IdentityType = Parts[p++],
              Latitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Longitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Radius = int.Parse(Parts[p++]),
              ProfileImageMask = Parts[p++],
              ProfileImageChance = int.Parse(Parts[p++]),
              ThumbnailImageMask = Parts[p++],
              ThumbnailImageChance = int.Parse(Parts[p++]),
              Group = Parts[p++],
              Index = int.Parse(Parts[p++]),
              Count = int.Parse(Parts[p++])
            };


            bool createCountValid = (1 <= command.CreateCount) && (command.CreateCount <= 99999);
            if (!createCountValid)
            {
              log.Error("CreateCount '{0}' on line {1} is invalid. It must be an integer between 1 and 99999.", command.CreateCount, LineNumber);
              break;
            }

            bool latitudeValid = new GpsLocation(command.Latitude, 0).IsValid();
            if (!latitudeValid)
            {
              log.Error("Latitude '{0}' on line {1} is invalid. It must be a decimal number between -90 and 90.", command.Latitude, LineNumber);
              break;
            }

            bool longitudeValid = new GpsLocation(0, command.Longitude).IsValid();
            if (!longitudeValid)
            {
              log.Error("Longitude '{0}' on line {1}. It must be a decimal number between -179.999999 and 180.", command.Longitude, LineNumber);
              break;
            }

            bool radiusValid = (0 <= command.Radius) && (command.Radius <= MaxRadius);
            if (!radiusValid)
            {
              log.Error("Radius '{0}' given on line {1}. It must be an integer between 0 and {2}.", command.Radius, LineNumber, MaxRadius);
              break;
            }

            bool profileImageChance = (0 <= command.ProfileImageChance) && (command.ProfileImageChance <= 100);
            if (!profileImageChance)
            {
              log.Error("ProfileImageChance '{0}' on line {1} is invalid. It must be an integer between 0 and 100.", command.ProfileImageChance, LineNumber);
              break;
            }

            bool thumbnailImageChance = (0 <= command.ThumbnailImageChance) && (command.ThumbnailImageChance <= 100);
            if (!thumbnailImageChance)
            {
              log.Error("ThumbnailImageChance '{0}' on line {1} is invalid. It must be an integer between 0 and 100.", command.ThumbnailImageChance, LineNumber);
              break;
            }

            bool indexValid = (1 <= command.Index) && (command.Index <= 999);
            if (!indexValid)
            {
              log.Error("Index '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Index, LineNumber);
              break;
            }

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Count, LineNumber);
              break;
            }

            countValid = command.Index + command.Count <= 999;
            if (!countValid)
            {
              log.Error("Having Index '{0}', Count '{1}' on line {2} is invalid. Index + Count must not be greater than 999.", command.Index, command.Count, LineNumber);
              break;
            }

            createCountValid = command.CreateCount - command.Count * 20000 <= 0;
            if (!createCountValid)
            {
              log.Error("Having Count '{0}', Count '{1}' on line {2} is invalid. CreateCount / Count must not be greater than 20000.", command.Count, command.CreateCount, LineNumber);
              break;
            }

            res = command;
            break;
          }


        case CommandType.CancelIdentity:
          {
            if (paramCount != 3)
            {
              log.Error("CancelIdentity requires 3 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandCancelIdentity command = new CommandCancelIdentity(LineNumber, line)
            {
              Name = Parts[p++],
              Index = int.Parse(Parts[p++]),
              Count = int.Parse(Parts[p++])
            };

            bool indexValid = (1 <= command.Index) && (command.Index <= 99999);
            if (!indexValid)
            {
              log.Error("Index '{0}' on line {1} is invalid. It must be an integer between 1 and 99999.", command.Index, LineNumber);
              break;
            }

            bool countValid = (1 <= command.Count) && (command.Count <= 99999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 99999.", command.Count, LineNumber);
              break;
            }

            countValid = command.Index + command.Count <= 99999;
            if (!countValid)
            {
              log.Error("Having Index '{0}', Count '{1}' on line {2} is invalid. Index + Count must not be greater than 99999.", command.Index, command.Count, LineNumber);
              break;
            }

            res = command;
            break;
          }


        case CommandType.Activity:
          {
            if (paramCount != 14)
            {
              log.Error("Activity requires 14 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandActivity command = new CommandActivity(LineNumber, line)
            {
              Name = Parts[p++],
              CreateCount = int.Parse(Parts[p++]),
              Latitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Longitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              PrecisionMin = int.Parse(Parts[p++]),
              PrecisionMax = int.Parse(Parts[p++]),
              Radius = int.Parse(Parts[p++]),
              StartTimeFrom = int.Parse(Parts[p++]),
              StartTimeTo = int.Parse(Parts[p++]),
              LifetimeFrom = int.Parse(Parts[p++]),
              LifetimeTo = int.Parse(Parts[p++]),
              Group = Parts[p++],
              Index = int.Parse(Parts[p++]),
              Count = int.Parse(Parts[p++])
            };


            bool createCountValid = (1 <= command.CreateCount) && (command.CreateCount <= 50000);
            if (!createCountValid)
            {
              log.Error("CreateCount '{0}' on line {1} is invalid. It must be an integer between 1 and 50000.", command.CreateCount, LineNumber);
              break;
            }

            bool latitudeValid = new GpsLocation(command.Latitude, 0).IsValid();
            if (!latitudeValid)
            {
              log.Error("Latitude '{0}' on line {1} is invalid. It must be a decimal number between -90 and 90.", command.Latitude, LineNumber);
              break;
            }

            bool longitudeValid = new GpsLocation(0, command.Longitude).IsValid();
            if (!longitudeValid)
            {
              log.Error("Longitude '{0}' on line {1}. It must be a decimal number between -179.999999 and 180.", command.Longitude, LineNumber);
              break;
            }

            bool precisionMinValid = (0 <= command.PrecisionMin) && (command.PrecisionMin <= 1000);
            if (!precisionMinValid)
            {
              log.Error("PrecisionMin '{0}' on line {1} is invalid. It must be an integer between 0 and 1000.", command.PrecisionMin, LineNumber);
              break;
            }

            bool precisionMaxValid = (0 <= command.PrecisionMax) && (command.PrecisionMax <= 1000);
            if (!precisionMaxValid)
            {
              log.Error("PrecisionMax '{0}' on line {1} is invalid. It must be an integer between 0 and 1000.", command.PrecisionMax, LineNumber);
              break;
            }

            precisionMaxValid = (0 <= command.PrecisionMax) && (command.PrecisionMax <= 1000);
            if (!precisionMaxValid)
            {
              log.Error("PrecisionMax '{0}' on line {1} is invalid. It must be greater than or equal to PrecisionMin ({2}).", command.PrecisionMax, LineNumber, command.PrecisionMin);
              break;
            }

            bool radiusValid = (0 <= command.Radius) && (command.Radius <= MaxRadius);
            if (!radiusValid)
            {
              log.Error("Radius '{0}' given on line {1}. It must be an integer between 0 and {2}.", command.Radius, LineNumber, MaxRadius);
              break;
            }

            bool startTimeFromValid = (-86000 <= command.StartTimeFrom) && (command.StartTimeFrom <= 86000);
            if (!startTimeFromValid)
            {
              log.Error("StartTimeFrom '{0}' on line {1} is invalid. It must be an integer between -86000 and 86000.", command.StartTimeFrom, LineNumber);
              break;
            }

            bool startTimeToValid = (-86000 <= command.StartTimeTo) && (command.StartTimeTo <= 86000);
            if (!startTimeToValid)
            {
              log.Error("StartTimeTo '{0}' on line {1} is invalid. It must be an integer between -86000 and 86000.", command.StartTimeTo, LineNumber);
              break;
            }

            startTimeToValid = command.StartTimeFrom <= command.StartTimeTo;
            if (!startTimeToValid)
            {
              log.Error("StartTimeTo '{0}' on line {1} is invalid. It must be greater than or equal to StartTimeFrom value {2}.", command.StartTimeTo, LineNumber, command.StartTimeFrom);
              break;
            }

            bool lifetimeFromValid = (1 <= command.LifetimeFrom) && (command.LifetimeFrom <= 86400);
            if (!lifetimeFromValid)
            {
              log.Error("LifetimeFrom '{0}' on line {1} is invalid. It must be an integer between 1 and 86400.", command.LifetimeFrom, LineNumber);
              break;
            }

            lifetimeFromValid = command.StartTimeFrom + command.LifetimeFrom > 0;
            if (!lifetimeFromValid)
            {
              log.Error("LifetimeFrom '{0}' on line {1} is invalid. LifetimeFrom + StartTimeFrom ({2}) must be greater than 0.", command.LifetimeFrom, LineNumber, command.StartTimeFrom);
              break;
            }

            bool lifetimeToValid = (1 <= command.LifetimeTo) && (command.LifetimeTo <= 86400);
            if (!lifetimeToValid)
            {
              log.Error("LifetimeTo '{0}' on line {1} is invalid. It must be an integer between 1 and 86400.", command.LifetimeTo, LineNumber);
              break;
            }

            lifetimeToValid = command.LifetimeTo >= command.LifetimeFrom;
            if (!lifetimeToValid)
            {
              log.Error("LifetimeTo '{0}' on line {1} is invalid. It must be greater than or equal to LifetimeFrom value {2}.", command.LifetimeTo, LineNumber, command.LifetimeFrom);
              break;
            }

            lifetimeToValid = command.StartTimeTo + command.LifetimeTo <= 86400;
            if (!lifetimeToValid)
            {
              log.Error("LifetimeTo '{0}' on line {1} is invalid. LifetimeTo + StartTimeTo ({2}) must be lower than or equal to 86400.", command.LifetimeTo, LineNumber, command.StartTimeTo);
              break;
            }

            bool indexValid = (1 <= command.Index) && (command.Index <= 999);
            if (!indexValid)
            {
              log.Error("Index '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Index, LineNumber);
              break;
            }

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Count, LineNumber);
              break;
            }

            countValid = command.Index + command.Count <= 999;
            if (!countValid)
            {
              log.Error("Having Index '{0}', Count '{1}' on line {2} is invalid. Index + Count must not be greater than 999.", command.Index, command.Count, LineNumber);
              break;
            }

            res = command;
            break;
          }


        case CommandType.DeleteActivity:
          {
            if (paramCount != 3)
            {
              log.Error("DeleteActivity requires 3 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandDeleteActivity command = new CommandDeleteActivity(LineNumber, line)
            {
              Name = Parts[p++],
              Index = int.Parse(Parts[p++]),
              Count = int.Parse(Parts[p++])
            };

            bool indexValid = (1 <= command.Index) && (command.Index <= 50000);
            if (!indexValid)
            {
              log.Error("Index '{0}' on line {1} is invalid. It must be an integer between 1 and 50000.", command.Index, LineNumber);
              break;
            }

            bool countValid = (1 <= command.Count) && (command.Count <= 50000);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 50000.", command.Count, LineNumber);
              break;
            }

            countValid = command.Index + command.Count <= 50000;
            if (!countValid)
            {
              log.Error("Having Index '{0}', Count '{1}' on line {2} is invalid. Index + Count must not be greater than 50000.", command.Index, command.Count, LineNumber);
              break;
            }

            res = command;
            break;
          }

        case CommandType.TestQuery:
          {
            if (paramCount != 9)
            {
              log.Error("TestQuery requires 9 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            int li = p + 6;
            string latStr = Parts[li++];
            string lonStr = Parts[li++];
            string radStr = Parts[li++];

            decimal latitude = GpsLocation.NoLocation.Latitude;
            decimal longitude = GpsLocation.NoLocation.Longitude;
            int radius = 0;

            bool noLocation = latStr == "NO_LOCATION";
            if (!noLocation)
            {
              latitude = decimal.Parse(latStr, CultureInfo.InvariantCulture);
              longitude = decimal.Parse(lonStr, CultureInfo.InvariantCulture);
              radius = int.Parse(radStr);
            }

            CommandTestQuery command = new CommandTestQuery(LineNumber, line)
            {
              Group = Parts[p++],
              Index = int.Parse(Parts[p++]),
              Count = int.Parse(Parts[p++]),
              NameFilter = Parts[p++],
              TypeFilter = Parts[p++],
              IncludeImages = bool.Parse(Parts[p++]),
              Latitude = latitude,
              Longitude = longitude,
              Radius = radius
            };


            bool indexValid = (1 <= command.Index) && (command.Index <= 999);
            if (!indexValid)
            {
              log.Error("Index '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Index, LineNumber);
              break;
            }

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Count, LineNumber);
              break;
            }

            countValid = command.Index + command.Count <= 999;
            if (!countValid)
            {
              log.Error("Having Index '{0}', Count '{1}' on line {2} is invalid. Index + Count must not be greater than 999.", command.Index, command.Count, LineNumber);
              break;
            }

            if (!noLocation)
            {
              bool latitudeValid = new GpsLocation(command.Latitude, 0).IsValid();
              if (!latitudeValid)
              {
                log.Error("Latitude '{0}' on line {1} is invalid. It must be a decimal number between -90 and 90.", command.Latitude, LineNumber);
                break;
              }

              bool longitudeValid = new GpsLocation(0, command.Longitude).IsValid();
              if (!longitudeValid)
              {
                log.Error("Longitude '{0}' on line {1}. It must be a decimal number between -179.999999 and 180.", command.Longitude, LineNumber);
                break;
              }

              bool radiusValid = (0 <= command.Radius) && (command.Radius <= MaxRadius);
              if (!radiusValid)
              {
                log.Error("Radius '{0}' given on line {1}. It must be an integer between 0 and {2}.", command.Radius, LineNumber, MaxRadius);
                break;
              }
            }

            res = command;
            break;
          }



        case CommandType.TestQueryActivity:
          {
            if (paramCount != 9)
            {
              log.Error("TestQueryActivity requires 9 parameters, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            int li = p + 4;
            string snaStr = Parts[li++];
            string enbStr = Parts[li++];
            string latStr = Parts[li++];
            string lonStr = Parts[li++];
            string radStr = Parts[li++];

            bool anyStart = snaStr == "ANY";
            bool anyExpiration = enbStr == "ANY";

            int? startNotAfter = null;
            if (!anyStart) startNotAfter = int.Parse(snaStr);

            int? expirationNotBefore = null;
            if (!anyExpiration) expirationNotBefore = int.Parse(enbStr);


            decimal latitude = GpsLocation.NoLocation.Latitude;
            decimal longitude = GpsLocation.NoLocation.Longitude;
            int radius = 0;

            bool noLocation = latStr == "NO_LOCATION";
            if (!noLocation)
            {
              latitude = decimal.Parse(latStr, CultureInfo.InvariantCulture);
              longitude = decimal.Parse(lonStr, CultureInfo.InvariantCulture);
              radius = int.Parse(radStr);
            }


            CommandTestQueryActivity command = new CommandTestQueryActivity(LineNumber, line)
            {
              Group = Parts[p++],
              Index = int.Parse(Parts[p++]),
              Count = int.Parse(Parts[p++]),
              TypeFilter = Parts[p++],
              StartNotAfter = startNotAfter,
              ExpirationNotBefore = expirationNotBefore,
              Latitude = latitude,
              Longitude = longitude,
              Radius = radius
            };


            bool indexValid = (1 <= command.Index) && (command.Index <= 999);
            if (!indexValid)
            {
              log.Error("Index '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Index, LineNumber);
              break;
            }

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count '{0}' on line {1} is invalid. It must be an integer between 1 and 999.", command.Count, LineNumber);
              break;
            }

            countValid = command.Index + command.Count <= 999;
            if (!countValid)
            {
              log.Error("Having Index '{0}', Count '{1}' on line {2} is invalid. Index + Count must not be greater than 999.", command.Index, command.Count, LineNumber);
              break;
            }

            if (command.StartNotAfter != null)
            {
              bool startNotAfterValid = (-86400 <= command.StartNotAfter) && (command.StartNotAfter <= 86400);
              if (!startNotAfterValid)
              {
                log.Error("StartNotAfter '{0}' on line {1} is invalid. It must be an integer between -86400 and 86400.", command.StartNotAfter, LineNumber);
                break;
              }
            }

            if (command.ExpirationNotBefore != null)
            {
              bool expirationNotBeforeValid = (-86400 <= command.ExpirationNotBefore) && (command.ExpirationNotBefore <= 86400);
              if (!expirationNotBeforeValid)
              {
                log.Error("ExpirationNotBefore '{0}' on line {1} is invalid. It must be an integer between -86400 and 86400.", command.ExpirationNotBefore, LineNumber);
                break;
              }

              if (command.StartNotAfter != null)
              {
                expirationNotBeforeValid = command.StartNotAfter < command.ExpirationNotBefore;
                if (!expirationNotBeforeValid)
                {
                  log.Error("ExpirationNotBefore '{0}' on line {1} is invalid. It must be greater than StartNotAfter ({2}).", command.ExpirationNotBefore, LineNumber, command.StartNotAfter);
                  break;
                }
              }
            }


            if (!noLocation)
            {
              bool latitudeValid = new GpsLocation(command.Latitude, 0).IsValid();
              if (!latitudeValid)
              {
                log.Error("Latitude '{0}' on line {1} is invalid. It must be a decimal number between -90 and 90.", command.Latitude, LineNumber);
                break;
              }

              bool longitudeValid = new GpsLocation(0, command.Longitude).IsValid();
              if (!longitudeValid)
              {
                log.Error("Longitude '{0}' on line {1}. It must be a decimal number between -179.999999 and 180.", command.Longitude, LineNumber);
                break;
              }

              bool radiusValid = (0 <= command.Radius) && (command.Radius <= MaxRadius);
              if (!radiusValid)
              {
                log.Error("Radius '{0}' given on line {1}. It must be an integer between 0 and {2}.", command.Radius, LineNumber, MaxRadius);
                break;
              }
            }

            res = command;
            break;
          }





        case CommandType.Delay:
          {
            if (paramCount != 1)
            {
              log.Error("Delay requires 1 parameter, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandDelay command = new CommandDelay(LineNumber, line)
            {
              Seconds = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture)
            };


            bool secondsValid = command.Seconds > 0;
            if (!secondsValid)
            {
              log.Error("Seconds '{0}' on line {1} is invalid. It must be a positive decimal number.", command.Seconds);
              break;
            }

            res = command;
            break;
          }


        case CommandType.TakeSnapshot:
          {
            if (paramCount != 1)
            {
              log.Error("TakeSnapshot requires 1 parameter, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandTakeSnapshot command = new CommandTakeSnapshot(LineNumber, line)
            {
              Name = Parts[p++]
            };


            res = command;
            break;
          }


        case CommandType.LoadSnapshot:
          {
            if (paramCount != 1)
            {
              log.Error("LoadSnapshot requires 1 parameter, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandLoadSnapshot command = new CommandLoadSnapshot(LineNumber, line)
            {
              Name = Parts[p++]
            };


            res = command;
            break;
          }

        case CommandType.DebugMode:
          {
            if (paramCount != 1)
            {
              log.Error("DebugMode requires 1 parameter, but {0} parameters found on line {1}.", paramCount, LineNumber);
              break;
            }

            string enable = Parts[p++].ToLowerInvariant();
            CommandDebugMode command = new CommandDebugMode(LineNumber, line)
            {
              Enable = enable == "on"
            };

            bool enableValid = (enable == "on") || (enable == "off");
            if (!enableValid)
            {
              log.Error("Enable on line {0} is invalid. It must be either 'on' or 'off'.", enable);
              break;
            }


            res = command;
            break;
          }

        default:
          log.Error("Invalid command '{0}' on line number {1}.", Parts[0], LineNumber);
          break;
      }


      log.Trace("(-):{0}", res != null ? "Command" : "null");
      return res;
    }
  }
}
