using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DrkRft_Shared.Api;
using Newtonsoft.Json;

namespace ServerTUI
{
    /// <summary>
    /// HTTP API 클라이언트 - 서버와 통신
    /// </summary>
    public class ApiClient
    {
        private readonly HttpClient _http;
        private string _baseUrl;

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ"
        };

        public string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = value.TrimEnd('/');
        }

        public ApiClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        #region Server

        public async Task<ApiResponse<ServerStatusDto>> GetServerStatusAsync()
        {
            return await GetAsync<ApiResponse<ServerStatusDto>>("/api/server/status");
        }

        #endregion

        #region Users

        public async Task<ApiResponse<List<OnlineUserDto>>> GetUsersAsync()
        {
            return await GetAsync<ApiResponse<List<OnlineUserDto>>>("/api/users");
        }

        public async Task<ApiResponse<OnlineUserDto>> GetUserAsync(string accountId)
        {
            return await GetAsync<ApiResponse<OnlineUserDto>>($"/api/users/{Uri.EscapeDataString(accountId)}");
        }

        public async Task<ApiResponse<ActionResultDto>> KickUserAsync(string accountId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/users/{Uri.EscapeDataString(accountId)}/kick", null);
        }

        public async Task<ApiResponse<ActionResultDto>> BroadcastAsync(string message)
        {
            var body = new BroadcastRequest { Message = message };
            return await PostAsync<ApiResponse<ActionResultDto>>("/api/broadcast", body);
        }

        public async Task<ApiResponse<ActionResultDto>> RegisterVirtualUserAsync(string accountId, int accountType)
        {
            var body = new RegisterVirtualUserRequest { AccountId = accountId, AccountType = accountType };
            return await PostAsync<ApiResponse<ActionResultDto>>("/api/users/virtual", body);
        }

        public async Task<ApiResponse<ActionResultDto>> RemoveVirtualUserAsync(string accountId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/users/{Uri.EscapeDataString(accountId)}/remove-virtual", null);
        }

        #endregion

        #region Parties

        public async Task<ApiResponse<List<PartyInfoDto>>> GetPartiesAsync()
        {
            return await GetAsync<ApiResponse<List<PartyInfoDto>>>("/api/parties");
        }

        public async Task<ApiResponse<PartyInfoDto>> GetPartyAsync(int partyId)
        {
            return await GetAsync<ApiResponse<PartyInfoDto>>($"/api/parties/{partyId}");
        }

        public async Task<ApiResponse<ActionResultDto>> DisbandPartyAsync(int partyId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/disband", null);
        }

        public async Task<ApiResponse<CreatePartyResultDto>> CreatePartyAsync(CreatePartyRequest request)
        {
            return await PostAsync<ApiResponse<CreatePartyResultDto>>("/api/parties", request);
        }

        public async Task<ApiResponse<InstanceInfoDto>> RequestInstanceAsync(int partyId)
        {
            return await PostAsync<ApiResponse<InstanceInfoDto>>($"/api/parties/{partyId}/request-instance", null);
        }

        public async Task<ApiResponse<ActionResultDto>> WaitInstanceAsync(int partyId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/wait-instance", null);
        }

        public async Task<ApiResponse<ActionResultDto>> AddPartyMemberAsync(int partyId, string accountId, string role)
        {
            var body = new AddPartyMemberRequest { AccountId = accountId, Role = role };
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/add-member", body);
        }

        public async Task<ApiResponse<ActionResultDto>> RemovePartyMemberAsync(int partyId, string accountId)
        {
            var body = new RemovePartyMemberRequest { AccountId = accountId };
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/remove-member", body);
        }

        public async Task<ApiResponse<ActionResultDto>> ChangeRoleAsync(int partyId, string accountId, string role)
        {
            var body = new ChangeRoleRequest { AccountId = accountId, Role = role };
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/change-role", body);
        }

        public async Task<ApiResponse<ActionResultDto>> ConfirmPartyAsync(int partyId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/confirm", null);
        }

        public async Task<ApiResponse<ActionResultDto>> SendWakeResultAsync(int partyId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/send-wake-result", null);
        }

        public async Task<ApiResponse<ActionResultDto>> SendCreatedAsync(int partyId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/send-created", null);
        }

        public async Task<ApiResponse<ActionResultDto>> TrainResultPushAsync(int partyId)
        {
            var body = new TrainResultPushApiRequest();
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/train-result-push", body);
        }

        public async Task<ApiResponse<ActionResultDto>> GracefulEndAsync(int partyId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/parties/{partyId}/graceful-end", null);
        }

        #endregion

        #region Instances

        public async Task<ApiResponse<List<InstanceInfoDto>>> GetInstancesAsync()
        {
            return await GetAsync<ApiResponse<List<InstanceInfoDto>>>("/api/instances");
        }

        public async Task<ApiResponse<InstanceInfoDto>> GetInstanceAsync(int instanceId)
        {
            return await GetAsync<ApiResponse<InstanceInfoDto>>($"/api/instances/{instanceId}");
        }

        public async Task<ApiResponse<ActionResultDto>> StopInstanceAsync(int instanceId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/instances/{instanceId}/stop", null);
        }

        public async Task<ApiResponse<ActionResultDto>> StartInstanceAsync(int instanceId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/instances/{instanceId}/start", null);
        }

        public async Task<ApiResponse<ActionResultDto>> PauseInstanceAsync(int instanceId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/instances/{instanceId}/pause", null);
        }

        public async Task<ApiResponse<ActionResultDto>> GracefulStopInstanceAsync(int instanceId)
        {
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/instances/{instanceId}/graceful-stop", null);
        }

        public async Task<ApiResponse<ActionResultDto>> AssignPartyToInstanceAsync(int instanceId, int partyId)
        {
            var body = new AssignPartyRequest { PartyId = partyId };
            return await PostAsync<ApiResponse<ActionResultDto>>($"/api/instances/{instanceId}/assign-party", body);
        }

        #endregion

        #region HTTP Helpers

        private async Task<T> GetAsync<T>(string path) where T : class
        {
            try
            {
                var response = await _http.GetAsync(_baseUrl + path);
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(json, JsonSettings);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<T> PostAsync<T>(string path, object body) where T : class
        {
            try
            {
                HttpContent content = null;
                if (body != null)
                {
                    var json = JsonConvert.SerializeObject(body, JsonSettings);
                    content = new StringContent(json, Encoding.UTF8, "application/json");
                }
                else
                {
                    content = new StringContent("", Encoding.UTF8, "application/json");
                }

                var response = await _http.PostAsync(_baseUrl + path, content);
                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<T>(responseJson, JsonSettings);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion
    }
}
