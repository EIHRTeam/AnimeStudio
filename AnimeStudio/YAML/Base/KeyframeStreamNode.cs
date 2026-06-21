using System;
using System.Collections.Generic;

namespace AnimeStudio
{
    /// <summary>
    /// Experimental, thread-scoped switch for the AnimationClip keyframe direct-write
    /// (DOM-bypass) path. When set, <see cref="AnimationCurve{T}.ExportYAML"/> emits its
    /// keyframe list through a <see cref="KeyframeStreamNodeBase{T}"/> that writes bytes
    /// straight to the <see cref="Emitter"/> instead of materializing one mapping node plus
    /// several scalar/flow-map nodes per keyframe.
    ///
    /// Default <c>false</c> reproduces the baseline DOM output byte-for-byte. The flag is
    /// thread-static because asset export runs under <c>Parallel.For</c>; each worker toggles
    /// its own copy around a single document build (see the self-check in
    /// AnimationClipExtensions.ConvertSerializedAnimationClip).
    /// </summary>
    public static class AnimationClipExportOptions
    {
        [ThreadStatic] private static bool t_streamKeyframes;

        public static bool StreamKeyframes
        {
            get => t_streamKeyframes;
            set => t_streamKeyframes = value;
        }
    }

    /// <summary>
    /// Streaming stand-in for <c>List&lt;Keyframe&lt;T&gt;&gt;.ExportYAML(version)</c>, which the
    /// DOM path materializes as a <see cref="YAMLSequenceNode"/> (Block) of keyframe
    /// <see cref="YAMLMappingNode"/>s. This node reports the same NodeType/IsMultiline/IsIndent
    /// as that sequence (so the parent mapping frames it identically) and overrides
    /// <see cref="Emit"/> to replay the exact same <see cref="Emitter"/> primitive sequence the
    /// DOM emit would, making the bytes identical by construction. The keyframe sub-tree is the
    /// dominant allocation in large AnimationClip DOMs (one clip can hold millions of keyframes,
    /// each otherwise allocating a mapping plus per-field scalar/flow-map nodes); streaming it
    /// removes those allocations. Byte-identity is guarded at runtime by the in-process
    /// self-check, which falls back to the DOM string on any mismatch.
    /// </summary>
    internal abstract class KeyframeStreamNodeBase<T> : YAMLNode where T : IYAMLExportable
    {
        private readonly List<Keyframe<T>> m_curve;
        private readonly int m_serializedVersion;
        private readonly bool m_hasWeights;

        protected KeyframeStreamNodeBase(List<Keyframe<T>> curve, int[] version)
        {
            m_curve = curve;
            // Mirrors Keyframe<T>.ToSerializedVersion + the 2018-and-up weighted fields,
            // so the emitted keys/branches match the DOM regardless of asset version.
            m_serializedVersion = version[0] >= 2018 ? 3
                : (version[0] > 5 || (version[0] == 5 && version[1] >= 5)) ? 2
                : 1;
            m_hasWeights = version[0] >= 2018;
        }

        public override YAMLNodeType NodeType => YAMLNodeType.Sequence;
        public override bool IsMultiline => m_curve.Count > 0;
        public override bool IsIndent => false;

        internal override void Emit(Emitter emitter)
        {
            // YAMLNode.Emit: no custom tag/anchor on this node => writes nothing.
            base.Emit(emitter);

            // YAMLSequenceNode.StartChildren (Block): empty sequence emits '['.
            if (m_curve.Count == 0)
            {
                emitter.Write('[');
            }

            for (int i = 0; i < m_curve.Count; i++)
            {
                Keyframe<T> kf = m_curve[i];

                // StartChild (Block, mapping child): "- " then IncreaseIndent (child.IsIndent).
                // The child mapping is not a sequence, so the NodeType==NodeType branch is skipped.
                emitter.Write('-').Write(' ');
                emitter.IncreaseIndent();

                // --- keyframe Block mapping (YAMLMappingNode.Emit) ---
                // Every value here is a scalar or a Flow mapping, i.e. not multiline and not
                // indented, so each entry is simply "key: <value>\n".
                if (m_serializedVersion > 1)
                {
                    EmitIntEntry(emitter, "serializedVersion", m_serializedVersion);
                }
                EmitFloatEntry(emitter, "time", kf.time);
                EmitLeafEntry(emitter, "value", kf.value);
                EmitLeafEntry(emitter, "inSlope", kf.inSlope);
                EmitLeafEntry(emitter, "outSlope", kf.outSlope);
                if (m_hasWeights)
                {
                    EmitIntEntry(emitter, "weightedMode", kf.weightedMode);
                    EmitLeafEntry(emitter, "inWeight", kf.inWeight);
                    EmitLeafEntry(emitter, "outWeight", kf.outWeight);
                }
                // Mapping EndChildren (Block, non-empty): WriteLine.
                emitter.WriteLine();

                // EndChild (Block, mapping child): WriteLine + DecreaseIndent.
                emitter.WriteLine();
                emitter.DecreaseIndent();
            }

            // EndChildren (Block): empty sequence emits ']'; always WriteLine.
            if (m_curve.Count == 0)
            {
                emitter.Write(']');
            }
            emitter.WriteLine();
        }

        // "key: <int>\n" — Block-map scalar entry (value not multiline/indent).
        private static void EmitIntEntry(Emitter e, string key, int value)
        {
            e.Write(key).Write(':').WriteWhitespace();
            e.Write(value);
            e.WriteLine();
        }

        // "key: <float>\n" — uses the same Emitter.Write(float) the DOM scalar uses.
        private static void EmitFloatEntry(Emitter e, string key, float value)
        {
            e.Write(key).Write(':').WriteWhitespace();
            e.Write(value);
            e.WriteLine();
        }

        // "key: <leaf>\n" — leaf is a Flow mapping (Vector3/Quaternion) or a scalar (Float).
        private void EmitLeafEntry(Emitter e, string key, T value)
        {
            e.Write(key).Write(':').WriteWhitespace();
            EmitLeaf(e, value);
            e.WriteLine();
        }

        protected abstract void EmitLeaf(Emitter e, T value);
    }

    internal sealed class Vector3KeyframeStreamNode : KeyframeStreamNodeBase<Vector3>
    {
        public Vector3KeyframeStreamNode(List<Keyframe<Vector3>> curve, int[] version)
            : base(curve, version)
        {
        }

        // Mirrors Vector3.ExportYAML (Flow mapping {x: X, y: Y, z: Z}).
        protected override void EmitLeaf(Emitter e, Vector3 v)
        {
            e.Write('{');
            e.Write("x").Write(':').WriteWhitespace(); e.Write(v.X); e.WriteSeparator().WriteWhitespace();
            e.Write("y").Write(':').WriteWhitespace(); e.Write(v.Y); e.WriteSeparator().WriteWhitespace();
            e.Write("z").Write(':').WriteWhitespace(); e.Write(v.Z); e.WriteSeparator().WriteWhitespace();
            e.WriteClose('}');
        }
    }

    internal sealed class QuaternionKeyframeStreamNode : KeyframeStreamNodeBase<Quaternion>
    {
        public QuaternionKeyframeStreamNode(List<Keyframe<Quaternion>> curve, int[] version)
            : base(curve, version)
        {
        }

        // Mirrors Quaternion.ExportYAML (Flow mapping {x: X, y: Y, z: Z, w: W}).
        protected override void EmitLeaf(Emitter e, Quaternion q)
        {
            e.Write('{');
            e.Write("x").Write(':').WriteWhitespace(); e.Write(q.X); e.WriteSeparator().WriteWhitespace();
            e.Write("y").Write(':').WriteWhitespace(); e.Write(q.Y); e.WriteSeparator().WriteWhitespace();
            e.Write("z").Write(':').WriteWhitespace(); e.Write(q.Z); e.WriteSeparator().WriteWhitespace();
            e.Write("w").Write(':').WriteWhitespace(); e.Write(q.W); e.WriteSeparator().WriteWhitespace();
            e.WriteClose('}');
        }
    }

    internal sealed class FloatKeyframeStreamNode : KeyframeStreamNodeBase<Float>
    {
        public FloatKeyframeStreamNode(List<Keyframe<Float>> curve, int[] version)
            : base(curve, version)
        {
        }

        // Mirrors Float.ExportYAML (a plain float scalar).
        protected override void EmitLeaf(Emitter e, Float value)
        {
            e.Write(value.Value);
        }
    }
}
