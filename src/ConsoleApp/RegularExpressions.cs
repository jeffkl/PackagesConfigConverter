using NuGet.Packaging.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PackagesConfigProjectConverter
{
    internal static class RegularExpressions
    {
        public static AnalyzerRegularExpressions Analyzers { get; } = new AnalyzerRegularExpressions();

        public static AssemblyReferenceRegularExpressions AssemblyReferences { get; } = new AssemblyReferenceRegularExpressions();

        public static ImportRegularExpressions Imports { get; } = new ImportRegularExpressions();
    }
    internal class AnalyzerRegularExpressions : RegularExpressionsForPackagesBase
    {
        protected override string GetRegularExpression(PackageIdentity packageIdentity)
        {
            return $@".*\\{Regex.Escape(packageIdentity.Id)}\.{SemVerPattern}\\analyzers\\.*";
        }
    }

    internal class AssemblyReferenceRegularExpressions : RegularExpressionsForPackagesBase
    {
        protected override string GetRegularExpression(PackageIdentity packageIdentity)
        {
            return $@".*\\{Regex.Escape(packageIdentity.Id)}\.{SemVerPattern}\\lib\\.*";
        }
    }

    internal class ImportRegularExpressions : RegularExpressionsForPackagesBase
    {
        protected override string GetRegularExpression(PackageIdentity packageIdentity)
        {
            return $@".*\\{Regex.Escape(packageIdentity.Id)}\.{SemVerPattern}\\build\\.*{Regex.Escape(packageIdentity.Id)}\.(?:props|targets)";
        }
    }

    internal abstract class RegularExpressionsForPackagesBase : IDictionary<PackageIdentity, Regex>
    {
        private readonly Dictionary<PackageIdentity, Regex> _regexes = new Dictionary<PackageIdentity, Regex>();

        protected const string SemVerPattern = @"(?<version>\d*\.\d*\.\d*\.?\d*-?[\w\d\-]*)";

        public int Count => _regexes.Count;

        public bool IsReadOnly => false;

        public ICollection<PackageIdentity> Keys => _regexes.Keys;

        public ICollection<Regex> Values => _regexes.Values;

        public Regex this[PackageIdentity key]
        {
            get
            {
                if (!_regexes.ContainsKey(key))
                {
                    _regexes.Add(key, new Regex(GetRegularExpression(key), RegexOptions.IgnoreCase));
                }

                return _regexes[key];
            }
            set => _regexes[key] = value;
        }

        public void Add(KeyValuePair<PackageIdentity, Regex> item)
        {
            _regexes.Add(item.Key, item.Value);
        }

        public void Add(PackageIdentity key, Regex value)
        {
            _regexes.Add(key, value);
        }

        public void Clear()
        {
            _regexes.Clear();
        }

        public bool Contains(KeyValuePair<PackageIdentity, Regex> item)
        {
            return _regexes.ContainsKey(item.Key);
        }

        public bool ContainsKey(PackageIdentity key)
        {
            return _regexes.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<PackageIdentity, Regex>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<PackageIdentity, Regex>> GetEnumerator()
        {
            return _regexes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Remove(KeyValuePair<PackageIdentity, Regex> item)
        {
            throw new NotImplementedException();
        }

        public bool Remove(PackageIdentity key)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(PackageIdentity key, out Regex value)
        {
            return _regexes.TryGetValue(key, out value);
        }

        protected abstract string GetRegularExpression(PackageIdentity packageIdentity);
    }
}