using System;
using System.Runtime.InteropServices;
using AnimeStudio.PInvoke;

namespace FMOD
{
    public static class VERSION
    {
        public const uint number = 0x00020314;
        public const string dll = "fmod";
    }

    public enum RESULT : int
    {
        OK,
        ERR_BADCOMMAND,
        ERR_CHANNEL_ALLOC,
        ERR_CHANNEL_STOLEN,
        ERR_DMA,
        ERR_DSP_CONNECTION,
        ERR_DSP_DONTPROCESS,
        ERR_DSP_FORMAT,
        ERR_DSP_INUSE,
        ERR_DSP_NOTFOUND,
        ERR_DSP_RESERVED,
        ERR_DSP_SILENCE,
        ERR_DSP_TYPE,
        ERR_FILE_BAD,
        ERR_FILE_COULDNOTSEEK,
        ERR_FILE_DISKEJECTED,
        ERR_FILE_EOF,
        ERR_FILE_ENDOFDATA,
        ERR_FILE_NOTFOUND,
        ERR_FORMAT,
        ERR_HEADER_MISMATCH,
        ERR_HTTP,
        ERR_HTTP_ACCESS,
        ERR_HTTP_PROXY_AUTH,
        ERR_HTTP_SERVER_ERROR,
        ERR_HTTP_TIMEOUT,
        ERR_INITIALIZATION,
        ERR_INITIALIZED,
        ERR_INTERNAL,
        ERR_INVALID_FLOAT,
        ERR_INVALID_HANDLE,
        ERR_INVALID_PARAM,
        ERR_INVALID_POSITION,
        ERR_INVALID_SPEAKER,
        ERR_INVALID_SYNCPOINT,
        ERR_INVALID_THREAD,
        ERR_INVALID_VECTOR,
        ERR_MAXAUDIBLE,
        ERR_MEMORY,
        ERR_MEMORY_CANTPOINT,
        ERR_NEEDS3D,
        ERR_NEEDSHARDWARE,
        ERR_NET_CONNECT,
        ERR_NET_SOCKET_ERROR,
        ERR_NET_URL,
        ERR_NET_WOULD_BLOCK,
        ERR_NOTREADY,
        ERR_OUTPUT_ALLOCATED,
        ERR_OUTPUT_CREATEBUFFER,
        ERR_OUTPUT_DRIVERCALL,
        ERR_OUTPUT_FORMAT,
        ERR_OUTPUT_INIT,
        ERR_OUTPUT_NODRIVERS,
        ERR_PLUGIN,
        ERR_PLUGIN_MISSING,
        ERR_PLUGIN_RESOURCE,
        ERR_PLUGIN_VERSION,
        ERR_RECORD,
        ERR_REVERB_CHANNELGROUP,
        ERR_REVERB_INSTANCE,
        ERR_SUBSOUNDS,
        ERR_SUBSOUND_ALLOCATED,
        ERR_SUBSOUND_CANTMOVE,
        ERR_TAGNOTFOUND,
        ERR_TOOMANYCHANNELS,
        ERR_TRUNCATED,
        ERR_UNIMPLEMENTED,
        ERR_UNINITIALIZED,
        ERR_UNSUPPORTED,
        ERR_VERSION,
        ERR_EVENT_ALREADY_LOADED,
        ERR_EVENT_LIVEUPDATE_BUSY,
        ERR_EVENT_LIVEUPDATE_MISMATCH,
        ERR_EVENT_LIVEUPDATE_TIMEOUT,
        ERR_EVENT_NOTFOUND,
        ERR_STUDIO_UNINITIALIZED,
        ERR_STUDIO_NOT_LOADED,
        ERR_INVALID_STRING,
        ERR_ALREADY_LOCKED,
        ERR_NOT_LOCKED,
        ERR_RECORD_DISCONNECTED,
        ERR_TOOMANYSAMPLES,
    }

    public enum OUTPUTTYPE : int
    {
        AUTODETECT,
        UNKNOWN,
        NOSOUND,
        WAVWRITER,
        NOSOUND_NRT,
        WAVWRITER_NRT,
        WASAPI,
        ASIO,
        PULSEAUDIO,
        ALSA,
        COREAUDIO,
        AUDIOTRACK,
        OPENSL,
        AUDIOOUT,
        AUDIO3D,
        WEBAUDIO,
        NNAUDIO,
        WINSONIC,
        AAUDIO,
        AUDIOWORKLET,
        PHASE,
        OHAUDIO,
        MAX,
    }

    public enum CHANNELORDER : int
    {
        DEFAULT,
        WAVEFORMAT,
        PROTOOLS,
        ALLMONO,
        ALLSTEREO,
        ALSA,
        MAX,
    }

    [Flags]
    public enum INITFLAGS : uint
    {
        NORMAL = 0x00000000,
        STREAM_FROM_UPDATE = 0x00000001,
        MIX_FROM_UPDATE = 0x00000002,
        _3D_RIGHTHANDED = 0x00000004,
        CLIP_OUTPUT = 0x00000008,
        CHANNEL_LOWPASS = 0x00000100,
        CHANNEL_DISTANCEFILTER = 0x00000200,
        PROFILE_ENABLE = 0x00010000,
        VOL0_BECOMES_VIRTUAL = 0x00020000,
        GEOMETRY_USECLOSEST = 0x00040000,
        PREFER_DOLBY_DOWNMIX = 0x00080000,
        THREAD_UNSAFE = 0x00100000,
        PROFILE_METER_ALL = 0x00200000,
        MEMORY_TRACKING = 0x00400000,
    }

    public enum SOUND_TYPE : int
    {
        UNKNOWN,
        AIFF,
        ASF,
        DLS,
        FLAC,
        FSB,
        IT,
        MIDI,
        MOD,
        MPEG,
        OGGVORBIS,
        PLAYLIST,
        RAW,
        S3M,
        USER,
        WAV,
        XM,
        XMA,
        AUDIOQUEUE,
        AT9,
        VORBIS,
        MEDIA_FOUNDATION,
        MEDIACODEC,
        FADPCM,
        OPUS,
        MAX,
    }

    public enum SOUND_FORMAT : int
    {
        NONE,
        PCM8,
        PCM16,
        PCM24,
        PCM32,
        PCMFLOAT,
        BITSTREAM,
        MAX,
    }

    [Flags]
    public enum MODE : uint
    {
        DEFAULT = 0x00000000,
        LOOP_OFF = 0x00000001,
        LOOP_NORMAL = 0x00000002,
        LOOP_BIDI = 0x00000004,
        _2D = 0x00000008,
        _3D = 0x00000010,
        CREATESTREAM = 0x00000080,
        CREATESAMPLE = 0x00000100,
        CREATECOMPRESSEDSAMPLE = 0x00000200,
        OPENUSER = 0x00000400,
        OPENMEMORY = 0x00000800,
        OPENRAW = 0x00001000,
        OPENONLY = 0x00002000,
        ACCURATETIME = 0x00004000,
        MPEGSEARCH = 0x00008000,
        NONBLOCKING = 0x00010000,
        UNIQUE = 0x00020000,
        _3D_HEADRELATIVE = 0x00040000,
        _3D_WORLDRELATIVE = 0x00080000,
        _3D_INVERSEROLLOFF = 0x00100000,
        _3D_LINEARROLLOFF = 0x00200000,
        _3D_LINEARSQUAREROLLOFF = 0x00400000,
        IGNORETAGS = 0x02000000,
        _3D_CUSTOMROLLOFF = 0x04000000,
        LOWMEM = 0x08000000,
        OPENMEMORY_POINT = 0x10000000,
        _3D_IGNOREGEOMETRY = 0x40000000,
        VIRTUAL_PLAYFROMSTART = 0x80000000,
    }

    [Flags]
    public enum TIMEUNIT : uint
    {
        MS = 0x00000001,
        PCM = 0x00000002,
        PCMBYTES = 0x00000004,
        RAWBYTES = 0x00000008,
        PCMFRACTION = 0x00000010,
        MODORDER = 0x00000100,
        MODROW = 0x00000200,
        MODPATTERN = 0x00000400,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CREATESOUNDEXINFO
    {
        public int cbsize;
        public uint length;
        public uint fileoffset;
        public int numchannels;
        public int defaultfrequency;
        public SOUND_FORMAT format;
        public uint decodebuffersize;
        public int initialsubsound;
        public int numsubsounds;
        public IntPtr inclusionlist;
        public int inclusionlistnum;
        public IntPtr pcmreadcallback_internal;
        public IntPtr pcmsetposcallback_internal;
        public IntPtr nonblockcallback_internal;
        public IntPtr dlsname;
        public IntPtr encryptionkey;
        public int maxpolyphony;
        public IntPtr userdata;
        public SOUND_TYPE suggestedsoundtype;
        public IntPtr fileuseropen_internal;
        public IntPtr fileuserclose_internal;
        public IntPtr fileuserread_internal;
        public IntPtr fileuserseek_internal;
        public IntPtr fileuserasyncread_internal;
        public IntPtr fileuserasynccancel_internal;
        public IntPtr fileuserdata;
        public int filebuffersize;
        public CHANNELORDER channelorder;
        public IntPtr initialsoundgroup;
        public uint initialseekposition;
        public TIMEUNIT initialseekpostype;
        public int ignoresetfilesystem;
        public uint audioqueuepolicy;
        public uint minmidigranularity;
        public int nonblockthreadid;
        public IntPtr fsbguid;
    }

    public static class Factory
    {
        static Factory()
        {
            DllLoader.RegisterDllImportResolver(typeof(Factory).Assembly);
        }

        public static RESULT System_Create(out System system)
        {
            AnimeStudio.PlatformCapabilities.EnsureFmodAudioConversionAvailable();

            try
            {
                var result = NativeMethods.System_Create(out var handle, VERSION.number);
                system = result == RESULT.OK ? new System(handle) : null;
                return result;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException)
            {
                throw AnimeStudio.PlatformCapabilities
                    .CreateNativeLibraryUnavailableException(VERSION.dll);
            }
        }
    }

    public sealed class System
    {
        private IntPtr handle;

        internal System(IntPtr handle)
        {
            this.handle = handle;
        }

        public RESULT release()
        {
            var result = NativeMethods.System_Release(handle);
            if (result == RESULT.OK)
            {
                handle = IntPtr.Zero;
            }
            return result;
        }

        public RESULT setOutput(OUTPUTTYPE output)
        {
            return NativeMethods.System_SetOutput(handle, output);
        }

        public RESULT init(int maxchannels, INITFLAGS flags, IntPtr extradriverdata)
        {
            return NativeMethods.System_Init(handle, maxchannels, flags, extradriverdata);
        }

        public RESULT update()
        {
            return NativeMethods.System_Update(handle);
        }

        public RESULT getVersion(out uint version)
        {
            return NativeMethods.System_GetVersion(handle, out version, out _);
        }

        public RESULT createSound(
            byte[] data,
            MODE mode,
            ref CREATESOUNDEXINFO exinfo,
            out Sound sound)
        {
            var result = NativeMethods.System_CreateSound(
                handle,
                data,
                mode,
                ref exinfo,
                out var soundHandle);
            sound = result == RESULT.OK ? new Sound(soundHandle) : null;
            return result;
        }

        public RESULT playSound(
            Sound sound,
            ChannelGroup channelgroup,
            bool paused,
            out Channel channel)
        {
            var result = NativeMethods.System_PlaySound(
                handle,
                sound?.Handle ?? IntPtr.Zero,
                channelgroup?.Handle ?? IntPtr.Zero,
                paused,
                out var channelHandle);
            channel = result == RESULT.OK ? new Channel(channelHandle) : null;
            return result;
        }

        public RESULT getMasterSoundGroup(out SoundGroup soundgroup)
        {
            var result = NativeMethods.System_GetMasterSoundGroup(
                handle,
                out var soundGroupHandle);
            soundgroup = result == RESULT.OK ? new SoundGroup(soundGroupHandle) : null;
            return result;
        }
    }

    public sealed class Sound
    {
        private IntPtr handle;

        internal Sound(IntPtr handle)
        {
            this.handle = handle;
        }

        internal IntPtr Handle => handle;

        public bool isValid()
        {
            return handle != IntPtr.Zero;
        }

        public RESULT release()
        {
            var result = NativeMethods.Sound_Release(handle);
            if (result == RESULT.OK)
            {
                handle = IntPtr.Zero;
            }
            return result;
        }

        public RESULT @lock(
            uint offset,
            uint length,
            out IntPtr ptr1,
            out IntPtr ptr2,
            out uint len1,
            out uint len2)
        {
            return NativeMethods.Sound_Lock(
                handle, offset, length, out ptr1, out ptr2, out len1, out len2);
        }

        public RESULT unlock(IntPtr ptr1, IntPtr ptr2, uint len1, uint len2)
        {
            return NativeMethods.Sound_Unlock(handle, ptr1, ptr2, len1, len2);
        }

        public RESULT getDefaults(out float frequency, out int priority)
        {
            return NativeMethods.Sound_GetDefaults(handle, out frequency, out priority);
        }

        public RESULT getSubSound(int index, out Sound subsound)
        {
            var result = NativeMethods.Sound_GetSubSound(
                handle,
                index,
                out var subSoundHandle);
            subsound = result == RESULT.OK ? new Sound(subSoundHandle) : null;
            return result;
        }

        public RESULT getLength(out uint length, TIMEUNIT lengthtype)
        {
            return NativeMethods.Sound_GetLength(handle, out length, lengthtype);
        }

        public RESULT getFormat(
            out SOUND_TYPE type,
            out SOUND_FORMAT format,
            out int channels,
            out int bits)
        {
            return NativeMethods.Sound_GetFormat(
                handle, out type, out format, out channels, out bits);
        }

        public RESULT getNumSubSounds(out int numsubsounds)
        {
            return NativeMethods.Sound_GetNumSubSounds(handle, out numsubsounds);
        }

        public RESULT setMode(MODE mode)
        {
            return NativeMethods.Sound_SetMode(handle, mode);
        }
    }

    public sealed class Channel
    {
        private readonly IntPtr handle;

        internal Channel(IntPtr handle)
        {
            this.handle = handle;
        }

        public RESULT getFrequency(out float frequency)
        {
            return NativeMethods.Channel_GetFrequency(handle, out frequency);
        }

        public RESULT setPosition(uint position, TIMEUNIT postype)
        {
            return NativeMethods.Channel_SetPosition(handle, position, postype);
        }

        public RESULT getPosition(out uint position, TIMEUNIT postype)
        {
            return NativeMethods.Channel_GetPosition(handle, out position, postype);
        }

        public RESULT stop()
        {
            return NativeMethods.Channel_Stop(handle);
        }

        public RESULT setPaused(bool paused)
        {
            return NativeMethods.Channel_SetPaused(handle, paused);
        }

        public RESULT getPaused(out bool paused)
        {
            return NativeMethods.Channel_GetPaused(handle, out paused);
        }

        public RESULT setMode(MODE mode)
        {
            return NativeMethods.Channel_SetMode(handle, mode);
        }

        public RESULT isPlaying(out bool isplaying)
        {
            return NativeMethods.Channel_IsPlaying(handle, out isplaying);
        }
    }

    public sealed class ChannelGroup
    {
        internal ChannelGroup(IntPtr handle)
        {
            Handle = handle;
        }

        internal IntPtr Handle { get; }
    }

    public sealed class SoundGroup
    {
        private readonly IntPtr handle;

        internal SoundGroup(IntPtr handle)
        {
            this.handle = handle;
        }

        public RESULT setVolume(float volume)
        {
            return NativeMethods.SoundGroup_SetVolume(handle, volume);
        }
    }

    internal static class NativeMethods
    {
        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_Create")]
        internal static extern RESULT System_Create(out IntPtr system, uint headerversion);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_Release")]
        internal static extern RESULT System_Release(IntPtr system);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_SetOutput")]
        internal static extern RESULT System_SetOutput(IntPtr system, OUTPUTTYPE output);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_Init")]
        internal static extern RESULT System_Init(
            IntPtr system, int maxchannels, INITFLAGS flags, IntPtr extradriverdata);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_Update")]
        internal static extern RESULT System_Update(IntPtr system);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_GetVersion")]
        internal static extern RESULT System_GetVersion(
            IntPtr system, out uint version, out uint buildnumber);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_CreateSound")]
        internal static extern RESULT System_CreateSound(
            IntPtr system,
            byte[] data,
            MODE mode,
            ref CREATESOUNDEXINFO exinfo,
            out IntPtr sound);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_PlaySound")]
        internal static extern RESULT System_PlaySound(
            IntPtr system,
            IntPtr sound,
            IntPtr channelgroup,
            bool paused,
            out IntPtr channel);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_System_GetMasterSoundGroup")]
        internal static extern RESULT System_GetMasterSoundGroup(
            IntPtr system, out IntPtr soundgroup);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_Release")]
        internal static extern RESULT Sound_Release(IntPtr sound);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_Lock")]
        internal static extern RESULT Sound_Lock(
            IntPtr sound,
            uint offset,
            uint length,
            out IntPtr ptr1,
            out IntPtr ptr2,
            out uint len1,
            out uint len2);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_Unlock")]
        internal static extern RESULT Sound_Unlock(
            IntPtr sound, IntPtr ptr1, IntPtr ptr2, uint len1, uint len2);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_GetDefaults")]
        internal static extern RESULT Sound_GetDefaults(
            IntPtr sound, out float frequency, out int priority);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_GetSubSound")]
        internal static extern RESULT Sound_GetSubSound(
            IntPtr sound, int index, out IntPtr subsound);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_GetLength")]
        internal static extern RESULT Sound_GetLength(
            IntPtr sound, out uint length, TIMEUNIT lengthtype);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_GetFormat")]
        internal static extern RESULT Sound_GetFormat(
            IntPtr sound,
            out SOUND_TYPE type,
            out SOUND_FORMAT format,
            out int channels,
            out int bits);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_GetNumSubSounds")]
        internal static extern RESULT Sound_GetNumSubSounds(
            IntPtr sound, out int numsubsounds);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Sound_SetMode")]
        internal static extern RESULT Sound_SetMode(IntPtr sound, MODE mode);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Channel_GetFrequency")]
        internal static extern RESULT Channel_GetFrequency(
            IntPtr channel, out float frequency);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Channel_SetPosition")]
        internal static extern RESULT Channel_SetPosition(
            IntPtr channel, uint position, TIMEUNIT postype);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Channel_GetPosition")]
        internal static extern RESULT Channel_GetPosition(
            IntPtr channel, out uint position, TIMEUNIT postype);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Channel_Stop")]
        internal static extern RESULT Channel_Stop(IntPtr channel);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Channel_SetPaused")]
        internal static extern RESULT Channel_SetPaused(IntPtr channel, bool paused);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Channel_GetPaused")]
        internal static extern RESULT Channel_GetPaused(IntPtr channel, out bool paused);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Channel_SetMode")]
        internal static extern RESULT Channel_SetMode(IntPtr channel, MODE mode);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_Channel_IsPlaying")]
        internal static extern RESULT Channel_IsPlaying(
            IntPtr channel, out bool isplaying);

        [DllImport(VERSION.dll, EntryPoint = "FMOD5_SoundGroup_SetVolume")]
        internal static extern RESULT SoundGroup_SetVolume(
            IntPtr soundgroup, float volume);
    }
}
