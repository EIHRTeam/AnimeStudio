using System;
using System.IO;
using System.Runtime.InteropServices;
using AnimeStudio;
using AnimeStudio.PInvoke;

namespace ACLLibs
{
    public struct DecompressedClip
    {
        public IntPtr Values;
        public int ValuesCount;
        public IntPtr Times;
        public int TimesCount;
    }

    internal static class DecompressedClipResult
    {
        public static void Copy(
            in DecompressedClip clip,
            out float[] values,
            out float[] times)
        {
            Validate(clip);

            values = new float[clip.ValuesCount];
            if (clip.ValuesCount > 0)
            {
                Marshal.Copy(clip.Values, values, 0, clip.ValuesCount);
            }

            times = new float[clip.TimesCount];
            if (clip.TimesCount > 0)
            {
                Marshal.Copy(clip.Times, times, 0, clip.TimesCount);
            }
        }

        private static void Validate(in DecompressedClip clip)
        {
            ValidateCountAndPointer(clip.ValuesCount, clip.Values, nameof(clip.ValuesCount));
            ValidateCountAndPointer(clip.TimesCount, clip.Times, nameof(clip.TimesCount));

            var resultBytes = checked(
                ((long)clip.ValuesCount + clip.TimesCount) * sizeof(float));
            var availableBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (availableBytes > 0 && resultBytes > availableBytes)
            {
                throw new InvalidDataException(
                    $"ACL result requires {resultBytes} bytes, exceeding the available memory budget of {availableBytes} bytes.");
            }
        }

        private static void ValidateCountAndPointer(int count, IntPtr pointer, string fieldName)
        {
            if (count < 0 || count > Array.MaxLength)
            {
                throw new InvalidDataException($"Invalid ACL {fieldName}: {count}.");
            }
            if (count > 0 && pointer == IntPtr.Zero)
            {
                throw new InvalidDataException($"ACL returned a null pointer for {fieldName}={count}.");
            }
        }
    }

    public static class ACL
    {
        private const string DLL_NAME = "acl";
        static ACL()
        {
            DllLoader.RegisterDllImportResolver(typeof(ACL).Assembly);
        }
        public static void DecompressAll(byte[] data, out float[] values, out float[] times)
        {
            PlatformCapabilities.EnsureNativeLibraryAvailable(DLL_NAME);
            var decompressedClip = new DecompressedClip();
            var nativeCallCompleted = false;
            try
            {
                DecompressAllNative(data, ref decompressedClip);
                nativeCallCompleted = true;
                DecompressedClipResult.Copy(decompressedClip, out values, out times);
            }
            catch (DllNotFoundException)
            {
                throw PlatformCapabilities.CreateNativeLibraryUnavailableException(DLL_NAME);
            }
            finally
            {
                if (nativeCallCompleted)
                {
                    DisposeNative(ref decompressedClip);
                }
            }
        }

        #region importfunctions

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "DecompressAll")]
        private static extern void DecompressAllNative(byte[] data, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Dispose")]
        private static extern void DisposeNative(ref DecompressedClip decompressedClip);

        #endregion
    }

    public static class SRACL
    {
        private const string DLL_NAME = "sracl";
        static SRACL()
        {
            DllLoader.RegisterDllImportResolver(typeof(SRACL).Assembly);
        }
        public static void DecompressAll(byte[] data, out float[] values, out float[] times)
        {
            PlatformCapabilities.EnsureNativeLibraryAvailable(DLL_NAME);
            var decompressedClip = new DecompressedClip();
            var nativeCallCompleted = false;
            try
            {
                DecompressAllNative(data, ref decompressedClip);
                nativeCallCompleted = true;
                DecompressedClipResult.Copy(decompressedClip, out values, out times);
            }
            catch (DllNotFoundException)
            {
                throw PlatformCapabilities.CreateNativeLibraryUnavailableException(DLL_NAME);
            }
            finally
            {
                if (nativeCallCompleted)
                {
                    DisposeNative(ref decompressedClip);
                }
            }
        }

        #region importfunctions

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "DecompressAll")]
        private static extern void DecompressAllNative(byte[] data, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Dispose")]
        private static extern void DisposeNative(ref DecompressedClip decompressedClip);

        #endregion
    }

    public static class DBACL
    {
        private const string DLL_NAME = "acldb";
        private const string DLL_NAME_ZZZ = "acldb_zzz";
        static DBACL()
        {
            DllLoader.RegisterDllImportResolver(typeof(DBACL).Assembly);
        }
        public static void DecompressTracks(byte[] data, byte[] db, out float[] values, out float[] times, bool isZZZ = false)
        {
            PlatformCapabilities.EnsureNativeLibraryAvailable(isZZZ ? DLL_NAME_ZZZ : DLL_NAME);
            var decompressedClip = new DecompressedClip();
            var dataPtr = IntPtr.Zero;
            var dbPtr = IntPtr.Zero;
            var nativeCallCompleted = false;

            try
            {
                dataPtr = Marshal.AllocHGlobal(data.Length + 8);
                var dataAligned = new IntPtr(16 * (((long)dataPtr + 15) / 16));
                Marshal.Copy(data, 0, dataPtr, data.Length);

                dbPtr = Marshal.AllocHGlobal(db.Length + 8);
                var dbAligned = new IntPtr(16 * (((long)dbPtr + 15) / 16));
                Marshal.Copy(db, 0, dbAligned, db.Length);

                // as long as m_ClipData is passed to acl_db.dll without the rest it should be fine
                // m_databaseData doesn't seem to be used. For now
                try
                {
                    if (isZZZ)
                    {
                        var streamer = new IntPtr(0);
                        DecompressTracksZZZ(dataAligned, dbAligned, streamer, ref decompressedClip);
                    }
                    else
                    {
                        DecompressTracks(dataAligned, dbAligned, ref decompressedClip);
                    }
                    nativeCallCompleted = true;
                }
                catch (DllNotFoundException)
                {
                    throw PlatformCapabilities.CreateNativeLibraryUnavailableException(
                        isZZZ ? DLL_NAME_ZZZ : DLL_NAME);
                }

                DecompressedClipResult.Copy(decompressedClip, out values, out times);
            }
            finally
            {
                try
                {
                    if (nativeCallCompleted)
                    {
                        if (isZZZ)
                        {
                            DisposeZZZ(ref decompressedClip);
                        }
                        else
                        {
                            Dispose(ref decompressedClip);
                        }
                    }
                }
                finally
                {
                    if (dataPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(dataPtr);
                    }
                    if (dbPtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(dbPtr);
                    }
                }
            }
        }

        #region importfunctions

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DecompressTracks(nint data, nint db, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Dispose(ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME_ZZZ, CallingConvention = CallingConvention.Cdecl, EntryPoint = "DecompressTracks")]
        private static extern void DecompressTracksZZZ(nint data, nint db, nint streamer, ref DecompressedClip decompressedClip);

        [DllImport(DLL_NAME_ZZZ, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Dispose")]
        private static extern void DisposeZZZ(ref DecompressedClip decompressedClip);

        #endregion
    }
}
