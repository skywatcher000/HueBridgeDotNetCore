﻿using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SocketLite.Model;
using SocketLite.Services;
using Microsoft.Extensions.Hosting;
using System.Threading;
using Microsoft.Extensions.Options;
using System.Net.NetworkInformation;
using System.Collections.Generic;

namespace HueBridge.ApplicationMain
{
    public class SsdpService : IHostedService, IDisposable
    {
        private IOptions<AppOptions> options;
        private string macAddr;
        private Task worker;
        private IDisposable subscriberUdpMilticast = null;

        public SsdpService(IOptions<AppOptions> optionsAccessor)
        {
            options = optionsAccessor;
        }

        public void Dispose()
        {
            subscriberUdpMilticast.Dispose();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            worker = Task.Factory.StartNew(StartSsdpDiscoveryListener);
            return worker;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            subscriberUdpMilticast.Dispose();
            return Task.CompletedTask;
        }

        private async void StartSsdpDiscoveryListener()
        {
            var communicationInterface = new CommunicationsInterface();
            var allInterfaces = communicationInterface.GetAllInterfaces();
            var networkInterface = allInterfaces.FirstOrDefault(x => x.IpAddress == options.Value.NetworkInterface);
            var allInterfacesSys = NetworkInterface.GetAllNetworkInterfaces();
            var networkInterfaceSys = allInterfacesSys.First(x => x.GetIPProperties().UnicastAddresses.Count(y => y.Address.ToString() == options.Value.NetworkInterface) == 1);

            if (networkInterface == null)
            {
                return;
            }

            macAddr = networkInterfaceSys.GetPhysicalAddress().ToString();

            var udpMulticast = new UdpSocketMulticastClient();
            var observerUdpMulticast = await udpMulticast.ObservableMulticastListener(
                "239.255.255.250",
                1900,
                networkInterface,
                allowMultipleBindToSamePort: false);
            var udpClient = new UdpClient();

            subscriberUdpMilticast = observerUdpMulticast.Subscribe(
                async udpMsg =>
                {
                    var msg = Encoding.UTF8.GetString(udpMsg.ByteData);
                    if (msg.StartsWith("M-SEARCH * HTTP/1.1") && msg.Contains("ssdp:discover"))
                    {
                        Console.BackgroundColor = ConsoleColor.Green;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine("Responding M-SEARCH request from " + udpMsg.RemoteAddress);
                        Console.ResetColor();

                        await Task.Delay(new Random().Next(0, 3000));

                        var responses = BuildResponse();
                        foreach (var r in responses)
                        {
                            var bytes = Encoding.UTF8.GetBytes(r);
                            await udpClient.SendAsync(bytes, bytes.Length, hostname: udpMsg.RemoteAddress, port: Convert.ToInt32(udpMsg.RemotePort));
                        }
                    }
                },
                ex =>
                {
                    //Insert your exception code here
                },
                () =>
                {
                    //Insert your completion code here
                });
        }

        private List<string> BuildResponse()
        {
            var ret = new List<string>(3);
            var sb = new StringBuilder();

            sb.Append("HTTP/1.1 200 OK\r\n");
            sb.Append("HOST: 239.255.255.250:1900\r\n");
            sb.Append("EXT:\r\n");
            sb.Append("CACHE-CONTROL: max-age=100\r\n");
            sb.Append($"LOCATION: http://{options.Value.NetworkInterface}/description.xml\r\n");
            sb.Append($"SERVER: {Environment.OSVersion.ToString()} UPnP/1.0 IpBridge/1.20.0\r\n");
            sb.Append($"hue-bridgeid: {macAddr.Substring(0, 6)}FFFE{macAddr.Substring(6)} \r\n");

            var customMessage1 = $"ST: upnp:rootdevice\r\nUSN: uuid:2f402f80-da50-11e1-9b23-{macAddr.ToLower()}::upnp:rootdevice\r\n\r\n";
            var customMessage2 = $"ST: uuid:2f402f80-da50-11e1-9b23-{macAddr.ToLower()}\r\nUSN: uuid:2f402f80-da50-11e1-9b23-{macAddr.ToLower()}\r\n\r\n";
            var customMessage3 = $"ST: urn:schemas-upnp-org:device:basic:1\r\nUSN: uuid:2f402f80-da50-11e1-9b23-{macAddr.ToLower()}\r\n\r\n";

            ret.Add(sb.ToString() + customMessage1);
            ret.Add(sb.ToString() + customMessage2);
            ret.Add(sb.ToString() + customMessage3);
            return ret;
        }
    }
}