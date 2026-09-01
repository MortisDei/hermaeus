using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hermaeus.Services;

public enum HfArtworkState
{
    Loading,
    Available,
    NoDeclaredArtwork,
    ExternalBlocked,
    Invalid,
    Unavailable
}

public enum HfArtworkSourceKind
{
    None,
    RepositoryFile,
    HuggingFaceSocialThumbnail,
    HuggingFaceAuthorAvatar
}

public enum HfArtworkFetchPolicy
{
    PinnedRepositoryRevision,
    HuggingFaceHostNoCredentials
}

public sealed record HfArtworkDescriptor(
    string RepoId,
    string RevisionSha,
    string DeclaredValue,
    string? RepositoryPath,
    HfArtworkSourceKind SourceKind,
    HfArtworkFetchPolicy FetchPolicy,
    string CacheKey);

public sealed record HfArtworkPlan(
    HfArtworkDescriptor? Descriptor,
    HfArtworkState State,
    string FailureCode,
    string Host);

public sealed record HfArtworkResult(
    HfArtworkState State,
    HfArtworkDescriptor? Descriptor,
    string? CachePath,
    string? MimeType,
    long ByteCount,
    int Width,
    int Height,
    string? ContentHash,
    string FailureCode,
    string Host,
    HfArtworkSourceKind SourceKind = HfArtworkSourceKind.None)
{
    public HfArtworkSourceKind EffectiveSourceKind => Descriptor?.SourceKind ?? SourceKind;

    public static HfArtworkResult Loading(HfArtworkDescriptor? descriptor = null) =>
        new(HfArtworkState.Loading, descriptor, null, null, 0, 0, 0, null, string.Empty, string.Empty);
}

public sealed record HfArtworkCacheInfo(long ByteCount, int EntryCount);

/// <summary>
/// Strict, decoration-only artwork acquisition for selected Hugging Face repositories.
/// The service never sends credentials, never follows redirects implicitly, and never
/// constructs a bitmap. Bytes are independently preflighted before the desktop layer can
/// hand them to Avalonia.
/// </summary>
public sealed class HuggingFaceArtworkService
{
    public const int MaxDeclaredBytes = 2048;
    public const int MaxArtworkBytes = 2 * 1024 * 1024;
    public const int MaxRedirects = 5;
    public const int MaxDimension = 4096;
    public const long MaxDecodedPixels = 16_777_216;
    public const long MaxDecodedBytes = 64 * 1024 * 1024;

    private const string HubHost = "huggingface.co";
    private const string AvatarHost = "cdn-avatars.huggingface.co";
    private static readonly HashSet<string> DeliveryHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cas-server.xethub.hf.co",
        "cas-server.xethub-eu.hf.co",
        "transfer.xethub.hf.co",
        "transfer.xethub-eu.hf.co",
        "us.aws.cdn.hf.co",
        "us.gcp.cdn.hf.co",
        "cdn-lfs-us-1.hf.co",
        "cdn-lfs-eu-1.hf.co",
        "cas-bridge.xethub.hf.co"
    };

    private static readonly HttpClient DefaultHttp = BuildDefaultClient();
    private readonly HttpClient _http;

    public HuggingFaceArtworkService(HttpClient? http = null)
    {
        _http = http ?? DefaultHttp;
        // This client is an anonymous decoration channel. A caller must not be able
        // to accidentally carry a provider or Hub token into an artwork request.
        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.Remove("Cookie");
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Hermaeus/1.0");
    }

    public static HfArtworkPlan Describe(
        string repoId,
        HfModelCard card,
        IReadOnlyList<HfTreeEntry> tree)
    {
        if (!IsRepoId(repoId) || !IsImmutableRevision(card.Sha))
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "missing_immutable_revision", string.Empty);

        var declared = card.Thumbnail;
        if (string.IsNullOrWhiteSpace(declared))
            return new HfArtworkPlan(null, HfArtworkState.NoDeclaredArtwork, "not_declared", string.Empty);
        if (Encoding.UTF8.GetByteCount(declared) > MaxDeclaredBytes)
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "declared_value_too_large", string.Empty);
        if (declared.Any(char.IsControl))
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "control_character", string.Empty);

        if (!Uri.TryCreate(declared, UriKind.RelativeOrAbsolute, out var uri))
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "malformed_uri", string.Empty);

        if (!uri.IsAbsoluteUri)
        {
            if (TryResolveRepositoryPath(declared, repoId, card.Sha, tree, out var relativePath))
                return BuildDescriptor(repoId, card.Sha, declared, relativePath, HfArtworkSourceKind.RepositoryFile,
                    HfArtworkFetchPolicy.PinnedRepositoryRevision);
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "repository_path_not_in_revision", HubHost);
        }

        var host = uri.Host;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !IsSafeAuthority(uri))
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "unsafe_declared_url", host);
        if (!string.Equals(host, HubHost, StringComparison.OrdinalIgnoreCase))
            return new HfArtworkPlan(null, HfArtworkState.ExternalBlocked, "external_host", host);
        if (HasEncodedPathHazard(uri))
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "encoded_path_hazard", host);
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "declared_query_or_fragment", host);

        var path = uri.AbsolutePath.Trim('/');
        var parts = path.Split('/', StringSplitOptions.None);
        if (parts.Length >= 5
            && string.Equals(parts[0], repoId.Split('/')[0], StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[1], repoId.Split('/')[1], StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], "resolve", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[3], card.Sha, StringComparison.OrdinalIgnoreCase))
        {
            var repositoryPath = string.Join('/', parts.Skip(4));
            if (TryResolveRepositoryPath(repositoryPath, repoId, card.Sha, tree, out var canonicalPath))
                return BuildDescriptor(repoId, card.Sha, declared, canonicalPath, HfArtworkSourceKind.RepositoryFile,
                    HfArtworkFetchPolicy.PinnedRepositoryRevision);
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "repository_path_not_in_revision", host);
        }

        if (!TryValidateSocialPath(path))
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "unsafe_hub_path", host);

        return BuildDescriptor(repoId, card.Sha, declared, null, HfArtworkSourceKind.HuggingFaceSocialThumbnail,
            HfArtworkFetchPolicy.HuggingFaceHostNoCredentials);
    }

    public static HfArtworkPlan DescribeAuthorAvatar(
        string repoId,
        HfModelCard card,
        string? authorAvatarUrl)
    {
        if (!IsRepoId(repoId) || !IsImmutableRevision(card.Sha))
            return new HfArtworkPlan(null, HfArtworkState.Invalid, "missing_immutable_revision", string.Empty);
        if (!TryValidateAuthorAvatarUrl(authorAvatarUrl, out var avatarUri))
            return new HfArtworkPlan(null, HfArtworkState.NoDeclaredArtwork, "author_avatar_unavailable", AvatarHost);

        return BuildDescriptor(repoId, card.Sha, avatarUri!.AbsoluteUri, null,
            HfArtworkSourceKind.HuggingFaceAuthorAvatar,
            HfArtworkFetchPolicy.HuggingFaceHostNoCredentials);
    }

    public async Task<HfArtworkResult> FetchAsync(
        string repoId,
        HfModelCard card,
        IReadOnlyList<HfTreeEntry> tree,
        string cacheRoot,
        string? authorAvatarUrl = null,
        CancellationToken ct = default)
    {
        var plan = Describe(repoId, card, tree);
        if (plan.State == HfArtworkState.NoDeclaredArtwork && !string.IsNullOrWhiteSpace(authorAvatarUrl))
            plan = DescribeAuthorAvatar(repoId, card, authorAvatarUrl);
        if (plan.Descriptor is null)
            return new HfArtworkResult(plan.State, null, null, null, 0, 0, 0, null, plan.FailureCode, plan.Host);

        try
        {
            var cached = await HuggingFaceArtworkCache.TryGetAsync(plan.Descriptor, cacheRoot, ct);
            if (cached is not null)
                return cached;

            var current = BuildInitialUri(plan.Descriptor);
            for (var redirect = 0; ; redirect++)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsFetchUriSafe(current, plan.Descriptor.SourceKind, isInitial: redirect == 0,
                        previousHost: null, out var requestHost, out var uriFailure))
                    return Failure(plan.Descriptor, HfArtworkState.Invalid, uriFailure, requestHost);

                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                requestHost = current.Host;
                if (IsRedirect(response.StatusCode))
                {
                    if (redirect >= MaxRedirects)
                        return Failure(plan.Descriptor, HfArtworkState.Unavailable, "redirect_limit", requestHost);
                    if (response.Headers.Location is null)
                        return Failure(plan.Descriptor, HfArtworkState.Invalid, "redirect_without_location", requestHost);

                    var redirectFailure = string.Empty;
                    if (!Uri.TryCreate(current, response.Headers.Location, out var next)
                        || !IsFetchUriSafe(next, plan.Descriptor.SourceKind, isInitial: false,
                            previousHost: current.Host, out var nextHost, out redirectFailure))
                        return Failure(plan.Descriptor, HfArtworkState.Invalid, redirectFailure, next?.Host ?? requestHost);
                    current = next;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    return Failure(plan.Descriptor, HfArtworkState.Unavailable, $"http_{(int)response.StatusCode}", requestHost);
                if (response.Content.Headers.ContentEncoding.Count > 0)
                    return Failure(plan.Descriptor, HfArtworkState.Invalid, "content_encoding", requestHost);

                var mime = response.Content.Headers.ContentType?.MediaType?.Trim().ToLowerInvariant();
                if (!IsSupportedMime(mime))
                    return Failure(plan.Descriptor, HfArtworkState.Invalid, "mime_not_allowed", requestHost);
                if (response.Content.Headers.ContentLength is < 0 or > MaxArtworkBytes)
                    return Failure(plan.Descriptor, HfArtworkState.Invalid, "content_length_limit", requestHost);

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var bytes = await ReadBoundedAsync(stream, MaxArtworkBytes, response.Content.Headers.ContentLength, ct);
                if (bytes is null)
                    return Failure(plan.Descriptor, HfArtworkState.Invalid, "body_limit_or_length", requestHost);
                if (!ArtworkImagePreflight.TryRead(bytes, mime!, out var image, out var imageFailure))
                    return Failure(plan.Descriptor, HfArtworkState.Invalid, imageFailure, requestHost);

                var contentHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                return await HuggingFaceArtworkCache.StoreAsync(
                    plan.Descriptor, cacheRoot, mime!, bytes, image.Width, image.Height, contentHash,
                    response.Headers.ETag?.Tag, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Failure(plan.Descriptor, HfArtworkState.Unavailable, "network_error", plan.Host);
        }
        catch (IOException)
        {
            return Failure(plan.Descriptor, HfArtworkState.Unavailable, "cache_io_error", plan.Host);
        }
        catch (JsonException)
        {
            return Failure(plan.Descriptor, HfArtworkState.Unavailable, "cache_metadata_error", plan.Host);
        }
    }

    public Task<HfArtworkResult?> TryGetCachedAsync(
        string repoId,
        string revisionSha,
        string cacheRoot,
        CancellationToken ct = default) =>
        HuggingFaceArtworkCache.FindVerifiedAsync(repoId, revisionSha, cacheRoot, ct);

    internal static bool IsSupportedMime(string? mime) =>
        mime is "image/png" or "image/jpeg" or "image/webp";

    public static bool IsImmutableRevision(string value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);

    public static bool IsRepoId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Split('/');
        return parts.Length == 2
            && parts.All(part => !string.IsNullOrWhiteSpace(part))
            && !value.Contains("..", StringComparison.Ordinal)
            && !value.Any(char.IsControl);
    }

    private static HttpClient BuildDefaultClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static HfArtworkPlan BuildDescriptor(
        string repoId,
        string revision,
        string declared,
        string? repositoryPath,
        HfArtworkSourceKind sourceKind,
        HfArtworkFetchPolicy fetchPolicy)
    {
        var sourceIdentity = sourceKind == HfArtworkSourceKind.HuggingFaceAuthorAvatar
            ? "author:" + new Uri(declared).AbsolutePath
            : repositoryPath ?? "social:" + new Uri(declared).AbsolutePath;
        var identity = string.Join('|', repoId.ToLowerInvariant(), revision.ToLowerInvariant(), sourceKind,
            sourceIdentity.ToLowerInvariant());
        var cacheKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new HfArtworkPlan(
            new HfArtworkDescriptor(repoId, revision, declared, repositoryPath, sourceKind, fetchPolicy, cacheKey),
            HfArtworkState.Loading, string.Empty,
            sourceKind == HfArtworkSourceKind.HuggingFaceAuthorAvatar ? AvatarHost : HubHost);
    }

    private static bool TryResolveRepositoryPath(
        string value,
        string repoId,
        string revision,
        IReadOnlyList<HfTreeEntry> tree,
        out string normalized)
    {
        normalized = string.Empty;
        if (!IsSafeRepositoryPath(value))
            return false;

        var candidate = value.Trim('/');
        var entry = tree.FirstOrDefault(item =>
            string.Equals(item.Path.Replace('\\', '/').Trim('/'), candidate, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return false;
        normalized = entry.Path.Replace('\\', '/').Trim('/');
        return normalized.Length > 0 && IsImmutableRevision(revision) && IsRepoId(repoId);
    }

    private static bool IsSafeRepositoryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('/') || value.Contains('\\')
            || value.Any(char.IsControl) || value.Contains('?', StringComparison.Ordinal)
            || value.Contains('#', StringComparison.Ordinal) || HasEncodedPathHazard(value))
            return false;

        var parts = value.Split('/');
        return parts.Length > 0 && parts.All(part => part.Length > 0 && part is not "." and not "..");
    }

    private static bool TryValidateSocialPath(string path) =>
        path.Length > 0 && IsSafeRepositoryPath(path);

    private static bool IsSafeAuthority(Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443 || uri.IsLoopback)
            return false;
        if (IPAddress.TryParse(uri.Host, out _))
            return false;
        return uri.Host.All(ch => ch < 128 && (char.IsLetterOrDigit(ch) || ch is '.' or '-'))
            && string.Equals(uri.Host, uri.DnsSafeHost, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasEncodedPathHazard(Uri uri) =>
        HasEncodedPathHazard(uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped));

    private static bool HasEncodedPathHazard(string value) =>
        value.Contains("%2f", StringComparison.OrdinalIgnoreCase)
        || value.Contains("%5c", StringComparison.OrdinalIgnoreCase)
        || value.Contains("%2e", StringComparison.OrdinalIgnoreCase);

    private static Uri BuildInitialUri(HfArtworkDescriptor descriptor)
    {
        if (descriptor.SourceKind == HfArtworkSourceKind.RepositoryFile)
            return new Uri(HuggingFaceClient.ResolveDownloadUrl(descriptor.RepoId, descriptor.RepositoryPath!, descriptor.RevisionSha));

        if (descriptor.SourceKind == HfArtworkSourceKind.HuggingFaceAuthorAvatar)
            return new Uri(descriptor.DeclaredValue);

        var social = new Uri(descriptor.DeclaredValue);
        return new Uri($"https://{HubHost}{social.AbsolutePath}");
    }

    private static bool IsFetchUriSafe(
        Uri uri,
        HfArtworkSourceKind sourceKind,
        bool isInitial,
        string? previousHost,
        out string host,
        out string failure)
    {
        host = uri.Host;
        failure = "unsafe_redirect";
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !IsSafeAuthorityForHost(uri))
        {
            failure = "unsafe_scheme_or_authority";
            return false;
        }

        var isHub = string.Equals(host, HubHost, StringComparison.OrdinalIgnoreCase);
        var isAuthorAvatar = string.Equals(host, AvatarHost, StringComparison.OrdinalIgnoreCase);
        var isDelivery = DeliveryHosts.Contains(host);
        if (isInitial && sourceKind == HfArtworkSourceKind.HuggingFaceAuthorAvatar && !isAuthorAvatar)
        {
            failure = "initial_avatar_host_not_allowed";
            return false;
        }
        if (isInitial && sourceKind != HfArtworkSourceKind.HuggingFaceAuthorAvatar && !isHub)
        {
            failure = "initial_host_not_hub";
            return false;
        }
        if (!isInitial && sourceKind == HfArtworkSourceKind.HuggingFaceAuthorAvatar
            && (!isAuthorAvatar || previousHost is null
                || !string.Equals(previousHost, AvatarHost, StringComparison.OrdinalIgnoreCase)))
        {
            failure = "avatar_redirect_host_changed";
            return false;
        }
        if (!isInitial && previousHost is not null
            && DeliveryHosts.Contains(previousHost)
            && !string.Equals(previousHost, host, StringComparison.OrdinalIgnoreCase))
        {
            failure = "delivery_origin_changed";
            return false;
        }
        if (!isHub && !isDelivery && !isAuthorAvatar)
        {
            failure = "redirect_host_not_allowed";
            return false;
        }
        if ((isHub || isAuthorAvatar) && !string.IsNullOrEmpty(uri.Query))
        {
            failure = "hub_redirect_query";
            return false;
        }
        if (!string.IsNullOrEmpty(uri.Fragment) || HasEncodedPathHazard(uri))
        {
            failure = "redirect_path_hazard";
            return false;
        }
        return true;
    }

    private static bool IsSafeAuthorityForHost(Uri uri) =>
        !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Port == 443
        && !uri.IsLoopback
        && !IPAddress.TryParse(uri.Host, out _)
        && uri.Host.All(ch => ch < 128 && (char.IsLetterOrDigit(ch) || ch is '.' or '-'));

    private static bool TryValidateAuthorAvatarUrl(string? value, out Uri? validated)
    {
        validated = null;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, AvatarHost, StringComparison.OrdinalIgnoreCase)
            || !IsSafeAuthorityForHost(uri)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath == "/"
            || HasEncodedPathHazard(uri))
            return false;

        validated = uri;
        return true;
    }

    internal static string HostFor(HfArtworkSourceKind sourceKind) =>
        sourceKind == HfArtworkSourceKind.HuggingFaceAuthorAvatar ? AvatarHost : HubHost;

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static HfArtworkResult Failure(
        HfArtworkDescriptor descriptor,
        HfArtworkState state,
        string code,
        string host) =>
        new(state, descriptor, null, null, 0, 0, 0, null, code, host);

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream stream,
        int maxBytes,
        long? declaredLength,
        CancellationToken ct)
    {
        var rented = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            using var output = new MemoryStream();
            while (true)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), ct);
                if (read == 0)
                    break;
                if (output.Length > maxBytes - read)
                    return null;
                await output.WriteAsync(rented.AsMemory(0, read), ct);
            }

            if (declaredLength is { } length && output.Length != length)
                return null;
            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

internal sealed record ArtworkImageInfo(int Width, int Height);

internal static class ArtworkImagePreflight
{
    public static bool TryRead(byte[] bytes, string mime, out ArtworkImageInfo image, out string failure)
    {
        image = new ArtworkImageInfo(0, 0);
        failure = "magic_mismatch";
        var parsed = mime switch
        {
            "image/png" => ParsePng(bytes, out failure),
            "image/jpeg" => ParseJpeg(bytes, out failure),
            "image/webp" => ParseWebp(bytes, out failure),
            _ => null
        };
        if (parsed is null)
            return false;

        if (!WithinLimits(parsed.Value, out failure))
            return false;
        image = new ArtworkImageInfo(parsed.Value.Width, parsed.Value.Height);
        failure = string.Empty;
        return true;
    }

    private static (int Width, int Height)? ParsePng(byte[] bytes, out string failure)
    {
        failure = "png_header";
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return null;

        var offset = 8;
        var foundHeader = false;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > int.MaxValue || offset + 12L + length > bytes.Length)
                return null;
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var data = offset + 8;
            if (type == "acTL")
            {
                failure = "animated_png";
                return null;
            }
            if (type == "IHDR")
            {
                if (foundHeader || length != 13)
                    return null;
                foundHeader = true;
                var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(data, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(data + 4, 4));
                if (width > int.MaxValue || height > int.MaxValue)
                    return null;
                var result = ((int)width, (int)height);
                offset += checked((int)(12 + length));
                while (offset <= bytes.Length - 12)
                {
                    var nextLength = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
                    if (nextLength > int.MaxValue || offset + 12L + nextLength > bytes.Length)
                        return null;
                    var nextType = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                    if (nextType == "acTL")
                    {
                        failure = "animated_png";
                        return null;
                    }
                    offset += checked((int)(12 + nextLength));
                    if (nextType == "IEND")
                    {
                        if (offset != bytes.Length)
                            return null;
                        return result;
                    }
                }
                return null;
            }
            offset += checked((int)(12 + length));
        }
        return null;
    }

    private static (int Width, int Height)? ParseJpeg(byte[] bytes, out string failure)
    {
        failure = "jpeg_header";
        if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8
            || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            return null;

        var offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 0xff)
                return null;
            while (offset < bytes.Length && bytes[offset] == 0xff)
                offset++;
            if (offset >= bytes.Length)
                return null;
            var marker = bytes[offset++];
            if (marker == 0xd9 || marker == 0xda)
                break;
            if (marker == 0xd8 || marker is >= 0xd0 and <= 0xd7)
                continue;
            if (offset + 2 > bytes.Length)
                return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || offset + length > bytes.Length)
                return null;
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                if (length < 7)
                    return null;
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2));
                return (width, height);
            }
            offset += length;
        }
        return null;
    }

    private static (int Width, int Height)? ParseWebp(byte[] bytes, out string failure)
    {
        failure = "webp_header";
        if (bytes.Length < 20 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) + 8 != bytes.Length)
            return null;

        var offset = 12;
        (int Width, int Height)? result = null;
        while (offset <= bytes.Length - 8)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            if (size > int.MaxValue || offset + 8L + size > bytes.Length)
                return null;
            var type = Encoding.ASCII.GetString(bytes, offset, 4);
            var data = offset + 8;
            if (type == "ANIM")
            {
                failure = "animated_webp";
                return null;
            }
            if (type == "VP8X")
            {
                if (size < 10)
                    return null;
                if ((bytes[data] & 0x02) != 0)
                {
                    failure = "animated_webp";
                    return null;
                }
                var width = 1 + ReadUInt24Little(bytes, data + 4);
                var height = 1 + ReadUInt24Little(bytes, data + 7);
                result = (width, height);
            }
            else if (type == "VP8 " && size >= 10 && bytes[data + 3] == 0x9d && bytes[data + 4] == 0x01 && bytes[data + 5] == 0x2a)
            {
                var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(data + 6, 2)) & 0x3fff;
                var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(data + 8, 2)) & 0x3fff;
                result ??= (width, height);
            }
            else if (type == "VP8L" && size >= 5 && bytes[data] == 0x2f)
            {
                var width = 1 + ((bytes[data + 1] | (bytes[data + 2] << 8)) & 0x3fff);
                var height = 1 + (((bytes[data + 2] >> 6) | (bytes[data + 3] << 2) | (bytes[data + 4] << 10)) & 0x3fff);
                result ??= (width, height);
            }
            offset += checked((int)(8 + size + (size & 1)));
        }
        return result;
    }

    private static int ReadUInt24Little(byte[] bytes, int offset) =>
        bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);

    private static bool WithinLimits((int Width, int Height) image, out string failure)
    {
        failure = string.Empty;
        if (image.Width <= 0 || image.Height <= 0 || image.Width > HuggingFaceArtworkService.MaxDimension
            || image.Height > HuggingFaceArtworkService.MaxDimension)
        {
            failure = "dimension_limit";
            return false;
        }
        try
        {
            var pixels = checked((long)image.Width * image.Height);
            var decodedBytes = checked(pixels * 4);
            if (pixels > HuggingFaceArtworkService.MaxDecodedPixels || decodedBytes > HuggingFaceArtworkService.MaxDecodedBytes)
            {
                failure = "decoded_size_limit";
                return false;
            }
            return true;
        }
        catch (OverflowException)
        {
            failure = "decoded_size_overflow";
            return false;
        }
    }
}

public static class HuggingFaceArtworkCache
{
    private const int MaximumEntries = 64;
    private const long MaximumBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal sealed record Metadata(
        string LookupKey,
        string CacheKey,
        string RepoId,
        string RevisionSha,
        string? RepositoryPath,
        HfArtworkSourceKind SourceKind,
        string MimeType,
        long ByteCount,
        int Width,
        int Height,
        string ContentHash,
        string FileName,
        string? ETag,
        DateTimeOffset FetchedAtUtc,
        DateTimeOffset LastAccessedUtc);

    public static string ResolveRoot(string dataRoot) =>
        Path.Combine(Path.GetFullPath(dataRoot), "cache", "huggingface-artwork");

    internal static async Task<HfArtworkResult?> TryGetAsync(
        HfArtworkDescriptor descriptor,
        string cacheRoot,
        CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var metadata = await ReadMetadataAsync(descriptor, cacheRoot, ct);
            if (metadata is null)
                return null;
            var path = SafeChildPath(cacheRoot, metadata.FileName);
            if (path is null || !File.Exists(path) || new FileInfo(path).Length != metadata.ByteCount
                || IsReparsePoint(path))
                return null;

            var bytes = await File.ReadAllBytesAsync(path, ct);
            if (bytes.Length != metadata.ByteCount
                || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), metadata.ContentHash, StringComparison.OrdinalIgnoreCase)
                || !ArtworkImagePreflight.TryRead(bytes, metadata.MimeType, out var image, out _)
                || image.Width != metadata.Width || image.Height != metadata.Height)
                return null;

            var touched = metadata with { LastAccessedUtc = DateTimeOffset.UtcNow };
            try { await WriteMetadataAsync(cacheRoot, touched, ct); }
            catch (IOException) { }
            return new HfArtworkResult(HfArtworkState.Available, descriptor with { CacheKey = metadata.CacheKey }, path, metadata.MimeType,
                metadata.ByteCount, metadata.Width, metadata.Height, metadata.ContentHash, string.Empty,
                HuggingFaceArtworkService.HostFor(metadata.SourceKind), metadata.SourceKind);
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task<HfArtworkResult> StoreAsync(
        HfArtworkDescriptor descriptor,
        string cacheRoot,
        string mime,
        byte[] bytes,
        int width,
        int height,
        string contentHash,
        string? etag,
        CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            EnsureRoot(cacheRoot);
            var extension = mime switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                _ => ".webp"
            };
            var cacheKey = $"{descriptor.CacheKey}-{contentHash}";
            // Keep lookup metadata repository/revision/source specific, but share
            // verified bytes when different records resolve to the same image.
            var fileName = $"{contentHash}{extension}";
            var filePath = SafeChildPath(cacheRoot, fileName)
                ?? throw new IOException("Artwork cache path was unsafe.");
            var metadata = new Metadata(
                descriptor.CacheKey, cacheKey, descriptor.RepoId, descriptor.RevisionSha, descriptor.RepositoryPath,
                descriptor.SourceKind, mime, bytes.LongLength, width, height, contentHash, fileName, etag,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

            var tempFile = Path.Combine(cacheRoot, $".{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(tempFile, bytes, ct);
                File.Move(tempFile, filePath, overwrite: true);
                await WriteMetadataAsync(cacheRoot, metadata, ct);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }

            await EvictAsync(cacheRoot, ct);
            return new HfArtworkResult(HfArtworkState.Available, descriptor with { CacheKey = cacheKey }, filePath, mime,
                bytes.LongLength, width, height, contentHash, string.Empty,
                HuggingFaceArtworkService.HostFor(descriptor.SourceKind), descriptor.SourceKind);
        }
        finally
        {
            Gate.Release();
        }
    }

    internal static async Task<HfArtworkResult?> FindVerifiedAsync(
        string repoId,
        string revisionSha,
        string cacheRoot,
        CancellationToken ct)
    {
        if (!HuggingFaceArtworkService.IsRepoId(repoId) || !HuggingFaceArtworkService.IsImmutableRevision(revisionSha))
            return null;

        await Gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(cacheRoot) || IsReparsePoint(cacheRoot))
                return null;
            foreach (var metadataPath in Directory.EnumerateFiles(cacheRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                Metadata? metadata;
                try { metadata = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(metadataPath, ct), JsonOptions); }
                catch { continue; }
                if (metadata is null || !string.Equals(metadata.RepoId, repoId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(metadata.RevisionSha, revisionSha, StringComparison.OrdinalIgnoreCase)
                    || !HuggingFaceArtworkService.IsImmutableRevision(metadata.RevisionSha)
                    || !IsValidMetadata(metadata))
                    continue;
                var path = SafeChildPath(cacheRoot, metadata.FileName);
                if (path is null || !File.Exists(path) || IsReparsePoint(path)
                    || new FileInfo(path).Length != metadata.ByteCount)
                    continue;
                var bytes = await File.ReadAllBytesAsync(path, ct);
                if (bytes.LongLength != metadata.ByteCount
                    || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), metadata.ContentHash, StringComparison.OrdinalIgnoreCase)
                    || !ArtworkImagePreflight.TryRead(bytes, metadata.MimeType, out var image, out _)
                    || image.Width != metadata.Width || image.Height != metadata.Height)
                    continue;
                var touched = metadata with { LastAccessedUtc = DateTimeOffset.UtcNow };
                try { await WriteMetadataAsync(cacheRoot, touched, ct); }
                catch (IOException) { }
                return new HfArtworkResult(HfArtworkState.Available, null, path, metadata.MimeType,
                    metadata.ByteCount, metadata.Width, metadata.Height, metadata.ContentHash, string.Empty,
                    HuggingFaceArtworkService.HostFor(metadata.SourceKind), metadata.SourceKind);
            }
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<HfArtworkCacheInfo> GetInfoAsync(string cacheRoot, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try { return await GetInfoUnlockedAsync(cacheRoot, ct); }
        finally { Gate.Release(); }
    }

    public static async Task<HfArtworkCacheInfo> ClearAsync(string cacheRoot, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(cacheRoot))
                return new HfArtworkCacheInfo(0, 0);
            if (IsReparsePoint(cacheRoot))
                throw new IOException("Artwork cache directory is a reparse point.");
            foreach (var file in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                if (IsReparsePoint(file))
                    throw new IOException("Artwork cache contains a reparse point.");
                File.Delete(file);
            }
            return new HfArtworkCacheInfo(0, 0);
        }
        finally { Gate.Release(); }
    }

    private static async Task EvictAsync(string cacheRoot, CancellationToken ct)
    {
        var rows = new List<(Metadata Metadata, string MetadataPath, string ImagePath)>();
        foreach (var metadataPath in Directory.EnumerateFiles(cacheRoot, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(metadataPath, ct), JsonOptions);
                var imagePath = metadata is null ? null : SafeChildPath(cacheRoot, metadata.FileName);
                if (metadata is not null && IsValidMetadata(metadata)
                    && imagePath is not null && File.Exists(imagePath) && !IsReparsePoint(imagePath))
                    rows.Add((metadata, metadataPath, imagePath));
            }
            catch { }
        }

        var imageLengths = rows
            .Select(row => row.ImagePath)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, path => new FileInfo(path).Length, StringComparer.Ordinal);
        var metadataLengths = rows.ToDictionary(
            row => row.MetadataPath,
            row => new FileInfo(row.MetadataPath).Length,
            StringComparer.Ordinal);
        long bytes = imageLengths.Values.Sum() + metadataLengths.Values.Sum();
        var owned = rows
            .SelectMany(row => new[] { row.ImagePath, row.MetadataPath })
            .ToHashSet(StringComparer.Ordinal);
        var remainingEntries = rows.Count;
        foreach (var orphan in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => !owned.Contains(path)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                bytes -= new FileInfo(orphan).Length;
                File.Delete(orphan);
            }
            catch { }
        }
        foreach (var row in rows.OrderBy(row => row.Metadata.LastAccessedUtc).ToList())
        {
            if (remainingEntries <= MaximumEntries && bytes <= MaximumBytes)
                break;
            if (metadataLengths.TryGetValue(row.MetadataPath, out var metadataLength))
            {
                try
                {
                    File.Delete(row.MetadataPath);
                    bytes -= metadataLength;
                }
                catch { }
            }
            rows.Remove(row);
            if (!rows.Any(other => string.Equals(other.ImagePath, row.ImagePath, StringComparison.Ordinal)))
            {
                if (imageLengths.TryGetValue(row.ImagePath, out var imageLength))
                {
                    try
                    {
                        File.Delete(row.ImagePath);
                        bytes -= imageLength;
                    }
                    catch { }
                }
            }
            remainingEntries--;
        }
    }

    private static async Task<HfArtworkCacheInfo> GetInfoUnlockedAsync(string cacheRoot, CancellationToken ct)
    {
        if (!Directory.Exists(cacheRoot) || IsReparsePoint(cacheRoot))
            return new HfArtworkCacheInfo(0, 0);
        long bytes = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            if (IsReparsePoint(file))
                continue;
            bytes += new FileInfo(file).Length;
            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return new HfArtworkCacheInfo(bytes, count);
    }

    private static async Task<Metadata?> ReadMetadataAsync(HfArtworkDescriptor descriptor, string cacheRoot, CancellationToken ct)
    {
        if (!Directory.Exists(cacheRoot) || IsReparsePoint(cacheRoot))
            return null;
        var path = SafeChildPath(cacheRoot, descriptor.CacheKey + ".json");
        if (path is null || !File.Exists(path) || IsReparsePoint(path))
            return null;
        var metadata = JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(path, ct), JsonOptions);
        return metadata is not null
            && string.Equals(metadata.LookupKey, descriptor.CacheKey, StringComparison.Ordinal)
            && string.Equals(metadata.RepoId, descriptor.RepoId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metadata.RevisionSha, descriptor.RevisionSha, StringComparison.OrdinalIgnoreCase)
            && IsValidMetadata(metadata)
            ? metadata
            : null;
    }

    private static bool IsValidMetadata(Metadata metadata) =>
        IsSafeCacheKey(metadata.LookupKey)
        && IsSafeCacheKey(metadata.CacheKey)
        && metadata.CacheKey.StartsWith(metadata.LookupKey + "-", StringComparison.Ordinal)
        && metadata.ByteCount is > 0 and <= HuggingFaceArtworkService.MaxArtworkBytes
        && metadata.Width > 0
        && metadata.Height > 0
        && HuggingFaceArtworkService.IsSupportedMime(metadata.MimeType)
        && metadata.ContentHash is { Length: 64 } contentHash
        && contentHash.All(Uri.IsHexDigit)
        && string.Equals(metadata.FileName,
            metadata.ContentHash + ExtensionForMime(metadata.MimeType), StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeCacheKey(string? value) =>
        value is { Length: > 0 } && value.All(character => Uri.IsHexDigit(character) || character == '-');

    private static string ExtensionForMime(string mime) => mime switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        _ => ".webp"
    };

    private static async Task WriteMetadataAsync(string cacheRoot, Metadata metadata, CancellationToken ct)
    {
        EnsureRoot(cacheRoot);
        var path = SafeChildPath(cacheRoot, metadata.LookupKey + ".json")
            ?? throw new IOException("Artwork metadata path was unsafe.");
        var temp = Path.Combine(cacheRoot, $".{Guid.NewGuid():N}.json.tmp");
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(metadata, JsonOptions), ct);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static void EnsureRoot(string cacheRoot)
    {
        var full = Path.GetFullPath(cacheRoot);
        if (Directory.Exists(full) && IsReparsePoint(full))
            throw new IOException("Artwork cache directory is a reparse point.");
        Directory.CreateDirectory(full);
    }

    private static string? SafeChildPath(string root, string child)
    {
        if (string.IsNullOrWhiteSpace(child) || child.Contains(Path.DirectorySeparatorChar)
            || child.Contains(Path.AltDirectorySeparatorChar) || child.Contains('\0'))
            return null;
        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(fullRoot, child));
        return path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : null;
    }

    private static bool IsReparsePoint(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch { return true; }
    }
}
