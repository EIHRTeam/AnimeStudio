using System;
using System.Reflection;
using AnimeStudio.PInvoke;

namespace AnimeStudio
{
    public static class PlatformCapabilities
    {
        private static readonly Assembly UtilityAssembly = typeof(PlatformCapabilities).Assembly;

        public static string PlatformName { get; } = GetPlatformName();

        public static bool SupportsAclAnimationDecompression => OperatingSystem.IsWindows();

        public static bool SupportsDirectXShaderDecompilation => OperatingSystem.IsWindows();

        public static bool SupportsFmodAudioConversion =>
            TryGetFmodAudioConversionSupport(out _);

        public static bool TryGetAclAnimationDecompressionSupport(
            AnimationClip animationClip,
            Game game,
            out string reason)
        {
            var aclClip = animationClip?.m_MuscleClip?.m_Clip?.m_ACLClip;
            if (aclClip?.IsSet != true)
            {
                reason = string.Empty;
                return true;
            }

            return TryGetAclAnimationDecompressionSupport(aclClip, game, out reason);
        }

        public static bool TryGetAclAnimationDecompressionSupport(
            ACLClip aclClip,
            Game game,
            out string reason)
        {
            if (!SupportsAclAnimationDecompression)
            {
                reason = $"ACL animation decompression is disabled on {PlatformName}.";
                return false;
            }

            var libraryName = GetAclLibraryName(aclClip, game);
            if (libraryName != null && !TryGetNativeLibrarySupport(libraryName, out reason))
            {
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool TryGetDirectXShaderDecompilationSupport(out string reason)
        {
            if (!SupportsDirectXShaderDecompilation)
            {
                reason = $"DirectX shader decompilation is not available on {PlatformName}.";
                return false;
            }

            return TryGetNativeLibrarySupport("HLSLDecompiler", out reason);
        }

        public static bool TryGetFmodAudioConversionSupport(out string reason)
        {
            return TryGetNativeLibrarySupport("fmod", out reason);
        }

        internal static void EnsureFmodAudioConversionAvailable()
        {
            EnsureNativeLibraryAvailable("fmod");
        }

        internal static void EnsureNativeLibraryAvailable(string libraryName)
        {
            if (!TryGetNativeLibrarySupport(libraryName, out var reason))
            {
                throw new PlatformNotSupportedException(reason);
            }
        }

        internal static PlatformNotSupportedException CreateNativeLibraryUnavailableException(
            string libraryName)
        {
            return new PlatformNotSupportedException(
                $"Native library '{libraryName}' became unavailable on {PlatformName}.");
        }

        private static bool TryGetNativeLibrarySupport(string libraryName, out string reason)
        {
            if (DllLoader.IsLibraryAvailable(libraryName, UtilityAssembly))
            {
                reason = string.Empty;
                return true;
            }

            reason = $"Native library '{libraryName}' is unavailable on {PlatformName}.";
            return false;
        }

        private static string GetAclLibraryName(ACLClip aclClip, Game game)
        {
            if (game.Type.IsSRGroup())
            {
                return "sracl";
            }

            return aclClip switch
            {
                GIACLClip => "acldb",
                MHYACLClip when game.Type.IsZZZ() => "acldb_zzz",
                MHYACLClip => "acl",
                _ => null,
            };
        }

        private static string GetPlatformName()
        {
            if (OperatingSystem.IsWindows())
            {
                return "Windows";
            }

            if (OperatingSystem.IsLinux())
            {
                return "Linux";
            }

            if (OperatingSystem.IsMacOS())
            {
                return "macOS";
            }

            return "this platform";
        }
    }
}
