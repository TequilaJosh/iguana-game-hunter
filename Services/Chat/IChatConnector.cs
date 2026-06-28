using System;
using System.Threading.Tasks;
using GameTracker.Models;

namespace GameTracker.Services.Chat
{
    /// <summary>A live-chat source (read-only). May aggregate several platforms.</summary>
    public interface IChatConnector : IDisposable
    {
        string Name { get; }
        bool IsConnected { get; }

        event Action<ChatMessage>? MessageReceived;
        event Action<string>? StatusChanged;

        /// <param name="input">Channel name (Twitch), session ID (Social Stream Ninja), or token (Restream).</param>
        Task ConnectAsync(string input);
        Task DisconnectAsync();
    }
}
