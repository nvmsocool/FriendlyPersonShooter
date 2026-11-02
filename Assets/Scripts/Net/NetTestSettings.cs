using System;
using FriendlyPersonShooter.Core.Bots;
using FriendlyPersonShooter.Core.Lobbies;
using FriendlyPersonShooter.Core.World;

namespace FriendlyPersonShooter.Net
{
    /// <summary>What this process runs.</summary>
    public enum NetMode
    {
        /// <summary>Server, one playable client and the bots, all in this process (the Editor default).</summary>
        All,
        /// <summary>Dedicated server only.</summary>
        Server,
        /// <summary>One playable client that connects to a server.</summary>
        Client,
        /// <summary>Headless bots that connect to a server.</summary>
        Bots,
    }

    /// <summary>
    /// Settings for the networking test, read from the command line:
    /// <c>-fps-mode all|server|client|bots -fps-bots 50 -fps-port 7979 -fps-connect 127.0.0.1 -fps-grid 8 -fps-zone 40
    /// -fps-seed 1 -fps-account-base 1000 -fps-timescale 48 -fps-hour 12</c>.
    /// </summary>
    public static class NetTestSettings
    {
        public static NetMode Mode = NetMode.All;
        /// <summary>Bot accounts. Not all are online at once: the daily curve decides how many are.</summary>
        public static int BotCount = 30;

        /// <summary>Random seed for bot behavior; the same seed replays the same logins and parties.</summary>
        public static int BotSeed = 1;

        /// <summary>First bot account id. Give each bot process its own range so accounts don't collide.</summary>
        public static int AccountBase = 1000;

        /// <summary>Game seconds per real second for bot sessions; see <see cref="BotSimSettings.TimeScale"/>.</summary>
        public static double TimeScale = 48;

        /// <summary>Game hour of day the bot run starts at.</summary>
        public static double StartHour = 12;
        public static ushort Port = 7979;
        public static string ConnectAddress = "127.0.0.1";

        /// <summary>World is GridSize x GridSize zones. Small and fast for testing; the real game is far larger.</summary>
        public static int GridSize = 16;

        /// <summary>Zone side length in metres. The real game targets 3-5 minutes to cross; the test uses ~5 seconds.</summary>
        public static float ZoneSize = 30f;

        public static float PlayerSpeed = 6f;

        /// <summary>Seconds between lobby split/merge passes on the server.</summary>
        public static float LobbyInterval = 1f;

        public static float WorldSize => GridSize * ZoneSize;

        public static WorldGrid CreateGrid() => new WorldGrid(GridSize, GridSize, ZoneSize);

        public static LobbyRules CreateRules() => new LobbyRules();

        public static BotSimSettings CreateBotSettings() => new BotSimSettings
        {
            TimeScale = TimeScale,
            StartHour = StartHour,
            PlayerSpeed = PlayerSpeed,
        };

        private static bool _parsed;

        public static void ParseCommandLine()
        {
            if (_parsed) return;
            _parsed = true;

            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                string value = args[i + 1];
                switch (args[i])
                {
                    case "-fps-mode":
                        if (Enum.TryParse(value, true, out NetMode mode)) Mode = mode;
                        break;
                    case "-fps-bots":
                        if (int.TryParse(value, out int bots)) BotCount = Math.Max(0, bots);
                        break;
                    case "-fps-port":
                        if (ushort.TryParse(value, out ushort port)) Port = port;
                        break;
                    case "-fps-connect":
                        ConnectAddress = value;
                        break;
                    case "-fps-grid":
                        if (int.TryParse(value, out int grid)) GridSize = Math.Max(1, grid);
                        break;
                    case "-fps-account-base":
                        if (int.TryParse(value, out int accountBase)) AccountBase = Math.Max(1, accountBase);
                        break;
                    case "-fps-seed":
                        if (int.TryParse(value, out int seed)) BotSeed = seed;
                        break;
                    case "-fps-timescale":
                        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double scale)) TimeScale = Math.Max(1, scale);
                        break;
                    case "-fps-hour":
                        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double hour)) StartHour = hour;
                        break;
                    case "-fps-zone":
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float zone)) ZoneSize = Math.Max(1f, zone);
                        break;
                }
            }
        }
    }
}
