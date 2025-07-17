
using KitsuneSteamRestrict;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

public class SteamUserInfo
{
	public DateTime SteamAccountAge { get; set; }
	public int SteamLevel { get; set; }
	public int CS2Level { get; set; }
	public int CS2Playtime { get; set; }
	public bool IsPrivate { get; set; }
	public bool IsGameDetailsPrivate { get; set; }
	public bool HasPrime { get; set; }
	public bool IsTradeBanned { get; set; }
	public bool IsVACBanned { get; set; }
	public bool IsGameBanned { get; set; }
	public bool IsInSteamGroup { get; set; }
}

public class SteamService
{
	private readonly HttpClient _httpClient;
	private readonly string _steamWebAPIKey;
	private readonly PluginConfig _config;
	private readonly ILogger _logger;
	public SteamUserInfo? UserInfo = null;

	public SteamService(SteamRestrictPlugin plugin, SteamUserInfo userInfo)
	{
		_httpClient = plugin.Client;
		_config = plugin.Config;
		_logger = plugin.Logger;
		_steamWebAPIKey = _config.SteamWebAPI;
		UserInfo = userInfo;
	}

	public async Task FetchSteamUserInfo(string steamId)
	{
		//UserInfo.HasPrime = await FetchHasPrimeAsync(steamId);
		UserInfo!.CS2Playtime = await FetchCS2PlaytimeAsync(steamId) / 60;
		UserInfo.SteamLevel = await FetchSteamLevelAsync(steamId);
		await FetchProfilePrivacyAsync(steamId, UserInfo);
		await FetchTradeBanStatusAsync(steamId, UserInfo);
		await FetchGameBanStatusAsync(steamId, UserInfo);
		await FetchSteamGroupMembershipAsync(steamId, UserInfo);
	}

	private async Task<int> FetchCS2PlaytimeAsync(string steamId)
	{
		var url = $"https://47.76.54.206:50000/IPlayerService/GetOwnedGames/v1/?key={_steamWebAPIKey}&steamid={steamId}&format=json";
		var json = await GetApiResponseAsync(url);
		return json != null ? ParseCS2Playtime(json) : 0;
	}

	private async Task<int> FetchSteamLevelAsync(string steamId)
	{
		var url = $"http://47.76.54.206:50000/IPlayerService/GetSteamLevel/v1/?key={_steamWebAPIKey}&steamid={steamId}";
		var json = await GetApiResponseAsync(url);
		return json != null ? ParseSteamLevel(json) : 0;
	}

	private async Task FetchProfilePrivacyAsync(string steamId, SteamUserInfo userInfo)
	{
		var url = $"https://47.76.54.206:50000/ISteamUser/GetPlayerSummaries/v2/?key={_steamWebAPIKey}&steamids={steamId}";
		var json = await GetApiResponseAsync(url);
		if (json != null) ParseSteamUserInfo(json, userInfo);
	}

	private async Task FetchTradeBanStatusAsync(string steamId, SteamUserInfo userInfo)
	{
		var url = $"https://47.76.54.206:50000/ISteamUser/GetPlayerBans/v1/?key={_steamWebAPIKey}&steamids={steamId}";
		var json = await GetApiResponseAsync(url);
		if (json != null)
		{
			ParseTradeBanStatus(json, userInfo);
			ParseVACBanStatus(json, userInfo);
		}
	}

	private async Task FetchGameBanStatusAsync(string steamId, SteamUserInfo userInfo)
	{
		var url = $"https://47.76.54.206:50000/ISteamUser/GetUserGameBan/v1/?key={_steamWebAPIKey}&steamids={steamId}";
		var json = await GetApiResponseAsync(url);
		if (json != null) ParseGameBanStatus(json, userInfo);
	}

	private async Task FetchSteamGroupMembershipAsync(string steamId, SteamUserInfo userInfo)
	{
		if (!string.IsNullOrEmpty(_config.SteamGroupID))
		{
			var url = $"http://47.76.54.206:50000/ISteamUser/GetUserGroupList/v1/?key={_steamWebAPIKey}&steamid={steamId}";
			var json = await GetApiResponseAsync(url);

			userInfo.IsInSteamGroup = false;
			if (json != null)
			{
				JObject jsonObj = JObject.Parse(json);
				JToken? groups = jsonObj["response"]?["groups"];
				if (groups != null)
				{
					userInfo.IsInSteamGroup = groups.Any(group => (group["gid"]?.ToString() ?? "") == _config.SteamGroupID);
				}
			}
		}
		else
		{
			userInfo.IsInSteamGroup = true;
		}
	}

	private async Task<string?> GetApiResponseAsync(string url)
	{
		try
		{
			var response = await _httpClient.GetAsync(url);
			if (response.IsSuccessStatusCode)
			{
				return await response.Content.ReadAsStringAsync();
			}
		}
		catch (Exception e)
		{
			_logger.LogError($"An error occurred while fetching API response: {e.Message}");
		}
		return null;
	}

	private int ParseCS2Playtime(string json)
	{
		JObject data = JObject.Parse(json);
		JToken? game = data["response"]?["games"]?.FirstOrDefault(x => x["appid"]?.Value<int>() == 730);
		return game?["playtime_forever"]?.Value<int>() ?? 0;
	}

	private int ParseSteamLevel(string json)
	{
		JObject data = JObject.Parse(json);
		return (int)(data["response"]?["player_level"] ?? 0);
	}

	private void ParseSteamUserInfo(string json, SteamUserInfo userInfo)
	{
		JObject data = JObject.Parse(json);
		JToken? player = data["response"]?["players"]?.FirstOrDefault();
		if (player != null)
		{
			userInfo.IsPrivate = player["communityvisibilitystate"]?.ToObject<int?>() != 3;
			userInfo.IsGameDetailsPrivate = player["gameextrainfo"] == null;
			int? timeCreated = player["timecreated"]?.ToObject<int?>();
			userInfo.SteamAccountAge = timeCreated.HasValue
				? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timeCreated.Value)
				: DateTime.Now;
		}
	}

	private void ParseTradeBanStatus(string json, SteamUserInfo userInfo)
	{
		JObject data = JObject.Parse(json);
		JToken? playerBan = data["players"]?.FirstOrDefault();
		userInfo.IsTradeBanned = playerBan != null && (bool)(playerBan["CommunityBanned"] ?? false);
	}

	private void ParseGameBanStatus(string json, SteamUserInfo userInfo)
	{
		JObject data = JObject.Parse(json);
		JToken? userGameBan = data["players"]?.FirstOrDefault();
		userInfo.IsGameBanned = userGameBan != null && (bool)(userGameBan["IsGameBanned"] ?? false);
	}

	private void ParseVACBanStatus(string json, SteamUserInfo userInfo)
	{
		JObject data = JObject.Parse(json);
		JToken? userGameBan = data["players"]?.FirstOrDefault();
		userInfo.IsVACBanned = userGameBan != null && (bool)(userGameBan["VACBanned"] ?? false);
	}

	/*
	! This method is not used in the current implementation due to the people who bought Prime after
	! the CS2 release are not visible as Prime users. This method is kept here for reference if its gonna
	! be used in the future, whenever Volvo decides to fix their API.

	private async Task<bool> FetchHasPrimeAsync(string steamId)
	{
		var url = $"https://47.76.54.206:50000/IPlayerService/GetOwnedGames/v1/?key={_steamWebAPIKey}&steamid={steamId}&include_appinfo=false&include_played_free_games=true&format=json";
		var json = await GetApiResponseAsync(url);
		if (json != null)
		{
			JObject data = JObject.Parse(json);
			var games = data["response"]?["games"];
			if (games != null)
			{
				_logger.LogInformation($"Prime: {games.Any(game => game["appid"]?.Value<int>() == 624820 || game["appid"]?.Value<int>() == 54029)}");
				return games.Any(game => game["appid"]?.Value<int>() == 624820 || game["appid"]?.Value<int>() == 54029);
			}
		}
		return false;
	}*/
}
