﻿using System.Diagnostics;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Nett;

namespace ThingsOn_MQTT_Bench
{
    internal class Program
    {

        // Entry point of the program
        private static async Task Main()
        {

            // Prompt the user to start a new benchmark
            Console.Write("Do you want to start a new benchmark? (y/n): ");
            var answer = Console.ReadLine();

            // Run the benchmark if the user answers "y"
            if (answer?.ToLower() == "y")
            {
                try
                {
                    await RunBenchmark();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    Console.ResetColor();
                }
            }

            // Main logic of the benchmark
            async Task RunBenchmark()
            {
                // Load benchmark settings
                var (settings, serverUri, port, cleanSession, userName, password, keepAlivePeriod, connectionTimeOut,
                    mqttVersion, clientCount, messageCount, messageSize, qos, retain) = LoadSettings();

                // Create MQTT clients
                var factory = new MqttFactory();
                var clients = Enumerable.Range(0, clientCount).Select(_ => factory.CreateMqttClient()).ToList();

                // Connect to MQTT broker and measure connect time
                var stopwatchConnect = Stopwatch.StartNew();
                var connectTasks = clients.Select(c => c.ConnectAsync(CreateMqttClientOptions(serverUri, port, cleanSession, userName, password, keepAlivePeriod, connectionTimeOut, mqttVersion))).ToList();
                await Task.WhenAll(connectTasks);
                stopwatchConnect.Stop();
                var elapsedConnect = stopwatchConnect.Elapsed;

                // Generate messages
                var messages = Enumerable.Range(1, messageCount)
                    .Select(i => new MqttApplicationMessageBuilder()
                        .WithTopic($"test/{i}")
                        .WithPayload(new byte[messageSize])
                        .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qos)
                        .WithRetainFlag(retain)
                        .Build())
                    .ToList();

                // Start benchmark
                var stopwatch = Stopwatch.StartNew();

                // Send messages
                var totalMessagesSent = clientCount * messageCount;
                var sent = 0;
                var progressReport = 0;
                var sendTasks = clients.Select(client => Task.Run(async () =>
                {
                    var results = new List<MqttClientPublishResult>();
                    foreach (var message in messages)
                    {
                        // Publish message and store the result
                        var result = await client.PublishAsync(message);
                        results.Add(result);

                        // Update progress
                        sent++;
                        var newProgressReport = sent * 100 / totalMessagesSent;
                        if (newProgressReport >= progressReport + 10)
                        {
                            progressReport = newProgressReport / 10 * 10;
                            Console.CursorLeft = 0;
                            Console.Write($"\rProgress: {progressReport}%");
                        }
                    }

                    return results;
                })).ToList();

                // Wait for all messages to be sent
                var sendResults = await Task.WhenAll(sendTasks);
                Console.Write("\rProgress: 100%");
                Console.WriteLine();
                Console.WriteLine();

                // End benchmark
                stopwatch.Stop();
                var elapsed = stopwatch.Elapsed;
                var messagesSent = clientCount * messageCount;
                var throughput = messagesSent / elapsed.TotalSeconds;
                var lossRate = (double)sendResults.SelectMany(r => r).Count(r => r.ReasonCode != MqttClientPublishReasonCode.Success) / messagesSent;
                var successRate = 1.0 - lossRate;

                // Disconnect from MQTT broker and measure disconnect time
                var stopwatchDisconnect = Stopwatch.StartNew();
                var disconnectTasks = clients.Select(c => c.DisconnectAsync()).ToList();
                await Task.WhenAll(disconnectTasks);
                stopwatchDisconnect.Stop();
                var elapsedDisconnect = stopwatchDisconnect.Elapsed;

                // Print results
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Benchmark completed 🚀");
                Console.WriteLine("=======================================");
                Console.WriteLine($"{"Messages sent",-20} {messagesSent:N0}");
                Console.WriteLine($"{"Elapsed time",-20} {elapsed.TotalSeconds:F3}");
                Console.WriteLine($"{"Throughput",-20} {throughput:N0} messages/second");
                Console.WriteLine($"{"Connect time",-20} {elapsedConnect.TotalSeconds:F3} seconds");
                Console.WriteLine($"{"Disconnect time",-20} {elapsedDisconnect.TotalSeconds:F3} seconds");
                Console.WriteLine($"{"Success rate",-20} {successRate:P0}");
                Console.WriteLine($"{"Loss rate",-20} {lossRate:P0}");

                // Convert data size to appropriate units
                var dataSize = (double)(totalMessagesSent * settings.Get<int>("MessageSize"));
                var dataUnits = new[] { "B", "KB", "MB", "GB", "TB" };
                var dataUnitIndex = 0;
                while (dataSize >= 1024 && dataUnitIndex < dataUnits.Length - 1)
                {
                    dataSize /= 1024;
                    dataUnitIndex++;
                }

                Console.WriteLine($"{"Data sent",-20} {dataSize:N3} {dataUnits[dataUnitIndex]}");
                Console.WriteLine("=======================================");
                Console.ResetColor();

                // Prompt the user to run the benchmark again
                await Main();
            }
        }

        private static (TomlTable settings, string serverUri, int port, bool cleanSession, string userName, string password, TimeSpan keepAlivePeriod, TimeSpan connectionTimeOut, MqttProtocolVersion mqttVersion, int clientCount, int messageCount, int messageSize, int qos, bool retain) LoadSettings()
        {
            // Load settings from config.toml
            var settings = Toml.ReadFile(Path.Combine(Directory.GetCurrentDirectory(), "config.toml"));

            // Parse benchmark settings
            var serverUri = settings.Get<string>("ServerUri");
            var port = settings.Get<int>("Port");
            var cleanSession = settings.Get<bool>("CleanSession");
            var userName = settings.Get<string>("Username");
            var password = settings.Get<string>("Password");
            var keepAlivePeriod = TimeSpan.FromSeconds(settings.Get<int>("KeepAlivePeriod"));
            var connectionTimeOut = TimeSpan.FromSeconds(settings.Get<int>("ConnectionTimeOut"));
            var mqttVersion = ParseMqttProtocolVersion(settings.Get<string>("MqttVersion"));
            var clientCount = settings.Get<int>("ClientCount");
            var messageCount = settings.Get<int>("MessageCount");
            var messageSize = settings.Get<int>("MessageSize");
            var qos = settings.Get<int>("Qos");
            var retain = settings.Get<bool>("Retain");

            // Print benchmark settings
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"Benchmark settings: {clientCount} clients, {messageCount} messages/client, {messageSize:N0} bytes/message, QoS {qos}, retain {retain}, MQTT version {mqttVersion}.");
            Console.WriteLine($"Server URI: {serverUri}");
            Console.WriteLine($"Port: {port}");
            Console.WriteLine($"Clean session: {cleanSession}");
            Console.WriteLine($"User name: {userName}");
            Console.WriteLine(!string.IsNullOrEmpty(password) ? $"Password: {password[0]}{new string('*', password.Length - 1)}" : "Password: (empty)");
            Console.WriteLine($"Keep alive period: {keepAlivePeriod}");
            Console.WriteLine($"Connection timeout: {connectionTimeOut}");
            Console.ResetColor();
            return (settings, serverUri, port, cleanSession, userName, password, keepAlivePeriod, connectionTimeOut, mqttVersion, clientCount, messageCount, messageSize, qos, retain);
        }

        // Create MQTT client options from parsed settings
        private static MqttClientOptions CreateMqttClientOptions(string serverUri, int port, bool cleanSession, string userName, string password, TimeSpan keepAlivePeriod, TimeSpan connectionTimeOut, MqttProtocolVersion mqttVersion)
        {
            var options = new MqttClientOptionsBuilder()
                .WithClientId(Guid.NewGuid().ToString())
                .WithTcpServer(serverUri, port)
                .WithCleanSession(cleanSession)
                .WithTls(p =>
{
    p.CertificateValidationHandler = e =>
    {
        return true;
    };
})
                .WithCredentials(userName, password)
                .WithKeepAlivePeriod(keepAlivePeriod)
                .WithTimeout(connectionTimeOut)
                .WithProtocolVersion(mqttVersion)
                .Build();
            return options;
        }

        // Parse MQTT protocol version from a string
        private static MqttProtocolVersion ParseMqttProtocolVersion(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "v310":
                    return MqttProtocolVersion.V310;
                case "v311":
                    return MqttProtocolVersion.V311;
                case "v500":
                    return MqttProtocolVersion.V500;
                default:
                    throw new ArgumentException($"Invalid MQTT version '{value}'.");
            }
        }

    }
}