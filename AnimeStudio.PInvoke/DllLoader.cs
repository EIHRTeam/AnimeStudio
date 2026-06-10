using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AnimeStudio.PInvoke
{
    public static class DllLoader
    {
        private static readonly ConcurrentDictionary<Assembly, byte> RegisteredAssemblies = new();
        private static readonly ConcurrentDictionary<(Assembly Assembly, string LibraryName), bool> LibraryAvailability = new();

        public static void RegisterDllImportResolver(Assembly assembly)
        {
            if (!RegisteredAssemblies.TryAdd(assembly, 0))
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(assembly, ResolveLibrary);
        }

        public static bool IsLibraryAvailable(string libraryName, Assembly assembly)
        {
            return LibraryAvailability.GetOrAdd(
                (assembly, libraryName),
                static key => ProbeLibrary(key.LibraryName, key.Assembly));
        }

        private static bool ProbeLibrary(string libraryName, Assembly assembly)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = ResolveLibrary(libraryName, assembly, null);
                return handle != IntPtr.Zero;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or
                BadImageFormatException or
                PlatformNotSupportedException)
            {
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    NativeLibrary.Free(handle);
                }
            }
        }

        private static IntPtr ResolveLibrary(
            string libraryName,
            Assembly assembly,
            DllImportSearchPath? searchPath)
        {
            var fileName = GetPlatformFileName(libraryName);

            foreach (var directory in GetSearchDirectories(assembly))
            {
                var libraryPath = Path.Combine(directory, fileName);
                if (NativeLibrary.TryLoad(libraryPath, out var handle))
                {
                    return handle;
                }
            }

            return IntPtr.Zero;
        }

        private static string GetPlatformFileName(string libraryName)
        {
            if (OperatingSystem.IsWindows())
            {
                return $"{libraryName}.dll";
            }

            if (OperatingSystem.IsLinux())
            {
                return $"lib{libraryName}.so";
            }

            if (OperatingSystem.IsMacOS())
            {
                return $"lib{libraryName}.dylib";
            }

            throw new PlatformNotSupportedException(
                $"Native library loading is not configured for {RuntimeInformation.OSDescription}.");
        }

        private static string[] GetSearchDirectories(Assembly assembly)
        {
            var assemblyDirectory = Path.GetDirectoryName(assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory) ||
                string.Equals(assemblyDirectory, AppContext.BaseDirectory, StringComparison.Ordinal))
            {
                return [AppContext.BaseDirectory];
            }

            return [AppContext.BaseDirectory, assemblyDirectory];
        }
    }
}
