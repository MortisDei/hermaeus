namespace Hermaeus.Voice;

/// <summary>Reads and writes 16-bit PCM mono WAV files as raw float samples in [-1, 1].</summary>
internal static class WavFile
{
    public sealed record WavAudio(float[] Samples, int SampleRate, int Channels, int BitsPerSample);

    /// <summary>r24 doc 05 5.3: parses a RIFF/WAVE/fmt/data PCM stream, rejecting
    /// anything else with a clear message rather than feeding arbitrary bytes to
    /// the model. Only 16-bit PCM is supported (what every capture path and the
    /// STT contract produce).</summary>
    public static WavAudio Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Not a WAV file: missing RIFF header.");
        reader.ReadInt32(); // overall chunk size, unused
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Not a WAV file: missing WAVE tag.");

        short channels = 0, bitsPerSample = 0;
        int sampleRate = 0;
        short audioFormat = 0;
        byte[]? data = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadInt32();
            if (chunkSize < 0 || stream.Position + chunkSize > stream.Length)
                throw new InvalidDataException("Malformed WAV file: chunk size exceeds file length.");

            if (chunkId == "fmt ")
            {
                var chunkStart = stream.Position;
                audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byte rate
                reader.ReadInt16(); // block align
                bitsPerSample = reader.ReadInt16();
                stream.Position = chunkStart + chunkSize;
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }
            else
            {
                stream.Position += chunkSize;
            }

            if (chunkSize % 2 != 0 && stream.Position < stream.Length)
                stream.Position += 1; // chunks are word-aligned
        }

        if (audioFormat != 1)
            throw new InvalidDataException($"Unsupported WAV encoding (format code {audioFormat}); only uncompressed PCM is supported.");
        if (bitsPerSample != 16)
            throw new InvalidDataException($"Unsupported WAV bit depth ({bitsPerSample}-bit); only 16-bit PCM is supported.");
        if (channels != 1 || sampleRate <= 0)
            throw new InvalidDataException("Malformed WAV file: expected a positive-rate mono stream.");
        if (data is null)
            throw new InvalidDataException("Malformed WAV file: no data chunk found.");
        if (data.Length == 0 || data.Length % 2 != 0)
            throw new InvalidDataException("Malformed WAV file: audio data is empty or incomplete.");

        var sampleCount = data.Length / 2;
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var value = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
            samples[i] = value / (float)short.MaxValue;
        }

        return new WavAudio(samples, sampleRate, channels, bitsPerSample);
    }

    public static void Write(string path, float[] samples, int sampleRate)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        const int bitsPerSample = 16;
        const int channels = 1;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = channels * bitsPerSample / 8;
        var dataSize = samples.Length * (bitsPerSample / 8);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1f, 1f);
            writer.Write((short)(clamped * short.MaxValue));
        }
    }
}
