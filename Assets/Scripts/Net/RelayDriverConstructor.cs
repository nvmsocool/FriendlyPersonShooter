using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport.Relay;

namespace FriendlyPersonShooter.Net
{
    /// <summary>
    /// Network drivers that go through a Unity Relay server, so players connect with a join code instead of an
    /// IP address and open ports. The host's server listens on the relay (plus IPC for its own client and bots,
    /// which stay in-process, whatever the Editor's play mode type); a joining client connects to the relay. Assign to
    /// <see cref="NetworkStreamReceiveSystem.DriverConstructor"/> before creating the worlds.
    /// </summary>
    public sealed class RelayDriverConstructor : INetworkStreamDriverConstructor
    {
        private RelayServerData _server;
        private RelayServerData _client;

        public RelayDriverConstructor(RelayServerData server, RelayServerData client)
        {
            _server = server;
            _client = client;
        }

        public void CreateClientDriver(World world, ref NetworkDriverStore driver, NetDebug netDebug)
        {
            // The host's own client and bots have no relay data: they always reach the server in-process.
            if (_client.Endpoint.IsValid) DefaultDriverBuilder.RegisterClientDriver(world, ref driver, netDebug, ref _client);
            else DefaultDriverBuilder.RegisterClientIpcDriver(world, ref driver, netDebug, DefaultDriverBuilder.GetNetworkClientSettings());
        }

        public void CreateServerDriver(World world, ref NetworkDriverStore driver, NetDebug netDebug) =>
            DefaultDriverBuilder.RegisterServerDriver(world, ref driver, netDebug, ref _server);
    }
}
