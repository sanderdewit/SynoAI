using Microsoft.Extensions.Options;
using System.Text.Json;
using SynoAI.App;
using SynoAI.Models;
using SynoAI.Settings;

namespace SynoAI.Services
{
    internal class SynologyService : ISynologyService
    {
        // Synology session cookie value; refreshed automatically on expiry (errors 105/106).
        private string? _sessionCookieValue;

        private Dictionary<string, int> Cameras { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Discovered API versions (clamped to configured maximums).
        private int _authVersion;
        private int _cameraVersion;

        // Semaphore to prevent concurrent re-authentication races.
        private readonly SemaphoreSlim _reAuthSemaphore = new(1, 1);

        private const string API_LOGIN = "SYNO.API.Auth";
        private const string API_CAMERA = "SYNO.SurveillanceStation.Camera";

        private const string URI_INFO = "webapi/query.cgi?api=SYNO.API.Info&version=1&method=query";
        private const string URI_CAMERA_INFO = "webapi/{0}?api=SYNO.SurveillanceStation.Camera&method=List&version={1}";
        private const string URI_CAMERA_SNAPSHOT = "webapi/{0}?version={1}&id={2}&api=SYNO.SurveillanceStation.Camera&method=GetSnapshot";

        private string? LoginPath { get; set; }
        private string? CameraPath { get; set; }

        private readonly ILogger<SynologyService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<AppSettings> _options;

        public SynologyService(ILogger<SynologyService> logger, IHttpClientFactory httpClientFactory, IOptionsMonitor<AppSettings> options)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _options = options;
        }

        private AppSettings Settings => _options.CurrentValue;

        private HttpClient GetSynologyClient()
        {
            HttpClient client = _httpClientFactory.CreateClient("Synology");
            client.BaseAddress = new Uri(Settings.Url);
            return client;
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string uri)
        {
            HttpRequestMessage request = new(method, uri);
            if (_sessionCookieValue != null)
                request.Headers.Add("Cookie", $"id={_sessionCookieValue}");
            return request;
        }

        public async Task<bool> GetEndPointsAsync()
        {
            _logger.LogInformation("API: Querying end points");

            HttpClient client = GetSynologyClient();
            HttpResponseMessage result = await client.GetAsync(URI_INFO);
            if (result.IsSuccessStatusCode)
            {
                SynologyResponse<SynologyApiInfoResponse> response = await GetResponse<SynologyApiInfoResponse>(result);
                if (response.Success)
                {
                    if (response.Data.TryGetValue(API_LOGIN, out SynologyApiInfo? loginInfo))
                    {
                        _logger.LogDebug("API: Found path '{Path}' for {Api}", loginInfo.Path, API_LOGIN);
                        if (loginInfo.MaxVersion < Settings.ApiVersionAuth)
                        {
                            _logger.LogError("API: {Api} only supports max version {Max}, configured version is {Configured}.", API_CAMERA, loginInfo.MaxVersion, Settings.ApiVersionAuth);
                            return false;
                        }
                    }
                    else
                    {
                        _logger.LogError("API: Failed to find {Api}.", API_LOGIN);
                        return false;
                    }

                    if (response.Data.TryGetValue(API_CAMERA, out SynologyApiInfo? cameraInfo))
                    {
                        _logger.LogDebug("API: Found path '{Path}' for {Api}", cameraInfo.Path, API_CAMERA);
                        if (cameraInfo.MaxVersion < Settings.ApiVersionCamera)
                        {
                            _logger.LogError("API: {Api} only supports max version {Max}, configured version is {Configured}.", API_CAMERA, cameraInfo.MaxVersion, Settings.ApiVersionCamera);
                            return false;
                        }
                    }
                    else
                    {
                        _logger.LogError("API: Failed to find {Api}.", API_CAMERA);
                        return false;
                    }

                    LoginPath = loginInfo.Path;
                    CameraPath = cameraInfo.Path;

                    // Use the lesser of what the NAS supports and what we're configured to use.
                    _authVersion = Math.Min(loginInfo.MaxVersion, Settings.ApiVersionAuth);
                    _cameraVersion = Math.Min(cameraInfo.MaxVersion, Settings.ApiVersionCamera);

                    _logger.LogInformation("API: Successfully mapped all end points");
                    return true;
                }
                else
                {
                    _logger.LogError("API: Failed due to error code '{Code}'", response.Error?.Code);
                }
            }
            else
            {
                _logger.LogError("API: Failed due to HTTP status code '{StatusCode}'", result.StatusCode);
            }
            return false;
        }

        private async Task<string?> LoginAsync()
        {
            _logger.LogInformation("Login: Authenticating");

            string loginUrl = $"webapi/{LoginPath}";
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("api", "SYNO.API.Auth"),
                new KeyValuePair<string, string>("method", "Login"),
                new KeyValuePair<string, string>("version", _authVersion.ToString()),
                new KeyValuePair<string, string>("account", Settings.Username),
                new KeyValuePair<string, string>("passwd", Settings.Password),
                new KeyValuePair<string, string>("session", "SurveillanceStation"),
            });

            HttpClient client = GetSynologyClient();
            HttpResponseMessage result = await client.PostAsync(loginUrl, formContent);
            if (result.IsSuccessStatusCode)
            {
                SynologyResponse<SynologyLogin> response = await GetResponse<SynologyLogin>(result);
                if (response.Success)
                {
                    _logger.LogInformation("Login: Successful");

                    // Parse session cookie from Set-Cookie response header (UseCookies=false so we do it manually)
                    if (result.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
                    {
                        foreach (string cookie in setCookies)
                        {
                            string[] parts = cookie.Split(';')[0].Split('=');
                            if (parts.Length == 2 && parts[0].Trim().Equals("id", StringComparison.OrdinalIgnoreCase))
                                return parts[1].Trim();
                        }
                    }

                    _logger.LogError("Login: Successful but session cookie 'id' not found in response.");
                    return null;
                }
                else
                {
                    _logger.LogError("Login: Failed due to Synology API error code '{Code}'", response.Error?.Code);
                }
            }
            else
            {
                _logger.LogError("Login: Failed due to HTTP status code '{StatusCode}'", result.StatusCode);
            }
            return null;
        }

        private async Task<IEnumerable<SynologyCamera>?> GetCamerasAsync()
        {
            _logger.LogInformation("GetCameras: Fetching Cameras");

            HttpClient client = GetSynologyClient();
            string cameraInfoUri = string.Format(URI_CAMERA_INFO, CameraPath, _cameraVersion);
            using HttpRequestMessage request = BuildRequest(HttpMethod.Get, cameraInfoUri);
            HttpResponseMessage result = await client.SendAsync(request);

            SynologyResponse<SynologyCameras> response = await GetResponse<SynologyCameras>(result);
            if (response.Success)
            {
                int cameraCount = response.Data.Cameras.Count();
                _logger.LogInformation("GetCameras: Successful. Found {Count} cameras.", cameraCount);
                return response.Data.Cameras;
            }
            else
            {
                _logger.LogError("GetCameras: Failed due to error code '{Code}'", response.Error?.Code);
            }
            return null;
        }

        public async Task<byte[]?> TakeSnapshotAsync(string cameraName)
        {
            return await TakeSnapshotInternalAsync(cameraName, isRetry: false);
        }

        private async Task<byte[]?> TakeSnapshotInternalAsync(string cameraName, bool isRetry)
        {
            if (!Cameras.TryGetValue(cameraName, out int id))
            {
                _logger.LogError("The camera with the name '{CameraName}' was not found in the Synology camera list.", cameraName);
                return null;
            }

            _logger.LogDebug("{CameraName}: Found with Synology ID '{Id}'.", cameraName, id);

            string resource = string.Format(URI_CAMERA_SNAPSHOT + $"&profileType={(int)Settings.Quality}", CameraPath, _cameraVersion, id);
            _logger.LogDebug("{CameraName}: Taking snapshot from '{Resource}'.", cameraName, resource);
            _logger.LogInformation("{CameraName}: Taking snapshot", cameraName);

            HttpClient client = GetSynologyClient();
            using HttpRequestMessage request = BuildRequest(HttpMethod.Get, resource);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentType?.MediaType == "image/jpeg")
            {
                _logger.LogDebug("{CameraName}: Reading snapshot", cameraName);
                return await response.Content.ReadAsByteArrayAsync();
            }

            // Non-image response — likely an auth or API error
            SynologyResponse errorResponse = await GetErrorResponse(response);
            if (!isRetry && IsSessionExpiredError(errorResponse))
            {
                _logger.LogWarning("{CameraName}: Session expired (code {Code}), re-authenticating...", cameraName, errorResponse.Error?.Code);

                await _reAuthSemaphore.WaitAsync();
                try
                {
                    // Re-check: another thread may have already refreshed the session.
                    if (IsSessionExpiredError(errorResponse))
                    {
                        _sessionCookieValue = await LoginAsync();
                    }
                }
                finally
                {
                    _reAuthSemaphore.Release();
                }

                if (_sessionCookieValue != null)
                    return await TakeSnapshotInternalAsync(cameraName, isRetry: true);
            }
            else if (errorResponse.Success)
            {
                _logger.LogError("{CameraName}: Failed to get snapshot, but the API reported success.", cameraName);
            }
            else
            {
                _logger.LogError("{CameraName}: Failed to get snapshot with error code '{Code}'", cameraName, errorResponse.Error?.Code);
            }

            return null;
        }

        private static bool IsSessionExpiredError(SynologyResponse errorResponse)
        {
            string? code = errorResponse.Error?.Code;
            return code == "105" || code == "106";
        }

        private static async Task<SynologyResponse<T>> GetResponse<T>(HttpResponseMessage message)
        {
            string content = await message.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SynologyResponse<T>>(content, Shared.JsonOptions)!;
        }

        private static async Task<SynologyResponse> GetErrorResponse(HttpResponseMessage message)
        {
            string content = await message.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SynologyResponse>(content, Shared.JsonOptions)!;
        }

        public async Task InitialiseAsync()
        {
            _logger.LogInformation("Initialising");
            try
            {
                // Initialisation failures are non-fatal: the web server (and settings UI) stays up so the
                // configuration can be corrected. The camera webhook simply returns errors until a working
                // configuration is in place and initialisation is retried (via ReinitialiseAsync).
                bool retrievedEndPoints = await GetEndPointsAsync();
                if (!retrievedEndPoints)
                {
                    _logger.LogError("Initialisation incomplete: failed to map the Synology API endpoints.");
                    return;
                }

                _sessionCookieValue = await LoginAsync();
                if (_sessionCookieValue == null)
                {
                    _logger.LogError("Initialisation incomplete: failed to authenticate with Synology.");
                    return;
                }

                if (Settings.Cameras.Count == 0)
                {
                    _logger.LogWarning("Initialisation incomplete: no cameras were specified in the config.");
                    return;
                }

                IEnumerable<SynologyCamera>? synologyCameras = await GetCamerasAsync();
                if (synologyCameras == null)
                {
                    _logger.LogError("Initialisation incomplete: failed to fetch the Surveillance Station camera list.");
                    return;
                }

                Dictionary<string, int> cameras = new(StringComparer.OrdinalIgnoreCase);
                foreach (Camera camera in Settings.Cameras)
                {
                    SynologyCamera? match = synologyCameras.FirstOrDefault(x => x.GetName().Equals(camera.Name, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        _logger.LogWarning("Initialise: The camera '{CameraName}' was not found in the Surveillance Station camera list.", camera.Name);
                    else
                        cameras.Add(camera.Name, match.Id);
                }
                Cameras = cameras;

                _logger.LogInformation("Initialisation successful.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred initialising SynoAI.");
            }
        }
    }
}
