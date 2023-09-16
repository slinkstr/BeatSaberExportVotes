using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal class Program
{
    private static HttpClient _httpClient = new();

    private static async Task Main(string[] args)
    {
        try
        {
            ExportMode mode = AskForMode();

            string filePath;
            if (mode == ExportMode.Favorites)
            {
                filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "../LocalLow/Hyperbolic Magnetism/Beat Saber/PlayerData.dat");
            }
            else
            {
                Console.WriteLine("Enter Beat Saber path (contains 'Beat Saber.exe')");
                string dirInput = Console.ReadLine() ?? throw new Exception("Invalid path");
                if (!Directory.Exists(dirInput))
                {
                    throw new Exception($"Invalid path \"{dirInput}\"");
                }

                filePath = Path.Combine(dirInput, "UserData/votedSongs.json");
            }

            if (!File.Exists(filePath))
            {
                throw new Exception("Unable to locate song file: " + filePath);
            }

            string fileContent = File.ReadAllText(filePath);
            List<string> hashes;
            switch (mode)
            {
                case ExportMode.Favorites:
                    hashes = HashesFromPlayerData(fileContent);
                    break;
                case ExportMode.Upvotes:
                    hashes = HashesFromVote(fileContent);
                    break;
                case ExportMode.Downvotes:
                    hashes = HashesFromVote(fileContent, false);
                    break;
                default:
                    throw new Exception("Unknown export mode.");
            }

            if (hashes.Count < 1)
            {
                throw new Exception("No maps found.");
            }

            Console.WriteLine("Processing " + hashes.Count + " maps... (this might take a while)");

            List<BeatSaberMap> maps = await BeatSaverMapInfo(hashes);

            maps.Sort(CompareMaps);

            BeatSaberPlaylist playlist = new()
            {
                PlaylistAuthor = "BeatSaberExportMaps",
                PlaylistDescription = $"Exported at {DateTime.UtcNow}.",
                PlaylistTitle = $"{mode} ({DateTime.UtcNow})",
                Songs = maps,
            };

            string jsonSerialized = JsonConvert.SerializeObject(playlist, Formatting.None);
            string fileName = $"{mode}_{DateTime.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ")}.bplist";
            File.WriteAllText(fileName, jsonSerialized);

            Console.WriteLine("Done! Wrote to " + fileName);
            ExitPrompt();
        }
        catch (Exception exc)
        {
            Console.WriteLine(exc.ToString());
            Console.WriteLine();
            ExitPrompt();
        }
    }

    private static void ExitPrompt()
    {
        Console.WriteLine("Press any key to continue...");
        Console.ReadKey();
        Environment.Exit(0);
    }

    private static ExportMode AskForMode()
    {
        while (true)
        {
            Console.WriteLine("Choose [f]avorites, [u]pvotes, or [d]ownvotes.");
            string? line = Console.ReadLine();
            line = line?.ToLower();
            switch (line)
            {
                case "favorites":
                case "favorite":
                case "f":
                    return ExportMode.Favorites;
                case "upvotes":
                case "upvote":
                case "u":
                    return ExportMode.Upvotes;
                case "downvotes":
                case "downvote":
                case "d":
                    return ExportMode.Downvotes;
                default:
                    Console.WriteLine("Invalid mode.");
                    break;
            }
        }
    }

    private static List<string> HashesFromPlayerData(string fileContent)
    {
        List<string> hashes = new();
        JObject jobj = JObject.Parse(fileContent);
        JArray players = jobj.SelectToken("localPlayers")?.Value<JArray>() ?? throw new Exception("Error parsing players from PlayerInfo.dat");
        JObject player;
        if (players.Count == 0)
        {
            throw new Exception("No players found in PlayerInfo.dat");
        }
        if (players.Count > 1)
        {
            Console.WriteLine("Choose player:");
            for (int i = 0; i < players.Count; i++)
            {
                string name = players[i].SelectToken("playerName")?.Value<string>() ?? throw new Exception("Error parsing playerName from player data.");
                string id = players[i].SelectToken("playerId")?.Value<string>() ?? throw new Exception("Error parsing playerId from player data.");
                Console.WriteLine($"{i}. ID: {id}, Name: {name}");
            }
            string playerSelect = Console.ReadLine() ?? throw new Exception("Invalid selection for player.");
            int targetPlayer = int.Parse(playerSelect);
            player = (JObject)players[targetPlayer];
        }
        else
        {
            player = (JObject)players[0];
        }

        JArray favorites = player.SelectToken("favoritesLevelIds")?.Value<JArray>() ?? throw new Exception("Error reading favorites.");
        string customPrefix = "custom_level_";
        foreach (JValue val in favorites)
        {
            string favorite = val.ToString();
            if (!favorite.StartsWith(customPrefix))
            {
                continue;
            }

            string hash = favorite.Substring(customPrefix.Length);
            hashes.Add(hash);
        }

        return hashes;
    }

    private static List<string> HashesFromVote(string fileContent, bool upvotes = true)
    {
        List<string> hashes = new();
        JObject jobj = JObject.Parse(fileContent);
        string voteString = upvotes ? "Upvote" : "Downvote";
        foreach (var (key, val) in jobj)
        {
            string hash = key;
            string vote = val?.SelectToken("voteType")?.Value<string>() ?? throw new Exception("Error getting vote type for map " + hash);

            if (vote.Equals(voteString, StringComparison.OrdinalIgnoreCase))
            {
                hashes.Add(hash);
            }
        }

        return hashes;
    }

    private static async Task<List<BeatSaberMap>> BeatSaverMapInfo(List<string> hashes)
    {
        int max = 50;
        List<List<string>> buckets = new();
        List<BeatSaberMap> maps = new();

        for (int i = 0; i < hashes.Count; i += max)
        {
            buckets.Add(hashes.Skip(i).Take(max).ToList());
        }

        string host = "https://api.beatsaver.com";
        string path = "/maps/hash/";
        foreach (var bucket in buckets)
        {
            var response   = await _httpClient.GetAsync(host + path + string.Join(",", bucket));
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync();
            JObject jobj   = JObject.Parse(content);

            if (bucket.Count == 1)
            {
                string? error = jobj.SelectToken("error")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine($"Skipping map with hash {bucket[0]}, error: {error}");
                }

                string beatSaverId = jobj.SelectToken("id")?.Value<string>() ?? throw new Exception("Error parsing id from JObject " + jobj.ToString());
                string hash        = bucket[0];
                string name        = jobj.SelectToken("name")?.Value<string>() ?? throw new Exception("Error parsing name from JObject " + jobj.ToString());

                maps.Add(new()
                {
                    BeatSaverId = beatSaverId,
                    Hash        = hash,
                    Name        = name,
                });
            }
            else
            {
                foreach (var (key, val) in jobj)
                {

                    if (val == null || !val.HasValues)
                    {
                        Console.WriteLine($"Skipping map with hash {key}, data not found on BeatSaver.");
                        continue;
                    }

                    string hash        = key;
                    string beatSaverId = val?.SelectToken("id")?.Value<string>() ?? throw new Exception("Error parsing id from JObject " + val?.ToString());
                    string name        = val?.SelectToken("name")?.Value<string>() ?? throw new Exception("Error parsing name from JObject " + val?.ToString());

                    maps.Add(new()
                    {
                        BeatSaverId = beatSaverId,
                        Hash        = hash,
                        Name        = name,
                    });
                }
            }
        }

        return maps;
    }

    private static int CompareMaps(BeatSaberMap mapA, BeatSaberMap mapB)
    {
        int lengthDiff = mapA.BeatSaverId.Length - mapB.BeatSaverId.Length;
        if (lengthDiff != 0)
        {
            return lengthDiff;
        }

        return mapA.BeatSaverId.CompareTo(mapB.BeatSaverId);
    }

    private class BeatSaberMap
    {
        [JsonProperty("key")]
        public string BeatSaverId { get; set; } = "";

        [JsonProperty("hash")]
        public string Hash { get; set; } = "";

        [JsonProperty("songName")]
        public string Name { get; set; } = "";
    }

    private class BeatSaberPlaylist
    {
        [JsonProperty("playlistTitle")]
        public string PlaylistTitle { get; set; } = "";

        [JsonProperty("playlistAuthor")]
        public string PlaylistAuthor { get; set; } = "";

        [JsonProperty("playlistDescription")]
        public string PlaylistDescription { get; set; } = "";

        [JsonProperty("image")]
        public string? Image { get; set; } = null;

        [JsonProperty("songs")]
        public List<BeatSaberMap> Songs { get; set; } = new();
    }

    enum ExportMode
    {
        Favorites,
        Upvotes,
        Downvotes
    }
}