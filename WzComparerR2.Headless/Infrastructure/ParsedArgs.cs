using System;
using System.Collections.Generic;
using System.Linq;

namespace WzComparerR2.Headless
{
    internal sealed class ParsedArgs
    {
        private readonly Dictionary<string, string> values;
        private readonly HashSet<string> flags;

        private ParsedArgs()
        {
            this.Positionals = new List<string>();
            this.RawArguments = new List<string>();
            this.values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public List<string> Positionals { get; private set; }
        public List<string> RawArguments { get; private set; }

        public static ParsedArgs Parse(IEnumerable<string> args)
        {
            var parsed = new ParsedArgs();
            var list = args.ToList();
            parsed.RawArguments.AddRange(list);

            for (int i = 0; i < list.Count; i++)
            {
                string arg = list[i];
                if (string.Equals(arg, "--", StringComparison.Ordinal))
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        parsed.Positionals.Add(list[j]);
                    }
                    break;
                }

                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string key = arg.Substring(2);
                    if (key.Length == 0)
                    {
                        throw new UsageException("Empty option name.");
                    }

                    if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        parsed.values[key] = list[++i];
                    }
                    else
                    {
                        parsed.flags.Add(key);
                    }
                }
                else
                {
                    parsed.Positionals.Add(arg);
                }
            }

            return parsed;
        }

        public IReadOnlyList<string> GetTailArguments(int positionalCount)
        {
            if (positionalCount <= 0)
            {
                return this.RawArguments.ToList();
            }

            int seen = 0;
            for (int i = 0; i < this.RawArguments.Count; i++)
            {
                string arg = this.RawArguments[i];
                if (string.Equals(arg, "--", StringComparison.Ordinal))
                {
                    return this.RawArguments.Skip(i + 1).ToList();
                }

                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    seen++;
                    if (seen == positionalCount)
                    {
                        return this.RawArguments.Skip(i + 1).ToList();
                    }
                }
                else if (i + 1 < this.RawArguments.Count && !this.RawArguments[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    i++;
                }
            }

            return Array.Empty<string>();
        }

        public string GetValue(string key)
        {
            string value;
            return this.values.TryGetValue(key, out value) ? value : null;
        }

        public bool HasFlag(string key)
        {
            return this.flags.Contains(key) || this.values.ContainsKey(key);
        }

        public int GetInt(string key, int defaultValue)
        {
            string value = GetValue(key);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            int result;
            if (!int.TryParse(value, out result))
            {
                throw new UsageException("--" + key + " must be an integer.");
            }
            return result;
        }
    }
}
