using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BetaSharp.Launcher.Extensions;

namespace BetaSharp.Launcher.Features;

internal sealed class XboxService(IHttpClientFactory httpClientFactory)
{
    public sealed class Profile(string token, string hash)
    {
        public string Token => token;

        public string Hash => hash;
    }


}
