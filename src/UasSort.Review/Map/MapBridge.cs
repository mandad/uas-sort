// src/UasSort.Review/Map/MapBridge.cs
using System.Text.Json;

namespace UasSort.Review;

/// <summary>
/// The C# side of the map pane (Ref §9.6). Part 11's MapPane gives it PostWebMessageAsString as <c>post</c> and feeds
/// WebMessageReceived strings to <see cref="Dispatch"/>. It never throws on a bad message.
/// </summary>
public sealed partial class MapBridge : IDisposable
{
    private readonly Action<string> _post;
    private readonly IReviewLog _log;
    private readonly TimeProvider _time;

    public MapBridge(Action<string> post, IReviewLog log, TimeProvider time)
    {
        _post = post;
        _log = log;
        _time = time;
    }

    public event Action<MapToHost>? Received;

    public static string Serialize(HostToMap m) => JsonSerializer.Serialize(m, MapJsonContext.Default.HostToMap);
    public static MapToHost? Parse(string json) => JsonSerializer.Deserialize(json, MapJsonContext.Default.MapToHost);
    public static HostToMap? ParseHostMessage(string json) => JsonSerializer.Deserialize(json, MapJsonContext.Default.HostToMap);

    public void Send(HostToMap m) => _post(Serialize(m));

    public void Dispatch(string json)
    {
        MapToHost? message;
        try
        {
            message = Parse(json);
        }
        catch (JsonException ex)
        {
            _log.Warn("Map message ignored: " + ex.Message);
            return;
        }
        catch (NotSupportedException ex)
        {
            _log.Warn("Map message ignored: " + ex.Message);
            return;
        }
        if (message is null || message.GetType() == typeof(MapToHost))
        {
            _log.Warn("Map message ignored: unknown type");
            return;
        }
        Received?.Invoke(message);
    }

    public void Dispose() => DisposeThrottle();
}
