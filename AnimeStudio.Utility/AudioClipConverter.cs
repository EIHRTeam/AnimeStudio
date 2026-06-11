using FMOD;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AnimeStudio
{
    public class AudioClipConverter
    {
        private AudioClip m_AudioClip;

        public AudioClipConverter(AudioClip audioClip)
        {
            m_AudioClip = audioClip;
        }

        public byte[] ConvertToWav()
        {
            var m_AudioData = m_AudioClip.m_AudioData.GetData();
            if (m_AudioData == null || m_AudioData.Length == 0)
                return null;

            PlatformCapabilities.EnsureFmodAudioConversionAvailable();

            var exinfo = new CREATESOUNDEXINFO();
            var result = Factory.System_Create(out var system);
            if (result != RESULT.OK)
                return null;

            Sound sound = null;
            Sound subsound = null;
            try
            {
                result = system.setOutput(OUTPUTTYPE.NOSOUND);
                if (result != RESULT.OK)
                    return null;

                result = system.init(1, INITFLAGS.NORMAL, IntPtr.Zero);
                if (result != RESULT.OK)
                    return null;

                exinfo.cbsize = Marshal.SizeOf(exinfo);
                exinfo.length = (uint)m_AudioClip.m_Size;
                result = system.createSound(m_AudioData, MODE.OPENMEMORY, ref exinfo, out sound);
                if (result != RESULT.OK)
                    return null;

                result = sound.getNumSubSounds(out var numsubsounds);
                if (result != RESULT.OK)
                    return null;

                if (numsubsounds <= 0)
                {
                    return SoundToWav(sound);
                }

                result = sound.getSubSound(0, out subsound);
                return result == RESULT.OK ? SoundToWav(subsound) : null;
            }
            finally
            {
                subsound?.release();
                sound?.release();
                system.release();
            }
        }

        public byte[] SoundToWav(Sound sound)
        {
            var result = sound.getFormat(out _, out _, out int channels, out int bits);
            if (result != RESULT.OK)
                return null;
            result = sound.getDefaults(out var frequency, out _);
            if (result != RESULT.OK)
                return null;
            var sampleRate = (int)frequency;
            result = sound.getLength(out var length, TIMEUNIT.PCMBYTES);
            if (result != RESULT.OK)
                return null;
            result = sound.@lock(0, length, out var ptr1, out var ptr2, out var len1, out var len2);
            if (result != RESULT.OK)
                return null;

            var pcmLength = checked((int)(len1 + len2));
            var buffer = new byte[pcmLength + 44];
            try
            {
                Encoding.UTF8.GetBytes("RIFF").CopyTo(buffer, 0);
                BitConverter.GetBytes(pcmLength + 36).CopyTo(buffer, 4);
                Encoding.UTF8.GetBytes("WAVEfmt ").CopyTo(buffer, 8);
                BitConverter.GetBytes(16).CopyTo(buffer, 16);
                BitConverter.GetBytes((short)1).CopyTo(buffer, 20);
                BitConverter.GetBytes((short)channels).CopyTo(buffer, 22);
                BitConverter.GetBytes(sampleRate).CopyTo(buffer, 24);
                BitConverter.GetBytes(sampleRate * channels * bits / 8).CopyTo(buffer, 28);
                BitConverter.GetBytes((short)(channels * bits / 8)).CopyTo(buffer, 32);
                BitConverter.GetBytes((short)bits).CopyTo(buffer, 34);
                Encoding.UTF8.GetBytes("data").CopyTo(buffer, 36);
                BitConverter.GetBytes(pcmLength).CopyTo(buffer, 40);
                Marshal.Copy(ptr1, buffer, 44, (int)len1);
                if (len2 > 0)
                {
                    Marshal.Copy(ptr2, buffer, 44 + (int)len1, (int)len2);
                }
            }
            finally
            {
                result = sound.unlock(ptr1, ptr2, len1, len2);
            }

            if (result != RESULT.OK)
                return null;
            return buffer;
        }

        public string GetExtensionName()
        {
            if (m_AudioClip.version[0] < 5)
            {
                switch (m_AudioClip.m_Type)
                {
                    case FMODSoundType.ACC:
                        return ".m4a";
                    case FMODSoundType.AIFF:
                        return ".aif";
                    case FMODSoundType.IT:
                        return ".it";
                    case FMODSoundType.MOD:
                        return ".mod";
                    case FMODSoundType.MPEG:
                        return ".mp3";
                    case FMODSoundType.OGGVORBIS:
                        return ".ogg";
                    case FMODSoundType.S3M:
                        return ".s3m";
                    case FMODSoundType.WAV:
                        return ".wav";
                    case FMODSoundType.XM:
                        return ".xm";
                    case FMODSoundType.XMA:
                        return ".wav";
                    case FMODSoundType.VAG:
                        return ".vag";
                    case FMODSoundType.AUDIOQUEUE:
                        return ".fsb";
                }

            }
            else
            {
                switch (m_AudioClip.m_CompressionFormat)
                {
                    case AudioCompressionFormat.PCM:
                        return ".fsb";
                    case AudioCompressionFormat.Vorbis:
                        return ".fsb";
                    case AudioCompressionFormat.ADPCM:
                        return ".fsb";
                    case AudioCompressionFormat.MP3:
                        return ".fsb";
                    case AudioCompressionFormat.PSMVAG:
                        return ".fsb";
                    case AudioCompressionFormat.HEVAG:
                        return ".fsb";
                    case AudioCompressionFormat.XMA:
                        return ".fsb";
                    case AudioCompressionFormat.AAC:
                        return ".m4a";
                    case AudioCompressionFormat.GCADPCM:
                        return ".fsb";
                    case AudioCompressionFormat.ATRAC9:
                        return ".fsb";
                }
            }

            return ".AudioClip";
        }

        public bool IsSupport
        {
            get
            {
                if (m_AudioClip.version[0] < 5)
                {
                    switch (m_AudioClip.m_Type)
                    {
                        case FMODSoundType.AIFF:
                        case FMODSoundType.IT:
                        case FMODSoundType.MOD:
                        case FMODSoundType.S3M:
                        case FMODSoundType.XM:
                        case FMODSoundType.XMA:
                        case FMODSoundType.AUDIOQUEUE:
                            return true;
                        default:
                            return false;
                    }
                }
                else
                {
                    switch (m_AudioClip.m_CompressionFormat)
                    {
                        case AudioCompressionFormat.PCM:
                        case AudioCompressionFormat.Vorbis:
                        case AudioCompressionFormat.ADPCM:
                        case AudioCompressionFormat.MP3:
                        case AudioCompressionFormat.XMA:
                            return true;
                        default:
                            return false;
                    }
                }
            }
        }
    }
}
