using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;
using miMonitor.SetupHelper.Driver;
using miMonitor.SetupHelper.Utilities;

namespace miMonitor.SetupHelper
{
    public class Program
    {
        private static void Main(string[] args)
        {
            var showUsage = true;

            var clp = new CommandLineParser(args);

            if (clp.HasArgument("Driver"))
            {
                showUsage = false;
                try
                {
                    switch (clp.GetArgument("Driver").ToLower())
                    {
                        case "add":

                            Actions.InstallmiPDFconvertPrinter();
                            for (int i = 0; i < 3; i++)
                            {
                                if (Actions.IsRepairRequired())
                                {
                                    Actions.UninstallmiPDFconvertPrinter();
                                    Actions.WaitForPrintSpooler();
                                    Actions.InstallmiPDFconvertPrinter();
                                }
                            }
                            break;

                        case "remove":
                            Actions.UninstallmiPDFconvertPrinter();
                            break;

                        default:
                            showUsage = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.ExitCode = 1;
                }
            }

            if (clp.HasArgument("Printer"))
            {
                showUsage = false;
                try
                {
                    string name = clp.GetArgument("Name");
                    switch (clp.GetArgument("Printer").ToLower())
                    {
                        case "add":
                            Actions.AddPrinter(name);
                            break;

                        case "remove":
                            Actions.RemovePrinter(name);
                            break;

                        default:
                            showUsage = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.ExitCode = 1;
                }
            }

            if (clp.HasArgument("ComInterface"))
            {
                showUsage = false;
                try
                {
                    switch (clp.GetArgument("ComInterface").ToLower())
                    {
                        case "register":
                            RegisterComInterface();
                            break;

                        case "unregister":
                            UnregisterComInterface();
                            break;

                        default:
                            showUsage = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.ExitCode = 1;
                }
            }

            if (clp.HasArgument("TargetApp"))
            {
                showUsage = false;
                try
                {
                    SetTargetApplication(clp.GetArgument("TargetApp"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.ExitCode = 1;
                }
            }

            if (showUsage)
                Usage();
        }

        private static void Usage()
        {
            Console.WriteLine("SetupHelper " + Assembly.GetExecutingAssembly().GetName().Version);
            Console.WriteLine();
            Console.WriteLine("usage:");
            Console.WriteLine("SetupHelper.exe [/Driver=Add|Remove] [/Printer=Add|Remove /Name=Printer] [/ComInterface=Register|Unregister] [/TargetApp=<path>]");
        }

        /// <summary>
        /// Writes the given target application path into the TARGET_APPLICATION appSetting of
        /// miPDFconvert.dll.config (located next to SetupHelper.exe in the install directory).
        /// Uses XmlDocument so the file's UTF-8 encoding, formatting and comments are preserved
        /// and attribute values are correctly XML-escaped.
        /// </summary>
        private static void SetTargetApplication(string targetPath)
        {
            var appDir = GetApplicationDirectory();
            var configFile = Path.Combine(appDir, "miPDFconvert.dll.config");
            if (!File.Exists(configFile))
            {
                Console.WriteLine("Config file not found: " + configFile);
                Environment.ExitCode = 1;
                return;
            }

            var doc = new System.Xml.XmlDocument { PreserveWhitespace = true };
            doc.Load(configFile);

            var node = doc.SelectSingleNode("/configuration/appSettings/add[@key='TARGET_APPLICATION']") as System.Xml.XmlElement;
            if (node == null)
            {
                var appSettings = doc.SelectSingleNode("/configuration/appSettings") as System.Xml.XmlElement;
                if (appSettings == null)
                {
                    Console.WriteLine("appSettings section not found in " + configFile);
                    Environment.ExitCode = 1;
                    return;
                }
                node = doc.CreateElement("add");
                node.SetAttribute("key", "TARGET_APPLICATION");
                appSettings.AppendChild(node);
            }
            node.SetAttribute("value", targetPath ?? string.Empty);

            doc.Save(configFile);
            Console.WriteLine("TARGET_APPLICATION set to \"" + targetPath + "\".");
        }

        private static void RegisterComInterface()
        {
            CallRegAsm("miPDFconvert.exe", "/codebase /tlb");
        }

        private static void UnregisterComInterface()
        {
            CallRegAsm("miPDFconvert.exe", "/unregister");
        }

        private static void CallRegAsm(string fileName, string parameters)
        {
            // Auf 64-Bit-Systemen in beiden Registry-Sichten (32- und 64-Bit) registrieren
            if (Environment.Is64BitOperatingSystem)
                CallRegAsm(fileName, parameters, @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\.NETFramework");
            CallRegAsm(fileName, parameters, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\.NETFramework");
        }

        private static void CallRegAsm(string fileName, string parameters, string dotNetFrameworkRegPath)
        {
            Console.WriteLine(dotNetFrameworkRegPath);

            var dotNetPath = Registry.GetValue(dotNetFrameworkRegPath, "InstallRoot", null)?.ToString();
            if (string.IsNullOrEmpty(dotNetPath))
                throw new InvalidOperationException("Cannot find .Net Framework in " + dotNetFrameworkRegPath);
            var regAsmPath = Path.Combine(dotNetPath, "v4.0.30319\\RegAsm.exe");

            var shellDll = Path.Combine(GetApplicationDirectory(), fileName);
            var paramString = $"\"{shellDll}\" {parameters}";
            Console.WriteLine(regAsmPath + " " + paramString);

            var result = new ShellExecuteHelper().RunAsAdmin(regAsmPath, paramString);
            Console.WriteLine(result.ToString());
        }

        private static string GetApplicationDirectory()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}