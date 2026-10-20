using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jackett.Mobile.Core.Http
{
    public static class SetCookieSplitter
    {
        // A comma starts a new cookie only when it is followed by "name=". The comma inside
        // "Expires=Wed, 21 Oct 2015 07:28:00 GMT" is followed by " 21 Oct", so it is kept.
        private static readonly Regex CookieBoundary = new Regex(@",\s*(?=[^;,\s=]+=)", RegexOptions.Compiled);

        public static IEnumerable<string> Split(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return Enumerable.Empty<string>();

            return CookieBoundary.Split(header)
                                 .Select(c => c.Trim())
                                 .Where(c => c.Length > 0);
        }
    }
}
