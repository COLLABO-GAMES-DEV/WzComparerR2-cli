using System;
using System.Net.Sockets;

namespace WzComparerR2.Cli
{
    internal sealed class NetworkCommandDto
    {
        public string Command { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Mode { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
        public string ChatMessage { get; set; }

        public static NetworkCommandDto FromArgs(string command, ParsedArgs args)
        {
            return new NetworkCommandDto
            {
                Command = command,
                Host = args.GetValue("host") ?? "wc.kagamia.com",
                Port = args.GetInt("port", 2100),
                Mode = args.HasFlag("connect") ? "tcp-probe" : "dry-run",
                Success = true,
                ChatMessage = args.GetValue("message")
            };
        }
    }

    internal static class NetworkProbe
    {
        public static void TryConnect(NetworkCommandDto result, int timeoutSeconds)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var task = client.ConnectAsync(result.Host, result.Port);
                    if (!task.Wait(Math.Max(1, timeoutSeconds) * 1000))
                    {
                        result.Success = false;
                        result.Error = "TCP probe timed out.";
                        return;
                    }
                    result.Success = client.Connected;
                    result.Message = client.Connected ? "TCP connection succeeded." : "TCP connection failed.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }
        }
    }
}
