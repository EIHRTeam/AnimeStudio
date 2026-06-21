using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AnimeStudio
{
    public enum DirectYamlMode
    {
        // DOM only. Default; baseline output is byte-for-byte unchanged.
        None,

        // Build BOTH the DOM string and the streaming (DOM-bypass) string, compare them
        // byte-for-byte, fall back to the DOM string on any mismatch, and accumulate
        // match/mismatch stats. This is the de-risking harness: it proves the streaming
        // path is byte-identical without ever risking the output. It does extra work
        // (two builds), so it is slower than baseline, not faster.
        Verify,

        // Streaming only (skips the DOM build). This is the actual speedup, but the output
        // is NOT cross-checked, so it must only be used after Verify has proven byte-identity
        // across the corpus. A one-time warning is emitted to stderr when this mode is active.
        Trust,
    }

    /// <summary>
    /// Experimental AnimationClip YAML direct-write (DOM-bypass) for keyframes, gated by the
    /// <c>ANIMESTUDIO_EXP_DIRECT_YAML</c> environment variable:
    /// <list type="bullet">
    /// <item>unset / <c>0</c> / <c>off</c> -> <see cref="DirectYamlMode.None"/></item>
    /// <item><c>1</c> / <c>verify</c> / <c>on</c> -> <see cref="DirectYamlMode.Verify"/></item>
    /// <item><c>trust</c> -> <see cref="DirectYamlMode.Trust"/></item>
    /// </list>
    /// Off by default, so the instrumented binary reproduces the baseline AnimationClip output.
    /// </summary>
    public static class DirectYamlSelfCheck
    {
        public static readonly DirectYamlMode Mode = ParseMode();

        private static long s_checked;     // clips compared (Verify)
        private static long s_matched;     // byte-identical (Verify)
        private static long s_mismatched;  // diverged, fell back to DOM (Verify)
        private static long s_trusted;     // clips emitted streaming-only (Trust)
        private static int s_warnedTrust;  // 0/1 latch for the one-time Trust warning
        private static string s_firstMismatch;

        private static DirectYamlMode ParseMode()
        {
            var s = Environment.GetEnvironmentVariable("ANIMESTUDIO_EXP_DIRECT_YAML");
            if (string.IsNullOrEmpty(s))
            {
                return DirectYamlMode.None;
            }
            switch (s.Trim().ToLowerInvariant())
            {
                case "1":
                case "on":
                case "verify":
                    return DirectYamlMode.Verify;
                case "trust":
                    return DirectYamlMode.Trust;
                default:
                    return DirectYamlMode.None;
            }
        }

        public static void RecordTrusted()
        {
            Interlocked.Increment(ref s_trusted);
            if (Interlocked.Exchange(ref s_warnedTrust, 1) == 0)
            {
                Console.Error.WriteLine(
                    "WARNING: ANIMESTUDIO_EXP_DIRECT_YAML=trust streams AnimationClip keyframes "
                    + "WITHOUT the DOM cross-check. Output is unverified for this run; use =verify "
                    + "first to confirm byte-identity against the golden hash.");
            }
        }

        // Compares the streaming output against the authoritative DOM output. Returns the bytes
        // to emit: the streaming string when identical, otherwise the DOM string (safe fallback).
        public static string ReconcileVerify(AnimationClip clip, string dom, string stream)
        {
            Interlocked.Increment(ref s_checked);
            if (string.Equals(dom, stream, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref s_matched);
                return stream;
            }

            Interlocked.Increment(ref s_mismatched);
            // Capture (and surface once) a diagnostic for the first divergence.
            if (Interlocked.CompareExchange(ref s_firstMismatch, BuildDiff(clip, dom, stream), null) == null)
            {
                Console.Error.WriteLine(s_firstMismatch);
            }
            return dom;
        }

        private static string BuildDiff(AnimationClip clip, string dom, string stream)
        {
            int min = Math.Min(dom.Length, stream.Length);
            int i = 0;
            while (i < min && dom[i] == stream[i])
            {
                i++;
            }
            int start = Math.Max(0, i - 48);
            string name = clip != null ? clip.m_Name : "(null)";
            return "WARNING: ANIMESTUDIO_EXP_DIRECT_YAML streaming output diverged from DOM for "
                + $"AnimationClip \"{name}\" at byte {i} (dom len {dom.Length}, stream len {stream.Length}); "
                + "using DOM output.\n"
                + $"  dom    ...{Excerpt(dom, start, Math.Min(dom.Length, i + 16))}\n"
                + $"  stream ...{Excerpt(stream, start, Math.Min(stream.Length, i + 16))}";
        }

        private static string Excerpt(string s, int from, int to)
        {
            var sb = new StringBuilder(to - from);
            for (int i = from; i < to; i++)
            {
                char c = s[i];
                sb.Append(c == '\n' ? "\\n" : c == '\r' ? "\\r" : c == '\t' ? "\\t" : c.ToString());
            }
            return sb.ToString();
        }

        public static void Report(TextWriter w)
        {
            if (Mode == DirectYamlMode.None)
            {
                return;
            }

            w.WriteLine();
            w.WriteLine("=== DIRECT-YAML SELF-CHECK ===");
            w.WriteLine($"mode={Mode}");
            if (Mode == DirectYamlMode.Verify)
            {
                w.WriteLine(
                    $"checked={Interlocked.Read(ref s_checked)} "
                    + $"matched={Interlocked.Read(ref s_matched)} "
                    + $"mismatched={Interlocked.Read(ref s_mismatched)}");
                var first = Volatile.Read(ref s_firstMismatch);
                if (first != null)
                {
                    w.WriteLine(first);
                }
            }
            else if (Mode == DirectYamlMode.Trust)
            {
                w.WriteLine(
                    $"trusted(streamed)={Interlocked.Read(ref s_trusted)}  (output NOT cross-checked)");
            }
            w.WriteLine("=== END DIRECT-YAML SELF-CHECK ===");
        }
    }
}
