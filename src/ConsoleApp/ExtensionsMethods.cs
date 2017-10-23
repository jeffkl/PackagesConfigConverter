using System;
using System.Collections.Generic;
using System.Linq;

namespace ConsoleApp
{
    internal static class ExtensionsMethods
    {
        private static readonly char[] ArgumentValueSeparators = { ';', ',' };
        private static readonly char[] ArgumentNameSeparators = { ':' };

        public static IEnumerable<string> GetCommandLineArgumentValues(this string[] args, params string[] argumentNames)
        {
            foreach (string arg in args.Where(i => argumentNames.Any(x => i.StartsWith(x, StringComparison.OrdinalIgnoreCase))).Select(i => i.Split(ArgumentNameSeparators, 2).LastOrDefault()).Where(y => !String.IsNullOrWhiteSpace(y)))
            {
                foreach (string item in arg.Split(ArgumentValueSeparators, StringSplitOptions.RemoveEmptyEntries).Select(i => i.Trim()).Where(i => !String.IsNullOrWhiteSpace(i)))
                {
                    yield return item;
                }
            }
        }
    }
}