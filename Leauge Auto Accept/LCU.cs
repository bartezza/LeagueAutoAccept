using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Text.Json;
using RestSharp;
using System.Collections.Generic;

namespace Leauge_Auto_Accept
{
    internal class LCU
    {
        private static readonly NLog.ILogger Log = NLog.LogManager.GetCurrentClassLogger();
        private static readonly NLog.ILogger JsonLog = NLog.LogManager.GetLogger("JsonLog");

        private static string[] leagueAuth;
        private static int lcuPid = 0;
        private const int RequestTimeoutMilliseconds = 5000;
        private const int MaxRetryAttempts = 5;
        public static bool isLeagueOpen = false;

        public static void CheckIfLeagueClientIsOpenTask()
        {
            while (true)
            {
                Process client = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (client != null)
                {
                    if (lcuPid != client.Id)
                    {
                        //get token and port
                        string[] auth = getLeagueAuth(client);
                        if (auth == null)
                        {
                            // The client process exists but its auth data could not be read yet.
                            // This is common right after the client launches (the process appears
                            // before its command line is queryable) or during a transient WMI hiccup.
                            // Do NOT commit lcuPid here, otherwise the block above would be skipped
                            // on every future iteration and we'd stay stuck "not finding" the client
                            // for this whole session. Leaving lcuPid unchanged makes us retry.
                            isLeagueOpen = false;
                            MainLogic.isAutoAcceptOn = false;
                            Log.Warn("League client was found, but LCU auth data could not be read yet. Will retry.");
                            if (UI.currentWindow != "leagueClientIsClosedMessage" && UI.currentWindow != "exitMenu")
                            {
                                UI.leagueClientIsClosedMessage();
                            }
                            Thread.Sleep(2000);
                            continue;
                        }

                        // Only commit the PID once we actually have valid auth data.
                        lcuPid = client.Id;
                        leagueAuth = auth;

                        //reset restclient
                        S_restClient?.Dispose(); S_restClient = null;

                        // Check if preload data was enabled last time
                        if (Settings.preloadData)
                        {
                            bool championsLoaded = Data.loadChampionsList();
                            bool spellsLoaded = Data.loadSpellsList();
                            if (!championsLoaded || !spellsLoaded)
                            {
                                Console.Clear();
                                Print.printCentered("Some League data failed to load.", SizeHandler.HeightCenter - 1);
                                Print.printCentered("The app will keep running; try opening the selector again.");
                                Thread.Sleep(2500);
                            }
                        }
                        if (Settings.shouldAutoAcceptbeOn)
                        {
                            MainLogic.isAutoAcceptOn = true;
                        }
                        if (UI.currentWindow != "exitMenu")
                        {
                            UI.mainScreen();
                        }
                    }

                    // Reached only when the client is present AND auth is valid
                    // (the auth-failure path above uses 'continue').
                    isLeagueOpen = true;
                }
                else
                {
                    isLeagueOpen = false;
                    MainLogic.isAutoAcceptOn = false;
                    Data.champsSorted.Clear();
                    Data.spellsSorted.Clear();
                    Data.currentSummonerId = 0;
                    if (UI.currentWindow != "leagueClientIsClosedMessage" && UI.currentWindow != "exitMenu")
                    {
                        UI.leagueClientIsClosedMessage();
                    }
                }
                Thread.Sleep(2000);
            }
        }

        public static bool CheckIfLeagueClientIsOpen()
        {
            Process client = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
            if (client != null)
            {
                Log.Info($"LeagueClientUx process info: id={client.Id}");
                return true;
            }
            else
            {
                return false;
            }
        }

        private static string[] getLeagueAuth(Process client)
        {
            // Primary source: the LeagueClientUx command line (contains --app-port and
            // --remoting-auth-token). Read it via WMI.
            string[] auth = getLeagueAuthFromCommandLine(client);
            if (auth != null)
            {
                return auth;
            }

            // Fallback source: the LCU "lockfile" that the client writes to its install
            // directory. This works even when the command line cannot be read - e.g. the
            // client is running elevated while this app is not, or WMI is momentarily
            // unavailable right after the client starts.
            auth = getLeagueAuthFromLockfile(client);
            if (auth != null)
            {
                Log.Info("Recovered LCU auth data from the lockfile.");
                return auth;
            }

            return null;
        }

        private static string[] getLeagueAuthFromCommandLine(Process client)
        {
            string commandLine = string.Empty;

            try
            {
                string query = $"SELECT CommandLine FROM Win32_Process where ProcessId = {client.Id}";
                using (var searcher = new System.Management.ManagementObjectSearcher(query))
                using (var results = searcher.Get())
                {
                    foreach (var result in results)
                    {
                        commandLine = result["CommandLine"]?.ToString();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // WMI can throw (access denied, provider load failure, etc.). Treat it as
                // "not available" so the lockfile fallback gets a chance.
                Log.Warn(ex, "Failed to query the LeagueClientUx command line via WMI.");
                return null;
            }

            // Parse the port and auth token. Values may be wrapped in quotes, so allow them.
            var portMatch = Regex.Match(commandLine ?? "", @"--app-port=""?(\d+)""?");
            var authTokenMatch = Regex.Match(commandLine ?? "", @"--remoting-auth-token=""?([\w-]+)""?");
            if (!portMatch.Success || !authTokenMatch.Success)
            {
                Log.Warn("Failed to parse LCU auth data from command line. portFound={0} tokenFound={1}", portMatch.Success, authTokenMatch.Success);
                return null;
            }

            return buildAuth(authTokenMatch.Groups[1].Value, portMatch.Groups[1].Value);
        }

        private static string[] getLeagueAuthFromLockfile(Process client)
        {
            foreach (string dir in getLeagueInstallDirCandidates(client))
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                string lockfilePath = System.IO.Path.Combine(dir, "lockfile");
                if (!System.IO.File.Exists(lockfilePath))
                {
                    continue;
                }

                try
                {
                    string content;
                    // The client holds the lockfile open, so we must share read/write access.
                    using (var fs = new System.IO.FileStream(lockfilePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    using (var reader = new System.IO.StreamReader(fs))
                    {
                        content = reader.ReadToEnd();
                    }

                    // Format: <name>:<pid>:<port>:<password>:<protocol>
                    string[] parts = content.Split(':');
                    if (parts.Length < 4)
                    {
                        continue;
                    }

                    string port = parts[2];
                    string token = parts[3];
                    if (string.IsNullOrWhiteSpace(port) || string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }

                    return buildAuth(token, port);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Failed to read the LCU lockfile at {0}.", lockfilePath);
                }
            }

            return null;
        }

        private static IEnumerable<string> getLeagueInstallDirCandidates(Process client)
        {
            // LeagueClientUx.exe and the lockfile live in the install directory, so the
            // process's own module path is the most reliable source.
            string moduleDir = null;
            try
            {
                moduleDir = System.IO.Path.GetDirectoryName(client.MainModule?.FileName);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not read the LeagueClientUx module path.");
            }

            if (!string.IsNullOrWhiteSpace(moduleDir))
            {
                yield return moduleDir;
            }

            // Common default install locations as a last resort.
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return System.IO.Path.Combine(localAppData, "Riot Games", "League of Legends");

            foreach (string root in new[] { @"C:\Riot Games", @"C:\Program Files\Riot Games", @"C:\Program Files (x86)\Riot Games" })
            {
                yield return System.IO.Path.Combine(root, "League of Legends");
            }
        }

        private static string[] buildAuth(string authToken, string port)
        {
            // Compute the encoded Basic auth key: base64("riot:<token>").
            string auth = "riot:" + authToken;
            string authBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
            return new string[] { authBase64, port };
        }

        private static RestClient S_restClient;

        public static RestResponse clientRequest(string method, string url, object json)
        {
            var methodEnum = Enum.Parse<Method>(method, true);

            var req = new RestRequest(url, methodEnum);
            if (json != null)
                req.AddJsonBody(json, ContentType.Json);

            return clientRequest(req);
        }

        public static RestResponse clientRequest(string method, string url, string body = null)
        {
            var methodEnum = Enum.Parse<Method>(method, true);

            var req = new RestRequest(url, methodEnum);
            if (body != null)
            {
                //if (body.StartsWith("{"))
                req.AddJsonBody(body);
                //else
                //    req.AddStringBody(body, ContentType.Plain);
            }

             return clientRequest(req);
        }

        public static RestResponse clientRequest(RestRequest req)
        {
            if (!EnsureClient())
            {
                return new RestResponse(req);
            }

            Log.Debug("Initiating request {0} {1}", req.Method, req.Resource);

            //execute request
            RestResponse restResp;
            try
            {
                req.Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds);
                restResp = S_restClient.Execute(req);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "LCU request failed before response: {0} {1}", req.Method, req.Resource);
                return new RestResponse(req)
                {
                    ErrorException = ex,
                    ErrorMessage = ex.Message,
                    ResponseStatus = ResponseStatus.Error
                };
            }

            if (Log.IsDebugEnabled)
            {
                 Log.Debug("statusCode={0}, responseStatus={1}, isSuccessful={2}", restResp?.StatusCode, restResp?.ResponseStatus, restResp?.IsSuccessful);
            }

            WriteToJsonLog(req, restResp);

            return restResp ?? new RestResponse(new RestRequest());
        }

        private static Dictionary<string, string> S_JsonLog_Values = new Dictionary<string, string>();

        public static RestResponse clientRequestUntilSuccess(string method, string url, string body = null)
        {
            RestResponse request;
            int attempts = 0;
            do
            {
                request = clientRequest(method, url, body);
                if (request.IsSuccessStatusCode == true)
                {
                    return request;
                }
                else
                {
                    attempts++;
                    if (attempts < MaxRetryAttempts && CheckIfLeagueClientIsOpen())
                    {
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        Log.Warn("LCU request did not succeed after {0} attempts: {1} {2} statusCode={3} responseStatus={4}",
                            attempts, method, url, request?.StatusCode, request?.ResponseStatus);
                        return request;
                    }
                }
            } while (request.IsSuccessStatusCode == false && attempts < MaxRetryAttempts);

            return request;
        }

        public static RestResponse clientRequest<TResponse>(string method, string url, object json)
        {
            var methodEnum = Enum.Parse<Method>(method, true);

            var req = new RestRequest(url, methodEnum);
            if (json != null)
                req.AddJsonBody(json, ContentType.Json);

            return clientRequest<TResponse>(req);
        }

        public static RestResponse<TResponse> clientRequest<TResponse>(string method, string url, string body = null)
        {
            var methodEnum = Enum.Parse<Method>(method, true);

            var req = new RestRequest(url, methodEnum);
            if (body != null)
            {
                //if (body.StartsWith("{"))
                req.AddJsonBody(body);
                //else
                //    req.AddStringBody(body, ContentType.Plain);
            }

            return clientRequest<TResponse>(req);
        }

        public static RestResponse<TResponse> clientRequest<TResponse>(RestRequest req)
        {
            if (!EnsureClient())
            {
                return new RestResponse<TResponse>(req);
            }

            Log.Debug("Initiating request {0} {1}", req.Method, req.Resource);

            RestResponse<TResponse> restResp;
            try
            {
                req.Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds);
                restResp = S_restClient.Execute<TResponse>(req);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "LCU request failed before response: {0} {1}", req.Method, req.Resource);
                return new RestResponse<TResponse>(req)
                {
                    ErrorException = ex,
                    ErrorMessage = ex.Message,
                    ResponseStatus = ResponseStatus.Error
                };
            }


            if (Log.IsDebugEnabled)
            {
                Log.Debug("statusCode={0}, responseStatus={1}, isSuccessful={2}", restResp.StatusCode, restResp.ResponseStatus, restResp.IsSuccessful);
                if (restResp.IsSuccessful==false)
                {
                    Log.Debug("statusDescription={0}, errorMessage={1}", restResp.StatusDescription, restResp.ErrorMessage);
                }
            }

            WriteToJsonLog(req, restResp);

            return restResp ?? new RestResponse<TResponse>(new RestRequest());
        }

        public static RestResponse<TResponse> clientRequestUntilSuccess<TResponse>(string method, string url, string body = null)
        {
            RestResponse<TResponse> request;
            int attempts = 0;
            do
            {
                request = clientRequest<TResponse>(method, url, body);
                if (request.IsSuccessStatusCode == true && request.Data != null)
                {
                    return request;
                }
                else
                {
                    attempts++;
                    if (attempts < MaxRetryAttempts && CheckIfLeagueClientIsOpen())
                    {
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        Log.Warn("LCU typed request did not succeed after {0} attempts: {1} {2} statusCode={3} responseStatus={4} hasData={5}",
                            attempts, method, url, request?.StatusCode, request?.ResponseStatus, request != null && request.Data != null);
                        return request;
                    }
                }
            } while ((!request.IsSuccessStatusCode || request.Data == null) && attempts < MaxRetryAttempts);

            return request;
        }

        private static bool EnsureClient()
        {
            if (S_restClient != null)
            {
                return true;
            }

            if (leagueAuth == null || leagueAuth.Length < 2 || string.IsNullOrWhiteSpace(leagueAuth[0]) || string.IsNullOrWhiteSpace(leagueAuth[1]))
            {
                Log.Warn("LCU client cannot be created because auth data is missing.");
                return false;
            }

            S_restClient = new RestClient(c => {
                c.BaseUrl = new Uri("https://127.0.0.1:" + leagueAuth[1] + "/");

                //disable ssl checks for the local self-signed LCU certificate
                c.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicy) => true;

            }, h => {
                h.Authorization = new AuthenticationHeaderValue("Basic", leagueAuth[0]);
            });

            return true;
        }

        private static void WriteToJsonLog(RestRequest request, RestResponse restResp)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (JsonLog.IsDebugEnabled)
            {
                try
                {
                    string httprequest = $"{request.Method} {request.Resource}";
                    object body = request.Parameters.FirstOrDefault(x => x.Type == ParameterType.RequestBody)?.Value;
                    if (body != null) httprequest = httprequest + " " + body.GetHashCode().ToStringInvariant();
                    if (S_JsonLog_Values.TryGetValue(httprequest, out string storedvalue))
                    {
                        if (storedvalue == restResp?.Content) goto skipwrite;
                    }

                    S_JsonLog_Values[httprequest] = restResp?.Content;
                    var jdoc = JsonDocument.Parse(restResp?.Content ?? "");
                    JsonLog.Debug("{0} {1}:\n{2}", request.Method, request.Resource, JsonSerializer.Serialize(jdoc, new JsonSerializerOptions() { WriteIndented = true }));

                skipwrite:
                    ;
                }
                catch { }
            }
        }

    }
}
