namespace FMOD
{
    public static class Error
    {
        public static string String(RESULT result)
        {
            return result switch
            {
                RESULT.OK => "No errors.",
                RESULT.ERR_FILE_BAD => "Error loading the audio file.",
                RESULT.ERR_FILE_NOTFOUND => "Audio file not found.",
                RESULT.ERR_FORMAT => "Unsupported audio format.",
                RESULT.ERR_HEADER_MISMATCH => "FMOD wrapper and runtime versions do not match.",
                RESULT.ERR_INITIALIZATION => "FMOD initialization failed.",
                RESULT.ERR_INVALID_HANDLE => "An invalid FMOD object handle was used.",
                RESULT.ERR_INVALID_PARAM => "An invalid parameter was passed to FMOD.",
                RESULT.ERR_MEMORY => "FMOD could not allocate enough memory.",
                RESULT.ERR_OUTPUT_INIT => "FMOD could not initialize the audio output.",
                RESULT.ERR_OUTPUT_NODRIVERS => "No audio output driver is available.",
                RESULT.ERR_UNINITIALIZED => "FMOD has not been initialized.",
                RESULT.ERR_UNSUPPORTED => "The requested FMOD operation is unsupported.",
                _ => result.ToString(),
            };
        }
    }
}
