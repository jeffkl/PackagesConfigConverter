using NuGet.Versioning;

namespace ConsoleApp
{
    public static class NuGetVersionExtensions
    {
        public static string ToMajorMinorPatchString(this NuGetVersion version)
        {
            return $"{version.Major}.{version.Minor}.{version.Patch}";
        }
    }
}
