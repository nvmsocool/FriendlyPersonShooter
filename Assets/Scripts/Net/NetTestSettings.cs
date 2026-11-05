using System;

namespace FriendlyPersonShooter.Net
{
    /// <summary>What this process runs.</summary>
    public enum NetMode
    {
        /// <summary>Server, one playable client and the bot allies, all in this process (the Editor default).</summary>
        All,
        /// <summary>Dedicated server only.</summary>
        Server,
        /// <summary>One playable client that connects to a server.</summary>
        Client,
        /// <summary>Bot allies only, connecting to a server.</summary>
        Bots,
    }

    /// <summary>Which game runs.</summary>
    public enum GameMode
    {
        /// <summary>One first-person arena with enemies, payloads and bot allies.</summary>
        Arena,
        /// <summary>A generated layer with landmarks, essence, shrines, caches and a gate.</summary>
        Expedition,
    }

    /// <summary>
    /// Launch settings, read from the command line:
    /// <c>-fps-game arena|expedition -fps-mode all|server|client|bots -fps-port 7980 -fps-connect 127.0.0.1
    /// -fps-world-seed 1 -fps-arena-bots 3 -fps-arena-healers 1 -fps-start host|solo -fps-join CODE -fps-profile name</c>.
    /// </summary>
    public static class NetTestSettings
    {
        public static NetMode Mode = NetMode.All;

        /// <summary>Set by <c>-fps-game</c>, by the menu, or by playing a scene named "Arena" or "Expedition" in the Editor.</summary>
        public static GameMode Game = GameMode.Expedition;

        /// <summary>True when the command line chose the game (-fps-game), which skips the playtest build's menu.</summary>
        public static bool GameGiven;

        /// <summary>
        /// Online services profile (-fps-profile name). Each profile signs in as a different anonymous player,
        /// so two copies of the game on one machine can host and join each other.
        /// </summary>
        public static string Profile;

        /// <summary>Join code from -fps-join CODE: the menu joins that game straight away (for testing host and join).</summary>
        public static string JoinCode;

        /// <summary>-fps-start host|solo: the menu hosts or starts a solo game straight away (for testing).</summary>
        public static string Start;

        /// <summary>Bot allies. Each is a full client, so it receives the game state and can aim.</summary>
        public static int ArenaBots = 3;

        /// <summary>
        /// How many bot allies play the Medic profile (Vigor: revive, heal, keep buffs up). 1 by default;
        /// pass -fps-arena-healers 0 to make the human the only healer. The other bots alternate Haste
        /// support and Might damage.
        /// </summary>
        public static int ArenaHealers = 1;

        public static ushort Port = 7980;

        /// <summary>Seed for the expedition layer (-fps-world-seed). Server and clients must agree.</summary>
        public static uint WorldSeed = 20261102;
        public static string ConnectAddress = "127.0.0.1";

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
                    case "-fps-port":
                        if (ushort.TryParse(value, out ushort port)) Port = port;
                        break;
                    case "-fps-connect":
                        ConnectAddress = value;
                        break;
                    case "-fps-game":
                        if (Enum.TryParse(value, true, out GameMode game))
                        {
                            Game = game;
                            GameGiven = true;
                        }
                        break;
                    case "-fps-start":
                        Start = value?.ToLowerInvariant();
                        break;
                    case "-fps-join":
                        JoinCode = value;
                        break;
                    case "-fps-profile":
                        Profile = value;
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
                }
            }
        }
    }
}
