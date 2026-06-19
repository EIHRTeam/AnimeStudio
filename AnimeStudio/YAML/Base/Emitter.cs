using System;
using System.IO;
using System.Text;

namespace AnimeStudio
{
	internal class Emitter
	{
		public Emitter(TextWriter writer, bool formatKeys)
		{
			if (writer == null)
			{
				throw new ArgumentNullException(nameof(writer));
			}
			m_stream = writer;
			IsFormatKeys = formatKeys;
			if (formatKeys)
			{
				m_sb = new StringBuilder();
			}
		}

		public Emitter IncreaseIndent()
		{
			m_indent++;
			return this;
		}

		public Emitter DecreaseIndent()
		{
			if (m_indent == 0)
			{
				throw new Exception($"Increase/decrease indent mismatch");
			}
			m_indent--;
			return this;
		}

		public Emitter Write(char value)
		{
			WriteDelayed();
			m_stream.Write(value);
			return this;
		}

		public Emitter WriteRaw(char value)
		{
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(byte value)
		{
			WriteDelayed();
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(ushort value)
		{
			WriteDelayed();
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(short value)
		{
			WriteDelayed();
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(uint value)
		{
			WriteDelayed();
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(int value)
		{
			WriteDelayed();
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(ulong value)
		{
			WriteDelayed();
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(long value)
		{
			WriteDelayed();
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(float value)
		{
			WriteDelayed();
			// TextWriter.Write(float) allocates a string per value via
			// value.ToString(FormatProvider). Format into a stack span instead
			// (same null/CurrentCulture provider => byte-identical output) and write
			// the span, which StringWriter appends to its StringBuilder with no
			// intermediate string allocation. 32 chars hold any float/double
			// shortest-roundtrip form; the fallback keeps correctness if TryFormat
			// ever returns false. Verified byte-identical on Debian (anim hash
			// unchanged with the allocation-free path on vs off).
			Span<char> buf = stackalloc char[32];
			if (value.TryFormat(buf, out int written))
			{
				m_stream.Write(buf[..written]);
				return this;
			}
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(double value)
		{
			WriteDelayed();
			Span<char> buf = stackalloc char[32];
			if (value.TryFormat(buf, out int written))
			{
				m_stream.Write(buf[..written]);
				return this;
			}
			m_stream.Write(value);
			return this;
		}

		public Emitter Write(string value)
		{
			if (value.Length > 0)
			{
				WriteDelayed();
				m_stream.Write(value);
			}
			return this;
		}

		public Emitter WriteFormat(string value)
		{
			if (value.Length > 0)
			{
				WriteDelayed();
				if (value.Length > 2 && value.StartsWith("m_", StringComparison.Ordinal))
				{
					m_sb.Append(value, 2, value.Length - 2);
					if (char.IsUpper(m_sb[0]))
					{
						m_sb[0] = char.ToLower(m_sb[0]);
					}
					value = m_sb.ToString();
					m_sb.Clear();
				}
				m_stream.Write(value);
			}
			return this;
		}

		public Emitter WriteRaw(string value)
		{
			m_stream.Write(value);
			return this;
		}

		public Emitter WriteClose(char @char)
		{
			m_isNeedSeparator = false;
			m_isNeedWhitespace = false;
			m_isNeedLineBreak = false;
			return Write(@char);
		}

		public Emitter WriteClose(string @string)
		{
			m_isNeedSeparator = false;
			m_isNeedWhitespace = false;
			return Write(@string);
		}

		public Emitter WriteWhitespace()
		{
			m_isNeedWhitespace = true;
			return this;
		}

		public Emitter WriteSeparator()
		{
			m_isNeedSeparator = true;
			return this;
		}

		public Emitter WriteLine()
		{
			m_isNeedLineBreak = true;
			return this;
		}

		public void WriteMeta(MetaType type, string value)
		{
			Write('%').Write(type.ToString()).WriteWhitespace();
			Write(value).WriteLine();
		}

		public void WriteDelayed()
		{
			if (m_isNeedLineBreak)
			{
				m_stream.Write('\n');
				m_isNeedSeparator = false;
				m_isNeedWhitespace = false;
				m_isNeedLineBreak = false;
				WriteIndent();
			}
			if (m_isNeedSeparator)
			{
				m_stream.Write(',');
				m_isNeedSeparator = false;
			}
			if (m_isNeedWhitespace)
			{
				m_stream.Write(' ');
				m_isNeedWhitespace = false;
			}
		}

		private void WriteIndent()
		{
			for (int i = 0; i < m_indent * 2; i++)
			{
				m_stream.Write(' ');
			}
		}

		public bool IsFormatKeys { get; }
		public bool IsKey { get; set; }

		private readonly TextWriter m_stream;
		private readonly StringBuilder m_sb;

		private int m_indent = 0;
		private bool m_isNeedWhitespace = false;
		private bool m_isNeedSeparator = false;
		private bool m_isNeedLineBreak = false;
	}
}
