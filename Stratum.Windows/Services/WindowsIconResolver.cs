// Stratum.Windows - icon key resolution without Android resources.
// On Windows we render a letter-avatar, so only key matching matters.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Stratum.Core;

namespace Stratum.Windows.Services
{
    public partial class WindowsIconResolver : IIconResolver
    {
        // Subset of well-known service keys is enough: matching only needs to
        // return a stable key. Unknown issuers fall back to null (default avatar).
        private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "google", "microsoft", "apple", "amazon", "facebook", "github", "gitlab",
            "discord", "steam", "twitch", "twitter", "x", "instagram", "linkedin",
            "dropbox", "adobe", "atlassian", "aws", "azure", "cloudflare", "digitalocean",
            "hetzner", "ovh", "proton", "tuta", "bitwarden", "1password", "dashlane",
            "keeper", "lastpass", "enpass", "epicgames", "ubisoft", "blizzard",
            "electronicarts", "rockstargames", "nintendo", "playstation", "roblox",
            "riotgames", "wargaming", "gog", "humblebundle", "paypal", "stripe",
            "coinbase", "binance", "kraken", "reddit", "pinterest", "tumblr", "flickr",
            "spotify", "netflix", "twitch", "yahoo", "aol", "yandex", "vk", "ok",
            "samsung", "huawei", "xiaomi", "sony", "nvidia", "intel", "amd", "ibm",
            "oracle", "sap", "salesforce", "slack", "zoom", "discord", "telegram",
            "whatsapp", "signal", "mastodon", "wordpress", "godaddy", "namecheap",
            "ubuntuone", "mozilla", "firefox", "opera", "vivaldi", "duckduckgo",
            "steam", "origin", "uplay", "battlenet", "nexusmods", "curseforge",
            "yubico", "authy", "duo", "okta", "onelogin", "jumpcloud", "keycloak",
            "gitlab", "bitbucket", "sourceforge", "codeberg", "gitea", "docker",
            "kubernetes", "npm", "pypi", "nuget", "jetbrains", "figma", "notion",
            "evernote", "todoist", "trello", "asana", "jira", "confluence", "miro",
            "icloud", "onedrive", "googledrive", "mega", "pcloud", "tresorit",
            "protonmail", "fastmail", "tutanota", "zoho", "yandex", "mailru",
        };

        public string FindServiceKeyByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            static string Simplify(string input)
            {
                input = input.ToLowerInvariant();
                input = SimplifyRegex().Replace(input, "");
                return input.Trim();
            }

            var key = Simplify(name);

            if (KnownKeys.Contains(key))
                return key;

            var firstWordKey = Simplify(name.Split(new[] { ' ', '.' }, 2)[0]);
            return KnownKeys.Contains(firstWordKey) ? firstWordKey : null;
        }

        [GeneratedRegex("[^a-z0-9]")]
        private static partial Regex SimplifyRegex();
    }
}
