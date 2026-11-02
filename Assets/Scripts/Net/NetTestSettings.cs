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

    /// <summary>Which test runs.</summary>
    public enum GameMode
    {
        /// <summary>MVP step 1: cube players and bots on the wrapping grid, lobbies splitting and merging.</summary>
        Lobbies,
        /// <summary>MVP steps 2-3: one first-person arena with enemies, payloads and bot allies.</summary>
        Arena,
        /// <summary>MVP step 4: a generated layer-1 grid with landmarks, essence, shrines, caches and one gate.</summary>
        Expedition,
    }

    /// <summary>
    /// Settings for the networking test, read from the command line:
    /// <c>-fps-mode all|server|client|bots -fps-bots 50 -fps-port 7979 -fps-connect 127.0.0.1 -fps-grid 8 -fps-zone 40
    /// -fps-seed 1 -fps-account-base 1000 -fps-timescale 48 -fps-hour 12 -fps-approach 1</c>.
    /// </summary>
    public static class NetTestSettings
    {
        public static NetMode Mode = NetMode.All;

        /// <summary>Set by <c>-fps-game arena</c>, or by playing a scene named "Arena" in the Editor.</summary>
        public static GameMode Game = GameMode.Lobbies;

        /// <summary>Bot allies in the arena. Each is a full client, so it receives the game state and can aim.</summary>
        public static int ArenaBots = 3;

        /// <summary>
        /// How many arena bots play the Medic profile (Vigor: revive, heal, keep buffs up). 1 by default
        /// for step 3's four-player co-op; pass -fps-arena-healers 0 to make the human the only healer
        /// (the step 2 test). The other bots alternate Haste support and Might damage.
        /// </summary>
        public static int ArenaHealers = 1;
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
        /// <summary>True when the port came from the command line.</summary>
        public static bool PortGiven;
        /// <summary>The arena's default port, so it can run next to a lobby test.</summary>
        public const ushort ArenaPort = 7980;

        /// <summary>True for the first-person modes (arena and expedition), which share the arena's systems.</summary>
        public static bool IsFirstPerson => Game == GameMode.Arena || Game == GameMode.Expedition;

        /// <summary>Seed for the expedition layer (-fps-world-seed). Server and clients must agree.</summary>
        public static uint WorldSeed = 20261102;
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

        /// <summary>Matchmaker weight for lobbies heading toward each other (-fps-approach); null keeps the default.</summary>
        public static double? ApproachWeight;

        public static LobbyRules CreateRules()
        {
            var rules = new LobbyRules();
            if (ApproachWeight.HasValue) rules.ApproachWeight = ApproachWeight.Value;
            return rules;
        }

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
                        if (ushort.TryParse(value, out ushort port))
                        {
                            Port = port;
                            PortGiven = true;
                        }
                        break;
                    case "-fps-connect":
                        ConnectAddress = value;
                        break;
                    case "-fps-grid":
                        if (int.TryParse(value, out int grid)) GridSize = Math.Max(1, grid);
                        break;
                    case "-fps-game":
                        if (Enum.TryParse(value, true, out GameMode game)) Game = game;
                        break;
                    case "-fps-world-seed":
                        if (uint.TryParse(value, out uint worldSeed)) WorldSeed = worldSeed;
                        break;
                    case "-fps-arena-bots":
                        if (int.TryParse(value, out int arenaBots)) ArenaBots = Math.Max(0, arenaBots);
                        break;
                    case "-fps-arena-healers":
                        if (int.TryParse(value, out int healers)) ArenaHealers = Math.Max(0, healers);
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
                    case "-fps-approach":
                        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double approach)) ApproachWeight = approach;
                        break;
                }
            }
        }
    }
}
