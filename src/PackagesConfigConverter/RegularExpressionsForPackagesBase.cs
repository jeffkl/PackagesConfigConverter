// Copyright (c) Jeff Kluge. All rights reserved.
//
// Licensed under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NuGet.Packaging.Core;

namespace PackagesConfigConverter
{
    internal abstract class RegularExpressionsForPackagesBase : IDictionary<PackageIdentity, Regex>
    {
        protected const string SemVerPattern = @"(?<version>\d*\.\d*\.\d*\.?\d*-?[\w\d\-]*)";

        private readonly Dictionary<PackageIdentity, Regex> _regularExpressions = new();

        public int Count => _regularExpressions.Count;

        public bool IsReadOnly => false;

        public ICollection<PackageIdentity> Keys => _regularExpressions.Keys;

        public ICollection<Regex> Values => _regularExpressions.Values;

        public Regex this[PackageIdentity key]
        {
            get
            {
                if (!_regularExpressions.ContainsKey(key))
                {
                    _regularExpressions.Add(key, new Regex(GetRegularExpression(key), RegexOptions.IgnoreCase));
                }

                return _regularExpressions[key];
            }
            set => _regularExpressions[key] = value;
        }

        public void Add(KeyValuePair<PackageIdentity, Regex> item)
        {
            _regularExpressions.Add(item.Key, item.Value);
        }

        public void Add(PackageIdentity key, Regex value)
        {
            _regularExpressions.Add(key, value);
        }

        public void Clear()
        {
            _regularExpressions.Clear();
        }

        public bool Contains(KeyValuePair<PackageIdentity, Regex> item)
        {
            return _regularExpressions.ContainsKey(item.Key);
        }

        public bool ContainsKey(PackageIdentity key)
        {
            return _regularExpressions.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<PackageIdentity, Regex>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<PackageIdentity, Regex>> GetEnumerator()
        {
            return _regularExpressions.GetEnumerator();
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
            return _regularExpressions.TryGetValue(key, out value);
        }

        protected abstract string GetRegularExpression(PackageIdentity packageIdentity);
    }
}