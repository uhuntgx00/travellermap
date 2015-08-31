﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace Maps
{
    public static class Util
    {
        public const string MediaTypeName_Image_Png = "image/png";
        public static readonly Encoding UTF8_NO_BOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static string FixCapitalization(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            bool leading = true;
            foreach (char c in s)
            {
                if (Char.IsLetter(c) || c == '\'')
                {
                    if (leading)
                    {
                        sb.Append(Char.ToUpperInvariant(c));
                        leading = false;
                    }
                    else
                    {
                        sb.Append(Char.ToLowerInvariant(c));
                    }
                }
                else
                {
                    sb.Append(c);
                    leading = true;
                }
            }

            return sb.ToString();
        }

        public static void Swap<T>(ref T a, ref T b)
        {
            T tmp = a;
            a = b;
            b = tmp;
        }

        public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0)
                return min;
            else if (value.CompareTo(max) > 0)
                return max;
            else
                return value;
        }

        public static bool InRange<T>(T item, T a, T b) where T : IComparable<T> { return item.CompareTo(a) >= 0 && item.CompareTo(b) <= 0; }

        public static bool InList<T>(T item, T o1, T o2) { return item.Equals(o1) || item.Equals(o2); }
        public static bool InList<T>(T item, T o1, T o2, T o3) { return item.Equals(o1) || item.Equals(o2) || item.Equals(o3); }
        public static bool InList<T>(T item, T o1, T o2, T o3, T o4) { return item.Equals(o1) || item.Equals(o2) || item.Equals(o3) || item.Equals(o4); }
        public static bool InList<T>(T item, T o1, T o2, T o3, T o4, T o5) { return item.Equals(o1) || item.Equals(o2) || item.Equals(o3) || item.Equals(o4) || item.Equals(o5); }
        public static bool InList<T>(T item, T o1, T o2, T o3, T o4, T o5, T o6) { return item.Equals(o1) || item.Equals(o2) || item.Equals(o3) || item.Equals(o4) || item.Equals(o5) || item.Equals(o6); }

        public static string Truncate(this string value, int size)
        {
            return value.Length <= size ? value : value.Substring(0, size);
        }

        public static Stream ToStream(this string str, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            MemoryStream stream = new MemoryStream();
            using (StreamWriter writer = new NoCloseStreamWriter(stream, encoding))
            {
                writer.Write(str);
                writer.Flush();
            }
            stream.Position = 0;
            return stream;
        }

        // TODO: Could be a variant of Enumerable.Range(...).Select(...)
        public static IEnumerable<int> Sequence(int start, int end)
        {
            int c = start, d = (start < end) ? 1 : -1;
            yield return c;
            while (c != end)
            {
                c += d;
                yield return c;
            }
        }

        public static string SafeSubstring(string s, int start, int length)
        {
            if (start > s.Length)
                return string.Empty;
            if (start + length > s.Length)
                return s.Substring(start);
            return s.Substring(start, length);
        }


        public static Func<A1, A2, R> Memoize2<A1, A2, R>(this Func<A1, A2, R> f)
        {
            var map = new Dictionary<Tuple<A1, A2>, R>();
            return (a1, a2) =>
            {
                var key = Tuple.Create(a1, a2);
                R value;
                if (map.TryGetValue(key, out value))
                    return value;
                value = f(a1, a2);
                map.Add(key, value);
                return value;
            };
        }

    }

    // Like Regex, but takes shell-style globs:
    //  - case insensitive by default
    //  - * matches 0-or-more of anything
    //  - ? matches exactly one of anything
    [Serializable]
    public class Glob : Regex
    {
        public Glob(string pattern, RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.Singleline)
            : base("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", options) { }
        protected Glob(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    [Serializable]
    public class RegexDictionary<T> : Dictionary<Regex, T>
    {
        public RegexDictionary() { }
        protected RegexDictionary(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public virtual void Add(string r, T v) { Add(new Regex(r), v); }
        public virtual void Add(T v) { Add(new Regex("^" + Regex.Escape(v.ToString()) + "$"), v); }

        public T Match(string s)
        {
            return this.FirstOrDefault(pair => pair.Key.IsMatch(s)).Value;
        }
        public bool IsMatch(string s)
        {
            return this.Any(pair => pair.Key.IsMatch(s));
        }
    }

    [Serializable]
    public class GlobDictionary<T> : RegexDictionary<T>
    {
        public GlobDictionary() { }
        protected GlobDictionary(SerializationInfo info, StreamingContext context) : base(info, context) { }

        public override void Add(string r, T v) { Add(new Glob(r), v); }
        public override void Add(T v) { Add(new Glob(v.ToString()), v); }
    }

    // Don't close the underlying stream when disposed; must be disposed
    // before the underlying stream is.
    public class NoCloseStreamReader : StreamReader
    {
        public NoCloseStreamReader(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
            : base(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize)
        {
        }

        protected override void Dispose(bool disposeManaged)
        {
            base.Dispose(false);
        }
    }

    // Don't close the underlying stream when disposed; must be disposed
    // before the underlying stream is.
    public class NoCloseStreamWriter : StreamWriter
    {
        public NoCloseStreamWriter(Stream stream, Encoding encoding)
            : base(stream, encoding)
        {
        }

        protected override void Dispose(bool disposeManaged)
        {
            base.Dispose(false);
        }
    }

    sealed public class OrderedHashSet<T> : IEnumerable<T>
    {
        private List<T> m_list = new List<T>();
        private HashSet<T> m_set = new HashSet<T>();

        public void Add(T item)
        {
            if (m_set.Contains(item))
                return;
            m_list.Add(item);
            m_set.Add(item);
        }

        public void AddRange(IEnumerable<T> collection)
        {
            foreach (var item in collection)
                Add(item);
        }

        public void Remove(T item)
        {
            if (m_set.Remove(item))
                m_list.Remove(item);
        }

        public bool Contains(T item)
        {
            return m_set.Contains(item);
        }

        public void Clear()
        {
            m_list.Clear();
            m_set.Clear();
        }

        public bool Any(Func<T, bool> predicate)
        {
            return m_set.Any(predicate);
        }

        public IEnumerable<T> Where(Func<T, bool> predicate)
        {
            return m_list.Where(predicate);
        }

        public int Count() { return m_set.Count(); }

        public T this[int index] { get { return m_list[index]; } }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return m_list.GetEnumerator();
        }
    }

    public class ErrorLogger
    {
        public enum Severity {
            Fatal,
            Error,
            Warning,
            Hint
        }

        private struct Record
        {
            public Record(Severity severity, string message)
            {
                this.severity = severity;
                this.message = message;
            }
            public Severity severity;
            public string message;
        }

        public ErrorLogger() { }

        public void Log(Severity sev, string message)
        {
            log.Add(new Record(sev, message));
        }
        public void Log(Severity sev, string message, int lineNumber, string line)
        {
            log.Add(new Record(sev, String.Format("{0}, line {1}: {2}", message, lineNumber, line)));
        }
        public void Fatal(string message) { Log(Severity.Fatal, message); }
        public void Fatal(string message, int lineNumber, string line) { Log(Severity.Fatal, message, lineNumber, line); }
        public void Error(string message) { Log(Severity.Error, message); }
        public void Error(string message, int lineNumber, string line) { Log(Severity.Error, message, lineNumber, line); }
        public void Warning(string message) { Log(Severity.Warning, message); }
        public void Warning(string message, int lineNumber, string line) { Log(Severity.Warning, message, lineNumber, line); }
        public void Hint(string message) { Log(Severity.Hint, message); }
        public void Hint(string message, int lineNumber, string line) { Log(Severity.Hint, message, lineNumber, line); }

        private List<Record> log = new List<Record>();

        public bool Empty { get { return log.Count == 0; } }
        public int Count { get { return log.Count; } }

        public void Report(TextWriter writer)
        {
            foreach (var record in log)
            {
                writer.WriteLine("{0}: {1}", record.severity.ToString(), record.message);
            }
        }

        public override string ToString()
        {
            using (StringWriter writer = new StringWriter())
            {
                Report(writer);
                return writer.ToString();
            }
        }

        public void Prepend(Severity severity, string message)
        {
            log.Insert(0, new Record(severity, message));
        }
    }
}
