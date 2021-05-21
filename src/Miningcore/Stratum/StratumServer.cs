
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Miningcore.Banning;
using Miningcore.Configuration;
using Miningcore.JsonRpc;
using Miningcore.Time;
using Miningcore.Util;
using Newtonsoft.Json;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Stratum
{
    public abstract class StratumServer
    {
        protected StratumServer(IComponentContext ctx, IMasterClock clock)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));

            this.ctx = ctx;
            this.clock = clock;
        }

        static StratumServer()
        {
            if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ignoredSocketErrors = new HashSet<int>
                {
                    (int) SocketError.ConnectionReset,
                    (int) SocketError.ConnectionAborted,
                    (int) SocketError.OperationAborted
                };
            }

            else if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // see: http://www.virtsync.com/c-error-codes-include-errno
                ignoredSocketErrors = new HashSet<int>
                {
                    104, // ECONNRESET
                    125, // ECANCELED
                    103, // ECONNABORTED
                    110, // ETIMEDOUT
                    32,  // EPIPE
                };
            }
        }

        protected readonly Dictionary<string, StratumClient> clients = new Dictionary<string, StratumClient>();
        protected static readonly ConcurrentDictionary<string, X509Certificate2> certs = new ConcurrentDictionary<string, X509Certificate2>();
        protected static readonly HashSet<int> ignoredSocketErrors;
        protected static readonly MethodBase StreamWriterCtor = typeof(StreamWriter).GetConstructor(new[] { typeof(Stream), typeof(Encoding), typeof(int), typeof(bool) });

        protected readonly IComponentContext ctx;
        protected readonly IMasterClock clock;
        protected readonly Dictionary<int, Socket> ports = new Dictionary<int, Socket>();
        protected ClusterConfig clusterConfig;
        protected IBanManager banManager;
        protected ILogger logger;

        public void StartStratumServerListeners(params (IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint)[] stratumPorts)
        {
            Contract.RequiresNonNull(stratumPorts, nameof(stratumPorts));

            Task.Run(async () =>
            {
                // Setup sockets Array of all pool ports
                var sockets = stratumPorts.Select(port =>
                {
                    // Setup socket
                    var server = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    server.Bind(port.IPEndPoint);
                    server.Listen(512);

                    lock(ports)
                    {
                        ports[port.IPEndPoint.Port] = server;
                    }

                    return server;
                }).ToArray();

                logger.Info(() => $"Pool Stratum ports {string.Join(", ", stratumPorts.Select(x => $"{x.IPEndPoint.Address}:{x.IPEndPoint.Port}").ToArray())} online");

                // Setup Socket Accept Connections Tasks
                var tasks = sockets.Select(socket => socket.AcceptAsync()).ToArray();

                while(true)
                {
                    try
                    {
                        // Wait incoming connection on any of the monitored sockets
                        await Task.WhenAny(tasks);

                        // check tasks
                        for(var i = 0; i < tasks.Length; i++)
                        {
                            var task = tasks[i];
                            var port = stratumPorts[i];

                            // skip running tasks
                            if(!(task.IsCompleted || task.IsFaulted || task.IsCanceled))
                                continue;

                            // Accept Socket Connection if Successful
                            if(task.IsCompletedSuccessfully)
                            {
                                //  AcceptSocketConnection(task.Result, port);
                                // -------------------------------------------------------
                                Socket socket = task.Result;
                                var remoteEndpoint = (IPEndPoint) socket.RemoteEndPoint;
                                
                                // get rid of banned clients as early as possible
                                // IsBanned Check
                                if(banManager?.IsBanned(remoteEndpoint.Address) == true)
                                {
                                    logger.Info(() => $"Disconnecting banned ip {remoteEndpoint.Address}");
                                    socket.Close();
                                    return;
                                }

                                // Setup TLS Certificate
                                X509Certificate2 cert = null;
                                if(port.PoolEndpoint.Tls)
                                {
                                    if(!certs.TryGetValue(port.PoolEndpoint.TlsPfxFile, out cert))
                                        cert = AddCert(port);
                                }

                                // Setup Stratum Client
                                var connectionId = CorrelationIdGenerator.GetNextId();
                                var stratumClient = new StratumClient(logger, clock, connectionId);

                                // Register Client
                                Contract.RequiresNonNull(stratumClient, nameof(stratumClient));
                                lock(clients)
                                {
                                    clients[connectionId] = stratumClient;
                                }

                                OnConnect(stratumClient, port.IPEndPoint);

                                stratumClient.Run(socket, port, cert, OnClientRequest, OnClientComplete, OnClientError);

                                // ------------------------------------------------
                            }


                            // Refresh task
                            tasks[i] = sockets[i].AcceptAsync();
                        }
                    }

                    catch(ObjectDisposedException)
                    {
                        // ignored
                        break;
                    }

                    catch(Exception ex)
                    {
                        logger.Error(ex);
                    }
                }
            });
        }

 
        public void StopListeners()
        {
            lock(ports)
            {
                var portValues = ports.Values.ToArray();

                for(int i = 0; i < portValues.Length; i++)
                {
                    var socket = portValues[i];

                    socket.Close();
                }
            }
        }

        protected virtual void UnregisterClient(StratumClient client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            var subscriptionId = client.ConnectionId;

            if(!string.IsNullOrEmpty(subscriptionId))
            {
                lock(clients)
                {
                    clients.Remove(subscriptionId);
                }
            }
        }


        // Will be overwritten by PoolBase OnConnect 
        protected abstract void OnConnect(StratumClient client, IPEndPoint portItem);


        protected async Task OnClientRequest(StratumClient client, JsonRpcRequest request, CancellationToken ct)
        {
            // boot pre-connected clients
            if(banManager?.IsBanned(client.RemoteEndpoint.Address) == true)
            {
                logger.Info(() => $"[{client.ConnectionId}] Disconnecting banned client @ {client.RemoteEndpoint.Address}");
                DisconnectClient(client);
                return;
            }

            logger.Info(() =>  $"[{client.ConnectionId}] Request '{request.Method}' [{request.Id}]");
            logger.Trace(() => $"[{client.ConnectionId}] [STRATUM OnRequest]: {JsonConvert.SerializeObject(request)}");

            await OnRequestAsync(client, new Timestamped<JsonRpcRequest>(request, clock.UtcNow), ct);
        }

        protected abstract Task OnRequestAsync(StratumClient client, Timestamped<JsonRpcRequest> request, CancellationToken ct);

        protected virtual void OnClientError(StratumClient client, Exception ex)
        {
            if(ex is AggregateException)
                ex = ex.InnerException;

            if(ex is IOException && ex.InnerException != null)
                ex = ex.InnerException;

            switch(ex)
            {
                case SocketException sockEx:
                    if(!ignoredSocketErrors.Contains(sockEx.ErrorCode))
                        logger.Error(() => $"[{client.ConnectionId}] Connection error state: {ex}");
                    break;

                case JsonException jsonEx:
                    // junk received (invalid json)
                    logger.Error(() => $"[{client.ConnectionId}] Connection json error state: {jsonEx.Message}");

                    if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                    {
                        logger.Info(() => $"[{client.ConnectionId}] Banning client for sending junk");
                        banManager?.Ban(client.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                    }
                    break;

                case AuthenticationException authEx:
                    // junk received (SSL handshake)
                    logger.Error(() => $"[{client.ConnectionId}] Connection json error state: {authEx.Message}");

                    if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                    {
                        logger.Info(() => $"[{client.ConnectionId}] Banning client for failing SSL handshake");
                        banManager?.Ban(client.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                    }
                    break;

                case IOException ioEx:
                    // junk received (SSL handshake)
                    logger.Error(() => $"[{client.ConnectionId}] Connection json error state: {ioEx.Message}");

                    if(ioEx.Source == "System.Net.Security")
                    {
                        if(clusterConfig.Banning?.BanOnJunkReceive.HasValue == false || clusterConfig.Banning?.BanOnJunkReceive == true)
                        {
                            logger.Info(() => $"[{client.ConnectionId}] Banning client for failing SSL handshake");
                            banManager?.Ban(client.RemoteEndpoint.Address, TimeSpan.FromMinutes(3));
                        }
                    }
                    break;

                case ObjectDisposedException odEx:
                    // socket disposed
                    break;

                case ArgumentException argEx:
                    if(argEx.TargetSite != StreamWriterCtor || argEx.ParamName != "stream")
                        logger.Error(() => $"[{client.ConnectionId}] Connection error state: {ex}");
                    break;

                case InvalidOperationException invOpEx:
                    // The source completed without providing data to receive
                    break;

                default:
                    logger.Error(() => $"[{client.ConnectionId}] Connection error state: {ex}");
                    break;
            }

            UnregisterClient(client);
        }

        protected virtual void OnClientComplete(StratumClient client)
        {
            logger.Debug(() => $"[{client.ConnectionId}] Received EOF");

            UnregisterClient(client);
        }

        protected virtual void DisconnectClient(StratumClient client)
        {
            Contract.RequiresNonNull(client, nameof(client));

            client.Disconnect();
            UnregisterClient(client);
        }

        private X509Certificate2 AddCert((IPEndPoint IPEndPoint, PoolEndpoint PoolEndpoint) port)
        {
            try
            {
                var tlsCert = new X509Certificate2(port.PoolEndpoint.TlsPfxFile);
                certs.TryAdd(port.PoolEndpoint.TlsPfxFile, tlsCert);
                return tlsCert;
            }

            catch(Exception ex)
            {
                logger.Info(() => $"Failed to load TLS certificate {port.PoolEndpoint.TlsPfxFile}: {ex.Message}");
                throw;
            }
        }

        protected void ForEachClient(Action<StratumClient> action)
        {
            StratumClient[] tmp;

            lock(clients)
            {
                tmp = clients.Values.ToArray();
            }

            foreach(var client in tmp)
            {
                try
                {
                    action(client);
                }

                catch(Exception ex)
                {
                    logger.Error(ex);
                }
            }
        }

        protected IEnumerable<Task> ForEachClient(Func<StratumClient, Task> func)
        {
            StratumClient[] tmp;

            lock(clients)
            {
                tmp = clients.Values.ToArray();
            }

            return tmp.Select(x => func(x));
        }

      
    }
}
