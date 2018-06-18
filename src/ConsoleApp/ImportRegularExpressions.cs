using System.Text.RegularExpressions;
using NuGet.Packaging.Core;

namespace ConsoleApp
{
    internal class ImportRegularExpressions : RegularExpressionsForPackagesBase
    {
        protected override Regex GetRegularExpression(PackageIdentity packageIdentity)
        {
            // Handle the case when a package in packages.config is declared with full version, e.g. <package id="StyleCop.MSBuild" version="5.0.0.0" ...>
            // but on disk the package is cached in stylecop.msbuild\5.0.0 folder, i.e. withpuit the last zero.
            string shortVersion = $"{packageIdentity.Version.ToMajorMinorPatchString()}";

            return new Regex($@".*{Regex.Escape(packageIdentity.Id)}\.({Regex.Escape(packageIdentity.Version.ToString())}|{Regex.Escape(shortVersion)}).+{Regex.Escape(packageIdentity.Id)}\.(props|targets)", RegexOptions.IgnoreCase);
        }
    }
}