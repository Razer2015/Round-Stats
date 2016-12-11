using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.Net;
using System.Web;
using System.Data;
using System.Threading;
using System.Timers;
using System.Diagnostics;
using System.Reflection;

using PRoCon.Core;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;
using PRoCon.Core.Players;
using PRoCon.Core.Players.Items;
using PRoCon.Core.Battlemap;
using PRoCon.Core.Maps;

using System.Windows.Forms;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using System.Linq;

namespace PRoConEvents
{
    /// <summary>
    /// Delegate for decoding an encoded HTML
    /// </summary>
    /// <param name="encoded"></param>
    /// <returns></returns>
    public delegate String HTMLDecode(String encoded);

    public class CRoundStats : PRoConPluginAPI, IPRoConPluginInterface
    {
        private bool fIsEnabled;
        private int fDebugLevel;

        public String server_host = String.Empty;
        public String server_port = String.Empty;
        public String server_name = String.Empty;
        public String server_desc = String.Empty;

        private BattlelogClient BClient = new BattlelogClient();

        private List<CPlayerInfo> PlayersList = new List<CPlayerInfo>();
        private Dictionary<string, Stats> PlayerStats = new Dictionary<string, Stats>();
        private Dictionary<string, Stats> PlayerStatsEnd = new Dictionary<string, Stats>();
        private List<Stats> LastRoundStats = new List<Stats>();

        static readonly string[,] statsMatches = new string[,]
        {
                    { "heals", "heal", "Heals", "Healer", "%soldier% did %value% Heals in the last round." },
                    { "revives", "revive", "Revives", "Reviver", "%soldier% did %value% Revives in the last round."},
                    { "repairs", "repair", "Repairs", "Repairer", "%soldier% did %value% Repairs in the last round." },
                    { "resupplies", "resupply", "Resupplies", "Resupplier", "%soldier% did %value% Resupplies in the last round." }
        };
        DataContainer statsContainer = new DataContainer(statsMatches);

        private int m_waitOnRoundOver = 15;

        public CRoundStats()
        {
            fIsEnabled = false;
            fDebugLevel = 2;
        }

        #region Helpers
        public enum MessageType { Warning, Error, Exception, Normal };

        public String FormatMessage(String msg, MessageType type)
        {
            String prefix = "[^bDebug Logging^n] ";

            if (type.Equals(MessageType.Warning))
                prefix += "^1^bWARNING^0^n: ";
            else if (type.Equals(MessageType.Error))
                prefix += "^1^bERROR^0^n: ";
            else if (type.Equals(MessageType.Exception))
                prefix += "^1^bEXCEPTION^0^n: ";

            return prefix + msg;
        }

        public void LogWrite(String msg)
        {
            this.ExecuteCommand("procon.protected.pluginconsole.write", msg);
        }

        public void ConsoleWrite(string msg, MessageType type)
        {
            LogWrite(FormatMessage(msg, type));
        }
        public void ConsoleWrite(string msg)
        {
            ConsoleWrite(msg, MessageType.Normal);
        }
        public void ConsoleWarn(String msg)
        {
            ConsoleWrite(msg, MessageType.Warning);
        }
        public void ConsoleError(String msg)
        {
            ConsoleWrite(msg, MessageType.Error);
        }
        public void ConsoleException(String msg)
        {
            ConsoleWrite(msg, MessageType.Exception);
        }

        public void DebugWrite(string msg, int level)
        {
            if (fDebugLevel >= level) ConsoleWrite(msg, MessageType.Normal);
        }

        public void ServerCommand(params String[] args)
        {
            List<string> list = new List<string>();
            list.Add("procon.protected.send");
            list.AddRange(args);
            this.ExecuteCommand(list.ToArray());
        }
        #endregion

        #region Plugin information
        public string GetPluginName()
        {
            return "Round Stats";
        }
        public string GetPluginVersion()
        {
            return "0.0.0.1";
        }
        public string GetPluginAuthor()
        {
            return "xfileFIN";
        }
        public string GetPluginWebsite()
        {
            return "github.com/Razer2015";
        }
        public string GetPluginDescription()
        {
            return @"
<h2>Description</h2>
<p></p>

<h2>Commands</h2>
<p></p>

<h2>Settings</h2>

<h2>Development</h2>
<p>Developed by xfileFIN</p>

<h3>Changelog</h3>
<blockquote><h4>0.0.0.1 (11.12.2016)</h4>
	- initial version<br/>
</blockquote>
";
        }
        #endregion

        #region Plugin Variables
        public List<CPluginVariable> GetDisplayPluginVariables()
        {
            List<CPluginVariable> lstReturn = new List<CPluginVariable>();

            lstReturn.Add(new CPluginVariable("Debug|Debug level", fDebugLevel.GetType(), fDebugLevel));

            return lstReturn;
        }
        public List<CPluginVariable> GetPluginVariables()
        {
            return GetDisplayPluginVariables();
        }

        public void SetPluginVariable(string strVariable, string strValue)
        {
            if (Regex.Match(strVariable, @"Debug level").Success)
            {
                int tmp = 2;
                int.TryParse(strValue, out tmp);
                fDebugLevel = tmp;
            }
        }
        #endregion

        #region Plugin loading, enabling and disabling
        public void OnPluginLoaded(string strHostName, string strPort, string strPRoConVersion)
        {
            server_host = strHostName;
            server_port = strPort;
            this.RegisterEvents(this.GetType().Name, "OnListPlayers", "OnPlayerLeft", "OnPlayerKilled", "OnPlayerSpawned", "OnGlobalChat", "OnTeamChat", "OnSquadChat", "OnPlayerChat", "OnRoundOverPlayers", "OnRoundOver", "OnLevelLoaded", "OnLevelStarted");
        }

        public void OnPluginEnable()
        {
            fIsEnabled = true;
            ConsoleWrite("^2Enabled!");
        }
        public void OnPluginDisable()
        {
            fIsEnabled = false;
            PlayersList = null;
            ConsoleWrite("^1Disabled =(");
        }
        #endregion

        #region Player
        public override void OnPlayerJoin(string soldierName)
        {
            ServerCommand("listPlayers", "all");
        }
        public override void OnListPlayers(List<CPlayerInfo> players, CPlayerSubset subset)
        {
            PlayersList = players;

            foreach (var player in PlayersList)
                RefreshStats(player.SoldierName, ref PlayerStats, false);
        }
        public override void OnPlayerLeft(CPlayerInfo playerInfo)
        {
            ServerCommand("listPlayers", "all");
        }
        public override void OnPlayerKilled(Kill kKillerVictimDetails)
        {
        }
        public override void OnPlayerSpawned(string soldierName, Inventory spawnedInventory)
        {
        }
        #endregion

        #region Chat
        public override void OnGlobalChat(string speaker, string message)
        {
            ingame_Commands(speaker, message);
        }
        public override void OnTeamChat(string speaker, string message, int teamId)
        {
            ingame_Commands(speaker, message);
        }
        public override void OnSquadChat(string speaker, string message, int teamId, int squadId)
        {
            ingame_Commands(speaker, message);
        }
        public override void OnPlayerChat(string speaker, string message, string targetPlayer)
        {
            ingame_Commands(speaker, message);
        }
        #endregion

        #region Round
        public override void OnRoundOverPlayers(List<CPlayerInfo> players)
        {
            PlayersList = players;
        }
        public override void OnRoundOver(int winningTeamId)
        {
            Thread delayed = new Thread(new ThreadStart(delegate ()
            {
                Thread.Sleep(m_waitOnRoundOver * 1000);
                try
                {
                    PlayerStatsEnd.Clear();
                    LastRoundStats.Clear();

                    foreach (var player in PlayerStats) // Retrieve the new stats
                        RefreshStats(player.Key, ref PlayerStatsEnd, true);

                    foreach (var player in PlayerStats) // Compare
                        if (PlayerStatsEnd.ContainsKey(player.Key))
                            LastRoundStats.Add(StatsParser.Compare(player.Value, PlayerStatsEnd[player.Key]));
                }
                catch (Exception e)
                {
                    DebugWrite(e.ToString(), 2);
                }
            }));

            delayed.IsBackground = true;
            delayed.Name = "CRoundStats_OnRoundOver";
            delayed.Start();
        }
        #endregion

        #region Level
        public override void OnLevelLoaded(string mapFileName, string Gamemode, int roundsPlayed, int roundsTotal)
        {
            var mostHeals = LastRoundStats.OrderByDescending(i => i.heals).FirstOrDefault();
            var mostRevives = LastRoundStats.OrderByDescending(i => i.revives).FirstOrDefault();
            var mostResupplies = LastRoundStats.OrderByDescending(i => i.resupplies).FirstOrDefault();
            var mostRepairs = LastRoundStats.OrderByDescending(i => i.repairs).FirstOrDefault();

            if (!mostHeals.Equals(default(Stats)) && mostHeals.heals > 0
                || !mostRevives.Equals(default(Stats)) && mostRevives.revives > 0
                || !mostResupplies.Equals(default(Stats)) && mostResupplies.resupplies > 0
                || !mostRepairs.Equals(default(Stats)) && mostRepairs.repairs > 0)
                ServerCommand("admin.say", "-- Best Team Players (experimental) --", "all");
            if (!mostHeals.Equals(default(Stats)) && mostHeals.heals > 0)
                ServerCommand("admin.say", String.Format("-- Healer: {0} with {1} heals --", mostHeals.soldierName, mostHeals.heals), "all");
            if (!mostRevives.Equals(default(Stats)) && mostRevives.revives > 0)
                ServerCommand("admin.say", String.Format("-- Reviver: {0} with {1} revives --", mostRevives.soldierName, mostRevives.revives), "all");
            if (!mostResupplies.Equals(default(Stats)) && mostResupplies.resupplies > 0)
                ServerCommand("admin.say", String.Format("-- Resupplier: {0} with {1} resupplies --", mostResupplies.soldierName, mostResupplies.resupplies), "all");
            if (!mostRepairs.Equals(default(Stats)) && mostRepairs.repairs > 0)
                ServerCommand("admin.say", String.Format("-- Repairer: {0} with {1} repairs --", mostRepairs.soldierName, mostRepairs.resupplies), "all");

            PlayerStats.Clear();
            ServerCommand("listPlayers", "all");
        }
        public override void OnLevelStarted()
        {
            PlayerStats.Clear();
            ServerCommand("listPlayers", "all");
        }
        #endregion

        private void RefreshStats(string soldierName, ref Dictionary<string, Stats> Stats, bool forceFetch)
        {
            if (Stats.ContainsKey(soldierName) && !forceFetch)
                return;

            Hashtable data = BClient.getStats(soldierName);
            if (data == null)
                return;

            Hashtable generalStats = null;
            if (data.ContainsKey("generalStats"))
                generalStats = (Hashtable)data["generalStats"];

            if (generalStats == null)
                return;

            Stats playerStats = new Stats();
            StatsParser.Parse(generalStats, ref playerStats);

            if (data.ContainsKey("personaId"))
                playerStats.personaID = data["personaId"].ToString();
            playerStats.soldierName = soldierName;

            if (!Stats.ContainsKey(soldierName))
                Stats.Add(soldierName, playerStats);
            else
                Stats[soldierName] = playerStats;
        }

        private void ingame_Commands(string speaker, string message)
        {
            Char[] Prefixes = new Char[] { '!', '@', '#', '/' };
            Boolean iscommand = false;
            foreach (Char prefix in Prefixes)
                if (message.StartsWith(prefix.ToString()))
                {
                    iscommand = true;
                    break;
                }

            if (!iscommand)
                return;

            Match cmd_username = Regex.Match(message, @"[!@#/]([^\s]+)\s+([^\s]+)", RegexOptions.IgnoreCase);
            Match cmd_yourself = Regex.Match(message, @"[!@#/]([^\s]+)", RegexOptions.IgnoreCase);
            if (cmd_username.Success)
            {
                string refName = statsContainer.GetIndex(cmd_username.Groups[1].Value, 0);
                if (refName == null)
                    return;

                String target = BestPlayerMatch(message, speaker, cmd_username);
                if (String.IsNullOrEmpty(target))
                    return;

                if (LastRoundStats == null)
                {
                    ServerCommand("admin.say", "There were no stats of last round!", "player", speaker);
                    return;
                }

                var stats = LastRoundStats.Where(i => i.soldierName == target).FirstOrDefault();
                if (stats.Equals(default(Stats)))
                {
                    ServerCommand("admin.say", String.Format("There were no stats for player {0}!", target), "player", speaker);
                    return;
                }

                string _message = statsContainer.GetIndex(cmd_username.Groups[1].Value, 4);
                _message = _message.Replace("%soldier%", stats.soldierName).Replace("%value%", GetPropValue(stats, refName).ToString());
                ServerCommand("admin.say", _message, "player", speaker);

                return;
            }
            else if (cmd_yourself.Success && !cmd_username.Success)
            {
                string refName = statsContainer.GetIndex(cmd_yourself.Groups[1].Value, 0);
                if (refName == null)
                    return;

                if (LastRoundStats == null)
                {
                    ServerCommand("admin.say", "There were no stats of last round!", "player", speaker);
                    return;
                }

                var stats = LastRoundStats.Where(i => i.soldierName == speaker).FirstOrDefault();
                if (stats.Equals(default(Stats)))
                {
                    ServerCommand("admin.say", "There were no stats for your username!", "player", speaker);
                    return;
                }

                string _message = statsContainer.GetIndex(cmd_yourself.Groups[1].Value, 4);
                _message = _message.Replace("%soldier%", stats.soldierName).Replace("%value%", GetPropValue(stats, refName).ToString());
                ServerCommand("admin.say", _message, "player", speaker);

                return;
            }
        }

        public String BestPlayerMatch(String message, String speaker, Match cmd)
        {
            int found = 0;
            String name = cmd.Groups[2].Value;
            CPlayerInfo target = null;
            foreach (CPlayerInfo p in PlayersList)
            {
                if (p == null)
                    continue;

                if (Regex.Match(p.SoldierName, name, RegexOptions.IgnoreCase).Success)
                {
                    ++found;
                    target = p;
                }
            }

            if (found == 0)
            {
                ServerCommand("admin.say", "No such player name matches (" + name + ")", "player", speaker);
                DebugWrite("No such player name matches (" + name + ") " + " --> " + speaker, 2);
                return (String.Empty);
            }
            if (found > 1)
            {
                ServerCommand("admin.say", "Multiple players match the target name (" + name + "), try again!", "player", speaker);
                DebugWrite("Multiple players match the target name (" + name + "), try again! --> " + speaker, 2);
                return (String.Empty);
            }
            else
                return (target.SoldierName);
        }

        public static object GetPropValue(object src, string propName)
        {
            return src.GetType().GetProperty(propName).GetValue(src, null);
        }
    } // end CRoundStats

    public static class StatsParser
    {
        public static void Parse(Hashtable generalStats, ref Stats stats)
        {
            try
            {
                PropertyInfo[] fi = stats.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (PropertyInfo info in fi)
                {
                    if (info.PropertyType.Name.Equals("UInt32"))
                    {
                        if (generalStats.ContainsKey(info.Name))
                        {
                            var field = stats.GetType().GetProperty(info.Name);
                            UInt32 value = Convert.ToUInt32(generalStats[info.Name]);
                            object boxed = stats;
                            field.SetValue(boxed, value, null);
                            stats = (Stats)boxed;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        public static Stats Compare(Stats before, Stats after)
        {
            Stats compared = new Stats();
            compared.personaID = before.personaID;
            compared.soldierName = before.soldierName;
            try
            {
                PropertyInfo[] fi = typeof(Stats).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (PropertyInfo info in fi)
                {
                    if (info.PropertyType.Name.Equals("UInt32"))
                    {
                        UInt32 valueBefore = (UInt32)typeof(Stats).GetProperty(info.Name).GetValue(before, null);
                        UInt32 valueAfter = (UInt32)typeof(Stats).GetProperty(info.Name).GetValue(after, null);
                        UInt32 valueNow = valueAfter - valueBefore;

                        var field = compared.GetType().GetProperty(info.Name);
                        object boxed = compared;
                        field.SetValue(boxed, valueNow, null);
                        compared = (Stats)boxed;
                    }
                }
                return (compared);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return (compared);
            }
        }
    }

    public struct Stats
    {
        public String personaID { get; set; }
        public String soldierName { get; set; }

        #region Kit rankings
        public UInt32 assault { get; set; }
        public UInt32 engineer { get; set; }
        public UInt32 support { get; set; }
        public UInt32 recon { get; set; }
        public UInt32 commander { get; set; }
        #endregion

        #region Kit item rankings
        public UInt32 heals { get; set; }
        public UInt32 revives { get; set; }
        public UInt32 repairs { get; set; }
        public UInt32 resupplies { get; set; }
        #endregion

        #region General Rankings
        #region Kills
        public UInt32 avengerKills { get; set; }
        public UInt32 kills { get; set; }
        public UInt32 kills_assault { get; set; }
        public UInt32 kills_engineer { get; set; }
        public UInt32 kills_recon { get; set; }
        public UInt32 kills_support { get; set; }
        public UInt32 killsPerMinute { get; set; }
        #endregion

        #region Accuracy
        public UInt32 headshots { get; set; }
        #endregion

        #region Assists
        public UInt32 killAssists { get; set; }
        public UInt32 suppressionAssists { get; set; }
        #endregion

        #region Vehicle
        public UInt32 vehicleDamage { get; set; }
        public UInt32 vehiclesDestroyed { get; set; }
        #endregion
        #endregion
    }

    public class DataContainer
    {
        public enum Categories
        {
            VARIABLE,
            COMMAND,
            ACTION,
            CATEGORY,
            MESSAGE
        }

        private readonly string[,] _data;
        private List<string> _index;

        public DataContainer(string[,] data)
        {
            _data = data;
        }

        public bool Contains(string value)
        {
            if (_index == null)
            {
                _index = new List<string>();
                for (int i = 0; i < _data.GetLength(0); i++)
                {
                    for (int j = 0; j < _data.GetLength(1); j++)
                    {
                        _index.Add(_data[i, j]);
                    }
                }
            }
            return _index.Contains(value);
        }

        /// <summary>
        /// Get string matching the search word and the wanted category
        /// </summary>
        /// <param name="value"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public string GetIndex(string value, Categories index)
        {
            return (GetIndex(value, int.Parse(index.ToString())));
        }

        /// <summary>
        /// Get string matching the search word and the wanted category
        /// </summary>
        /// <param name="value"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public string GetIndex(string value, int index)
        {
            if (Contains(value))
                return (_data[((_index.IndexOf(value)) / _data.GetLength(1)), index]);
            else
                return (null);
        }
    }

    public class BattlelogClient
    {
        WebClient client = null;

        private String fetchWebPage(ref String html_data, String url)
        {
            try
            {
                if (client == null)
                    client = new WebClient();

                html_data = client.DownloadString(url);
                return html_data;

            }
            catch (WebException e)
            {
                if (e.Status.Equals(WebExceptionStatus.Timeout))
                    throw new Exception("HTTP request timed-out");
                else
                    throw;
            }
        }

        /// <summary>
        /// Retrieve the warsawdetailedstatspopulate JSON for the player
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        public Hashtable getStats(String player)
        {
            try
            {
                if (Reflector.htmldecode == null)
                    Reflector.Reflect();

                /* First fetch the player's main page to get the persona id */
                String result = "";
                fetchWebPage(ref result, "http://battlelog.battlefield.com/bf4/user/" + player);

                string decoded = Reflector.htmldecode(result);

                /* Extract the persona id */
                MatchCollection pid = Regex.Matches(decoded, @"bf4/soldier/" + player + @"/stats/(\d+)(/\w*)?/", RegexOptions.Singleline);

                String personaId = "";

                foreach (Match m in pid)
                {
                    if (m.Success && m.Groups[2].Value.Trim() == "/pc")
                    {
                        personaId = m.Groups[1].Value.Trim();
                    }
                }

                if (personaId == "")
                    throw new Exception("could not find persona-id for ^b" + player);

                return getStats(player, personaId);
            }
            catch (Exception e)
            {
                //Handle exceptions here however you want
                Console.WriteLine(e.ToString());
            }

            return null;
        }

        /// <summary>
        /// Retrieve the warsawdetailedstatspopulate JSON for the player when personaId is known
        /// </summary>
        /// <param name="player"></param>
        /// <param name="personaId"></param>
        /// <returns></returns>

        public Hashtable getStats(String player, String personaId)
        {
            try
            {
                /* First fetch the player's main page to get the persona id */
                String result = "";

                fetchWebPage(ref result, "http://battlelog.battlefield.com/bf4/warsawdetailedstatspopulate/" + personaId + "/1/");

                Hashtable json = (Hashtable)JSON.JsonDecode(result);

                // check we got a valid response
                if (!(json.ContainsKey("type") && json.ContainsKey("message")))
                    throw new Exception("JSON response does not contain \"type\" or \"message\" fields");

                String type = (String)json["type"];
                String message = (String)json["message"];

                /* verify we got a success message */
                if (!(type.StartsWith("success") && message.StartsWith("OK")))
                    throw new Exception("JSON response was type=" + type + ", message=" + message);


                /* verify there is data structure */
                Hashtable data = null;
                if (!json.ContainsKey("data") || (data = (Hashtable)json["data"]) == null)
                    throw new Exception("JSON response was does not contain a data field");

                return data;
            }
            catch (Exception e)
            {
                //Handle exceptions here however you want
                Console.WriteLine(e.ToString());
            }

            return null;
        }
    } // end BattelelogClient

    /// <summary>
    /// Compile code using external source code and assemblies
    /// </summary>
    public class Reflector
    {
        public static Dictionary<string, string> weapons;
        public static String encoded;
        public static String decoded;

        public static HTMLDecode htmldecode;

        public static bool Reflect()
        {
            string file_path = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, @"xfileHelpers\Snippets.cs");
            if (!File.Exists(file_path))
                return (false);

            String cs_path = File.ReadAllText(file_path);
            Assembly assembly = CompileSource(cs_path);
            if (assembly == null)
                return (false);

            Type SnippetClass = assembly.GetType("Snippets");

            MethodInfo method = SnippetClass.GetMethod("HTMLDecode");
            htmldecode = (HTMLDecode)Delegate.CreateDelegate(typeof(HTMLDecode), method);

            return (true);
        }
        private static Assembly CompileSource(string sourceCode)
        {
            String SystemWeb = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, @"xfileHelpers\System.Web.dll");

            if (!File.Exists(SystemWeb))
                return (null);

            CodeDomProvider cpd = new CSharpCodeProvider();
            CompilerParameters cp = new CompilerParameters();
            cp.ReferencedAssemblies.Add("System.dll");
            cp.ReferencedAssemblies.Add("System.Data.dll");
            cp.ReferencedAssemblies.Add("System.Web.dll");
            cp.GenerateExecutable = false;
            // True - memory generation, false - external file generation
            cp.GenerateInMemory = true;
            // Invoke compilation.
            CompilerResults cr = cpd.CompileAssemblyFromSource(cp, sourceCode);

            if (cr.Errors.HasErrors)
            {
                StringBuilder sb = new StringBuilder();

                foreach (CompilerError error in cr.Errors)
                {
                    sb.AppendLine(String.Format("Error ({0}): {1}", error.ErrorNumber, error.ErrorText));
                }

                throw new InvalidOperationException(sb.ToString());
            }

            return cr.CompiledAssembly;
        }
    } // end Reflector

} // end namespace PRoConEvents



