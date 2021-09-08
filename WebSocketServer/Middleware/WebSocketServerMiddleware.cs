using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace WebSocketServer.Middleware
{
    public class WebSocketServerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly WebSocketServerConnectionManager _manager;

        public WebSocketServerMiddleware(RequestDelegate next, WebSocketServerConnectionManager manager)
        {
            _next = next;
            _manager = manager;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                Console.WriteLine("WebSocket connected");

                string ConnID = _manager.AddSocket(webSocket);
                await SendConnIDAsync(webSocket, ConnID);
                
                await RecieveMessage(webSocket, async (result, buffer) => 
                {
                    if(result.MessageType == WebSocketMessageType.Text)
                    {
                        Console.WriteLine("Message received");
                        Console.WriteLine($"Message: {Encoding.UTF8.GetString(buffer, 0, result.Count)}");
                        await RouteJSONMessageAsync(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        return;
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        string id = _manager.GetAllSockets().FirstOrDefault(s => s.Value == webSocket).Key;
                        Console.WriteLine("Received close message");
                        _manager.GetAllSockets().TryRemove(id, out WebSocket sock);
                        await sock.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription,
                            CancellationToken.None);
                        return;
                    }
                });
            }
            else
            {
                Console.WriteLine("Hello from the 2nd request delegate");
                await _next(context);
            }
        }

        private async Task SendConnIDAsync(WebSocket socket, string connID)
        {
            var buffer = Encoding.UTF8.GetBytes("ConnID: " + connID);
            await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task RecieveMessage(
            WebSocket socket, 
            Action<WebSocketReceiveResult,byte[]> handleMessage)
        {
            var buffer = new byte[1024*4];

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer: new ArraySegment<byte>(buffer),
                cancellationToken: CancellationToken.None);
                handleMessage(result, buffer);
            }
        }

        public async Task RouteJSONMessageAsync(string message)
        {
            var routeObj = JsonConvert.DeserializeObject<dynamic>(message);

            if(Guid.TryParse(routeObj.To.ToString(), out Guid guidOutput))
            {
                Console.WriteLine("Targeted client");
                var sock = _manager.GetAllSockets().FirstOrDefault( s => s.Key == routeObj.To.ToString());
                if(sock.Value != null)
                {
                    if(sock.Value.State == WebSocketState.Open)
                    {
                        await sock.Value.SendAsync(Encoding.UTF8.GetBytes(routeObj.Message.ToString()),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                else
                {
                    Console.WriteLine("Invalid recipient");
                }
            }
            else
            {
                Console.WriteLine("Broadcast");
                foreach (var sock in _manager.GetAllSockets())
                {
                    if (sock.Value.State == WebSocketState.Open)
                    {
                        await sock.Value.SendAsync(Encoding.UTF8.GetBytes(routeObj.Message.ToString()),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        }

        /*         public void WriteRequestParam(HttpContext context)
        {
            Console.WriteLine($"Request method: {context.Request.Method}");
            Console.WriteLine($"Request protocol: {context.Request.Protocol}");

            if(context.Request.Headers != null)
            {
                foreach(var header in context.Request.Headers)
                {
                    Console.WriteLine(" --> " + header.Key + ":" + header.Value);
                }
            }
        } */
    }
}