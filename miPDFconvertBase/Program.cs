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
using static System.Net.Mime.MediaTypeNames;

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
            // Quote the executable path: it contains spaces ("Program Files (x86)"), and
            // CreateProcessAsUser is called with lpApplicationName = null, so an unquoted path
            // would be parsed ambiguously.
            string miPDFconvertCommandLine = $"\"{miPDFconvertPath}\"{(string.IsNullOrEmpty(arguments) ? null : " " + arguments)}";
           

            //INFODATAFILE parsing
            var clp = new CommandLineParser(args);
            if (clp.HasArgument("INFODATAFILE"))
            {
                try
                {
                    string infFile = clp.GetArgument("INFODATAFILE");

                    if (File.Exists(infFile))
                    {
                        Dictionary<string, Dictionary<string, string>> iniData = ReadIniFile(infFile);
                        string psFile = infFile.Replace(".inf",".ps");
                        string clientComputer = iniData["0"]["ClientComputer"];
                        //move ps file to document title name (sanitized, overwriting any leftover)
                        string rawTitle = Path.GetFileNameWithoutExtension(iniData["0"]["DocumentTitle"]);
                        string documentTitle = SanitizeFileName(rawTitle) + ".ps";
                        string targetPsPath = Path.Combine(Path.GetDirectoryName(psFile)!, documentTitle);
                        LOGGER.Info($"Renaming spool file \"{psFile}\" to \"{targetPsPath}\".");
                        File.Move(psFile, targetPsPath, true);
                        psFile = targetPsPath;
                        arguments = $"-psfile \"{psFile}\"";
                        if (clientComputer.ToLower() == Environment.MachineName.ToLower())
                        {
                            string username = iniData["0"]["Username"];
                            LOGGER.Info($"miPDFConverBase found following user in inffile '{username}'");
                            miPDFconvertCommandLine = $"\"{miPDFconvertPath}\"{(string.IsNullOrEmpty(arguments) ? null : " " + arguments)}";
                            string mainLogMessage = $"Trying to start miPDFconvert.exe application using '{miPDFconvertCommandLine}'";
                            LOGGER.Info(mainLogMessage);
                            ProcessUtils.LaunchUsingExplorerProcessOfUser(miPDFconvertCommandLine, username);
                        }
                        else
                        {
                            //maybe network interaction is required
                            LOGGER.Info($"Skipping launch of miPDFconvertBase as ClientComputer '{clientComputer}' does not match local machine name '{Environment.MachineName}'");
                        }
                    }
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    LOGGER.Error("Exception while processing INFODATAFILE and launching miPDFconvert.", ex);
                    Console.WriteLine(ex);
                    Environment.ExitCode = 1;
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

            foreach (string line in File.ReadAllLines(filePath))
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
