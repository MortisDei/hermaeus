namespace Hermaeus.Core.Services;

public static class NullableNumericDefaults
{
    public static double? FirstSpin(double? current, string field, double minimum, double maximum, double increment, int direction)
    {
        if (current is not null) return Math.Clamp(current.Value, minimum, maximum);
        var neutral = field switch
        {
            "repeat_penalty" => 1d,
            "frequency_penalty" => 0d,
            "presence_penalty" => 0d,
            "top_p" => 1d,
            "min_p" => 0d,
            _ => (double?)null
        };
        if (neutral is null) return null;
        return Math.Clamp(neutral.Value + (direction >= 0 ? increment : -increment), minimum, maximum);
    }
}
