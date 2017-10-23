using NuGet.Packaging.Core;
using System.Text.RegularExpressions;

namespace ConsoleApp
{
    internal class AssemblyReferenceRegularExpressions : RegularExpressionsForPackagesBase
    {
        protected override Regex GetRegularExpression(PackageIdentity packageIdentity)
        {
            return new Regex($@".*{Regex.Escape(packageIdentity.Id)}\.{Regex.Escape(packageIdentity.Version.ToString())}\\.+", RegexOptions.IgnoreCase);
        }
    }
}