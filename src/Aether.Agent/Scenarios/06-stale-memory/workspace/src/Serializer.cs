using System.Text.Json;

public static class Serializer
{
    public static string Save<T>(T value) => JsonSerializer.Serialize(value);
}
