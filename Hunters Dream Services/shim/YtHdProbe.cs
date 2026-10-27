// Yharnam Together x The Hunter's Dream (YTHD) probe shim - SPDX-License-Identifier: GPL-2.0-or-later
//
// Yharnam Together's run.py starts the game engine from the BB_PROBE environment variable (bin\bb-probe.exe when it
// is not set). This program is that BB_PROBE: it sits in bin\ beside bb-probe.exe (run.py looks for the GPU helper
// programs next to it) and starts the real engine with the same arguments.
//
// What it changes, only while the mod is switched ON (launcher > Mods page) and the game starts online:
// the launcher points the game's own services (messages, bloodstains, wandering ghosts, chalice search and
// sharing, sign-in) at the host's backend through user\host_overrides.custom.json. This program writes its own
// user\host_overrides.hunters-dream.json instead and hands that file to the engine, so those services come from
// The Hunter's Dream, exactly like Yharnam Together's own default (run.py: online_overrides). Co-op itself (the
// team lobby, the relay, summons) stays on the host's shadNet server.
//
// It changes no Yharnam Together file. When The Hunter's Dream does not answer, it leaves the launcher's file in
// place for that start. Build: shim\install.ps1 (uses the C# compiler Windows ships).
// "ythd-probe.exe --ythd-check" prints what it would do and exits.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;

static class YtHdProbe
{
    const string GameServer = "https://ss4.scej-network.jp:20443";   // the address the game asks for ss.info and its services
    const int CommunityApiPort = 18671;                              // where the community server's API (summons, play logs) is
    static readonly string[] PlayLogHosts = { "bb-playlog-prod.s3.amazonaws.com", "bb-playlog-test.s3.amazonaws.com" };

    static void Say(string message)
    {
        try { Console.Out.WriteLine("YTHD: " + message); Console.Out.Flush(); } catch (IOException) { }
    }

    // CommandLineToArgvW-compatible quoting, so the engine receives exactly the arguments run.py built.
    static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
        var text = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { text.Append('\\', slashes * 2 + 1).Append('"'); }
            else { text.Append('\\', slashes).Append(c); }
            slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }

    static string Env(string name, string fallback)
    {
        string value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    // mods.json as the launcher writes it: {"order": [...], "disabled": ["folder", ...]}.
    static bool Disabled(string config, string folderName)
    {
        try
        {
            if (!File.Exists(config)) return false;
            var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(config)) as IDictionary;
            var list = root != null && root.Contains("disabled") ? root["disabled"] as IEnumerable : null;
            if (list == null) return false;
            foreach (object name in list)
                if (string.Equals(name as string, folderName, StringComparison.Ordinal)) return true;
        }
        catch (Exception) { }
        return false;
    }

    // This mod's folder: a folder of the mods directory that has ythd.ini and a dvdroot_ps4 (the launcher lists
    // exactly the folders that have one).
    static string FindMod(string modsDir)
    {
        if (!Directory.Exists(modsDir)) return null;
        string[] folders = Directory.GetDirectories(modsDir);
        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
            if (File.Exists(Path.Combine(folder, "ythd.ini")) && Directory.Exists(Path.Combine(folder, "dvdroot_ps4")))
                return folder;
        return null;
    }

    // KEY=VALUE lines; '#' and ';' start a comment line.
    static Dictionary<string, string> ReadSettings(string path)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return settings;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string value = line.Substring(eq + 1).Trim();
            if (value.Length > 0) settings[line.Substring(0, eq).Trim()] = value;
        }
        return settings;
    }

    static string Setting(Dictionary<string, string> settings, string key, string fallback)
    {
        string value;
        return settings.TryGetValue(key, out value) ? value : fallback;
    }

    static bool Flag(Dictionary<string, string> settings, string key, bool fallback)
    {
        string value = Setting(settings, key, fallback ? "1" : "0");
        return value != "0" && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) && !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);
    }

    // "https://host[:port][/path]" -> a Uri that is http or https; throws otherwise.
    static Uri ParseWeb(string text, string what)
    {
        Uri uri;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || uri.Host.Length == 0)
            throw new FormatException(what + " is not an http(s) address: " + text);
        return uri;
    }

    static string JsonString(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value)
        {
            if (c == '"' || c == '\\') text.Append('\\').Append(c);
            else if (c < 0x20) text.Append("\\u").Append(((int)c).ToString("x4"));
            else text.Append(c);
        }
        return text.Append('"').ToString();
    }

    static bool Reachable(string host, int port, int timeoutMs)
    {
        try
        {
            using (var client = new TcpClient())
            {
                IAsyncResult attempt = client.BeginConnect(host, port, null, null);
                if (!attempt.AsyncWaitHandle.WaitOne(timeoutMs, false)) return false;
                client.EndConnect(attempt);
                return client.Connected;
            }
        }
        catch (Exception) { return false; }
    }

    // The addresses file for one start. The same entries Yharnam Together's run.py writes by default (game server ->
    // the community server, summon signs -> the shadNet WebAPI of the host), plus the play-log redirect the Windows
    // port uses, plus any other entry the launcher's own file had.
    static string BuildOverrides(Uri community, string webapi, bool playLogs, string launcherFile)
    {
        string baseUrl = community.Scheme + "://" + community.Host;
        var entries = new List<KeyValuePair<string, string>>();
        entries.Add(new KeyValuePair<string, string>(GameServer, baseUrl));
        if (!string.IsNullOrEmpty(webapi))
        {
            entries.Add(new KeyValuePair<string, string>("https://" + community.Host + ":" + CommunityApiPort + "/summon_messenger", webapi));
            entries.Add(new KeyValuePair<string, string>("http://" + community.Host + ":" + CommunityApiPort + "/summon_messenger", webapi));
        }
        if (playLogs)
            foreach (string host in PlayLogHosts)
                entries.Add(new KeyValuePair<string, string>("https://" + host + ":443", baseUrl + ":" + CommunityApiPort));
        try
        {
            if (!string.IsNullOrEmpty(launcherFile) && File.Exists(launcherFile))
            {
                var existing = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(launcherFile)) as IDictionary;
                if (existing != null)
                    foreach (DictionaryEntry pair in existing)
                    {
                        string key = pair.Key as string, value = pair.Value as string;
                        if (key == null || value == null || key == GameServer) continue;
                        bool known = false;
                        foreach (KeyValuePair<string, string> e in entries) if (e.Key == key) known = true;
                        if (!known) entries.Add(new KeyValuePair<string, string>(key, value));
                    }
            }
        }
        catch (Exception) { }
        var json = new StringBuilder("{\r\n");
        for (int i = 0; i < entries.Count; i++)
            json.Append("  ").Append(JsonString(entries[i].Key)).Append(": ").Append(JsonString(entries[i].Value)).Append(i + 1 < entries.Count ? ",\r\n" : "\r\n");
        return json.Append("}\r\n").ToString();
    }

    static string UserDir(string[] args, string data)
    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--user") return args[i + 1];
        return Env("BB_USER_DIR", Path.Combine(data, "user"));
    }

    static int Main(string[] args)
    {
        string bin = AppDomain.CurrentDomain.BaseDirectory;
        string engine = Path.Combine(bin, "bb-probe.exe");
        string data = Env("BB_DATA_DIR", Directory.GetParent(bin.TrimEnd('\\', '/')).FullName);
        string modsDir = Env("BB_MODS_DIR", Path.Combine(data, "mods"));
        string config = Env("BB_MODS_CONFIG", Path.Combine(data, "mods.json"));

        string mod = FindMod(modsDir);
        string why = null;
        if (mod == null) why = "no YTHD mod folder (with ythd.ini) in " + modsDir;
        else if (Env("YTHD_ENABLED", "1") == "0") why = "YTHD_ENABLED=0";
        else if (Env("BB_MODS_ENABLED", "1") == "0") why = "mods are switched off in the launcher (Load mods)";
        else if (Disabled(config, Path.GetFileName(mod))) why = "switched off in the launcher's Mods page";
        var settings = mod != null ? ReadSettings(Path.Combine(mod, "ythd.ini")) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (args.Length == 1 && args[0] == "--ythd-check")
        {
            Console.WriteLine("engine       : " + engine + (File.Exists(engine) ? "" : "  (MISSING)"));
            Console.WriteLine("mods folder  : " + modsDir);
            Console.WriteLine("mod folder   : " + (mod ?? "(not found)"));
            Console.WriteLine("mod is       : " + (why == null ? "ON" : "OFF - " + why));
            if (why == null)
            {
                try
                {
                    Uri community = ParseWeb(Setting(settings, "community", "https://thehuntersdream.com"), "community");
                    Console.WriteLine("game services: " + community.Scheme + "://" + community.Host + " (ss.info on port 20443; the API on port " + CommunityApiPort + ")");
                    int port = int.Parse(Setting(settings, "probe_port", "20443"));
                    if (Flag(settings, "fallback", true))
                        Console.WriteLine("reachable now: " + (Reachable(community.Host, port, int.Parse(Setting(settings, "probe_timeout_ms", "3000"))) ? "yes" : "NO (a start would use the host's own services)"));
                }
                catch (Exception e) { Console.WriteLine("settings     : " + e.Message); }
            }
            return 0;
        }

        var start = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        string online = Env("BB_ONLINE", "");
        string launcherFile = Environment.GetEnvironmentVariable("SHADPS4_HTTP_HOST_OVERRIDES_JSON");
        if (why != null) Say("off - " + why);
        else if (online != "1") Say("off - the game starts offline");
        else if (string.IsNullOrEmpty(launcherFile)) Say("off - run.py gave the game no addresses file (not an online start)");
        else
        {
            try
            {
                Uri community = ParseWeb(Setting(settings, "community", "https://thehuntersdream.com"), "community");
                string webapi = Environment.GetEnvironmentVariable("BB_SHADNET_WEBAPI");
                if (!string.IsNullOrEmpty(webapi)) webapi = ParseWeb(webapi, "BB_SHADNET_WEBAPI").ToString().TrimEnd('/');
                int port = int.Parse(Setting(settings, "probe_port", "20443"));
                int timeout = int.Parse(Setting(settings, "probe_timeout_ms", "3000"));
                if (Flag(settings, "fallback", true) && !Reachable(community.Host, port, timeout))
                    Say("The Hunter's Dream (" + community.Host + ":" + port + ") does not answer - this start keeps your host's own services");
                else
                {
                    string user = UserDir(args, data);
                    Directory.CreateDirectory(user);
                    string file = Path.Combine(user, "host_overrides.hunters-dream.json");
                    File.WriteAllText(file, BuildOverrides(community, webapi, Flag(settings, "playlogs", true), launcherFile), new UTF8Encoding(false));
                    start.EnvironmentVariables["SHADPS4_HTTP_HOST_OVERRIDES_JSON"] = file;
                    Say("on - messages, bloodstains, ghosts and chalice search come from " + community.Host + (string.IsNullOrEmpty(webapi) ? "" : "; co-op summons stay on " + webapi));
                }
            }
            catch (Exception e) { Say("not applied (" + e.Message + ") - this start keeps your host's own services"); }
        }

        if (!File.Exists(engine)) { Say("cannot start " + engine + " (missing)"); return 1; }
        var arguments = new StringBuilder();
        foreach (string arg in args) arguments.Append(arguments.Length > 0 ? " " : "").Append(Quote(arg));
        start.FileName = engine;
        start.Arguments = arguments.ToString();
        using (Process game = Process.Start(start))
        {
            game.WaitForExit();
            return game.ExitCode;
        }
    }
}
