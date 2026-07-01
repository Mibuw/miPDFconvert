using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace miMonitor.SetupHelper.Utilities
{
    public interface IAssemblyHelper
    {
        string GetCurrentAssemblyDirectory();

        Version GetCurrentAssemblyVersion();
    }

    public class AssemblyHelper : IAssemblyHelper
    {
        public string GetCurrentAssemblyDirectory()
        {
            var assembly = GetCurrentAssemblyFromStackTrace();

            return System.IO.Path.GetDirectoryName(GetAssemblyPath(assembly));
        }

        public Version GetCurrentAssemblyVersion()
        {
            var assembly = GetCurrentAssemblyFromStackTrace();

            return assembly.GetName().Version;
        }

        private string GetAssemblyPath(Assembly assembly)
        {
            var assemblyPath = assembly.CodeBase;

            if (string.IsNullOrEmpty(assemblyPath))
                assemblyPath = assembly.Location;

            if (assemblyPath.StartsWith(@"file:///", StringComparison.OrdinalIgnoreCase))
                assemblyPath = assemblyPath.Substring(8);

            return assemblyPath;
        }

        private Assembly GetCurrentAssemblyFromStackTrace()
        {
            var stackTrace = new StackTrace(); // get call stack
            var stackFrames = stackTrace.GetFrames(); // get method calls (frames)

            // skip stack frames coming from this class
            var relevantFrame = stackFrames
                .SkipWhile(x => x.GetMethod().DeclaringType == GetType())
                .First();

            var assembly = relevantFrame.GetMethod().DeclaringType?.Assembly;

            return assembly;
        }
    }
}