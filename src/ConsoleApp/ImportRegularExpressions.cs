using System.Text.RegularExpressions;
using NuGet.Packaging.Core;

namespace ConsoleApp
{
    internal class ImportRegularExpressions : RegularExpressionsForPackagesBase
    {
        protected override Regex GetRegularExpression(PackageIdentity packageIdentity)
        {
            return new Regex($@".*{Regex.Escape(packageIdentity.Id)}\.{Regex.Escape(packageIdentity.Version.ToString())}.+{Regex.Escape(packageIdentity.Id)}\.(props|targets)", RegexOptions.IgnoreCase);
        }
    }
}