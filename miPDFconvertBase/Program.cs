// miPDFconvert - virtual PDF printer
// Copyright (C) 2026 Wolfgang Mitterbucher (mitterbucher.com)
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version. This program is distributed WITHOUT ANY WARRANTY.
// See the GNU AGPL v3 (LICENSE file) for details and THIRD-PARTY-NOTICES.md
// for the licenses of bundled components.

using log4net;
using log4net.Config;
using System.Text;

namespace miPDFconvertBase
{
    public class Program
    {
        private static readonly ILog LOGGER = LogManager.GetLogger(typeof(Program));

        static void Main(string[] args)
        {
            args ??= Array.Empty<string>();
            XmlConfigurator.Configure();
            var arguments = args.Length > 0 ? String.Join(" ", args.Select(arg => arg.Contains(" ") ? $"\"{arg}\"" : arg)) : null;

            LOGGER.Info($"miPDFconvertBase started with arguments: '{arguments}'");

            string launcherPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string miPDFconvertPath = Path.Combine(Path.GetDirectoryName(launcherPath)!, "miPDFconvert.exe");

            var clp = new CommandLineParser(args);
            if (!clp.HasArgument("INFODATAFILE"))
            {
                LOGGER.Warn("No /INFODATAFILE argument given - nothing to do.");
                return;
            }

            string infFile = clp.GetArgument("INFODATAFILE");
            bool deleteInfFile = false;
            try
            {
                if (!File.Exists(infFile))
                {
                    LOGGER.Error($"INFODATAFILE \"{infFile}\" does not exist. Cannot process print job.");
                    Environment.ExitCode = 1;
                    return;
                }

                Dictionary<string, Dictionary<string, string>> iniData = ReadIniFile(infFile);
                if (!iniData.TryGetValue("0", out Dictionary<string, string>? job))
                {
                    LOGGER.Error($"INFODATAFILE \"{infFile}\" contains no [0] section. Cannot process print job.");
                    Environment.ExitCode = 1;
                    return;
                }

                // The monitor writes the spool file path into the inf; fall back to the
                // inf path with .ps extension for older monitor versions.
                string psFile = job.TryGetValue("SpoolFileName", out string? spoolFileName) && !string.IsNullOrWhiteSpace(spoolFileName)
                    ? spoolFileName
                    : Path.ChangeExtension(infFile, ".ps");

                // Rename the spool file to the (sanitized) document title. The job id makes the
                // name unique: without it, printing the same document twice in a row fails,
                // because the first conversion still has "<title>.ps" open when the second job
                // tries to overwrite it - the job would be dropped without any visible error.
                // The suffix also defuses reserved device names (CON, PRN, ...) as titles.
                job.TryGetValue("DocumentTitle", out string? rawDocumentTitle);
                job.TryGetValue("JobId", out string? jobId);
                string rawTitle = Path.GetFileNameWithoutExtension(rawDocumentTitle ?? "");
                string uniqueSuffix = string.IsNullOrWhiteSpace(jobId) ? DateTime.Now.ToString("HHmmssfff") : jobId;
                string documentTitle = SanitizeFileName(rawTitle) + "_" + uniqueSuffix + ".ps";
                string targetPsPath = Path.Combine(Path.GetDirectoryName(psFile)!, documentTitle);
                LOGGER.Info($"Renaming spool file \"{psFile}\" to \"{targetPsPath}\".");
                File.Move(psFile, targetPsPath, true);
                psFile = targetPsPath;

                job.TryGetValue("ClientComputer", out string? clientComputer);
                clientComputer = (clientComputer ?? "").TrimStart('\\');
                if (clientComputer.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    deleteInfFile = true;
                    string username = job.TryGetValue("Username", out string? user) ? user : "";
                    LOGGER.Info($"miPDFconvertBase found following user in inffile '{username}'");
                    // Quote the executable path: it contains spaces ("Program Files (x86)"), and
                    // CreateProcessAsUser is called with lpApplicationName = null, so an unquoted path
                    // would be parsed ambiguously.
                    string miPDFconvertCommandLine = $"\"{miPDFconvertPath}\" -psfile \"{psFile}\"";
                    LOGGER.Info($"Trying to start miPDFconvert.exe application using '{miPDFconvertCommandLine}'");
                    ProcessUtils.LaunchUsingExplorerProcessOfUser(miPDFconvertCommandLine, username);
                }
                else
                {
                    //maybe network interaction is required - keep the inf file around in that case
                    LOGGER.Info($"Skipping launch of miPDFconvertBase as ClientComputer '{clientComputer}' does not match local machine name '{Environment.MachineName}'");
                }
            }
            catch (Exception ex)
            {
                LOGGER.Error("Exception while processing INFODATAFILE and launching miPDFconvert.", ex);
                Console.WriteLine(ex);
                Environment.ExitCode = 1;
            }
            finally
            {
                // The inf file is fully consumed at this point - clean it up so the spool
                // directory does not grow unbounded.
                if (deleteInfFile)
                {
                    try
                    {
                        File.Delete(infFile);
                    }
                    catch (Exception ex)
                    {
                        LOGGER.Warn($"Could not delete INFODATAFILE \"{infFile}\": {ex.Message}");
                    }
                }
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "document";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static Dictionary<string, Dictionary<string, string>> ReadIniFile(string filePath)
        {
            Dictionary<string, Dictionary<string, string>> iniData = new Dictionary<string, Dictionary<string, string>>();
            string currentSection = "";

            // The monitor writes the inf via WritePrivateProfileStringW into a BOM-less file,
            // i.e. in the system ANSI code page - NOT UTF-8. Reading it as UTF-8 garbles
            // umlauts in Username/DocumentTitle (a user like "Müller" would never match an
            // explorer process and the job would be dropped). A BOM, if ever present, still
            // takes precedence over the passed encoding.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding ansi = Encoding.GetEncoding(0); // 0 = system default ANSI code page

            foreach (string line in File.ReadAllLines(filePath, ansi))
            {
                string trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    iniData[currentSection] = new Dictionary<string, string>();
                }
                else if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith(";"))
                {
                    int equalsIndex = trimmedLine.IndexOf('=');
                    if (equalsIndex >= 0)
                    {
                        string key = trimmedLine.Substring(0, equalsIndex).Trim();
                        string value = trimmedLine.Substring(equalsIndex + 1).Trim();
                        iniData[currentSection][key] = value;
                    }
                }
            }

            return iniData;
        }
    }

    public class CommandLineParser
    {
        private readonly Dictionary<string, string> _args;

        public CommandLineParser(IEnumerable<string> args)
        {
            _args = AnalyzeCommandLine(args);
        }

        public bool HasArgument(string key)
        {
            return _args.ContainsKey(key.ToLowerInvariant());
        }

        public string GetArgument(string key)
        {
            return _args[key.ToLowerInvariant()];
        }

        private static Dictionary<string, string> AnalyzeCommandLine(IEnumerable<string> args)
        {
            var arguments = new Dictionary<string, string>();

            foreach (var arg in args)
            {
                if (string.IsNullOrEmpty(arg))
                    continue;

                var c = arg[0];
                if (c != '/' && c != '-')
                    continue;

                var s = arg.Substring(1);
                var pos = s.IndexOf('=');

                if (pos < 0)
                {
                    arguments.Add(s.ToLowerInvariant(), string.Empty);
                }
                else
                {
                    var argPair = s.Split(new[] { '=' }, 2);
                    arguments.Add(argPair[0].ToLowerInvariant(), argPair[1]);
                }
            }

            return arguments;
        }
    }
}
