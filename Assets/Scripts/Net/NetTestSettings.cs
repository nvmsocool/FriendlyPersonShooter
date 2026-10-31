using System;
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
    /// <c>-fps-mode all|server|client|bots -fps-bots 50 -fps-port 7979 -fps-connect 127.0.0.1 -fps-grid 8 -fps-zone 40</c>.
    /// </summary>
    public static class NetTestSettings
    {
        public static NetMode Mode = NetMode.All;
        public static int BotCount = 20;
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
                    case "-fps-zone":
                        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float zone)) ZoneSize = Math.Max(1f, zone);
                        break;
                }
            }
        }
    }
}
